using Akka.Actor;
using Akka.Persistence.RavenDB;
using Akka.Persistence.Snapshot;
using Akka.Serialization;

namespace Akka.Persistence.RavenDB.Snapshot
{
    public class RavenDbSnapshotStore : SnapshotStore
    {
        private readonly SnapshotRavenDbPersistence _storage;
        private readonly Akka.Serialization.Serialization _serialization;

        public RavenDbSnapshotStore()
        {
            _storage = Context.System.WithExtension<SnapshotRavenDbPersistence, SnapshotRavenDbPersistenceProvider>();
            _serialization = Context.System.Serialization;
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            // TODO: add API to scan backwards
            using var session = _storage.OpenAsyncSession();
            using var cts = RavenDbPersistence.CancellationTokenSource;

            var results = await session.Advanced.StreamAsync<SnapshotMetadata>(startsWith: GetSnapshotPrefix(persistenceId), startAfter: GetSnapshotId(persistenceId, criteria.MinSequenceNr - 1), token: cts.Token);
            SnapshotMetadata lastValidSnapshot = null; // yuck!
            string lastValidSnapshotId = null;
            while (await results.MoveNextAsync()) 
            {
                var current = results.Current.Document;
                var isBetween = criteria.MinTimestamp <= current.Timestamp && criteria.MaxTimeStamp >= current.Timestamp; 
                if (current.SequenceNr <= criteria.MaxSequenceNr && isBetween)
                {
                    lastValidSnapshotId = results.Current.Id;
                    lastValidSnapshot = current;
                }
            }

            if (lastValidSnapshot == null)
                // throw new InvalidOperationException($"no snapshot found for {persistenceId} and criteria {criteria}");
                return null;

            var snapshot = await session.Advanced.Attachments.GetAsync(lastValidSnapshotId, "snapshot", cts.Token);
            var buffer = new byte[snapshot.Details.Size];
            await using var source = snapshot.Stream;
            await using var destination = new MemoryStream(buffer);
            await source.CopyToAsync(destination, cts.Token);
            
            var serializer = _serialization.FindSerializerForType(typeof(Serialization.Snapshot));
            var data = serializer.FromBinary<Serialization.Snapshot>(buffer).Data;
            return new SelectedSnapshot(new SnapshotMetadata(lastValidSnapshot.PersistenceId, lastValidSnapshot.SequenceNr, lastValidSnapshot.Timestamp), data);
        }

        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            var id = GetSnapshotId(metadata);
            var serializer = _serialization.FindSerializerForType(typeof(Serialization.Snapshot));

            var bytes = serializer.ToBinary(new Serialization.Snapshot(snapshot));
            using var stream = new MemoryStream(bytes);

            using var session = _storage.OpenAsyncSession();
            using var cts = RavenDbPersistence.CancellationTokenSource;
            await session.StoreAsync(metadata, id, cts.Token);
            session.Advanced.Attachments.Store(id, "snapshot", stream);
            await session.SaveChangesAsync(cts.Token);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            var id = GetSnapshotId(metadata);
            using var session = _storage.OpenAsyncSession();
            using var cts = RavenDbPersistence.CancellationTokenSource;
            session.Delete(id);
            await session.SaveChangesAsync(cts.Token);
        }

        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            //TODO delete by prefix (upto)
            using var session = _storage.OpenAsyncSession();
            using var cts = RavenDbPersistence.CancellationTokenSource;
            var results = await session.Advanced.StreamAsync<SnapshotMetadata>(startsWith: GetSnapshotPrefix(persistenceId), startAfter: GetSnapshotId(persistenceId, criteria.MinSequenceNr - 1), token: cts.Token);
            while (await results.MoveNextAsync())
            {
                var current = results.Current.Document;
                var shouldDelete =  
                    criteria.MaxSequenceNr >= current.SequenceNr && 
                    criteria.MaxTimeStamp >= current.Timestamp;

                if (shouldDelete)
                    session.Delete(results.Current.Id);
            }

            await session.SaveChangesAsync(cts.Token);
        }

        private string GetSnapshotPrefix(string persistenceId) => $"snapshot/{persistenceId}/";
        private string GetSnapshotId(SnapshotMetadata metadata) => GetSnapshotId(metadata.PersistenceId, metadata.SequenceNr);
        private string GetSnapshotId(string persistenceId, long sequenceNr)
        {
            if (sequenceNr < 0) 
                sequenceNr = 0;

            return $"snapshot/{persistenceId}/{sequenceNr.ToLeadingZerosFormat()}";
        }
    }
}
