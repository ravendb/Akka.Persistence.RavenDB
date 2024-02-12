using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography.X509Certificates;
using Akka.Configuration;
using Raven.Client.Documents;
using Raven.Client;
using Raven.TestDriver;
using static System.Net.WebRequestMethods;
using Raven.Client.Exceptions;
using Raven.Client.ServerWide.Operations;
using Raven.Client.ServerWide;
using Raven.Client.Util;
using Sparrow;
using static Akka.Actor.ProviderSelection;
using Raven.Client.Exceptions.Cluster;
using Raven.Client.Exceptions.Database;
using Raven.Client.Documents.Smuggler;
using System.Collections.Concurrent;
using System.Net;
using static Raven.Client.Constants;
using Google.Protobuf.WellKnownTypes;
using Raven.Client.Documents.Conventions;
using Raven.Client.Documents.Session;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbFixture : RavenTestDriver
    {
        private IDocumentStore _store;

        private static readonly string[] Urls =  new[] { "http://localhost:3579" };
        
        static RavenDbFixture()
        {
            if (IsSecure)
            {
                return;
            }
            
            var options = new TestServerOptions
            {
                ServerUrl = Urls[0],
                DataDirectory = @"C:\Work\akka\RavenDBTestDir"
            };

            ConfigureServer(options);
        }

        public string Name => _store.Database;

        public RavenDbFixture()
        {
            if (IsSecure)
                _store = GetDocumentStoreSecured(new X509Certificate2(CertificatePath));
            else
                _store = GetDocumentStore();
        }
        
        public override void Dispose()
        {
            _store.Dispose();
            base.Dispose();
            DisposeStores();
        }

        public Config CreateSpecConfigAndStore(out IDocumentStore store)
        {
            if (IsSecure)
                store = GetDocumentStoreSecured(new X509Certificate2(CertificatePath), new());
            else
                store = GetDocumentStore(new());
            
            return CreateSpecConfig(store.Database);
        }

        private static bool IsSecure = false;
        private static readonly string[] SecureUrls = new[] { "https://a.akkatest.development.run/" };
        private static readonly string? CertificatePath =
            @"C:\\Work\\Akka\\SecureServerV5.4\\AkkaTest.Cluster.Settings 2024-01-28 09-43\\admin.client.certificate.akkatest.pfx";
        //TODO akka needs path to be with \\
        //TODO password for certificate?
        private readonly ConcurrentDictionary<DocumentStore, object> _documentStores = new ();
        private static IDocumentStore SecureStaticStore = null;
        protected IDocumentStore GetDocumentStoreSecured(X509Certificate2 clientCertificate, GetDocumentStoreOptions options = null, [CallerMemberName] string database = null)
        {
            if (SecureStaticStore == null)
            {
                SecureStaticStore = new DocumentStore
                {
                    Urls = SecureUrls,
                    Conventions =
                    {
                        DisableTopologyCache = true
                    },
                    Certificate = clientCertificate
                }.Initialize();
            }
            if (string.Equals(database, ".ctor", StringComparison.OrdinalIgnoreCase))
                database = $"{GetType().Name}_ctor";
            else if (string.Equals(database, ".cctor", StringComparison.OrdinalIgnoreCase))
                database = $"{GetType().Name}_cctor";

            var name = database + "_" + Guid.NewGuid();

            SecureStaticStore.Maintenance.Server.Send(new CreateDatabaseOperation(new DatabaseRecord(name)));
            
            var store = new DocumentStore
            {
                Urls = SecureUrls,
                Database = name,
                Conventions =
                {
                    DisableTopologyCache = true
                },
                Certificate = clientCertificate
            };

            PreInitialize(store);

            store.Initialize();

            store.AfterDispose += (sender, args) =>
            {
                if (_documentStores.TryRemove(store, out _) == false)
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

            AsyncHelpers.RunSync(() => ImportDatabaseAsync(store, name));

            SetupDatabase(store);

            if (options?.WaitForIndexingTimeout.HasValue == true)
                WaitForIndexing(store, name, options.WaitForIndexingTimeout);

            _documentStores[store] = null;

            return store;

            async Task ImportDatabaseAsync(DocumentStore docStore, string database, TimeSpan? timeout = null)
            {
                var op = new DatabaseSmugglerImportOptions();
                if (DatabaseDumpFilePath != null)
                {
                    var operation = await docStore.Smuggler.ForDatabase(database).ImportAsync(op, DatabaseDumpFilePath);
                    await operation.WaitForCompletionAsync(timeout);
                }
                else if (DatabaseDumpFileStream != null)
                {
                    var operation = await docStore.Smuggler.ForDatabase(database).ImportAsync(op, DatabaseDumpFileStream);
                    await operation.WaitForCompletionAsync(timeout);
                }
            }
        }

        private void DisposeStores()
        {
            if (IsDisposed)
                return;

            var exceptions = new List<Exception>();
            var stores = _documentStores.Keys.ToList();
            foreach (var documentStore in stores)
            {
                try
                {
                    documentStore.Dispose();
                }
                catch (Exception e)
                {
                    exceptions.Add(e);
                }
            }

            DatabaseDumpFileStream?.Dispose();
            
            if (exceptions.Count > 0)
                throw new AggregateException(exceptions);
        }

        public Config CreateSpecConfig(string name = null)
        {
            var timeout = Debugger.IsAttached ? "300s" : "3s";
            var specString = $$$$"""
                                 akka.test.single-expect-default = {{{{timeout}}}}
                                 akka.persistence {
                                    publish-plugin-commands = on
                                    journal {
                                        plugin = "akka.persistence.journal.ravendb"
                                        ravendb {
                                            class = "Akka.Persistence.RavenDb.Journal.RavenDbJournal, Akka.Persistence.RavenDb"
                                            name = "{{{{name ?? Name}}}}"
                                            urls = ["{{{{(IsSecure ? SecureUrls[0] : Urls[0])}}}}"]
                                            request-timeout = 20s
                                            cert = "{{{{(IsSecure ? CertificatePath : "" )}}}}"
                                            http-version = "1.1"
                                            event-adapters {
                                                 color-tagger  = "Akka.Persistence.TCK.Query.ColorFruitTagger, Akka.Persistence.TCK"
                                             }
                                             event-adapter-bindings = {
                                                 "System.String" = color-tagger
                                             }
                                        }
                                    }
                                    snapshot-store {
                                         plugin = "akka.persistence.snapshot-store.ravendb"
                                         ravendb {
                                             class = "Akka.Persistence.RavenDb.Snapshot.RavenDbSnapshotStore, Akka.Persistence.RavenDb"
                                             auto-initialize = on
                                         }
                                     }
                                     query {
                                         ravendb {
                                             class = "Akka.Persistence.RavenDb.Query.RavenDbReadJournalProvider, Akka.Persistence.RavenDb"
                                             refresh-interval = 1s
                                             wait-for-non-stale = true
                                         }
                                     }
                                 }
                                 
                                 """;

            return ConfigurationFactory.ParseString(specString);
        }
    }
}

