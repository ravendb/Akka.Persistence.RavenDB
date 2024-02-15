using Akka.Persistence.TCK.Serialization;
using Raven.Client.Documents;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbSnapshotStoreSerializationSpec : SnapshotStoreSerializationSpec, IClassFixture<RavenDbFixture>
    {
        private readonly IDocumentStore _store;

        public RavenDbSnapshotStoreSerializationSpec(RavenDbFixture ravenDbFixture, ITestOutputHelper output) : base(ravenDbFixture.CreateSpecConfigAndStore(out var store), nameof(RavenDbSnapshotStoreSerializationSpec), output)
        {
            _store = store;
        }

        protected override void Dispose(bool disposing)
        {
            _store.Dispose();
            base.Dispose(disposing);
        }
    }
}
