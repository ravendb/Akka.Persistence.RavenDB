using Akka.Configuration;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using System.Text;

namespace Akka.Persistence.RavenDb.Hosting
{
    public sealed class RavenDbJournalOptions : JournalOptions
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

        public string Name { get; set; }
        public string[] Urls { get; set; }
        public string? CertificatePath { get; set; }
        public Version? HttpVersion { get; set; }
        public bool? DisableTcpCompression { get; set; }
        public TimeSpan? SaveChangesTimeout { get; set; }

        public override string Identifier { get; set; }
        protected override Config InternalDefaultConfig { get; } = Default;

        //TODO stav: there is an option to override isolation level

        protected override StringBuilder Build(StringBuilder sb)
        {
            sb.AppendLine($"name = {Name.ToHocon()}");

            sb.AppendLine($"urls = [{string.Join(',', Urls.Select(x => x.ToHocon()))}]");

            if (CertificatePath is not null)
                sb.AppendLine($"certificate-path = {CertificatePath.ToHocon()}");

            if (HttpVersion is not null)
                sb.AppendLine($"http-version = {HttpVersion.ToString().ToHocon()}"); //TODO stav: check if works

            if (DisableTcpCompression is not null)
                sb.AppendLine($"disable-tcp-compression = {DisableTcpCompression.ToHocon()}");

            if (SaveChangesTimeout is not null)
                sb.AppendLine($"save-changes-timeout = {SaveChangesTimeout.ToHocon(allowInfinite: true)}"); //TODO stav: zeroIsInfinite?

            return base.Build(sb);
        }
    }
}
