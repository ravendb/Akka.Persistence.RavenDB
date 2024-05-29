using Akka.Serialization;
using Akka.Util;

namespace Akka.Persistence.RavenDb.Snapshot.Types;

public class Snapshot
{
    public DateTime Timestamp;
    public long SequenceNr;
    public string PersistenceId;
    public string Manifest;
    public int SerializationId;
    public object Payload;


    public void CopyTo(Snapshot copy)
    {
        if (copy.SequenceNr != SequenceNr)
            throw new InvalidOperationException($"You can't modify '{copy.PersistenceId}' with sequence '{copy.SequenceNr}', existing sequence is {SequenceNr}");

        copy.Timestamp = Timestamp;
        copy.PersistenceId = PersistenceId;
        copy.Manifest = Manifest;
        copy.SerializationId = SerializationId;
        copy.Payload = Payload;
    }
    public static Snapshot Serialize(Akka.Serialization.Serialization serialization, SnapshotMetadata metadata, object payload)
    {
        var snapshot = new Snapshot
        {
            PersistenceId = metadata.PersistenceId,
            SequenceNr = metadata.SequenceNr,
            Timestamp = metadata.Timestamp
        };

        var serializer = serialization.FindSerializerFor(payload);

        if (serializer.Identifier == 1) // inject our own serializer instead
        {
            snapshot.Payload = payload;
            snapshot.SerializationId = 1;
        }
        else
        {
            snapshot.Payload = serialization.Serialize(payload);
            snapshot.SerializationId = serialization.FindSerializerFor(payload).Identifier;
        }

        snapshot.Manifest = serializer switch
        {
            SerializerWithStringManifest stringManifest => stringManifest.Manifest(payload),
            { IncludeManifest: true } => payload.GetType().TypeQualifiedName(),
            _ => string.Empty,
        };

        return snapshot;
    }

    public SelectedSnapshot ToSelectedSnapshot(Akka.Serialization.Serialization serialization)
    {
        if (SerializationId != 1)
            Payload = serialization.Deserialize((byte[])Payload, SerializationId, Manifest);

        return new SelectedSnapshot(new SnapshotMetadata(PersistenceId, SequenceNr, Timestamp), Payload);
    }
}
