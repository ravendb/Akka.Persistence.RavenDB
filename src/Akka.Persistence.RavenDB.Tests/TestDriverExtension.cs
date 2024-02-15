using Raven.Client.Documents;
using Raven.Client.Exceptions.Cluster;
using Raven.Client.Exceptions.Database;
using Raven.Client.ServerWide;
using Raven.Client.ServerWide.Operations;
using Raven.TestDriver;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;

namespace Akka.Persistence.RavenDb.Tests
{
    public class TestDriverExtension : RavenTestDriver
    {
        public static bool Secure = false;
        public static readonly string[] SecureUrls = new[] { "https://a.akkatest.development.run/" };
        public static readonly string? CertificatePath =
            @"C:\\Work\\Akka\\SecureServerV5.4\\AkkaTest.Cluster.Settings 2024-01-28 09-43\\admin.client.certificate.akkatest.pfx";
        
        private static readonly IDocumentStore? SecureCreationStore = Secure ? new DocumentStore
        {
            Urls = SecureUrls,
            Conventions =
            {
                DisableTopologyCache = true
            },
            Certificate = new X509Certificate2(CertificatePath)
        }.Initialize() : null;

        private static readonly ConcurrentDictionary<DocumentStore, object> _securedDocumentStores = new();

        public IDocumentStore GetDocumentStore(GetDocumentStoreOptions options = null)
        {
            if (Secure)
                return GetSecuredDocumentStore(options);
            
            return base.GetDocumentStore(options);
        }

        protected IDocumentStore GetSecuredDocumentStore(GetDocumentStoreOptions options = null, [CallerMemberName] string database = null)
        {
            if (string.Equals(database, ".ctor", StringComparison.OrdinalIgnoreCase))
                database = $"{GetType().Name}_ctor";
            else if (string.Equals(database, ".cctor", StringComparison.OrdinalIgnoreCase))
                database = $"{GetType().Name}_cctor";

            var name = database + "_" + Guid.NewGuid();

            SecureCreationStore.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(name)));

            var store = new DocumentStore
            {
                Urls = SecureUrls,
                Database = name,
                Conventions =
                {
                    DisableTopologyCache = true
                },
                Certificate = new X509Certificate2(CertificatePath)
            };

            PreInitialize(store);

            store.Initialize();

            store.AfterDispose += (sender, args) =>
            {
                if (_securedDocumentStores.TryRemove(store, out _) == false)
                    return;

                try
                {
                    store.Maintenance.Server.Send(new DeleteDatabasesOperation(store.Database, hardDelete: true));
                }
                catch (DatabaseDoesNotExistException)
                {
                }
                catch (NoLeaderException)
                {
                }
            };

            SetupDatabase(store);

            if (options?.WaitForIndexingTimeout.HasValue == true)
                WaitForIndexing(store, name, options.WaitForIndexingTimeout);

            _securedDocumentStores[store] = null;//TODO stav: why

            return store;
        }

        public override void Dispose()
        {
            if (IsDisposed)
                return;

            var exceptions = new List<Exception>();
            var stores = _securedDocumentStores.Keys.ToList();
            foreach (var documentStore in stores)
            {
                if (documentStore == null)//TODO stav: why
                    continue;
                try
                {
                    documentStore.Dispose();
                }
                catch (Exception e)
                {
                    exceptions.Add(e);
                }
            }

            SecureCreationStore?.Dispose();

            if (exceptions.Count > 0)
                throw new AggregateException(exceptions);

            base.Dispose();
        }
    }
}
