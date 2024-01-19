using System;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams.Dsl;
using System.Threading.Channels;
using Akka.Persistence.RavenDB.Journal;
using Akka.Persistence.RavenDB.Journal.Types;
using Akka.Persistence.RavenDB.Query.ContinuousQuery;
using Akka.Persistence.Serialization;
using Nito.AsyncEx;
using Raven.Client.Documents.Linq;
using Raven.Client.Documents.Subscriptions;

namespace Akka.Persistence.RavenDB.Query
{
    public partial class RavenDbReadJournal :
        IPersistenceIdsQuery,
        ICurrentPersistenceIdsQuery,
        IEventsByPersistenceIdQuery,
        ICurrentEventsByPersistenceIdQuery,
        IEventsByTagQuery,
        ICurrentEventsByTagQuery,
        IAllEventsQuery,
        ICurrentAllEventsQuery
    {
        /// <summary>
        /// HOCON identifier
        /// </summary>
        public const string Identifier = "akka.persistence.query.ravendb";

        private readonly string _writeJournalPluginId;
        private readonly int _maxBufferSize;
        private readonly ExtendedActorSystem _system;
        public readonly JournalRavenDbPersistence Storage;
        private readonly Akka.Serialization.Serialization _serialization;
        public readonly TimeSpan RefreshInterval;

        public RavenDbReadJournal(ExtendedActorSystem system, Config config)
        {
            _writeJournalPluginId = config.GetString("write-plugin");
            _maxBufferSize = config.GetInt("max-buffer-size"); 
            if (_maxBufferSize == 0)
                _maxBufferSize = 64 * 1024;

            _system = system;
            _serialization = system.Serialization;
            
            Storage = system.WithExtension<JournalRavenDbPersistence, JournalRavenDbPersistenceProvider>();
            RefreshInterval = config.GetTimeSpan("refresh-interval", @default: TimeSpan.FromSeconds(5));
            Storage.WaitForNonStale = config.GetBoolean("wait-for-non-stale");
        }

        public void PreStart()
        {
            new EventsByTagAndChangeVector().Execute(RavenDbPersistence.Instance, database: Storage.Database);
            new ActorsByChangeVector().Execute(RavenDbPersistence.Instance, database: Storage.Database);
        }

        public Source<string, NotUsed> PersistenceIds()
        {
            var channel = Channel.CreateBounded<string>(_maxBufferSize);
            var q = new PersistenceIds(this, channel);
            Task.Run(q.Run);

            return Source.ChannelReader(channel.Reader);
        }

        public Source<string, NotUsed> CurrentPersistenceIds()
        {
            var currentPersistenceIdsChannel = Channel.CreateBounded<string>(_maxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = Storage.OpenAsyncSession();
                    await using var results = await session.Advanced.StreamAsync(session.Query<ActorId>());
                    while (await results.MoveNextAsync())
                    {
                        var id = results.Current.Document.PersistenceId;
                        await currentPersistenceIdsChannel.Writer.WriteAsync(id);
                    }

                    currentPersistenceIdsChannel.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    currentPersistenceIdsChannel.Writer.TryComplete(e);
                }
            });

