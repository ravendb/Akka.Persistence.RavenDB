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
        public readonly TimeSpan? RequestTimeout;
        public readonly bool WaitForNonStale;
        

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
                try
                {
                    HttpVersion = Version.Parse(httpVersion);
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException(
                        $"Could not parse 'http-version' configuration parameter value '{httpVersion}'.", e);
                    //TODO stav: error doesn't bubble up when running test - only shows when debugging
                }
            }
            //TODO should use defaults from the document conventions?
            DisableTcpCompression = config.GetBoolean("disable-tcp-compression");
            RequestTimeout = config.GetTimeSpan("request-timeout"); //example 20s
            WaitForNonStale = config.GetBoolean("wait-for-non-stale");
            
            //TODO stav: what about global-http-timeout
        }

        public DocumentConventions ToDocumentConventions()
        {
            var conventions = new DocumentConventions();

            conventions.HttpVersion = HttpVersion ?? conventions.HttpVersion;
            conventions.DisableTcpCompression = DisableTcpCompression ?? conventions.DisableTcpCompression;
            conventions.RequestTimeout = RequestTimeout == TimeSpan.Zero ? conventions.RequestTimeout : RequestTimeout;
            //conventions.Serialization = 

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

        public RavenDbQueryConfiguration(Config config)
        {
            RefreshInterval = config.GetTimeSpan("request-timeout", @default: TimeSpan.FromSeconds(5));
            MaxBufferSize = config.GetInt("max-buffer-size");
            if (MaxBufferSize == 0)
                MaxBufferSize = 64 * 1024;
        }
    }

    public class RavenDbSnapshotConfiguration
    {
        public RavenDbSnapshotConfiguration(Config config)
        {
        }
    }
}
