using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Persistence.RavenDb.Journal;
using Akka.Persistence.RavenDb.Journal.Types;
using Akka.Persistence.RavenDb.Query.ContinuousQuery;
using Akka.Streams.Dsl;
using System.Threading.Channels;

namespace Akka.Persistence.RavenDb.Query
{
    public class RavenDbReadJournal :
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

        public readonly RavenDbPersistence Storage;
        private readonly Akka.Serialization.Serialization _serialization;
        
        private readonly RavenDbStore _store;
        private readonly IActorRef _journalRef;
        public RavenDbStore Store => _store;

        public RavenDbReadJournal(ExtendedActorSystem system, Config queryConfig)//TODO stav: need to use this config to fetch query configuration (this contains query only)
        {
            _journalRef = Persistence.Instance.Apply(system).JournalFor("");
            _serialization = system.Serialization;//TODO move to configuration class?
            Storage = system.WithExtension<RavenDbPersistence, RavenDbPersistenceProvider>();
            _store ??= new RavenDbStore(Storage.JournalConfiguration); //TODO stav: eventually remove
        }

        public class CreateIndexesMessage
        {
        }

        public void EnsureJournalCreatedIndexes()
        {
            _journalRef.Ask(new CreateIndexesMessage()).Wait();
        }

        public Source<string, NotUsed> PersistenceIds()
        {
            var channel = Channel.CreateBounded<string>(Storage.QueryConfiguration.MaxBufferSize);
            var q = new PersistenceIds(this, channel);
            Task.Run(q.Run);

            return Source.ChannelReader(channel.Reader);
        }

        public Source<string, NotUsed> CurrentPersistenceIds()
        {
            var currentPersistenceIdsChannel = Channel.CreateBounded<string>(Storage.QueryConfiguration.MaxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = _store.Instance.OpenAsyncSession();
                    using var cts = _store.GetReadCancellationTokenSource();
                    await using var results = await session.Advanced.StreamAsync(session.Query<ActorId>(), cts.Token).ConfigureAwait(false);
                    while (await results.MoveNextAsync().ConfigureAwait(false))
                    {
                        var id = results.Current.Document.PersistenceId;
                        await currentPersistenceIdsChannel.Writer.WriteAsync(id, cts.Token).ConfigureAwait(false);
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
            var channel = Channel.CreateBounded<EventEnvelope>(Storage.QueryConfiguration.MaxBufferSize);
            var q = new EventsByPersistenceId(persistenceId, fromSequenceNr, toSequenceNr, channel, this);
            Task.Run(q.Run);

            return Source.ChannelReader(channel.Reader);
        }

        public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(string persistenceId, long fromSequenceNr, long toSequenceNr)
        {
            var currentEventsByPersistenceIdChannel = Channel.CreateBounded<EventEnvelope>(Storage.QueryConfiguration.MaxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = _store.Instance.OpenAsyncSession();
                    using var cts = _store.GetReadCancellationTokenSource();
                    session.Advanced.SessionInfo.SetContext(persistenceId);

                    await using var results = await session.Advanced.StreamAsync<Journal.Types.Event>(
                        startsWith: _store.GetEventPrefix(persistenceId),
                        startAfter: _store.GetSequenceId(persistenceId, fromSequenceNr - 1),
                        token: cts.Token).ConfigureAwait(false);
                    while (await results.MoveNextAsync().ConfigureAwait(false))
                    {
                        var @event = results.Current.Document;
                        if (results.Current.Document.SequenceNr > toSequenceNr)
                            break;

                        var persistent = Journal.Types.Event.Deserialize(_serialization, @event, ActorRefs.NoSender);
                        var e = new EventEnvelope(new Sequence(@event.Timestamp), @event.PersistenceId,
                            @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                        await currentEventsByPersistenceIdChannel.Writer.WriteAsync(e, cts.Token).ConfigureAwait(false);
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
            var channel = Channel.CreateBounded<EventEnvelope>(Storage.QueryConfiguration.MaxBufferSize);
            var q = new EventsByTag(tag, ChangeVectorOffset.Convert(offset), this, channel);
            Task.Run(q.Run);

            return Source.ChannelReader(channel.Reader);
        }

        public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset)
        {
            var currentEventsByTag = Channel.CreateBounded<EventEnvelope>(Storage.QueryConfiguration.MaxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = _store.Instance.OpenAsyncSession();
                    session.Advanced.SessionInfo.SetContext(tag);

                    var q = session.Advanced.AsyncDocumentQuery<Journal.Types.Event>(nameof(EventsByTagAndChangeVector)).ContainsAny(e => e.Tags, new[] { tag });
                    q = ChangeVectorOffset.Convert(offset).ApplyOffset(q);

                    using var cts = _store.GetReadCancellationTokenSource();
                    await using var results = await session.Advanced.StreamAsync(q, cts.Token).ConfigureAwait(false);
                    while (await results.MoveNextAsync().ConfigureAwait(false))
                    {
                        var @event = results.Current.Document;
                        var persistent = Journal.Types.Event.Deserialize(_serialization, @event, ActorRefs.NoSender);
                        var e = new EventEnvelope(new ChangeVectorOffset(results.Current.ChangeVector), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                        await currentEventsByTag.Writer.WriteAsync(e, cts.Token).ConfigureAwait(false);
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
            var channel = Channel.CreateBounded<EventEnvelope>(Storage.QueryConfiguration.MaxBufferSize);
            var q = new AllEvents(this, channel, ChangeVectorOffset.Convert(offset));
            Task.Run(q.Run);

            return Source.ChannelReader(channel.Reader);
        }

        public Source<EventEnvelope, NotUsed> CurrentAllEvents(Offset offset)
        {
            var currentAllEvents = Channel.CreateBounded<EventEnvelope>(Storage.QueryConfiguration.MaxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = _store.Instance.OpenAsyncSession();
                    var q = session.Advanced.AsyncDocumentQuery<Journal.Types.Event>(indexName: nameof(EventsByTagAndChangeVector));
                    q = ChangeVectorOffset.Convert(offset).ApplyOffset(q);

                    using var cts = _store.GetReadCancellationTokenSource();
                    await using var results = await session.Advanced.StreamAsync(q, cts.Token).ConfigureAwait(false);
                    while (await results.MoveNextAsync().ConfigureAwait(false))
                    {
                        var @event = results.Current.Document;
                        var persistent = Journal.Types.Event.Deserialize(_serialization, @event, ActorRefs.NoSender);
                        var e = new EventEnvelope(new ChangeVectorOffset(results.Current.ChangeVector), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                        await currentAllEvents.Writer.WriteAsync(e, cts.Token).ConfigureAwait(false);
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
    }
}
