using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Akka.Persistence.Journal;
using Akka.Persistence.RavenDb.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Raven.Client.Http;
using System.Security.Cryptography.X509Certificates;
using Akka.Configuration;

namespace Akka.Persistence.RavenDb.Tests.Hosting
{
    public class RavenDbPersistenceHostingConfigurationTests : IClassFixture<RavenDbFixture>
    {
        private string _databaseName = $"{nameof(RavenDbPersistenceHostingConfigurationTests)}_db";
        private string _actorSystemName = $"{nameof(RavenDbPersistenceHostingConfigurationTests)}ActorSystem";

        [Fact]
        public async Task RavenDbBasicPersistence()
        {
            using var host = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddAkka(_actorSystemName, (builder, provider) =>
                    {
                        builder.WithRavenDbPersistence(
                            urls: new []{ "http://localhost:8080"},
                            databaseName: _databaseName,
                            modifyDocumentConventions: conventions =>
                            {
                                conventions.SaveEnumsAsIntegers = false;
                                conventions.LoadBalanceBehavior = LoadBalanceBehavior.UseSessionContext;
                                conventions.HttpVersion = new Version(2, 0);
                            },
                            mode: PersistenceMode.Journal);
                        builder.WithRavenDbPersistence(
                            urls: new[] { "http://localhost:8081/" },
                            databaseName: _databaseName,
                            mode: PersistenceMode.SnapshotStore);
                    });
                }).Build();

            var actorSystem = host.Services.GetRequiredService<ActorSystem>();
            var persistence = RavenDbPersistence.Get(actorSystem);
            
