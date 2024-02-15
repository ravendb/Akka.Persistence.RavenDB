using Akka.Persistence.TCK.Serialization;
using Raven.Client.Documents;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbJournalSerializationSpec : JournalSerializationSpec, IClassFixture<RavenDbFixture>
    {
        private readonly IDocumentStore _store;
        public RavenDbJournalSerializationSpec(RavenDbFixture ravenDbFixture, ITestOutputHelper output) : base(ravenDbFixture.CreateSpecConfigAndStore(out var store), nameof(RavenDbJournalSerializationSpec), output)
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
