using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Snapshot;
using Raven.Client.Documents.Session;

namespace Akka.Persistence.RavenDb.Snapshot
{
    public class RavenDbSnapshotStore : SnapshotStore, IWithUnboundedStash
    {
        private readonly RavenDbSnapshotConfiguration _configuration;
        private readonly Akka.Serialization.Serialization _serialization;
        private readonly RavenDbStore _store;
        private readonly IActorRef _journalRef;

        public RavenDbSnapshotStore() : this(RavenDbPersistence.Get(Context.System).SnapshotConfiguration)
        {
        }

        public RavenDbSnapshotStore(Config config) : this(new RavenDbSnapshotConfiguration(config))
        {
        }

        public RavenDbSnapshotStore(RavenDbSnapshotConfiguration configuration)
        {
            _configuration = configuration;
            _serialization = Context.System.Serialization;
            _journalRef = Persistence.Instance.Apply(Context.System).JournalFor("");
            _store = new RavenDbStore(_configuration);
        }

        public async Task<object> Initialize()
        {
            if (_configuration.AutoInitialize)
            {
                return await _store.CreateDatabaseAsync();
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

        protected override void PostStop()
        {
            _store.Stop();
            _store.Dispose();
            base.PostStop();
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            // TODO: add API to scan backwards
            using var session = _store.Instance.OpenAsyncSession();
            using var cts = _store.GetReadCancellationTokenSource();
            session.Advanced.SessionInfo.SetContext(persistenceId);

            await using var results = await session.Advanced.StreamAsync<Snapshot>(startsWith: GetSnapshotPrefix(persistenceId), startAfter: GetSnapshotId(persistenceId, criteria.MinSequenceNr - 1), token: cts.Token);
            Snapshot lastValidSnapshot = null; // yuck!
            while (await results.MoveNextAsync()) 
            {
                var current = results.Current.Document;
                var isBetween = criteria.MinTimestamp <= current.Timestamp && criteria.MaxTimeStamp >= current.Timestamp; 
                if (current.SequenceNr <= criteria.MaxSequenceNr && isBetween)
                {
                    lastValidSnapshot = results.Current.Document;
                }
            }

            if (lastValidSnapshot == null)
                return null;

            return lastValidSnapshot.ToSelectedSnapshot(_serialization);
        }

        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            var id = GetSnapshotId(metadata);
            using var session = _store.Instance.OpenAsyncSession();
            session.Advanced.SessionInfo.SetContext(metadata.PersistenceId);

            using var cts = _store.GetWriteCancellationTokenSource();
            await session.StoreAsync(Snapshot.Serialize(_serialization, metadata, snapshot), id, cts.Token);

            _store.SetConsistencyLevel(_configuration, session);

            await session.SaveChangesAsync(cts.Token);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            var id = GetSnapshotId(metadata);
            using var session = _store.Instance.OpenAsyncSession();
            session.Advanced.SessionInfo.SetContext(metadata.PersistenceId);
            using var cts = _store.GetWriteCancellationTokenSource();
            session.Delete(id);
            await session.SaveChangesAsync(cts.Token);
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            //TODO delete by prefix (upto)
            using var session = _store.Instance.OpenAsyncSession();
            using var readCts = _store.GetReadCancellationTokenSource();
            session.Advanced.SessionInfo.SetContext(persistenceId);

            await using var results = await session.Advanced.StreamAsync<SnapshotMetadata>(startsWith: GetSnapshotPrefix(persistenceId), startAfter: GetSnapshotId(persistenceId, criteria.MinSequenceNr - 1), token: readCts.Token);
            while (await results.MoveNextAsync())
            {
                var current = results.Current.Document;
                var shouldDelete =  
                    criteria.MaxSequenceNr >= current.SequenceNr && 
                    criteria.MaxTimeStamp >= current.Timestamp;

                if (shouldDelete)
                    session.Delete(results.Current.Id);
            }

            using var writeCts = _store.GetWriteCancellationTokenSource();
            await session.SaveChangesAsync(writeCts.Token);
        }

        private string GetSnapshotPrefix(string persistenceId) => $"{_store.SnapshotsCollection}/{persistenceId}/";
        private string GetSnapshotId(SnapshotMetadata metadata) => GetSnapshotId(metadata.PersistenceId, metadata.SequenceNr);
        private string GetSnapshotId(string persistenceId, long sequenceNr)
        {
            if (sequenceNr < 0) 
                sequenceNr = 0;

            return $"{_store.SnapshotsCollection}/{persistenceId}/{sequenceNr.ToLeadingZerosFormat()}";
        }
    }
}
