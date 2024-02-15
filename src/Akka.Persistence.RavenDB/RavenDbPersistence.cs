using Akka.Actor;
using Akka.Persistence.RavenDb.Journal.Types;
using Raven.Client.Documents;
using Raven.Client.Documents.Session;
using Raven.Client.Http;
using System.Security.Cryptography.X509Certificates;
using Akka.Configuration;

namespace Akka.Persistence.RavenDb
{
    public class RavenDbPersistence : IExtension
    {
        public IDocumentStore Instance => _instance ??= CreateStore();
        private IDocumentStore _instance;

        public RavenDbJournalConfiguration JournalConfiguration;
        public RavenDbQueryConfiguration QueryConfiguration;
        
        public readonly Akka.Serialization.Serialization Serialization;

        public RavenDbPersistence(ExtendedActorSystem system)
        {
            if (system == null)
                throw new ArgumentNullException(nameof(system));

            JournalConfiguration = new RavenDbJournalConfiguration(system.Settings.Config.GetConfig("akka.persistence.journal.ravendb"));
            QueryConfiguration = new RavenDbQueryConfiguration(system.Settings.Config.GetConfig("akka.persistence.query.ravendb"));

            Serialization = system.Serialization;
        }

        public IAsyncDocumentSession OpenAsyncSession() => Instance.OpenAsyncSession(JournalConfiguration.Name);

        private IDocumentStore CreateStore()
        {
            var store = new DocumentStore
            {
                Urls = JournalConfiguration.Urls.ToArray(),
                Conventions = JournalConfiguration.ToDocumentConventions()
            };

            if (string.IsNullOrEmpty(JournalConfiguration.CertificatePath) == false)
                store.Certificate = new X509Certificate2(JournalConfiguration.Certificate);

            store.Conventions.LoadBalanceBehavior = LoadBalanceBehavior.UseSessionContext;
            store.Initialize();

            return store;
        }

        public static Config DefaultConfiguration()
        {
            return ConfigurationFactory.FromResource<RavenDbPersistence>("Akka.Persistence.RavenDb.reference.conf");
        }

        private readonly CancellationTokenSource _stopTokenSource = new CancellationTokenSource();

        public void Stop()
        {
            _stopTokenSource.Cancel();
            //TODO stav: should dispose store here?
        }

        public CancellationTokenSource GetCancellationTokenSource(bool useSaveChangesTimeout)
        {
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_stopTokenSource.Token);
            if(useSaveChangesTimeout)
                cts.CancelAfter(JournalConfiguration.SaveChangesTimeout);
            return cts;
        }

        public string GetMetadataId(string persistenceId) => $"{EventsMetadataCollection}/{persistenceId}";

        public string GetEventPrefix(string persistenceId) => $"{EventsCollection}/{persistenceId}/";

        public string GetSequenceId(string persistenceId, long sequenceNr)
        {
            if (sequenceNr <= 0)
                sequenceNr = 0;

            return $"{GetEventPrefix(persistenceId)}{sequenceNr.ToLeadingZerosFormat()}";
        }

        public string EventsCollection => _instance.Conventions.FindCollectionName(typeof(Journal.Types.Event));//TODO make non function - readonly
        public string EventsMetadataCollection => _instance.Conventions.FindCollectionName(typeof(Metadata));
    }
}
