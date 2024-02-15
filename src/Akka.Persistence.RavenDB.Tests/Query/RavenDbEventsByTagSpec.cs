using Akka.Actor;
using Akka.Persistence.Query;
using Akka.Persistence.RavenDb.Query;
using Raven.Client.Documents;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests.Query
{
    public class RavenDbEventsByTagSpec : TCK.Query.EventsByTagSpec, IClassFixture<RavenDbFixture>
    {
        private readonly IDocumentStore _store;
        protected override bool SupportsTagsInEventEnvelope => true;

        public RavenDbEventsByTagSpec(ITestOutputHelper output, RavenDbFixture database)
            : base(database.CreateSpecConfigAndStore(out var store), nameof(RavenDbEventsByTagSpec), output)
        {
            _store = store;
            ReadJournal = Sys.ReadJournalFor<RavenDbReadJournal>(RavenDbReadJournal.Identifier);
        }

        protected override void Dispose(bool disposing)
        {
            _store.Dispose();
            base.Dispose(disposing);
        }
    }
}
