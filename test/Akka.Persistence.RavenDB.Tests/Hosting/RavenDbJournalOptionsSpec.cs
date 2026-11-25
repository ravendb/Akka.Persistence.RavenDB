using System.Net;
using Akka.Configuration;
using Akka.Persistence.RavenDb.Hosting;
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

            Assert.Equal("akka.persistence.journal.ravendb", config.GetString("akka.persistence.journal.plugin"));
            Assert.True(config.HasPath("akka.persistence.journal.ravendb"));
        }

        [Fact(DisplayName = "Empty RavenDbJournalOptions should equal empty config with default fallback")]
        public void DefaultJournalOptionsTest()
        {
            var options = new RavenDbJournalOptions(false);
            var emptyRootConfig = options.ToConfig().WithFallback(options.DefaultConfig);
            var baseRootConfig = Config.Empty
                .WithFallback(RavenDbPersistence.DefaultConfiguration());

            Assert.Equal(baseRootConfig.GetString("akka.persistence.journal.plugin"), emptyRootConfig.GetString("akka.persistence.journal.plugin"));

            var config = emptyRootConfig.GetConfig("akka.persistence.journal.ravendb");
            var baseConfig = baseRootConfig.GetConfig("akka.persistence.journal.ravendb");
            Assert.NotNull(config);
            Assert.NotNull(baseConfig);

            Assert.Equal(baseConfig.GetString("class"), config.GetString("class"));
            Assert.Equal(baseConfig.GetBoolean("auto-initialize"), config.GetBoolean("auto-initialize"));
            Assert.Equal(baseConfig.GetString("plugin-dispatcher"), config.GetString("plugin-dispatcher"));
            Assert.Equal(baseConfig.GetString("name"), config.GetString("name"));
            Assert.Equal(baseConfig.GetStringList("urls"), config.GetStringList("urls"));
            Assert.Equal(baseConfig.GetString("certificate-path"), config.GetString("certificate-path"));
            Assert.Equal(baseConfig.GetString("http-version"), config.GetString("http-version"));
            Assert.Equal(baseConfig.GetBoolean("disable-tcp-compression"), config.GetBoolean("disable-tcp-compression"));
            Assert.Equal(baseConfig.GetTimeSpan("save-changes-timeout"), config.GetTimeSpan("save-changes-timeout"));
        }

        [Fact(DisplayName = "Empty RavenDbJournalOptions with custom identifier should equal empty config with default fallback")]
        public void CustomIdJournalOptionsTest()
        {
            var options = new RavenDbJournalOptions(false, "custom");
            var emptyRootConfig = options.ToConfig().WithFallback(options.DefaultConfig);
            var baseRootConfig = Config.Empty
                .WithFallback(RavenDbPersistence.DefaultConfiguration());

            Assert.Equal(baseRootConfig.GetString("akka.persistence.journal.plugin"), emptyRootConfig.GetString("akka.persistence.journal.plugin"));

            var config = emptyRootConfig.GetConfig("akka.persistence.journal.custom");
            var baseConfig = baseRootConfig.GetConfig("akka.persistence.journal.ravendb");
            Assert.NotNull(config);
            Assert.NotNull(baseConfig);

            Assert.Equal(baseConfig.GetString("class"), config.GetString("class"));
            Assert.Equal(baseConfig.GetBoolean("auto-initialize"), config.GetBoolean("auto-initialize"));
            Assert.Equal(baseConfig.GetString("plugin-dispatcher"), config.GetString("plugin-dispatcher"));
            Assert.Equal(baseConfig.GetString("name"), config.GetString("name"));
            Assert.Equal(baseConfig.GetStringList("urls"), config.GetStringList("urls"));
            Assert.Equal(baseConfig.GetString("certificate-path"), config.GetString("certificate-path"));
            Assert.Equal(baseConfig.GetString("http-version"), config.GetString("http-version"));
            Assert.Equal(baseConfig.GetBoolean("disable-tcp-compression"), config.GetBoolean("disable-tcp-compression"));
            Assert.Equal(baseConfig.GetTimeSpan("save-changes-timeout"), config.GetTimeSpan("save-changes-timeout"));
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

            Assert.Equal("akka.persistence.journal.custom", baseConfig.GetString("akka.persistence.journal.plugin"));

            var config = baseConfig.GetConfig("akka.persistence.journal.custom");
            Assert.NotNull(config);
            Assert.Equal(options.AutoInitialize, config.GetBoolean("auto-initialize"));
            Assert.Equal(options.Urls, config.GetStringList("urls"));
            Assert.Equal(options.Name, config.GetString("name"));
            Assert.Equal(options.CertificatePath, config.GetString("certificate-path"));
            Assert.Equal(options.HttpVersion.ToString(), config.GetString("http-version"));
            Assert.Equal(options.DisableTcpCompression.Value, config.GetBoolean("disable-tcp-compression"));
            Assert.Equal(options.SaveChangesTimeout.Value, config.GetTimeSpan("save-changes-timeout"));
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
            Assert.Equal(new string[] {"http://localhost:8081"}, options.Urls);
            Assert.Equal("customravendb", options.Identifier);
            Assert.True(options.AutoInitialize);
            Assert.False(options.IsDefaultPlugin);
            Assert.Equal("CustomJournalDb", options.Name);
            Assert.Equal(@"C:\Work\Akka\cert.pfx", options.CertificatePath);
            Assert.Equal(HttpVersion.Version11, options.HttpVersion);
            Assert.Equal(TimeSpan.FromMinutes(10), options.SaveChangesTimeout);
            Assert.Equal("hyperion", options.Serializer);
            Assert.False(options.DisableTcpCompression);
        }
    }
}
