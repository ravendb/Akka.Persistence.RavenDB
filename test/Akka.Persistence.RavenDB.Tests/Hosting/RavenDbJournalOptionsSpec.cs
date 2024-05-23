using System.Net;
using Akka.Configuration;
using Akka.Persistence.RavenDb.Hosting;
using FluentAssertions;
using FluentAssertions.Extensions;
using Microsoft.Extensions.Configuration;
using System.Text;

namespace Akka.Persistence.RavenDb.Tests.Hosting
{
    public class RavenDbJournalOptionsSpec
    {
        [Fact(DisplayName = "RavenDbJournalOptions as default plugin should generate plugin setting")]
        public void DefaultPluginJournalOptionsTest()
        {
            var options = new RavenDbJournalOptions(true);
            var config = options.ToConfig();

            config.GetString("akka.persistence.journal.plugin").Should().Be("akka.persistence.journal.ravendb");
            config.HasPath("akka.persistence.journal.ravendb").Should().BeTrue();
        }

        [Fact(DisplayName = "Empty RavenDbJournalOptions should equal empty config with default fallback")]
        public void DefaultJournalOptionsTest()
        {
            var options = new RavenDbJournalOptions(false);
            var emptyRootConfig = options.ToConfig().WithFallback(options.DefaultConfig);
            var baseRootConfig = Config.Empty
                .WithFallback(RavenDbPersistence.DefaultConfiguration());

            emptyRootConfig.GetString("akka.persistence.journal.plugin").Should().Be(baseRootConfig.GetString("akka.persistence.journal.plugin"));

            var config = emptyRootConfig.GetConfig("akka.persistence.journal.ravendb");
            var baseConfig = baseRootConfig.GetConfig("akka.persistence.journal.ravendb");
            config.Should().NotBeNull();
            baseConfig.Should().NotBeNull();

            config.GetString("class").Should().Be(baseConfig.GetString("class"));
            config.GetBoolean("auto-initialize").Should().Be(baseConfig.GetBoolean("auto-initialize"));
            config.GetString("plugin-dispatcher").Should().Be(baseConfig.GetString("plugin-dispatcher"));
            config.GetString("name").Should().Be(baseConfig.GetString("name"));
            config.GetStringList("urls").Should().BeEquivalentTo(baseConfig.GetStringList("urls"));
            config.GetString("certificate-path").Should().Be(baseConfig.GetString("certificate-path"));
            config.GetString("http-version").Should().Be(baseConfig.GetString("http-version"));
            config.GetBoolean("disable-tcp-compression").Should().Be(baseConfig.GetBoolean("disable-tcp-compression"));
            config.GetTimeSpan("save-changes-timeout").Should().Be(baseConfig.GetTimeSpan("save-changes-timeout"));
        }

        [Fact(DisplayName = "Empty RavenDbJournalOptions with custom identifier should equal empty config with default fallback")]
        public void CustomIdJournalOptionsTest()
        {
            var options = new RavenDbJournalOptions(false, "custom");
            var emptyRootConfig = options.ToConfig().WithFallback(options.DefaultConfig);
            var baseRootConfig = Config.Empty
                .WithFallback(RavenDbPersistence.DefaultConfiguration());

            emptyRootConfig.GetString("akka.persistence.journal.plugin").Should().Be(baseRootConfig.GetString("akka.persistence.journal.plugin"));

            var config = emptyRootConfig.GetConfig("akka.persistence.journal.custom");
            var baseConfig = baseRootConfig.GetConfig("akka.persistence.journal.ravendb");
            config.Should().NotBeNull();
            baseConfig.Should().NotBeNull();

            config.GetString("class").Should().Be(baseConfig.GetString("class"));
            config.GetBoolean("auto-initialize").Should().Be(baseConfig.GetBoolean("auto-initialize"));
            config.GetString("plugin-dispatcher").Should().Be(baseConfig.GetString("plugin-dispatcher"));
            config.GetString("name").Should().Be(baseConfig.GetString("name"));
            config.GetStringList("urls").Should().BeEquivalentTo(baseConfig.GetStringList("urls"));
            config.GetString("certificate-path").Should().Be(baseConfig.GetString("certificate-path"));
            config.GetString("http-version").Should().Be(baseConfig.GetString("http-version"));
            config.GetBoolean("disable-tcp-compression").Should().Be(baseConfig.GetBoolean("disable-tcp-compression"));
            config.GetTimeSpan("save-changes-timeout").Should().Be(baseConfig.GetTimeSpan("save-changes-timeout"));
        }

        [Fact(DisplayName = "RavenDbJournalOptions should generate proper config")]
        public void JournalOptionsTest()
        {
            var options = new RavenDbJournalOptions(true)
            {
                Identifier = "custom",
                AutoInitialize = true,
                Urls = new string[] { "http://localhost:8080", "http://localhost:8081" },
                CertificatePath = @"C:\Work\Akka\cert.pfx",
                DisableTcpCompression = true,
                HttpVersion = new Version(2, 0),
                Name = "JournalTestDb",
                SaveChangesTimeout = TimeSpan.FromSeconds(10),
            };

            var baseConfig = options.ToConfig();

            baseConfig.GetString("akka.persistence.journal.plugin").Should().Be("akka.persistence.journal.custom");

            var config = baseConfig.GetConfig("akka.persistence.journal.custom");
            config.Should().NotBeNull();
            config.GetBoolean("auto-initialize").Should().Be(options.AutoInitialize);
            config.GetStringList("urls").Should().BeEquivalentTo(options.Urls);
            config.GetString("name").Should().Be(options.Name);
            config.GetString("certificate-path").Should().Be(options.CertificatePath);
            config.GetString("http-version").Should().Be(options.HttpVersion.ToString());
            config.GetBoolean("disable-tcp-compression").Should().Be(options.DisableTcpCompression.Value);
            config.GetTimeSpan("save-changes-timeout").Should().Be(options.SaveChangesTimeout.Value);
        }
        
        const string Json = @"
        {
          ""Akka"": {
            ""JournalOptions"": {
              ""Identifier"": ""customravendb"",
              ""AutoInitialize"": true,
              ""IsDefaultPlugin"": false,
              ""Name"": ""CustomJournalDb"",
              ""Urls"": [""http://localhost:8081""],
              ""CertificatePath"": ""C:\\Work\\Akka\\cert.pfx"",
              ""HttpVersion"" : ""1.1"",
              ""SaveChangesTimeout"": ""00:10:00"",
              ""Serializer"": ""hyperion"",
              ""DisableTcpCompression"": false
            }
          }
        }";

        [Fact(DisplayName = "RavenDbJournalOptions should be bindable to IConfiguration")]
        public void JournalOptionsIConfigurationBindingTest()
        {
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(Json));
            var jsonConfig = new ConfigurationBuilder().AddJsonStream(stream).Build();

            var options = jsonConfig.GetSection("Akka:JournalOptions").Get<RavenDbJournalOptions>();
            options.Urls.Should().BeEquivalentTo(new string[] {"http://localhost:8081"});
            options.Identifier.Should().Be("customravendb");
            options.AutoInitialize.Should().BeTrue();
            options.IsDefaultPlugin.Should().BeFalse();
            options.Name.Should().Be("CustomJournalDb");
            options.CertificatePath.Should().Be(@"C:\Work\Akka\cert.pfx");
            options.HttpVersion.Should().Be(HttpVersion.Version11);
            options.SaveChangesTimeout.Should().Be(10.Minutes());
            options.Serializer.Should().Be("hyperion");
            options.DisableTcpCompression.Should().BeFalse();
        }
    }
}
