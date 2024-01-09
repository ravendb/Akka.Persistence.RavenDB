using System.Text.RegularExpressions;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;
using Akka.Streams.Dsl;
using System.Threading.Channels;
using Akka.Persistence.RavenDB.Journal;
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

        /// <inheritdoc />
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

            var subscription = RavenDbPersistence.Instance.Subscriptions.Update(new SubscriptionUpdateOptions
            {
                Query = "from UniqueActors",
                Name = $"PersistenceIds/{_system.Name}",
                CreateNew = true
            }, _ravendb.Database);

            var worker = RavenDbPersistence.Instance.Subscriptions.GetSubscriptionWorker<RavenDbJournal.UniqueActor>(subscription, _ravendb.Database);
            worker.Run(async batch =>
            {
                foreach (var item in batch.Items)
                {
                    await persistenceIdsChannel.Writer.WriteAsync(item.Result.PersistenceId);
                }
            }).ContinueWith(t=>
            {
                try
                {
                    t.GetAwaiter().GetResult(); // already completed
                }
                catch (Exception e)
                {
                    persistenceIdsChannel.Writer.TryComplete(e);
                    return;
                }

                persistenceIdsChannel.Writer.TryComplete();
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

                    var results = await session.Advanced.StreamAsync(session.Query<RavenDbJournal.UniqueActor>());
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
            var subscription = RavenDbPersistence.Instance.Subscriptions.Update(new SubscriptionUpdateOptions
            {
                Query = $"from JournalEvents where startsWith(id(),'{RavenDbJournal.GetEventPrefix(persistenceId)}') AND SequenceNr >= {fromSequenceNr} AND SequenceNr <= {toSequenceNr}",
                Name = $"EventsByPersistenceId/{persistenceId}/{fromSequenceNr}/{toSequenceNr}/{_system.Name}",
                CreateNew = true
            }, _ravendb.Database);

            var worker = RavenDbPersistence.Instance.Subscriptions.GetSubscriptionWorker<RavenDbJournal.JournalEvent>(subscription, _ravendb.Database);
            worker.Run(async batch =>
            {
                foreach (var item in batch.Items)
                {
                    var @event = item.Result;
                    var persistent = RavenDbJournal.JournalEvent.Deserialize(_serializer, @event, ActorRefs.NoSender);
                    var e = new EventEnvelope(new ChangeVectorOffset(item.ChangeVector), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                    await eventsByPersistenceId.Writer.WriteAsync(e);
                    
                    if (e.SequenceNr == toSequenceNr)
                    {
                        await worker.DisposeAsync(waitForSubscriptionTask: false);
                    }
                }
            }).ContinueWith(t=>
            {
                try
                {
                    t.GetAwaiter().GetResult(); // already completed
                }
                catch (Exception e)
                {
                    eventsByPersistenceId.Writer.TryComplete(e);
                    return;
                }

                eventsByPersistenceId.Writer.TryComplete();
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
                    var results = await session.Advanced.StreamAsync<RavenDbJournal.JournalEvent>(startsWith: RavenDbJournal.GetEventPrefix(persistenceId), startAfter: RavenDbJournal.GetSequenceId(persistenceId, fromSequenceNr));
                    while (await results.MoveNextAsync())
                    {
                        var @event = results.Current.Document;
                        if (results.Current.Document.SequenceNr > toSequenceNr)
                            break;

                        var persistent = RavenDbJournal.JournalEvent.Deserialize(_serializer, @event, ActorRefs.NoSender);
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

            var subscription = RavenDbPersistence.Instance.Subscriptions.Update(new SubscriptionUpdateOptions
            {
                Query = $"from JournalEvents as j where j.Tags.includes('{tag}')",
                Name = $"EventsByTag/{tag}/{_system.Name}",
                CreateNew = true,
                ChangeVector = Offset(offset)
            }, _ravendb.Database);

            var worker = RavenDbPersistence.Instance.Subscriptions.GetSubscriptionWorker<RavenDbJournal.JournalEvent>(subscription, _ravendb.Database);
            worker.Run(async batch =>
            {
                foreach (var item in batch.Items)
                {
                    var @event = item.Result;
                    var persistent = RavenDbJournal.JournalEvent.Deserialize(_serializer, @event, ActorRefs.NoSender);
                    var e = new EventEnvelope(new ChangeVectorOffset(item.ChangeVector), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                    await eventByTag.Writer.WriteAsync(e);
                }
            }).ContinueWith(t=>
            {
                try
                {
                    t.GetAwaiter().GetResult(); // already completed
                }
                catch (Exception e)
                {
                    eventByTag.Writer.TryComplete(e);
                    return;
                }

                eventByTag.Writer.TryComplete();
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
                    var q = session.Query<RavenDbJournal.JournalEvent>();
                    var results = await session.Advanced.StreamAsync(q.Where(j => j.Tags.Contains(tag)));
                    while (await results.MoveNextAsync())
                    {
                        var @event = results.Current.Document;
                        var currentChangeVectorOffset = new ChangeVectorOffset(results.Current.ChangeVector);

                        if (currentChangeVectorOffset.CompareTo(offset) <= 0)
                            continue;

                        var persistent = RavenDbJournal.JournalEvent.Deserialize(_serializer, @event, ActorRefs.NoSender);
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

            var subscription = RavenDbPersistence.Instance.Subscriptions.Update(new SubscriptionUpdateOptions
            {
                Query = $"from JournalEvents",
                Name = $"AllEvents/{_system.Name}",
                CreateNew = true,
                ChangeVector = Offset(offset)
            }, _ravendb.Database);

            var worker = RavenDbPersistence.Instance.Subscriptions.GetSubscriptionWorker<RavenDbJournal.JournalEvent>(subscription, _ravendb.Database);
            worker.Run(async batch =>
            {
                foreach (var item in batch.Items)
                {
                    var @event = item.Result;
                    var persistent = RavenDbJournal.JournalEvent.Deserialize(_serializer, @event, ActorRefs.NoSender);
                    var e = new EventEnvelope(new ChangeVectorOffset(item.ChangeVector), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp, @event.Tags);
                    await allEventsChannel.Writer.WriteAsync(e);
                }
            }).ContinueWith(t=>
            {
                try
                {
                    t.GetAwaiter().GetResult(); // already completed
                }
                catch (Exception e)
                {
                    allEventsChannel.Writer.TryComplete(e);
                    return;
                }

                allEventsChannel.Writer.TryComplete();
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
                    var results = await session.Advanced.StreamAsync(session.Query<RavenDbJournal.JournalEvent>());
                    while (await results.MoveNextAsync())
                    {
                        var @event = results.Current.Document;
                        var currentChangeVectorOffset = new ChangeVectorOffset(results.Current.ChangeVector);
                        if (currentChangeVectorOffset.CompareTo(offset) <= 0)
                            continue;

                        var persistent = RavenDbJournal.JournalEvent.Deserialize(_serializer, @event, ActorRefs.NoSender);
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
                _ => throw new ArgumentException($"ReadJournal does not support {offset.GetType().Name} offsets")
            };

        public class ChangeVectorOffset : Offset
        {
            private readonly string _changeVector;

            public ChangeVectorOffset(string changeVector)
            {
                _changeVector = changeVector;
            }

            public override int CompareTo(Offset other)
            {
                if (other == null)
                    return 1;

                if (ReferenceEquals(this, other))
                    return 0;

                if (other is NoOffset)
                {
                    if (_changeVector == null)
                        return 0;

                    return 1;
                }

                if (other is Sequence { Value: 0 }) 
                    return 1;

                if (other is ChangeVectorOffset changeVectorOffset == false)
                    throw new InvalidOperationException($"Can't compare {other.GetType()} with {GetType()}");

                return (int)(Etag - changeVectorOffset.Etag);
            }

            private long? _etag;
            public long Etag => _etag ??= long.Parse(EtagMatcher.Match(_changeVector).Groups[1].Value);

            private static Regex EtagMatcher => new Regex(@"\w{1,4}:(\d+)-.{22}", RegexOptions.Compiled);
            public override string ToString() => _changeVector;
        }
    }
}
