using Akka.Persistence.Query;
using Akka.Persistence.RavenDb.Query;
using Akka.Persistence.TCK.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests.Query;

public class RavenDbCurrentAllEventsSpec : CurrentAllEventsSpec, IClassFixture<RavenDbFixture>
{
    private readonly string _databaseName;
    
    public RavenDbCurrentAllEventsSpec(ITestOutputHelper output, RavenDbFixture databaseFixture) 
        : base(databaseFixture.CreateSpecConfigAndStore(nameof(RavenDbCurrentAllEventsSpec), out var databaseName), nameof(RavenDbCurrentAllEventsSpec), output)
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