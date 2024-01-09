using Akka.Persistence.TestKit.Performance;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Akka.Configuration;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDB.Tests
{
    public class RavenDbJournalPerfSpec : JournalPerfSpec, IClassFixture<RavenDbFixture>
    {
        public RavenDbJournalPerfSpec(ITestOutputHelper output, RavenDbFixture databaseFixture) : base(databaseFixture.CreateSpecConfig(), "RavenDbJournalPerfSpec", output)
        {
            EventsCount = 1000;
            ExpectDuration = TimeSpan.FromMinutes(1);
            MeasurementIterations = 1;
        }
    }
}
