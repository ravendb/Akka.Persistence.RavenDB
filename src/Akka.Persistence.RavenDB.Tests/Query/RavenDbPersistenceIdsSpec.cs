using Akka.Persistence.Query;
using Akka.Persistence.RavenDb.Query;
using Akka.Persistence.TCK.Query;
using Raven.Client.Documents;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests.Query;

public class RavenDbPersistenceIdsSpec : PersistenceIdsSpec, IClassFixture<RavenDbFixture>
{
    private IDocumentStore _store;
    public RavenDbPersistenceIdsSpec(ITestOutputHelper output, RavenDbFixture databaseFixture) 
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
