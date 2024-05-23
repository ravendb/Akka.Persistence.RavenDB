using Akka.Persistence.Query;
using Akka.Persistence.RavenDb.Query;
using Akka.Persistence.TCK.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests.Query;

public class RavenDbCurrentEventsByPersistenceIdSpec : CurrentEventsByPersistenceIdSpec, IClassFixture<RavenDbFixture>
{
    private readonly string _databaseName;
    
    public RavenDbCurrentEventsByPersistenceIdSpec(ITestOutputHelper output, RavenDbFixture databaseFixture) 
        : base(databaseFixture.CreateSpecConfigAndStore(nameof(RavenDbCurrentEventsByPersistenceIdSpec), out var databaseName), nameof(RavenDbCurrentEventsByPersistenceIdSpec), output)
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