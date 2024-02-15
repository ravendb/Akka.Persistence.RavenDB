using Akka.Actor;
using Akka.Persistence.Snapshot;
using Akka.Serialization;
using Akka.Util;

namespace Akka.Persistence.RavenDb.Snapshot
{
    public class RavenDbSnapshotStore : SnapshotStore
    {
        private readonly RavenDbPersistence _storage;
        private readonly Akka.Serialization.Serialization _serialization;

        public RavenDbSnapshotStore()
        {
            _storage = Context.System.WithExtension<RavenDbPersistence, RavenDbPersistenceProvider>();
            _serialization = Context.System.Serialization;
        }

        protected override void PostStop()
        {
            _storage.Stop();
            base.PostStop();
        }

        protected override async Task<SelectedSnapshot> LoadAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            // TODO: add API to scan backwards
            using var session = _storage.OpenAsyncSession();
            using var cts = _storage.GetCancellationTokenSource(useSaveChangesTimeout: false);
            session.Advanced.SessionInfo.SetContext(persistenceId);

            await using var results = await session.Advanced.StreamAsync<Snapshot>(startsWith: GetSnapshotPrefix(persistenceId), startAfter: GetSnapshotId(persistenceId, criteria.MinSequenceNr - 1), token: cts.Token);
            Snapshot lastValidSnapshot = null; // yuck!
            string lastValidSnapshotId = null;
            while (await results.MoveNextAsync()) 
            {
                var current = results.Current.Document;
                var isBetween = criteria.MinTimestamp <= current.Timestamp && criteria.MaxTimeStamp >= current.Timestamp; 
                if (current.SequenceNr <= criteria.MaxSequenceNr && isBetween)
                {
                    lastValidSnapshotId = results.Current.Id;
                    lastValidSnapshot = results.Current.Document;
                }
            }

            if (lastValidSnapshot == null)
                return null;

            var snapshotAttachment = await session.Advanced.Attachments.GetAsync(lastValidSnapshotId, "snapshot", cts.Token);
            var buffer = new byte[snapshotAttachment.Details.Size];
            using var source = snapshotAttachment.Stream;
            using var destination = new MemoryStream(buffer);
            await source.CopyToAsync(destination);
            
            var snapshot = lastValidSnapshot.GetSnapshot(_serialization, buffer);
            return new SelectedSnapshot(new SnapshotMetadata(lastValidSnapshot.PersistenceId, lastValidSnapshot.SequenceNr, lastValidSnapshot.Timestamp), snapshot);
        }

        protected override async Task SaveAsync(SnapshotMetadata metadata, object snapshot)
        {
            var id = GetSnapshotId(metadata);

            var snapshotType = snapshot.GetType();
            var serializer = _serialization.FindSerializerForType(snapshotType/*, _config.DefaultSerializer*/);
            var bytes = Akka.Serialization.Serialization.WithTransport(
                system: _serialization.System,
                state: (serializer, snapshot),
                action: state => state.serializer.ToBinary(state.snapshot));

            var manifest = serializer switch
            {
                SerializerWithStringManifest stringManifest => stringManifest.Manifest(snapshot),
                { IncludeManifest: true } => snapshotType.TypeQualifiedName(),
                _ => string.Empty,
            };

            using var stream = new MemoryStream(bytes);

            using var session = _storage.OpenAsyncSession();
            using var cts = _storage.GetCancellationTokenSource(useSaveChangesTimeout: true);
            await session.StoreAsync(new Snapshot
            {
                PersistenceId = metadata.PersistenceId,
                SequenceNr = metadata.SequenceNr,
                Timestamp = metadata.Timestamp,
                Manifest = manifest,
                SerializerId = serializer.Identifier,
            }, id, cts.Token);
            session.Advanced.Attachments.Store(id, "snapshot", stream);
            await session.SaveChangesAsync(cts.Token);
        }

        protected override async Task DeleteAsync(SnapshotMetadata metadata)
        {
            var id = GetSnapshotId(metadata);
            using var session = _storage.OpenAsyncSession();
            session.Advanced.SessionInfo.SetContext(metadata.PersistenceId);
            using var cts = _storage.GetCancellationTokenSource(useSaveChangesTimeout: true);
            session.Delete(id);
            await session.SaveChangesAsync(cts.Token);
        }

        //TODO stav: some methods have both streaming and saving on same cts - separate?
        protected override async Task DeleteAsync(string persistenceId, SnapshotSelectionCriteria criteria)
        {
            //TODO delete by prefix (upto)
            using var session = _storage.OpenAsyncSession();
            using var cts = _storage.GetCancellationTokenSource(useSaveChangesTimeout: false);
            session.Advanced.SessionInfo.SetContext(persistenceId);

            await using var results = await session.Advanced.StreamAsync<SnapshotMetadata>(startsWith: GetSnapshotPrefix(persistenceId), startAfter: GetSnapshotId(persistenceId, criteria.MinSequenceNr - 1), token: cts.Token);
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

        private string GetSnapshotPrefix(string persistenceId) => $"Snapshots/{persistenceId}/";
        private string GetSnapshotId(SnapshotMetadata metadata) => GetSnapshotId(metadata.PersistenceId, metadata.SequenceNr);
        private string GetSnapshotId(string persistenceId, long sequenceNr)
        {
            if (sequenceNr < 0) 
                sequenceNr = 0;

            return $"Snapshots/{persistenceId}/{sequenceNr.ToLeadingZerosFormat()}";
        }

        public class Snapshot
        {
            public DateTime Timestamp;
            public long SequenceNr;
            public string PersistenceId;
            public string Manifest;
            public int? SerializerId;

            public object GetSnapshot(Akka.Serialization.Serialization serialization, byte[] bytes)
            {
                if (SerializerId is null)
                {
                    var type = Type.GetType(Manifest, true);

                    // TODO: hack. Replace when https://github.com/akkadotnet/akka.net/issues/3811
                    return Akka.Serialization.Serialization.WithTransport(
                        system: serialization.System,
                        state: (
                            // DefaultSerializer = config.GetString("serializer");
                            serializer: serialization.FindSerializerForType(type/*, _config.DefaultSerializer*/),
                            bytes,
                            type),
                        action: state => state.serializer.FromBinary(state.bytes, state.type));
                }

                var serializerId = SerializerId.Value;
                return serialization.Deserialize(bytes, serializerId, Manifest);
            }
        }
    }
}
