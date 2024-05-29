namespace Akka.Persistence.RavenDb.Journal.Types
{
    // we need this metadata to keep track of the actor even when all events are deleted
    public class EventMetadata
    {
        public string Id;
        public string PersistenceId;
        public long MaxSequenceNr;
        public DateTime Timestamp = DateTime.UtcNow;
    }
}
