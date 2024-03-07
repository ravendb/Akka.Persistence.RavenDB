using Raven.Client.Documents;
using Raven.Client.ServerWide.Operations;
using System.Security.Cryptography.X509Certificates;

namespace Akka.Persistence.RavenDb.Tests
{
    public class TestDriverExtension
    {
        public static bool Secure = false;
        public static readonly string[] SecureUrls = new[] { "https://a.akkatest.development.run/" };
        public static readonly string? CertificatePath =
            @"C:\\Work\\Akka\\SecureServerV5.4\\AkkaTest.Cluster.Settings 2024-01-28 09-43\\admin.client.certificate.akkatest.pfx";
        public static readonly string[] Urls = new[] { "http://localhost:3579" };

        private static readonly IDocumentStore GlobalStore = new DocumentStore
        {
            Urls = Secure ? SecureUrls : Urls,
            Conventions =
            {
                DisableTopologyCache = true
            },
            Certificate = Secure ? new X509Certificate2(CertificatePath) : null
        }.Initialize();

        public static void DeleteDatabase(string databaseName)
        {
            try
            {
                GlobalStore.Maintenance.Server.Send(
                    new DeleteDatabasesOperation(databaseName, hardDelete: true));
            }
            catch (Exception e)
            {
                File.AppendAllText(@"C:\\Work\\Akka\\testLogs.txt", $"\n\nAkka DeleteDatabase: {databaseName}:\n{e.ToString()}");
            }
        }
    }
}
