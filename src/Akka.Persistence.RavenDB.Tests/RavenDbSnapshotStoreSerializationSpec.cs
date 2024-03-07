using Akka.Persistence.TCK.Serialization;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbSnapshotStoreSerializationSpec : SnapshotStoreSerializationSpec, IClassFixture<RavenDbFixture>
    {
        private readonly string _databaseName;
        
        public RavenDbSnapshotStoreSerializationSpec(RavenDbFixture ravenDbFixture, ITestOutputHelper output) : 
            base(ravenDbFixture.CreateSpecConfigAndStore(nameof(RavenDbSnapshotStoreSerializationSpec), out var databaseName), nameof(RavenDbSnapshotStoreSerializationSpec), output)
        {
            output.WriteLine(databaseName);
            _databaseName = databaseName;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            TestDriverExtension.DeleteDatabase(_databaseName);
        }
    }
}
