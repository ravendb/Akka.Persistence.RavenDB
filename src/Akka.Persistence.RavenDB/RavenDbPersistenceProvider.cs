using Akka.Actor;
using Akka.Persistence.RavenDb;

namespace Akka.Persistence.RavenDb
{
    public class JournalRavenDbPersistenceProvider : ExtensionIdProvider<JournalRavenDbPersistence>
    {
        public override JournalRavenDbPersistence CreateExtension(ExtendedActorSystem system)
        {
            return new JournalRavenDbPersistence(system);
        }
    }

    public class SnapshotRavenDbPersistenceProvider : ExtensionIdProvider<SnapshotRavenDbPersistence>
    {
        public override SnapshotRavenDbPersistence CreateExtension(ExtendedActorSystem system)
        {
            return new SnapshotRavenDbPersistence(system);
        }
    }
}
