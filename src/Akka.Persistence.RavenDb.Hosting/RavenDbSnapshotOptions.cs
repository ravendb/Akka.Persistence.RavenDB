using Akka.Configuration;
using Akka.Persistence.Hosting;
using System.Text;

namespace Akka.Persistence.RavenDb.Hosting
{
    public sealed class RavenDbSnapshotOptions : SnapshotOptions
    {
        private static readonly Config Default = RavenDbPersistence.DefaultConfiguration()
            .GetConfig(RavenDbSnapshotConfiguration.Identifier);

        public RavenDbSnapshotOptions() : this(true)
        {
        }

        public RavenDbSnapshotOptions(bool isDefault, string identifier = "ravendb") : base(isDefault)
        {
            Identifier = identifier;
        }



        public override string Identifier { get; set; }
        protected override Config InternalDefaultConfig { get; } = Default;

        //TODO stav: there is an option to override isolation level

        protected override StringBuilder Build(StringBuilder sb)
        {
            //sb.AppendLine($"name = {Name.ToHocon()}");

            //sb.AppendLine($"urls = [{string.Join(',', Urls.Select(x => x.ToHocon()))}]");


            return base.Build(sb);
        }
    }
}