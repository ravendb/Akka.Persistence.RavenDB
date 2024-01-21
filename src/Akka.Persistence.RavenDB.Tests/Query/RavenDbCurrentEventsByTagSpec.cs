using Akka.Actor;
using Akka.Persistence.Query;
using Akka.Persistence.RavenDb.Query;
using Akka.Persistence.TCK.Query;
using Raven.Client.Documents;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDb.Tests.Query;

public class RavenDbCurrentEventsByTagSpec : CurrentEventsByTagSpec, IClassFixture<RavenDbFixture>
{
    protected override bool SupportsTagsInEventEnvelope => true;
    private readonly IDocumentStore _store;

    public RavenDbCurrentEventsByTagSpec(ITestOutputHelper output, RavenDbFixture databaseFixture) 
        : base(databaseFixture.CreateSpecConfigAndStore(out var store), "RavenDbCurrentEventsByTagSpec", output)
    {
        _store = store;
        ReadJournal = Sys.ReadJournalFor<RavenDbReadJournal>(RavenDbReadJournal.Identifier);
    }

    protected override void Dispose(bool disposing)
    {
        _store.Dispose();
        base.Dispose(disposing);
    }
}