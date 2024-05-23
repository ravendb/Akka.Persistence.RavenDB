using Akka.Persistence.TCK.Journal;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbJournalSpec : JournalSpec, IClassFixture<RavenDbFixture>
    {
        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        private readonly string _databaseName;

        public RavenDbJournalSpec(ITestOutputHelper output, RavenDbFixture database) 
            : base(database.CreateSpecConfigAndStore(nameof(RavenDbJournalSpec), out var databaseName), nameof(RavenDbJournalSpec), output)
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