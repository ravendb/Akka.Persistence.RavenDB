using Akka.Persistence.Query;
using Akka.Persistence.RavenDb.Query;
using Akka.Persistence.TCK.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests.Query;

public class RavenDbCurrentPersistenceIdsSpec : CurrentPersistenceIdsSpec, IClassFixture<RavenDbFixture>
{
    private readonly string _databaseName;
    
    public RavenDbCurrentPersistenceIdsSpec(ITestOutputHelper output, RavenDbFixture databaseFixture) 
        : base(databaseFixture.CreateSpecConfigAndStore(nameof(RavenDbCurrentPersistenceIdsSpec), out var databaseName), nameof(RavenDbCurrentPersistenceIdsSpec), output)
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