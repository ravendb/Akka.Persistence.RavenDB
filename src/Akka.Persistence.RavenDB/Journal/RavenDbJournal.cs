using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Journal;
using Akka.Persistence.RavenDb.Journal.Types;
using Akka.Persistence.RavenDb.Query;
using Nito.AsyncEx;
using Raven.Client.Documents.Commands.Batches;
using Raven.Client.Documents.Operations;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using Raven.Client.Exceptions;
using Akka.Persistence.RavenDb.Query.ContinuousQuery;
using Raven.Client.Documents.Session;

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
        public RavenDbJournal(Config config) : this(new RavenDbJournalConfiguration(config))
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
            var currentMetadata = await session.LoadAsync<Metadata>(_store.GetMetadataId(persistenceId), cts.Token).ConfigureAwait(false);
            var currentMax = currentMetadata.MaxSequenceNr;

            // when running in a cluster we can connect to a node that doesn't have all events just yet.
            // so we subscribe to the node to get notified when new events for this actor will arrive
            var waiter = new AsyncManualResetEvent();
            var changes = _store.Instance.Changes(_store.Instance.Database, node.ClusterTag).ForDocumentsStartingWith(prefix);
            using var prefixChanges = changes.Subscribe(_ => waiter.Set());

            await changes.EnsureSubscribedNow().ConfigureAwait(false);

            while (true)
            {
                await using var results = await session.Advanced.StreamAsync<Types.Event>(startsWith: _store.GetEventPrefix(persistenceId),
                    startAfter: _store.GetSequenceId(persistenceId, fromSequenceNr - 1), token: cts.Token).ConfigureAwait(false);
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
                var maxSequenceNr = 0L;
                var id = _store.GetMetadataId(persistenceId);
                var topology = await _store.GetTopologyAsync().ConfigureAwait(false);
                foreach (var node in topology.Nodes)
                {
                    var doc = await _store.Instance.Maintenance.SendAsync(new GetDocumentOperation<Metadata>(id, node.ClusterTag), cts.Token).ConfigureAwait(false);
                    if (doc == null)
                        continue;
                   
                    maxSequenceNr = Math.Max(maxSequenceNr, doc.MaxSequenceNr);
                }

                return maxSequenceNr;
            }
        }

        

        /// <summary>
        /// Each <see cref="AtomicWrite"/> message will be unwrapped and create one document per <see cref="IPersistentRepresentation"/> with the following ID format 'Events/[PersistenceId]/[SequenceNr]'<br/>
        /// All those documents will be persisted atomically in a single transaction.<para/>
        /// 
        /// As part of the transaction will also update a metadata document for the given actor with the ID of 'Metadatas/[PersistenceId]', which we use for concurrency check and keep the latest SequenceNr <br/>  
        /// For a newly created actor we will also create an immutable document with the ID 'UniqueActors/[PersistenceId]' that we use for <see cref="RavenDbReadJournal.PersistenceIds"/> and <see cref="RavenDbReadJournal.CurrentPersistenceIds"/> queries.<para/>
        /// 
        /// It may happen that two writes will go to different nodes, to ensure gap less sequence of events we check on the server-side and allow to persist only the next sequence events (if latest is 100, so we can persist only 101), otherwise we will throw <see cref="ConcurrencyException"/> 
        /// </summary>
        /// <param name="messages"></param>
        /// <exception cref="ConcurrencyException"></exception>
        /// <returns></returns>
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
            session.Advanced.SessionInfo.SetContext(persistenceId);
            // TODO stav: might need to set transaction mode here
            // session.Advanced.SetTransactionMode(mode);

            var highest = long.MinValue;
            var lowest = long.MaxValue;

            foreach (var atomicWrite in atomicWrites)
            {
                lowest = Math.Min(lowest, atomicWrite.LowestSequenceNr);
                highest = Math.Max(highest, atomicWrite.HighestSequenceNr);

                var payload = (IImmutableList<IPersistentRepresentation>)atomicWrite.Payload;

                foreach (var representation in payload)
                {
                    var id = _store.GetSequenceId(representation.PersistenceId, representation.SequenceNr);
                    var journalEvent = Types.Event.Serialize(_serialization, representation);
                    // events are immutable and should always be new 
                    await session.StoreAsync(journalEvent, changeVector: string.Empty, id, cts.Token).ConfigureAwait(false);
                }
            }

            var metadataId = _store.GetMetadataId(persistenceId);
            session.Advanced.Defer(new PatchCommandData(metadataId, changeVector: null, patch: new PatchRequest
            {
                Script = Metadata.UpdateScript,
                Values = new Dictionary<string, object>
                {
                    [nameof(Metadata.MaxSequenceNr)] = highest,
                    ["check"] = lowest - 1,
                }
            }, new PatchRequest
            {
                Script = Metadata.CreateNewScript,
                Values = new Dictionary<string, object>
                {
                    [nameof(Metadata.PersistenceId)] = persistenceId,
                    [nameof(Metadata.MaxSequenceNr)] = highest,
                    ["collection"] = _store.EventsMetadataCollection,
                    ["type"] = _store.Instance.Conventions.FindClrTypeName(typeof(Metadata)),
                    ["collection2"] = _store.Instance.Conventions.FindCollectionName(typeof(ActorId)),
                    ["type2"] = _store.Instance.Conventions.FindClrTypeName(typeof(ActorId))
                }
            }));

            if (Persistence.QueryConfiguration.WaitForNonStale) // used for tests
                session.Advanced.WaitForIndexesAfterSaveChanges();

            _store.SetConsistencyLevel(_configuration, session);

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
                await using var results = await session.Advanced.StreamAsync<Types.Event>(startsWith: _store.GetEventPrefix(persistenceId), pageSize: batch, token: readCts.Token).ConfigureAwait(false);
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
