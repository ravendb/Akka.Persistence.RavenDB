using Akka.Configuration;
using Akka.Util.Internal;
using Raven.Embedded;
using Raven.TestDriver;
using System.Diagnostics;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbFixture : TestDriverExtension
    {
        private static readonly AtomicCounter Counter = new AtomicCounter(0);

        static RavenDbFixture()
        {
            if (Secure)
            {
                return;
            }

            var options = new TestServerOptions
            {
                ServerUrl = Urls[0],
                DataDirectory = @"C:\Work\akka\RavenDBTestDir\",
            };

            var server = EmbeddedServer.Instance;
            server.StartServer(options);
        }

        // For every test (actor system) this is called
        public Config CreateSpecConfigAndStore(string className, out string databaseName)
        {
            databaseName = $"{className}_{Counter.GetAndIncrement()}";
            return CreateSpecConfig(databaseName);
        }

        private Config CreateSpecConfig(string databaseName)
        {
            var timeout = Debugger.IsAttached ? "300s" : "10s";
            var specString = $$$$$"""
                                  akka.test.single-expect-default = {{{{{timeout}}}}}
                                  akka.persistence {
                                     publish-plugin-commands = on
                                     journal {
                                         plugin = "akka.persistence.journal.ravendb"
                                         ravendb {
                                             class = "Akka.Persistence.RavenDb.Journal.RavenDbJournal, Akka.Persistence.RavenDb"
                                             name = "{{{{{databaseName}}}}}"
                                             urls = ["{{{{{(Secure ? SecureUrls[0] : Urls[0])}}}}}"]
                                             save-changes-timeout = 30s
                                             certificate-path = "{{{{{(Secure ? CertificatePath : "" )}}}}}"
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
                                              name = "{{{{{databaseName}}}}}"
                                              urls = ["{{{{{(Secure ? SecureUrls[0] : Urls[0])}}}}}"]
                                              save-changes-timeout = 30s
                                              certificate-path = "{{{{{(Secure ? CertificatePath : "" )}}}}}"
                                              http-version = "2.0"
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
                //.WithFallback(RavenDbPersistence.DefaultConfiguration());
        }
    }
}

