using Akka.Persistence.Query;
using Akka.Persistence.RavenDb.Query;
using Akka.Persistence.TCK.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests.Query;

public class RavenDbCurrentEventsByTagSpec : CurrentEventsByTagSpec, IClassFixture<RavenDbFixture>
{
    protected override bool SupportsTagsInEventEnvelope => true;
    private readonly string _databaseName;
    
    public RavenDbCurrentEventsByTagSpec(ITestOutputHelper output, RavenDbFixture databaseFixture) 
        : base(databaseFixture.CreateSpecConfigAndStore(nameof(RavenDbCurrentEventsByTagSpec), out var databaseName), nameof(RavenDbCurrentEventsByTagSpec), output)
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