using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Persistence.TCK.Snapshot;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbSnapshotStoreSpec : SnapshotStoreSpec, IClassFixture<RavenDbFixture>
    {
        public RavenDbSnapshotStoreSpec(RavenDbFixture database) 
            : base(database.CreateSpecConfig(), "RavenDbSnapshotStoreSpec")
        {
            Initialize();
        }
    }
}
