namespace Akka.Persistence.RavenDb.Journal.Types;

// we need this to be an unmodified entity, so we can have a subscription for PersistenceIds() without duplicates
public class UniqueActor
{
    public string PersistenceId;
    public DateTime CreatedAt = DateTime.UtcNow;
}
