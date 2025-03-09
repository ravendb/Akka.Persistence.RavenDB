using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.RavenDb.Journal.Types;
using Akka.Persistence.RavenDb.Query;
using Akka.Persistence.RavenDb.Query.ContinuousQuery;
using Nito.AsyncEx;
using Raven.Client.Documents.Session;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Akka.Persistence.RavenDb.Journal
{
    public class RavenDbJournal : AsyncWriteJournal, IWithUnboundedStash
    {
        private static readonly RavenDbPersistence Persistence = Context.System.WithExtension<RavenDbPersistence, RavenDbPersistenceProvider>();
        private readonly RavenDbJournalConfiguration _configuration;
        private readonly Akka.Serialization.Serialization _serialization;
        private readonly RavenDbStore _store;
        
        //requests for the highest sequence number may be made concurrently to writes executing for the same persistenceId.
        private readonly ConcurrentDictionary<string, AsyncReaderWriterLock> _lockPerActor = new ConcurrentDictionary<string, AsyncReaderWriterLock>();
        public RavenDbJournal() : this(RavenDbPersistence.Get(Context.System).JournalConfiguration)
        {
        }

        // This constructor is needed because config can come from both Akka.Persistence and Akka.Cluster.Sharding
        public RavenDbJournal(Config config) : this(new RavenDbJournalConfiguration(config, Context.System))
        {
        }

        private RavenDbJournal(RavenDbJournalConfiguration configuration)
        {
            _configuration = configuration;
            _serialization = Context.System.Serialization;
            _store = new RavenDbStore(_configuration);
        }

        public async Task<object> Initialize()
        {
            if (_configuration.AutoInitialize)
            {
                return await _store.CreateDatabaseAsync().ConfigureAwait(false);
            }

            return new Status.Success(NotUsed.Instance);
        }

        protected override void PreStart()
        {
            base.PreStart();

            // Call the Initialize method and pipe the result back to signal that
            // the database is ready to use, if it needs to be initialized
            Initialize().PipeTo(Self);

            // WaitingForInitialization receive handler will wait for a success/fail
            // result back from the Initialize method
            BecomeStacked(WaitingForInitialization);
        }

        public IStash Stash { get; set; }

        private bool WaitingForInitialization(object message)
        {
            switch (message)
            {
                // Database is already created or successfully created all needed databases
                case Status.Success _:
                    UnbecomeStacked();
                    // Unstash all messages received while we were initializing our database
                    Stash.UnstashAll();
                    break;

                case Status.Failure fail:
                    // Failed creating database. Log an error and stop the actor.
                    //_log.Error(fail.Cause, "Failure during {0} initialization.", Self);
                    Context.Stop(Self);
                    break;

                default:
                    // By default, stash all received messages while we're waiting for the
                    // Initialize method.
                    Stash.Stash();
                    break;
            }
            return true;
        }

        protected override bool ReceivePluginInternal(object message)
        {
            switch (message)
            {
                case RavenDbReadJournal.CreateIndexesMessage create:
                    var sender = Sender;
                    _store.EnsureIndexesCreatedAsync().PipeTo(sender);
                    return true;
                
                default:
                    return false;
            }
        }

        protected override void PostStop()
        {
            _store.Stop();
            _store.Dispose();
            base.PostStop();
        }

        public override async Task ReplayMessagesAsync(
            IActorContext context, 
            string persistenceId, 
            long fromSequenceNr, 
            long toSequenceNr, 
            long max, 
            Action<IPersistentRepresentation> recoveryCallback)
        {
            using var session = _store.Instance.OpenAsyncSession();
            using var cts = _store.GetReadCancellationTokenSource(TimeSpan.FromMinutes(5));

            session.Advanced.SessionInfo.SetContext(persistenceId);
            var node = await session.Advanced.GetCurrentSessionNode().ConfigureAwait(false);
            var prefix = _store.GetEventPrefix(persistenceId);

            // metadata document will indicate what is the possible current max sequence on this node
            var currentMetadata = await session.LoadAsync<EventMetadata>(_store.GetEventMetadataId(persistenceId), cts.Token).ConfigureAwait(false);
            var currentMax = currentMetadata.MaxSequenceNr;

            // when running in a cluster we can connect to a node that doesn't have all events just yet.
            // so we subscribe to the node to get notified when new events for this actor will arrive
            var waiter = new AsyncManualResetEvent();
            var changes = _store.Instance.Changes(_store.Instance.Database, node.ClusterTag);
            await changes.EnsureConnectedNow().ConfigureAwait(false);

            var prefixChanges = changes.ForDocumentsStartingWith(prefix);
            using var subscribe = prefixChanges.Subscribe(_ => waiter.Set());
            await prefixChanges.EnsureSubscribedNow().ConfigureAwait(false);

            while (true)
            {
                await using var results = await session.Advanced.StreamAsync<Types.Event>(startsWith: _store.GetEventPrefix(persistenceId),
                    startAfter: _store.GetEventSequenceId(persistenceId, fromSequenceNr - 1), token: cts.Token).ConfigureAwait(false);
                while (await results.MoveNextAsync().ConfigureAwait(false))
                {
                    if (max <= 0)
                        return;

                    var message = results.Current.Document;
                    if (message.SequenceNr > toSequenceNr)
                        return;

                    var persistent = Types.Event.Deserialize(_serialization, message, context.Sender);
                    recoveryCallback(persistent);
                    
                    max--;
                    if (message.SequenceNr == toSequenceNr)
                        return;

                    fromSequenceNr = message.SequenceNr;
                }

                // no more events could be found for this actor
                if (currentMax >= toSequenceNr)
                    return;

                await waiter.WaitAsync(cts.Token).ConfigureAwait(false);
                waiter.Reset();
            }
        }

        // we read the metadata document from all member nodes in order to ensure recovery to the latest state
        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            using (await GetLocker(persistenceId).WriterLockAsync().ConfigureAwait(false))
            {
                using var session = _store.Instance.OpenAsyncSession();
                using var cts = _store.GetReadCancellationTokenSource();
                session.Advanced.SessionInfo.SetContext(persistenceId);
                session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);

                var id = _store.GetEventMetadataId(persistenceId);
                var compareExchange = await session.LoadAsync<EventMetadata>(id, cts.Token).ConfigureAwait(false);
                return compareExchange?.MaxSequenceNr ?? 0;
            }
        }

        protected override async Task<IImmutableList<Exception?>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var builder = ImmutableList.CreateBuilder<Exception?>();
            using var cts = _store.GetWriteCancellationTokenSource();
            var writes = new Dictionary<string, Task>();
            var original = messages.ToList();

            // we can have multiple atomic writes with the _same_ actor :(
            foreach (var atomicWrites in original.GroupBy(m => m.PersistenceId))
            {
                var t = AtomicWriteForActor(atomicWrites, cts);
                writes.Add(atomicWrites.Key, t);
            }

            foreach (var atomicWrite in original)
            {
                try
                {
                    await writes[atomicWrite.PersistenceId].ConfigureAwait(false); // unwrap the exception if needed
                    builder.Add(null);
                }
                catch (Exception e)
                {
                    builder.Add(e);
                }
            }
           
            return builder.ToImmutable();
        }

        // we group multiple atomic writes for a given actor into a single atomic write
        private async Task AtomicWriteForActor(IGrouping<string, AtomicWrite> atomicWrites, CancellationTokenSource cts)
        {
            var persistenceId = atomicWrites.Key;
            using var _ = await GetLocker(persistenceId).ReaderLockAsync(cts.Token).ConfigureAwait(false);
            using var session = _store.Instance.OpenAsyncSession();

            if (Persistence.QueryConfiguration.WaitForNonStale) // used for tests
                session.Advanced.WaitForIndexesAfterSaveChanges();

            session.Advanced.SessionInfo.SetContext(persistenceId);
            session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);

            var highest = long.MinValue;
            var lowest = long.MaxValue;

            foreach (var atomicWrite in atomicWrites)
            {
                lowest = Math.Min(lowest, atomicWrite.LowestSequenceNr);
                highest = Math.Max(highest, atomicWrite.HighestSequenceNr);

                var payload = (IImmutableList<IPersistentRepresentation>)atomicWrite.Payload;

                foreach (var representation in payload)
                {
                    var id = _store.GetEventSequenceId(representation.PersistenceId, representation.SequenceNr);
                    var journalEvent = Types.Event.Serialize(_serialization, representation);
                    await session.StoreAsync(journalEvent, id, cts.Token).ConfigureAwait(false);
                }
            }

            var metadataId = _store.GetEventMetadataId(persistenceId);
            var metadata = await session.LoadAsync<EventMetadata>(metadataId, cts.Token).ConfigureAwait(false);
            if (metadata == null)
            {
                await session.StoreAsync(new UniqueActor
                    {
                        PersistenceId = persistenceId,
                    }, $"UniqueActors/{persistenceId}", cts.Token).ConfigureAwait(false);

                metadata = new EventMetadata
                {
                    PersistenceId = persistenceId,
                    MaxSequenceNr = highest
                };
                await session.StoreAsync(metadata, metadataId, cts.Token).ConfigureAwait(false);
            }
            metadata.Timestamp = DateTime.UtcNow;
            metadata.MaxSequenceNr = highest;

            await session.SaveChangesAsync(cts.Token).ConfigureAwait(false);
        }

        protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            var batch = 1024;
            int deleted;
            do
            {
                deleted = 0;
                using var readCts = _store.GetReadCancellationTokenSource();
                using var session = _store.Instance.OpenAsyncSession();
                
                session.Advanced.SessionInfo.SetContext(persistenceId);
                session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);

                await using var results = await session.Advanced
                    .StreamAsync<Types.Event>(startsWith: _store.GetEventPrefix(persistenceId), pageSize: batch, token: readCts.Token).ConfigureAwait(false);
                while (await results.MoveNextAsync().ConfigureAwait(false))
                {
                    var current = results.Current.Document;
                    if (current.SequenceNr > toSequenceNr)
                        break;

                    deleted++;
                    session.Delete(current.Id);
                }

                using var writeCts = _store.GetReadCancellationTokenSource();
                await session.SaveChangesAsync(writeCts.Token).ConfigureAwait(false);
            } while (deleted == batch);
        }

        private AsyncReaderWriterLock GetLocker(string persistenceId) => _lockPerActor.GetOrAdd(persistenceId, new AsyncReaderWriterLock());
    }
}
