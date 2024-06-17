using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: Xunit.TestCaseOrderer("Akka.Persistence.RavenDb.Tests.SkipByNameOrderer", "Akka.Persistence.RavenDb.Tests")]

namespace Akka.Persistence.RavenDb.Tests
{
    public class SkipByNameOrderer : ITestCaseOrderer
    {
        public static readonly bool SkipPerformance;
        
        static SkipByNameOrderer()
        {
            SkipPerformance = bool.Parse(Environment.GetEnvironmentVariable("SKIP_PERF") ?? "FALSE");
        }


        public SkipByNameOrderer(IMessageSink diagnosticMessageSink)
        {
        }

        public IEnumerable<TTestCase> OrderTestCases<TTestCase>(IEnumerable<TTestCase> testCases) where TTestCase : ITestCase
            => testCases.Where(x => SkipPerformance  == false || x.TestMethod.TestClass.Class.Name.StartsWith(typeof(RavenDbJournalPerfSpec).FullName, StringComparison.Ordinal) == false);
    }
}
