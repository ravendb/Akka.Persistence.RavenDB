using System.Linq.Expressions;
using Akka.Configuration;
using Akka.Persistence.Hosting;
using System.Text;
using Raven.Client.Documents.Conventions;

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
        public Version? HttpVersion { get; set; }
        public bool? DisableTcpCompression { get; set; }
        public TimeSpan? SaveChangesTimeout { get; set; }
        public override string Identifier { get; set; }
        protected override Config InternalDefaultConfig { get; } = Default;
        RavenDbConventions IRavenDbOptions.Conventions { get; set; }
        public void AddConvention<T>(Expression<Func<DocumentConventions, T>> path, T value) => RavenDbOptions.AddConvention(this, path, value);

        protected override StringBuilder Build(StringBuilder sb)
        {
            RavenDbOptions.Build(sb, this);
            return base.Build(sb);
        }
    }
}
