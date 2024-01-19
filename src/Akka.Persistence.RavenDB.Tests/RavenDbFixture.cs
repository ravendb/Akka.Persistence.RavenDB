using System.Diagnostics;
using Akka.Configuration;
using Raven.Client.Documents;
using Raven.Client.ServerWide;
using Raven.TestDriver;
using static System.Net.WebRequestMethods;

namespace Akka.Persistence.RavenDB.Tests
{
    public class RavenDbFixture : RavenTestDriver
    {
        private IDocumentStore _store;

        private static readonly string[] Urls = new[] { "http://localhost:3579" };

        static RavenDbFixture()
        {
            ConfigureServer(new TestServerOptions
            {
                ServerUrl = Urls[0],
                DataDirectory = @"C:\Work\akka\RavenDBTestDir",
            });
        }

        public string Name => _store.Database;

        public RavenDbFixture()
        {
            _store = GetDocumentStore();
        }

        public override void Dispose()
        {
            _store.Dispose();
            base.Dispose();
        }

        public Config CreateSpecConfigAndStore(out IDocumentStore store)
        {
            store = GetDocumentStore();
            return CreateSpecConfig(store.Database);
        }

        public Config CreateSpecConfig(string name = null)
        {
            var timeout = Debugger.IsAttached ? "300s" : "3s";
            var specString = $$$"""
                                akka.test.single-expect-default = {{{ timeout }}}
                                akka.persistence {
                                   publish-plugin-commands = on
                                   journal {
                                       plugin = "akka.persistence.journal.ravendb"
                                       ravendb {
                                           class = "Akka.Persistence.RavenDB.Journal.RavenDbJournal, Akka.Persistence.RavenDB"
                                           name = {{{name ?? Name}}}
                                           urls = ["{{{Urls[0]}}}"]
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
                                            class = "Akka.Persistence.RavenDB.Snapshot.RavenDbSnapshotStore, Akka.Persistence.RavenDB"
                                            auto-initialize = on
                                        }
                                    }
                                    query {
                                        ravendb {
                                            class = "Akka.Persistence.RavenDB.Query.RavenDbReadJournalProvider, Akka.Persistence.RavenDB"
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
