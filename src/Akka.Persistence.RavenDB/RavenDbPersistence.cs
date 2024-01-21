using System.Linq.Expressions;
using System.Reflection;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.RavenDb.Journal;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Documents.Subscriptions;
using Raven.Client.Http;

namespace Akka.Persistence.RavenDb
{

    public class JournalRavenDbPersistence : RavenDbPersistence
    {
        public JournalRavenDbPersistence(ExtendedActorSystem system) : base(system)
        {
           
        }
    }

    public class SnapshotRavenDbPersistence : RavenDbPersistence
    {
        public SnapshotRavenDbPersistence(ExtendedActorSystem system) : base(system)
        {
            
        }
    }
    public abstract class RavenDbPersistence : IExtension
    {
        public static IDocumentStore Instance => _instance ??= CreateStore();
        private static IDocumentStore _instance;

        private static readonly TimeSpan? _timout = null;
        public string Database;
        public static IList<string> Urls;
        public bool WaitForNonStale { get; set; }

        public readonly Akka.Serialization.Serialization Serialization;

        protected RavenDbPersistence(ExtendedActorSystem system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));

            var journalConfig = system.Settings.Config.GetConfig("akka.persistence.journal.ravendb");
            Database = journalConfig.GetString("name") ?? throw new ArgumentException("name must be provided");
            Urls = journalConfig.GetStringList("urls") ?? throw new ArgumentException("urls must be provided");
            Serialization = system.Serialization;
        }


        public IAsyncDocumentSession OpenAsyncSession() => Instance.OpenAsyncSession(Database); 

        private static IDocumentStore CreateStore()
        {
            var store = new DocumentStore
            {
                Urls = Urls.ToArray(), 
            };
            store.Conventions.LoadBalanceBehavior = LoadBalanceBehavior.UseSessionContext;
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
