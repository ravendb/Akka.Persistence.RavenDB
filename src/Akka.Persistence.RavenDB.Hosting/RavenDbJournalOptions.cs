using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Raven.Client.Documents.Conventions;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace Akka.Persistence.RavenDb.Hosting
{
    public sealed class RavenDbJournalOptions : JournalOptions, IRavenDbOptions
    {
        private static readonly Config Default = RavenDbPersistence.DefaultConfiguration()
            .GetConfig(RavenDbJournalConfiguration.Identifier);

        public RavenDbJournalOptions() : this(true)
        {
        }

        public RavenDbJournalOptions(bool isDefault, string identifier = "ravendb") : base(isDefault)
        {
            Identifier = identifier;
        }

        public string? Name { get; set; }
        public string[] Urls { get; set; }
        public string? CertificatePath { get; set; }
        public X509Certificate2? Certificate { get; set; }
        public Version? HttpVersion { get; set; }
        public bool? DisableTcpCompression { get; set; }
        public TimeSpan? SaveChangesTimeout { get; set; }
        public override string Identifier { get; set; }
        protected override Config InternalDefaultConfig { get; } = Default;
        public Action<DocumentConventions>? ModifyDocumentConventions { get; set; }

        protected override StringBuilder Build(StringBuilder sb)
        {
            RavenDbOptions.Build(sb, this);
            return base.Build(sb);
        }

        public void Apply(AkkaConfigurationBuilder builder)
        {
            if (string.IsNullOrEmpty(CertificatePath) == false && Certificate != null)
            {
                throw new ConfigurationException(
                    $"Both {nameof(CertificatePath)} and {nameof(Certificate)} were provided for the same store. Only one must be provided.");
            }

            var setup = builder.Setups.FirstOrDefault(s => s is RavenDbJournalSetup) as RavenDbJournalSetup;
            setup ??= new RavenDbJournalSetup();

            setup.Certificate = Certificate;
            setup.ModifyDocumentConventions = ModifyDocumentConventions;

            builder.AddSetup(setup);
        }
    }
}
