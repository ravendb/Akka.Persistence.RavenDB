using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Persistence.TCK.Snapshot;
using Raven.Client.Documents;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbSnapshotStoreSpec : SnapshotStoreSpec, IClassFixture<RavenDbFixture>
    {
        private readonly IDocumentStore _store;

        public RavenDbSnapshotStoreSpec(RavenDbFixture database) 
            : base(database.CreateSpecConfigAndStore(out var store), nameof(RavenDbSnapshotStoreSpec)) //TODO stav: no output?
        {
            _store = store;
            Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            _store.Dispose();
            base.Dispose(disposing);
        }
    }
}
