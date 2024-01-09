using Akka.Persistence.Query;
using Akka.Persistence.RavenDB.Query;
using Akka.Persistence.TCK.Query;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDB.Tests.Query;

public class RavenDbCurrentEventsByPersistenceIdSpec : CurrentEventsByPersistenceIdSpec, IClassFixture<RavenDbFixture>
{
    public RavenDbCurrentEventsByPersistenceIdSpec(ITestOutputHelper output, RavenDbFixture databaseFixture) 
        : base(databaseFixture.CreateSpecConfig(), "RavenDbCurrentEventsByPersistenceIdSpec", output)
    {
        ReadJournal = Sys.ReadJournalFor<RavenDbReadJournal>(RavenDbReadJournal.Identifier);
    }
}