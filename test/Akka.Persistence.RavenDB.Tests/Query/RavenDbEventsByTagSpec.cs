using Akka.Persistence.Query;
using Akka.Persistence.RavenDb.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests.Query
{
    public class RavenDbEventsByTagSpec : TCK.Query.EventsByTagSpec, IClassFixture<RavenDbFixture>
    {
        private readonly string _databaseName;
        protected override bool SupportsTagsInEventEnvelope => true;

        public RavenDbEventsByTagSpec(ITestOutputHelper output, RavenDbFixture database)
            : base(database.CreateSpecConfigAndStore(nameof(RavenDbEventsByTagSpec), out var databaseName), nameof(RavenDbEventsByTagSpec), output)
        {
            _databaseName = databaseName;
            ReadJournal = Sys.ReadJournalFor<RavenDbReadJournal>(RavenDbReadJournal.Identifier);
            output.WriteLine(databaseName);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            TestDriverExtension.DeleteDatabase(_databaseName);
        }
    }
}
