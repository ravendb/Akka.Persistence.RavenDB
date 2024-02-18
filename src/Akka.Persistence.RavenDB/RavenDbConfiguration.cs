using Akka.Configuration;
using Raven.Client.Documents.Conventions;
using System.Security.Cryptography.X509Certificates;

namespace Akka.Persistence.RavenDb
{
    public class RavenDbConfiguration
    {
        public readonly string Name;
        public readonly string[] Urls;
        public readonly string? CertificatePath;
        public readonly X509Certificate2 Certificate;
        public readonly Version? HttpVersion;
        public readonly bool? DisableTcpCompression;
        public readonly TimeSpan SaveChangesTimeout;
        
        //TODO stav: currently impossible to change timeout of stream (only save-changes timeout). Should add support for this?

        public RavenDbConfiguration(Config config)
        {
            Name = config.GetString("name") ?? throw new ArgumentException("name must be provided");
            Urls = config.GetStringList("urls")?.ToArray() ?? throw new ArgumentException("urls must be provided");
            CertificatePath = config.GetString("cert");

            //TODO DisposeCertificate in DocumentConventions
            
            if (string.IsNullOrEmpty(CertificatePath) == false)
            {
                var certPassword = Environment.GetEnvironmentVariable("RAVEN_CERTIFICATE_PASSWORD");
                Certificate = certPassword != null ? new X509Certificate2(CertificatePath, certPassword) : new X509Certificate2(CertificatePath);
            }

            var httpVersion = config.GetString("http-version");
            if (string.IsNullOrEmpty(httpVersion) == false)
            {
                //TODO stav: error gets swallowed in akka and doesn't bubble up
                HttpVersion = Version.Parse(httpVersion);
            }
            
            DisableTcpCompression = config.GetBoolean("disable-tcp-compression");
            SaveChangesTimeout = config.GetTimeSpan("save-changes-timeout", TimeSpan.FromSeconds(15));
        }

        public DocumentConventions ToDocumentConventions()
        {
            var conventions = new DocumentConventions();

            conventions.HttpVersion = HttpVersion ?? conventions.HttpVersion;
            conventions.DisableTcpCompression = DisableTcpCompression ?? conventions.DisableTcpCompression;
            
            return conventions;
        }
    }

    public class RavenDbJournalConfiguration : RavenDbConfiguration
    {
        public RavenDbJournalConfiguration(Config config) : base(config)
        {
        }
    }

    public class RavenDbQueryConfiguration
    {
        public readonly TimeSpan RefreshInterval;
        public readonly int MaxBufferSize;
        public readonly bool WaitForNonStale;

        public RavenDbQueryConfiguration(Config config)
        {
            RefreshInterval = config.GetTimeSpan("refresh-interval", @default: TimeSpan.FromSeconds(3));
            MaxBufferSize = config.GetInt("max-buffer-size", @default: 64 * 1024);
            WaitForNonStale = config.GetBoolean("wait-for-non-stale");
        }
    }

    public class RavenDbSnapshotConfiguration
    {
        public RavenDbSnapshotConfiguration(Config config)
        {
        }
    }
}
