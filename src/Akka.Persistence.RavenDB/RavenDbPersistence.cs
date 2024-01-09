using System.Linq.Expressions;
using Akka.Actor;
using Akka.Configuration;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Documents.Subscriptions;

namespace Akka.Persistence.RavenDB
{

    public class JournalRavenDbPersistence : RavenDbPersistence
    {
        public JournalRavenDbPersistence(ExtendedActorSystem system) : base(system)
        {
            var journalConfig = system.Settings.Config.GetConfig("akka.persistence.journal.ravendb");
            Database = journalConfig.GetString("name") ?? throw new ArgumentException("name must be provided");
        }
    }

    public class SnapshotRavenDbPersistence : RavenDbPersistence
    {
        public SnapshotRavenDbPersistence(ExtendedActorSystem system) : base(system)
        {
            var snapshotConfig = system.Settings.Config.GetConfig("akka.persistence.snapshot-store.ravendb");
            Database = snapshotConfig.GetString("name") ?? throw new ArgumentException("name must be provided");
        }
    }
    public abstract class RavenDbPersistence : IExtension
    {
        public static IDocumentStore Instance => _instance ??= CreateStore();
        private static IDocumentStore _instance;

        private static readonly TimeSpan? _timout = null;
        public string Database;

        protected RavenDbPersistence(ExtendedActorSystem system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));
        }

        public IAsyncDocumentSession OpenAsyncSession() => Instance.OpenAsyncSession(Database); 

        private static IDocumentStore CreateStore()
        {
            var store = new DocumentStore
            {
                Urls = new[] { "http://localhost:8080" }, 
                // TODO add load balancing
            };

            store.Initialize();

            return store;
        }

        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<RavenDbPersistence>("Akka.Persistence.RavenDb.reference.conf");
        }

        public static CancellationTokenSource StopTokenSource = new CancellationTokenSource();
        public void Stop() => StopTokenSource.Cancel();

        public static CancellationTokenSource CancellationTokenSource
        {
            get
            {
                var cts = CancellationTokenSource.CreateLinkedTokenSource(StopTokenSource.Token);
                if (_timout.HasValue)
                    cts.CancelAfter(_timout.Value);
                return cts;
            }
        }
    }
}
