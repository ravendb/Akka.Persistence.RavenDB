using Akka.Persistence.TCK.Snapshot;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbSnapshotStoreSpec : SnapshotStoreSpec, IClassFixture<RavenDbFixture>
    {
        private readonly string _databaseName;

        public RavenDbSnapshotStoreSpec(ITestOutputHelper output, RavenDbFixture database) 
            : base(database.CreateSpecConfigAndStore(nameof(RavenDbSnapshotStoreSpec), out var databaseName), nameof(RavenDbSnapshotStoreSpec), output)
        {
            _databaseName = databaseName;
            Initialize();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            TestDriverExtension.DeleteDatabase(_databaseName);
        }
    }
}
