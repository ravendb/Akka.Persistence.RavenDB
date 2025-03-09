using Raven.Client.Documents;
using Raven.Client.ServerWide.Operations;
using System.Security.Cryptography.X509Certificates;

namespace Akka.Persistence.RavenDb.Tests
{
    public class TestDriverExtension
    {
        public static bool Secure = false;
        public static readonly string[] SecureUrls = new[] { "https://a.akkatest.development.run/" };
        public static readonly string? CertificatePath = @"dummyClientCert.pfx";
        public static readonly string CertificatePassword = "1234";
        public static string[] Urls = new[] { "http://localhost:8080" };

        private static readonly IDocumentStore GlobalStore = new DocumentStore
        {
            Urls = Secure ? SecureUrls : Urls,
            Conventions =
            {
                DisableTopologyCache = true
            },
            Certificate = Secure ? new X509Certificate2(CertificatePath, CertificatePassword) : null
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
                //TODO stav: log this
            }
        }
    }
}