            var journalStore = new RavenDbStore(persistence.JournalConfiguration);
            Assert.Null(journalStore.Instance.Certificate);
            Assert.False(journalStore.Instance.Conventions.SaveEnumsAsIntegers);
            Assert.Equal(LoadBalanceBehavior.UseSessionContext, journalStore.Instance.Conventions.LoadBalanceBehavior);
            Assert.Equal(new Version(2, 0), journalStore.Instance.Conventions.HttpVersion);
        }

        [Fact]
        public async Task RavenDbPersistenceWithCertificateInstance()
        {
            var journalCert = X509CertificateLoader.LoadPkcs12FromFile(TestDriverExtension.CertificatePath, TestDriverExtension.CertificatePassword);
            var snapshotCert = X509CertificateLoader.LoadPkcs12FromFile(TestDriverExtension.CertificatePath, TestDriverExtension.CertificatePassword);
            using var host = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddAkka(_actorSystemName, (builder, provider) =>
                    {
                        builder.WithRavenDbPersistence(
                            urls: new[] { "https://localhost:8080" },
                            databaseName: _databaseName,
                            certificate: journalCert,
                            modifyDocumentConventions: conventions =>
                            {
                                conventions.SaveEnumsAsIntegers = false;
                                conventions.LoadBalanceBehavior = LoadBalanceBehavior.UseSessionContext;
                                conventions.HttpVersion = new Version(2, 0);
                            },
                            mode: PersistenceMode.Journal);
                        builder.WithRavenDbPersistence(
                            urls: new[] { "https://localhost:8081/" },
                            databaseName: _databaseName,
                            certificate: snapshotCert,
                            modifyDocumentConventions: conventions =>
                            {
                                conventions.DisableTopologyCache = true;
                                conventions.HttpVersion = new Version(1, 0);
                            },
                            mode: PersistenceMode.SnapshotStore);
                    });
                }).Build();

            var actorSystem = host.Services.GetRequiredService<ActorSystem>();
            var persistence = RavenDbPersistence.Get(actorSystem);

            var journalStore = new RavenDbStore(persistence.JournalConfiguration);
            Assert.NotNull(journalStore.Instance.Certificate);
            Assert.Equal(journalCert, journalStore.Instance.Certificate);
            Assert.False(journalStore.Instance.Conventions.SaveEnumsAsIntegers);
            Assert.Equal(LoadBalanceBehavior.UseSessionContext, journalStore.Instance.Conventions.LoadBalanceBehavior);
            Assert.Equal(new Version(2, 0), journalStore.Instance.Conventions.HttpVersion);

            var snapshotStore = new RavenDbStore(persistence.SnapshotConfiguration);
            Assert.NotNull(snapshotStore.Instance.Certificate);
            Assert.Equal(snapshotCert, snapshotStore.Instance.Certificate);
            Assert.True(snapshotStore.Instance.Conventions.DisableTopologyCache);
            Assert.Equal(new Version(1, 0), snapshotStore.Instance.Conventions.HttpVersion);
        }

        [Fact]
        public async Task RavenDbPersistenceWithCertificatePath()
        {
            var cert = X509CertificateLoader.LoadPkcs12FromFile(TestDriverExtension.CertificatePath, TestDriverExtension.CertificatePassword);
            
            using var host = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddAkka(_actorSystemName, (builder, provider) =>
                    {
                        builder.WithRavenDbPersistence(
                            urls: new[] { "https://localhost:8080" },
                            databaseName: _databaseName,
                            certificatePath: TestDriverExtension.CertificatePath,
                            certificatePassword: TestDriverExtension.CertificatePassword,
                            modifyDocumentConventions: conventions =>
                            {
                                conventions.SaveEnumsAsIntegers = false;
                                conventions.LoadBalanceBehavior = LoadBalanceBehavior.UseSessionContext;
                                conventions.HttpVersion = new Version(2, 0);
                            },
                            mode: PersistenceMode.Both);
                    });
                }).Build();

            var actorSystem = host.Services.GetRequiredService<ActorSystem>();
            var persistence = RavenDbPersistence.Get(actorSystem);

            var journalStore = new RavenDbStore(persistence.JournalConfiguration);
            Assert.NotNull(journalStore.Instance.Certificate);
            Assert.Equal(cert, journalStore.Instance.Certificate);
            Assert.False(journalStore.Instance.Conventions.SaveEnumsAsIntegers);
            Assert.Equal(LoadBalanceBehavior.UseSessionContext, journalStore.Instance.Conventions.LoadBalanceBehavior);
            Assert.Equal(new Version(2, 0), journalStore.Instance.Conventions.HttpVersion);

            var snapshotStore = new RavenDbStore(persistence.SnapshotConfiguration);
            Assert.NotNull(snapshotStore.Instance.Certificate);
            Assert.Equal(cert, snapshotStore.Instance.Certificate);
            Assert.False(snapshotStore.Instance.Conventions.SaveEnumsAsIntegers);
            Assert.Equal(LoadBalanceBehavior.UseSessionContext, snapshotStore.Instance.Conventions.LoadBalanceBehavior);
            Assert.Equal(new Version(2, 0), snapshotStore.Instance.Conventions.HttpVersion);
        }

        [Fact]
        public async Task RavenDbPersistenceWithBothCertificatePathAndInstanceShouldThrow()
        {
            var cert = X509CertificateLoader.LoadPkcs12FromFile(TestDriverExtension.CertificatePath, TestDriverExtension.CertificatePassword);

            using var host = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddAkka(_actorSystemName, (builder, provider) =>
                    {
                        builder.WithRavenDbPersistence(new RavenDbJournalOptions()
                        {
                            Urls = new[] { "https://localhost:8080" },
                            Name = _databaseName,
                            CertificatePath = TestDriverExtension.CertificatePath,
                            Certificate = cert
                        });
                    });
                }).Build();

            var error = await Assert.ThrowsAsync<ConfigurationException>(async () => host.Services.GetRequiredService<ActorSystem>());
            Assert.Contains("one must be provided", error.Message);
        }

        [Fact]
        public async Task RavenDbPersistenceAppliesEventAdaptersFromJournalBuilder()
        {
            using var host = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddAkka(_actorSystemName, (builder, provider) =>
                    {
                        builder.WithRavenDbPersistence(
                            urls: ["http://localhost:8080"],
                            databaseName: _databaseName,
                            mode: PersistenceMode.Journal,
                            journalBuilder: journal => journal.AddWriteEventAdapter<TaggingEventAdapter>("tagger", [typeof(AdaptedEvent)]),
                            autoInitialize: false);
                    });
                }).Build();

            await AssertTaggingEventAdapterIsApplied(host.Services.GetRequiredService<ActorSystem>());
        }

        [Fact]
        public async Task RavenDbPersistenceWithOptionsAppliesEventAdaptersFromJournalBuilder()
        {
            using var host = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddAkka(_actorSystemName, (builder, provider) =>
                    {
                        builder.WithRavenDbPersistence(
                            journalOptions: new RavenDbJournalOptions
                            {
                                Urls = ["http://localhost:8080"],
                                Name = _databaseName,
                                AutoInitialize = false
                            },
                            journalBuilder: journal => journal.AddWriteEventAdapter<TaggingEventAdapter>("tagger", [typeof(AdaptedEvent)]));
                    });
                }).Build();

            await AssertTaggingEventAdapterIsApplied(host.Services.GetRequiredService<ActorSystem>());
        }

        private static async Task AssertTaggingEventAdapterIsApplied(ActorSystem actorSystem)
        {
            try
            {
                // Ask Akka which adapter the journal will use for this event type
                var adapter = Persistence.Instance.Apply(actorSystem).AdaptersFor("akka.persistence.journal.ravendb").Get(typeof(AdaptedEvent));

                var tagged = Assert.IsType<Tagged>(adapter.ToJournal(new AdaptedEvent()));
                Assert.Equal(TaggingEventAdapter.Tag, Assert.Single(tagged.Tags));
            }
            finally
            {
                await actorSystem.Terminate();
            }
        }

        public sealed record AdaptedEvent;

        public sealed class TaggingEventAdapter : IWriteEventAdapter
        {
            public const string Tag = "hosting-adapter";

            public string Manifest(object evt) => string.Empty;

            public object ToJournal(object evt) => new Tagged(evt, new[] { Tag });
        }
    }
}
