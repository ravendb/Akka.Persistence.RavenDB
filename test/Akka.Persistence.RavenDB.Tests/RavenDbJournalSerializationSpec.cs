using Akka.Persistence.TCK.Serialization;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbJournalSerializationSpec : JournalSerializationSpec, IClassFixture<RavenDbFixture>
    {
        private readonly string _databaseName;
        public RavenDbJournalSerializationSpec(RavenDbFixture ravenDbFixture, ITestOutputHelper output) : 
            base(ravenDbFixture.CreateSpecConfigAndStore(nameof(RavenDbJournalSerializationSpec), out var databaseName), nameof(RavenDbJournalSerializationSpec), output)
        {
            _databaseName = databaseName;
            output.WriteLine(databaseName);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            TestDriverExtension.DeleteDatabase(_databaseName);
        }
    }
}
