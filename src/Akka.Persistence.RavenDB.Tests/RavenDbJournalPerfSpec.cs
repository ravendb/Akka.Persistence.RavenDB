using Akka.Persistence.TestKit.Performance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Configuration;
using Xunit.Abstractions;
using Raven.Client.Documents;

namespace Akka.Persistence.RavenDb.Tests
{
    public class RavenDbJournalPerfSpec : JournalPerfSpec, IClassFixture<RavenDbFixture>
    {
        private readonly IDocumentStore _store;

        public RavenDbJournalPerfSpec(ITestOutputHelper output, RavenDbFixture databaseFixture) : base(databaseFixture.CreateSpecConfigAndStore(out var store), nameof(RavenDbJournalPerfSpec), output)
        {
            _store = store;
            EventsCount = 1000;
            ExpectDuration = TimeSpan.FromMinutes(1);
            MeasurementIterations = 1;
        }

        protected override void Dispose(bool disposing)
        {
            _store.Dispose();
            base.Dispose(disposing);
        }
    }
}
