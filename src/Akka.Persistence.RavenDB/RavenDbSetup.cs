using Akka.Actor.Setup;
using Raven.Client.Documents.Conventions;
using System.Security.Cryptography.X509Certificates;

namespace Akka.Persistence.RavenDb
{
    public class RavenDbSetup : Setup
    {
        public X509Certificate2? Certificate { get; set; }

        public Action<DocumentConventions>? ModifyDocumentConventions { get; set; }
    }

    public sealed class RavenDbSnapshotSetup : RavenDbSetup
    {
    }

    public sealed class RavenDbJournalSetup : RavenDbSetup
    {
    }
}
