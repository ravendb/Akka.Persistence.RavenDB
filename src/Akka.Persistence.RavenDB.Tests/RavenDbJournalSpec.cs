using Akka.Persistence.TCK.Journal;

namespace Akka.Persistence.RavenDB.Tests
{
    public class RavenDbJournalSpec : JournalSpec, IClassFixture<RavenDbFixture>
    {
        protected override bool SupportsRejectingNonSerializableObjects { get; } = false;

        public RavenDbJournalSpec(RavenDbFixture database) 
            : base(database.CreateSpecConfig(), "RavenDbJournalSpec")
        {
            Initialize();
        }
    }
}