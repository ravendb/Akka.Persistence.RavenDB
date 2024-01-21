using Akka.Persistence.TCK.Serialization;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbSnapshotStoreSerializationSpec : SnapshotStoreSerializationSpec, IClassFixture<RavenDbFixture>
    {
        public RavenDbSnapshotStoreSerializationSpec(RavenDbFixture ravenDbFixture, ITestOutputHelper output) : base(ravenDbFixture.CreateSpecConfig(), "RavenDbSnapshotStoreSerializationSpec", output)
        {
        }
    }
}
