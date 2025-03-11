using Akka.Actor;
using Akka.Configuration;
using Raven.Client.Documents.Conventions;
using Raven.Client.Http;
using System.Security.Cryptography.X509Certificates;

namespace Akka.Persistence.RavenDb
{
    public abstract class RavenDbConfiguration
    {
        public const string CertificatePasswordVariable = "RAVEN_CERTIFICATE_PASSWORD";

        public readonly string Name;
        public readonly string[] Urls;
        public readonly string CertificatePath;
        public readonly X509Certificate2? Certificate;
        public readonly Version? HttpVersion;
        public readonly bool DisableTcpCompression;
        public readonly TimeSpan SaveChangesTimeout;
        public readonly Action<DocumentConventions>? ModifyDocumentConventions;

        /// <summary>
        /// Flag determining whether the database should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        protected RavenDbConfiguration(Config config, ActorSystem system)
        {
            Name = config.GetString("name") ?? throw new ArgumentException("name must be provided");
            Urls = config.GetStringList("urls")?.ToArray() ?? throw new ArgumentException("urls must be provided");
            CertificatePath = config.GetString("certificate-path");
            AutoInitialize = config.GetBoolean("auto-initialize", true);

            var httpVersion = config.GetString("http-version", "2.0");
            //TODO stav: error gets swallowed in akka and doesn't bubble up
            HttpVersion = Version.Parse(httpVersion);
            
            DisableTcpCompression = config.GetBoolean("disable-tcp-compression");
            SaveChangesTimeout = config.GetTimeSpan("save-changes-timeout", TimeSpan.FromSeconds(30));

            var setup = GetSetup(system);

            Certificate = GetCertificate(setup);
            ModifyDocumentConventions = setup.ModifyDocumentConventions;
        }

        private X509Certificate2 GetCertificate(RavenDbSetup setup)
        {
            if (setup.Certificate != null)
                return setup.Certificate;

            var password = Environment.GetEnvironmentVariable(CertificatePasswordVariable);
            
            if (string.IsNullOrEmpty(CertificatePath) == false)
                return new X509Certificate2(CertificatePath, password);

            if (Urls.Any(x => x.Contains("https")))
            {
                throw new ArgumentException("RavenDB is using https but no certificate was provided");
            }

            return null;
        }

        protected abstract RavenDbSetup GetSetup(ActorSystem system);

        public DocumentConventions ToDocumentConventions()
        {
            var conventions = new DocumentConventions
            {
                HttpVersion = HttpVersion, 
                DisableTcpCompression = DisableTcpCompression,
                LoadBalanceBehavior = LoadBalanceBehavior.UseSessionContext
            };

            ModifyDocumentConventions?.Invoke(conventions);
            
            return conventions;
        }
    }

    public class RavenDbJournalConfiguration : RavenDbConfiguration
    {
        public const string Identifier = "akka.persistence.journal.ravendb";
        public RavenDbJournalConfiguration(Config config, ActorSystem system) : base(config, system)
        {
            if (config == null)
                throw new ArgumentNullException("config",
                    "RavenDB journal settings cannot be initialized, because required HOCON section couldn't be found");
        }

        protected override RavenDbSetup GetSetup(ActorSystem system)
        {
            // Need to be careful since this is called from ctor
            var setupOption = system.Settings.Setup.Get<RavenDbJournalSetup>();
            if (setupOption.HasValue)
            {
                var setup = setupOption.Value;
                return setup;
            }

            return new RavenDbSetup();
        }
    }

    public class RavenDbSnapshotConfiguration : RavenDbConfiguration
    {
        public const string Identifier = "akka.persistence.snapshot-store.ravendb";

        public RavenDbSnapshotConfiguration(Config config, ActorSystem system) : base(config, system)
        {
            if (config == null)
                throw new ArgumentNullException("config",
                    "RavenDB snapshot settings cannot be initialized, because required HOCON section couldn't be found");
        }

        protected override RavenDbSetup GetSetup(ActorSystem system)
        {
            // Need to be careful since this is called from ctor
            var setupOption = system.Settings.Setup.Get<RavenDbSnapshotSetup>();
            if (setupOption.HasValue)
            {
                var setup = setupOption.Value;
                return setup;
            }

            return new RavenDbSetup();
        }
    }

    public class RavenDbQueryConfiguration
    {
        public const string Identifier = "akka.persistence.query.ravendb";

        public readonly TimeSpan RefreshInterval;
        public readonly int MaxBufferSize;
        public readonly bool WaitForNonStale;

        public RavenDbQueryConfiguration(Config config)
        {
            if (config == null)
                throw new ArgumentNullException("config",
                    "RavenDB query settings cannot be initialized, because required HOCON section couldn't been found");

            RefreshInterval = config.GetTimeSpan("refresh-interval", @default: TimeSpan.FromSeconds(3));
            MaxBufferSize = config.GetInt("max-buffer-size", @default: 64 * 1024);
            WaitForNonStale = config.GetBoolean("wait-for-non-stale");
        }
    }

}
