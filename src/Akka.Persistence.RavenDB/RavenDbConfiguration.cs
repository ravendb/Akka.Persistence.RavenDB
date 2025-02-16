using System.ComponentModel;
using System.Security.Cryptography.X509Certificates;
using Akka.Configuration;
using Raven.Client.Documents.Conventions;

namespace Akka.Persistence.RavenDb
{
    public abstract class RavenDbConfiguration
    {
        public const string CertificatePathVariable = "RAVEN_Security_Certificate_Path";
        public const string CertificateBase64Variable = "RAVEN_Security_Certificate";
        public const string CertificatePasswordVariable = "RAVEN_Security_Certificate_Password";

        public readonly string Name;
        public readonly string[] Urls;
        public readonly string CertificatePath;
        public readonly Version? HttpVersion;
        public readonly bool DisableTcpCompression;
        public readonly TimeSpan SaveChangesTimeout;
        private readonly Config _conventions;

        /// <summary>
        /// Flag determining whether the database should be automatically initialized.
        /// </summary>
        public bool AutoInitialize { get; private set; }

        protected RavenDbConfiguration(Config config)
        {
            Name = config.GetString("name") ?? throw new ArgumentException("name must be provided");
            Urls = config.GetStringList("urls")?.ToArray() ?? throw new ArgumentException("urls must be provided");
            CertificatePath = config.GetString("certificate-path");
            AutoInitialize = config.GetBoolean("auto-initialize", true);
            _conventions = config.GetConfig("conventions");

            var httpVersion = config.GetString("http-version", "2.0");
            //TODO stav: error gets swallowed in akka and doesn't bubble up
            HttpVersion = Version.Parse(httpVersion);
            
            DisableTcpCompression = config.GetBoolean("disable-tcp-compression");
            SaveChangesTimeout = config.GetTimeSpan("save-changes-timeout", TimeSpan.FromSeconds(30));
        }

        public X509Certificate2 GetCertificate()
        {
            var password = Environment.GetEnvironmentVariable(CertificatePasswordVariable) ?? Environment.GetEnvironmentVariable("RAVEN_CERTIFICATE_PASSWORD");
            var certificate = Environment.GetEnvironmentVariable(CertificateBase64Variable);
            var path = Environment.GetEnvironmentVariable(CertificatePathVariable) ?? CertificatePath;

            if (string.IsNullOrEmpty(path) == false)
                return new X509Certificate2(path, password);

            if (string.IsNullOrEmpty(certificate) == false)
                return new X509Certificate2(Convert.FromBase64String(certificate), password);

            return null;
        }

        public DocumentConventions ToDocumentConventions()
        {
            var conventions = new DocumentConventions
            {
                HttpVersion = HttpVersion, 
                DisableTcpCompression = DisableTcpCompression
            };

            if (_conventions is { IsEmpty: false })
            {
                foreach (var option in _conventions.AsEnumerable())
                {
                    var key = option.Key;
                    var value = option.Value.GetString();
                    var property = typeof(DocumentConventions).GetProperty(key);
                    if (property == null)
                        throw new NotSupportedException($"The property {key} is not supported in 'DocumentConventions', please check casing.");

                    if (value == "null")
                        value = null;

                    var c = ConvertDocumentConvention(value, property.PropertyType);
                    property.SetValue(conventions, c);
                }
            }
            
            
            return conventions;
        }

        public static object ConvertDocumentConvention(string input, Type type)
        {
            try
            {
                var converter = TypeDescriptor.GetConverter(type);
                return converter.ConvertFromString(input);
            }
            catch (NotSupportedException)
            {
                return null;
            }
        }
    }

    public class RavenDbJournalConfiguration : RavenDbConfiguration
    {
        public const string Identifier = "akka.persistence.journal.ravendb";
        public RavenDbJournalConfiguration(Config config) : base(config)
        {
            if (config == null)
                throw new ArgumentNullException("config",
                    "RavenDB journal settings cannot be initialized, because required HOCON section couldn't been found");
        }
    }

    public class RavenDbSnapshotConfiguration : RavenDbConfiguration
    {
        public const string Identifier = "akka.persistence.snapshot-store.ravendb";

        public RavenDbSnapshotConfiguration(Config config) : base(config)
        {
            if (config == null)
                throw new ArgumentNullException("config",
                    "RavenDB snapshot settings cannot be initialized, because required HOCON section couldn't been found");
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
