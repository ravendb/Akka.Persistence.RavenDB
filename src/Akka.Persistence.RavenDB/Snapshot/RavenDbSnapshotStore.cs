﻿using Akka.Actor;
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

        protected override void PostStop()
        {
            _store.Stop();
            _store.Dispose();
            base.PostStop();
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            // need to find the most up-to-date snapshot
            // TODO: add API to scan backwards !!
            // or create an index

            using var session = _store.Instance.OpenAsyncSession();
            session.Advanced.SessionInfo.SetContext(persistenceId);
            session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);

            using var cts = _store.GetReadCancellationTokenSource();

            await using var results = await session.Advanced.StreamAsync<Types.Snapshot>(startsWith: GetSnapshotPrefix(persistenceId), startAfter: GetSnapshotId(persistenceId, criteria.MinSequenceNr - 1), token: cts.Token).ConfigureAwait(false);
            Types.Snapshot lastValidSnapshot = null; // yuck!
            while (await results.MoveNextAsync().ConfigureAwait(false)) 
            {
                var current = results.Current.Document;
                var validTime = criteria.MinTimestamp <= current.Timestamp && criteria.MaxTimeStamp >= current.Timestamp;
                var validSequence = criteria.MinSequenceNr <= current.SequenceNr && criteria.MaxSequenceNr >= current.SequenceNr;

                if (validSequence && validTime)
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
            using var session = _store.Instance.OpenAsyncSession();
            session.Advanced.SessionInfo.SetContext(metadata.PersistenceId);
            session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);

            using var cts = _store.GetWriteCancellationTokenSource();
            var id = GetSnapshotId(metadata);
            var snapshotEntity = Types.Snapshot.Serialize(_serialization, metadata, snapshot);

            var current = await session.LoadAsync<Types.Snapshot>(id, cts.Token).ConfigureAwait(false);
            if (current == null)
            {
                await session.StoreAsync(snapshotEntity, id, cts.Token).ConfigureAwait(false);
            }
            else
            {
                snapshotEntity.CopyTo(current);
            }
            await session.SaveChangesAsync(cts.Token).ConfigureAwait(false);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            var id = GetSnapshotId(metadata);
            using var session = _store.Instance.OpenAsyncSession();
            session.Advanced.SessionInfo.SetContext(metadata.PersistenceId);
            session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);

            using var cts = _store.GetWriteCancellationTokenSource();
            var snapshot = await session.LoadAsync<Types.Snapshot>(id, cts.Token).ConfigureAwait(false);
            if (snapshot.Timestamp > metadata.Timestamp && metadata.Timestamp > DateTime.MinValue) // due to fixes done here: https://github.com/akkadotnet/akka.net/pull/7313
                return;

            session.Delete(id);
            await session.SaveChangesAsync(cts.Token).ConfigureAwait(false);
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            //TODO delete by prefix (upto)
            using var session = _store.Instance.OpenAsyncSession();
            using var readCts = _store.GetReadCancellationTokenSource();
            session.Advanced.SessionInfo.SetContext(persistenceId);
            session.Advanced.SetTransactionMode(TransactionMode.ClusterWide);

            await using var results = await session.Advanced.StreamAsync<SnapshotMetadata>(startsWith: GetSnapshotPrefix(persistenceId), startAfter: GetSnapshotId(persistenceId, criteria.MinSequenceNr - 1), token: readCts.Token).ConfigureAwait(false);
            while (await results.MoveNextAsync().ConfigureAwait(false))
            {
                var current = results.Current.Document;
                var shouldDelete =  
                    criteria.MaxSequenceNr >= current.SequenceNr && 
                    criteria.MaxTimeStamp >= current.Timestamp;

                if (shouldDelete)
                    session.Delete(results.Current.Id);
            }

            using var writeCts = _store.GetWriteCancellationTokenSource();
            await session.SaveChangesAsync(writeCts.Token).ConfigureAwait(false);
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