            return Source.ChannelReader(currentPersistenceIdsChannel.Reader);
        }

        public Source<EventEnvelope, NotUsed> EventsByPersistenceId(string persistenceId, long fromSequenceNr, long toSequenceNr)
        {
            var channel = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);
            var q = new EventsByPersistenceId(persistenceId, fromSequenceNr, toSequenceNr, channel, this);
            Task.Run(q.Run);

            return Source.ChannelReader(channel.Reader);
        }

        public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(string persistenceId, long fromSequenceNr, long toSequenceNr)
        {
            var currentEventsByPersistenceIdChannel = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = Storage.OpenAsyncSession();
                    session.Advanced.SessionInfo.SetContext(persistenceId);

                    await using var results = await session.Advanced.StreamAsync<Journal.Types.Event>(startsWith: RavenDbJournal.GetEventPrefix(persistenceId), startAfter: RavenDbJournal.GetSequenceId(persistenceId, fromSequenceNr - 1));
                    while (await results.MoveNextAsync())
                    {
                        var @event = results.Current.Document;
                        if (results.Current.Document.SequenceNr > toSequenceNr)
                            break;

                        var persistent = Journal.Types.Event.Deserialize(_serialization, @event, ActorRefs.NoSender);
                        var e = new EventEnvelope(new Sequence(@event.Timestamp), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                        await currentEventsByPersistenceIdChannel.Writer.WriteAsync(e);
                    }

                    currentEventsByPersistenceIdChannel.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    currentEventsByPersistenceIdChannel.Writer.TryComplete(e);
                }
            });

            return Source.ChannelReader(currentEventsByPersistenceIdChannel.Reader);
        }

        public Source<EventEnvelope, NotUsed> EventsByTag(string tag, Offset offset)
        {
            var channel = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);
            var q = new EventsByTag(tag, Offset(offset), this, channel);
            Task.Run(q.Run);

            return Source.ChannelReader(channel.Reader);
        }

        public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset)
        {
            var currentEventsByTag = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = Storage.OpenAsyncSession();
                    session.Advanced.SessionInfo.SetContext(tag);

                    var q = session.Advanced.AsyncDocumentQuery<Journal.Types.Event>(nameof(EventsByTagAndChangeVector)).ContainsAny(e => e.Tags, new[] { tag });
                    q = Offset(offset).ApplyOffset(q);

                    await using var results = await session.Advanced.StreamAsync(q);
                    while (await results.MoveNextAsync())
                    {
                        var @event = results.Current.Document;
                        var persistent = Journal.Types.Event.Deserialize(_serialization, @event, ActorRefs.NoSender);
                        var e = new EventEnvelope(new ChangeVectorOffset(results.Current.ChangeVector), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                        await currentEventsByTag.Writer.WriteAsync(e);
                    }

                    currentEventsByTag.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    currentEventsByTag.Writer.TryComplete(e);
                }
            });

            return Source.ChannelReader(currentEventsByTag.Reader);
        }

        public Source<EventEnvelope, NotUsed> AllEvents(Offset offset)
        {
            var channel = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);
            var q = new AllEvents(this, channel, Offset(offset));
            Task.Run(q.Run);

            return Source.ChannelReader(channel.Reader);
        }

        public Source<EventEnvelope, NotUsed> CurrentAllEvents(Offset offset)
        {
            var currentAllEvents = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = Storage.OpenAsyncSession();
                    var q = session.Advanced.AsyncDocumentQuery<Journal.Types.Event>(indexName: nameof(EventsByTagAndChangeVector));
                    q = Offset(offset).ApplyOffset(q);

                    await using var results = await session.Advanced.StreamAsync(q);
                    while (await results.MoveNextAsync())
                    {
                        var @event = results.Current.Document;
                        var persistent = Journal.Types.Event.Deserialize(_serialization, @event, ActorRefs.NoSender);
                        var e = new EventEnvelope(new ChangeVectorOffset(results.Current.ChangeVector), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                        await currentAllEvents.Writer.WriteAsync(e);
                    }

                    currentAllEvents.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    currentAllEvents.Writer.TryComplete(e);
                }
            });

            return Source.ChannelReader(currentAllEvents.Reader);
        }

        private static ChangeVectorOffset Offset(Offset offset) =>
            offset switch
            {
                null => new ChangeVectorOffset(string.Empty),
                NoOffset _ => new ChangeVectorOffset(string.Empty),
                Sequence { Value: 0 } => new ChangeVectorOffset(string.Empty), 
                ChangeVectorOffset cv => cv,
                _ => throw new ArgumentException($"ReadJournal does not support {offset.GetType().Name} offsets")
            };
    }
}
