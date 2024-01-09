using Akka.Persistence.Query;
using Akka.Persistence.RavenDB.Query;
using Akka.Persistence.TCK.Query;
using Raven.Client.Documents;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDB.Tests.Query;

public class RavenDbCurrentPersistenceIdsSpec : CurrentPersistenceIdsSpec, IClassFixture<RavenDbFixture>
{
    private IDocumentStore _store;
    public RavenDbCurrentPersistenceIdsSpec(ITestOutputHelper output, RavenDbFixture databaseFixture) 
        : base(databaseFixture.CreateSpecConfigAndStore(out var store), $"RavenDbPersistenceIdsSpec", output)
    {
        _store = store;
        ReadJournal = Sys.ReadJournalFor<RavenDbReadJournal>(RavenDbReadJournal.Identifier);
    }

    protected override void Dispose(bool disposing)
    {
        _store?.Dispose();
        base.Dispose(disposing);
    }
}