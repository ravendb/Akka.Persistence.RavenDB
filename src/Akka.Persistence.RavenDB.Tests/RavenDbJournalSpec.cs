using System.Diagnostics;
using System.Runtime.Serialization;
using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.RavenDB;
using Akka.Persistence.TCK.Journal;
using Akka.Persistence.TestKit;
using Akka.Serialization;
using Raven.Embedded;

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