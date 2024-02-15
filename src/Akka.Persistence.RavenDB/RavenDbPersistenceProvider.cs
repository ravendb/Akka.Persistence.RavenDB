using Akka.Actor;

namespace Akka.Persistence.RavenDb
{
    public class RavenDbPersistenceProvider : ExtensionIdProvider<RavenDbPersistence>
    {
        public override RavenDbPersistence CreateExtension(ExtendedActorSystem system)
        {
            return new RavenDbPersistence(system);
        }
    }
}
