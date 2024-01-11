using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams.Dsl;
using System.Threading.Channels;
using Akka.Persistence.RavenDB.Journal;
using Akka.Persistence.RavenDB.Journal.Types;
using Akka.Persistence.Serialization;
using Raven.Client.Documents.Subscriptions;

namespace Akka.Persistence.RavenDB.Query
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

        private readonly string _writeJournalPluginId;
        private readonly int _maxBufferSize;
        private readonly ExtendedActorSystem _system;
        private readonly JournalRavenDbPersistence _ravendb;
        private readonly PersistenceMessageSerializer _serializer;

        public RavenDbReadJournal(ExtendedActorSystem system, Config config)
        {
            _writeJournalPluginId = config.GetString("write-plugin");
            _maxBufferSize = config.GetInt("max-buffer-size"); 
            if (_maxBufferSize == 0)
                _maxBufferSize = 64 * 1024;

            _system = system;
            _serializer = new PersistenceMessageSerializer(system);
            _ravendb = new JournalRavenDbPersistence(system);
        }

        public Source<string, NotUsed> PersistenceIds()
        {
            var persistenceIdsChannel = Channel.CreateBounded<string>(_maxBufferSize);
            var subscription = RavenDbPersistence.Instance.Subscriptions.Create(new SubscriptionCreationOptions<ActorId>
            {
                Name = $"{_system.Name}/PersistenceIds/{Guid.NewGuid()}",
            }, _ravendb.Database);

            var worker = RavenDbPersistence.Instance.Subscriptions.GetSubscriptionWorker<ActorId>(subscription, _ravendb.Database);
            worker.Run(async batch =>
            {
                foreach (var item in batch.Items)
                {
                    await persistenceIdsChannel.Writer.WriteAsync(item.Result.PersistenceId);
                }
            }).ContinueWith(async t=>
            {
                try
                {
                    await t; // already completed
                    persistenceIdsChannel.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    persistenceIdsChannel.Writer.TryComplete(e);
                }

                await RavenDbPersistence.Instance.Subscriptions.DeleteAsync(subscription, _ravendb.Database);
            });

            return Source.ChannelReader(persistenceIdsChannel.Reader);
        }

        public Source<string, NotUsed> CurrentPersistenceIds()
        {
            var currentPersistenceIdsChannel = Channel.CreateBounded<string>(_maxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = _ravendb.OpenAsyncSession();
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
            var eventsByPersistenceId = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);
            var prefix = RavenDbJournal.GetEventPrefix(persistenceId);
            var subscription = RavenDbPersistence.Instance.Subscriptions.Create(new SubscriptionCreationOptions<Journal.Types.Event>
            {
                Filter = e => e.Id.StartsWith(prefix) && e.SequenceNr >= fromSequenceNr && e.SequenceNr <= toSequenceNr,
                Name = $"{_system.Name}/EventsByPersistenceId/{persistenceId}/{Guid.NewGuid()}",
            }, _ravendb.Database);

            var worker = RavenDbPersistence.Instance.Subscriptions.GetSubscriptionWorker<Journal.Types.Event>(subscription, _ravendb.Database);
            worker.Run(async batch =>
            {
                foreach (var item in batch.Items)
                {
                    var @event = item.Result;
                    var persistent = Journal.Types.Event.Deserialize(_serializer, @event, ActorRefs.NoSender);
                    var e = new EventEnvelope(new ChangeVectorOffset(item.ChangeVector), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                    await eventsByPersistenceId.Writer.WriteAsync(e);
                    
                    if (e.SequenceNr == toSequenceNr)
                    {
                        await worker.DisposeAsync(waitForSubscriptionTask: false);
                    }
                }
            }).ContinueWith(async t=>
            {
                try
                {
                    await t; // already completed
                    eventsByPersistenceId.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    eventsByPersistenceId.Writer.TryComplete(e);
                }

                await RavenDbPersistence.Instance.Subscriptions.DeleteAsync(subscription, _ravendb.Database);
            });

            return Source.ChannelReader(eventsByPersistenceId.Reader);
        }

        public Source<EventEnvelope, NotUsed> CurrentEventsByPersistenceId(string persistenceId, long fromSequenceNr, long toSequenceNr)
        {
            var currentEventsByPersistenceIdChannel = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = _ravendb.OpenAsyncSession();
                    session.Advanced.SessionInfo.SetContext(persistenceId);

                    await using var results = await session.Advanced.StreamAsync<Journal.Types.Event>(startsWith: RavenDbJournal.GetEventPrefix(persistenceId), startAfter: RavenDbJournal.GetSequenceId(persistenceId, fromSequenceNr));
                    while (await results.MoveNextAsync())
                    {
                        var @event = results.Current.Document;
                        if (results.Current.Document.SequenceNr > toSequenceNr)
                            break;

                        var persistent = Journal.Types.Event.Deserialize(_serializer, @event, ActorRefs.NoSender);
                        var e = new EventEnvelope(new ChangeVectorOffset(results.Current.ChangeVector), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
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
            var eventByTag = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);

            var subscription = RavenDbPersistence.Instance.Subscriptions.Create(new SubscriptionCreationOptions<Journal.Types.Event>
            {
                Filter = e => e.Tags.Contains(tag),
                Name = $"{_system.Name}/EventsByTag/{tag}/{Guid.NewGuid()}",
                ChangeVector = Offset(offset)
            }, _ravendb.Database);

            var worker = RavenDbPersistence.Instance.Subscriptions.GetSubscriptionWorker<Journal.Types.Event>(subscription, _ravendb.Database);
            worker.Run(async batch =>
            {
                foreach (var item in batch.Items)
                {
                    var @event = item.Result;
                    var persistent = Journal.Types.Event.Deserialize(_serializer, @event, ActorRefs.NoSender);
                    var e = new EventEnvelope(new ChangeVectorOffset(item.ChangeVector), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                    await eventByTag.Writer.WriteAsync(e);
                }
            }).ContinueWith(async t=>
            {
                try
                {
                    await t;
                    eventByTag.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    eventByTag.Writer.TryComplete(e);
                }

                await RavenDbPersistence.Instance.Subscriptions.DeleteAsync(subscription, _ravendb.Database);
            });

            return Source.ChannelReader(eventByTag.Reader);
        }

        public Source<EventEnvelope, NotUsed> CurrentEventsByTag(string tag, Offset offset)
        {
            var currentEventsByTag = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = _ravendb.OpenAsyncSession();
                    var q = session.Query<Journal.Types.Event>();
                    await using var results = await session.Advanced.StreamAsync(q.Where(j => j.Tags.Contains(tag)));
                    while (await results.MoveNextAsync())
                    {
                        var @event = results.Current.Document;
                        var currentChangeVectorOffset = new ChangeVectorOffset(results.Current.ChangeVector);

                        if (currentChangeVectorOffset.CompareTo(offset) <= 0)
                            continue;

                        var persistent = Journal.Types.Event.Deserialize(_serializer, @event, ActorRefs.NoSender);
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
            var allEventsChannel = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);

            var subscription = RavenDbPersistence.Instance.Subscriptions.Create(new SubscriptionCreationOptions<Journal.Types.Event>
            {
                Name = $"{_system.Name}/AllEvents/{Guid.NewGuid()}",
                ChangeVector = Offset(offset)
            }, _ravendb.Database);

            var worker = RavenDbPersistence.Instance.Subscriptions.GetSubscriptionWorker<Journal.Types.Event>(subscription, _ravendb.Database);
            worker.Run(async batch =>
            {
                foreach (var item in batch.Items)
                {
                    var @event = item.Result;
                    var persistent = Journal.Types.Event.Deserialize(_serializer, @event, ActorRefs.NoSender);
                    var e = new EventEnvelope(new ChangeVectorOffset(item.ChangeVector), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                    await allEventsChannel.Writer.WriteAsync(e);
                }
            }).ContinueWith(async t=>
            {
                try
                {
                    await t;// already completed
                    allEventsChannel.Writer.TryComplete();
                }
                catch (Exception e)
                {
                    allEventsChannel.Writer.TryComplete(e);
                }

                await RavenDbPersistence.Instance.Subscriptions.DeleteAsync(subscription, _ravendb.Database);
            });

            return Source.ChannelReader(allEventsChannel.Reader);
        }

        public Source<EventEnvelope, NotUsed> CurrentAllEvents(Offset offset)
        {
            var currentAllEvents = Channel.CreateBounded<EventEnvelope>(_maxBufferSize);
            Task.Run(async () =>
            {
                try
                {
                    using var session = _ravendb.OpenAsyncSession();
                    await using var results = await session.Advanced.StreamAsync(session.Query<Journal.Types.Event>());
                    while (await results.MoveNextAsync())
                    {
                        var @event = results.Current.Document;
                        var currentChangeVectorOffset = new ChangeVectorOffset(results.Current.ChangeVector);
                        if (currentChangeVectorOffset.CompareTo(offset) <= 0)
                            continue;

                        var persistent = Journal.Types.Event.Deserialize(_serializer, @event, ActorRefs.NoSender);
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

        private static string Offset(Offset offset) =>
            offset switch
            {
                null => null,
                NoOffset _ => null,
                ChangeVectorOffset changeVector => changeVector.ToString(),
                Sequence seq => seq.Value == 0 ? null : throw new ArgumentException($"ReadJournal does not support {offset.GetType().Name} with offset other than zero."),
                _ => throw new ArgumentException($"ReadJournal does not support {offset.GetType().Name} offsets")
            };
    }
}
