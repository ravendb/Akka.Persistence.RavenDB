using Akka.Configuration;
using Raven.Client.Documents;
using Raven.TestDriver;
using System.Diagnostics;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbFixture : TestDriverExtension
    {
        private static readonly string[] Urls =  new[] { "http://localhost:3579" };
        
        static RavenDbFixture()
        {
            if (Secure)
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

        // Called once in the beginning of the first test and disposes at the end of the last test (of each testing class)
        public RavenDbFixture()
        {
        }

        // For every test (actor system) this is called
        public Config CreateSpecConfigAndStore(out IDocumentStore store)
        {
            store = GetDocumentStore();
            return CreateSpecConfig(store.Database);
        }

        private Config CreateSpecConfig(string databaseName)
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
                                            name = "{{{{databaseName}}}}"
                                            urls = ["{{{{(Secure ? SecureUrls[0] : Urls[0])}}}}"]
                                            save-changes-timeout = 30s
                                            certificate-path = "{{{{(Secure ? CertificatePath : "" )}}}}"
                                            http-version = "2.0"
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

            return ConfigurationFactory.ParseString(specString)
                .WithFallback(RavenDbPersistence.DefaultConfiguration());
        }
    }
}

