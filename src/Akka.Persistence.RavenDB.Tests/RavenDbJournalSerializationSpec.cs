﻿using Akka.Persistence.TCK.Serialization;
using Xunit.Abstractions;

namespace Akka.Persistence.RavenDB.Tests
{
    public class RavenDbJournalSerializationSpec : JournalSerializationSpec, IClassFixture<RavenDbFixture>
    {
        public RavenDbJournalSerializationSpec(RavenDbFixture ravenDbFixture, ITestOutputHelper output) : base(ravenDbFixture.CreateSpecConfig(), "RavenDbJournalSerializationSpec", output)
        {
        }
    }
}