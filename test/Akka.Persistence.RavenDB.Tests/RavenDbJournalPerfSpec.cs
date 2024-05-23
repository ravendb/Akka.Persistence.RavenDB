using Akka.Persistence.TestKit.Performance;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbJournalPerfSpec : JournalPerfSpec, IClassFixture<RavenDbFixture>
    {
        private readonly string _databaseName;

        public RavenDbJournalPerfSpec(ITestOutputHelper output, RavenDbFixture databaseFixture) : 
            base(databaseFixture.CreateSpecConfigAndStore(nameof(RavenDbJournalPerfSpec), out var databaseName), nameof(RavenDbJournalPerfSpec), output)
        {
            output.WriteLine(databaseName);
            _databaseName = databaseName;
            EventsCount = 1000;
            ExpectDuration = TimeSpan.FromMinutes(1);
            MeasurementIterations = 1;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            TestDriverExtension.DeleteDatabase(_databaseName);
        }
    }
}
