using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Akka.Persistence.RavenDb.Hosting;
using FluentAssertions;
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
            journalStore.Instance.Certificate.Should().BeNull();
            journalStore.Instance.Conventions.SaveEnumsAsIntegers.Should().Be(false);
            journalStore.Instance.Conventions.LoadBalanceBehavior.Should().Be(LoadBalanceBehavior.UseSessionContext);
            journalStore.Instance.Conventions.HttpVersion.Should().Be(new Version(2, 0));
        }

        [Fact]
        public async Task RavenDbPersistenceWithCertificateInstance()
        {
            var journalCert = new X509Certificate2(TestDriverExtension.CertificatePath, TestDriverExtension.CertificatePassword);
            var snapshotCert = new X509Certificate2(TestDriverExtension.CertificatePath, TestDriverExtension.CertificatePassword);
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
            journalStore.Instance.Certificate.Should().NotBeNull();
            journalStore.Instance.Certificate.Should().Be(journalCert);
            journalStore.Instance.Conventions.SaveEnumsAsIntegers.Should().Be(false);
            journalStore.Instance.Conventions.LoadBalanceBehavior.Should().Be(LoadBalanceBehavior.UseSessionContext);
            journalStore.Instance.Conventions.HttpVersion.Should().Be(new Version(2, 0));

            var snapshotStore = new RavenDbStore(persistence.SnapshotConfiguration);
            snapshotStore.Instance.Certificate.Should().NotBeNull();
            snapshotStore.Instance.Certificate.Should().Be(snapshotCert);
            snapshotStore.Instance.Conventions.DisableTopologyCache.Should().Be(true);
            snapshotStore.Instance.Conventions.HttpVersion.Should().Be(new Version(1, 0));
        }

        [Fact]
        public async Task RavenDbPersistenceWithCertificatePath()
        {
            var cert = new X509Certificate2(TestDriverExtension.CertificatePath, TestDriverExtension.CertificatePassword);
            
            using var host = new HostBuilder()
                .ConfigureServices((context, services) =>
                {
                    services.AddAkka(_actorSystemName, (builder, provider) =>
                    {
                        builder.WithRavenDbPersistence(
                            urls: new[] { "https://localhost:8080" },
                            databaseName: _databaseName,
                            certificatePath: TestDriverExtension.CertificatePath,
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
            journalStore.Instance.Certificate.Should().NotBeNull();
            journalStore.Instance.Certificate.Should().Be(cert);
            journalStore.Instance.Conventions.SaveEnumsAsIntegers.Should().Be(false);
            journalStore.Instance.Conventions.LoadBalanceBehavior.Should().Be(LoadBalanceBehavior.UseSessionContext);
            journalStore.Instance.Conventions.HttpVersion.Should().Be(new Version(2, 0));

            var snapshotStore = new RavenDbStore(persistence.SnapshotConfiguration);
            snapshotStore.Instance.Certificate.Should().NotBeNull();
            snapshotStore.Instance.Certificate.Should().Be(cert);
            snapshotStore.Instance.Conventions.SaveEnumsAsIntegers.Should().Be(false);
            snapshotStore.Instance.Conventions.LoadBalanceBehavior.Should().Be(LoadBalanceBehavior.UseSessionContext);
            snapshotStore.Instance.Conventions.HttpVersion.Should().Be(new Version(2, 0));
        }

        [Fact]
        public async Task RavenDbPersistenceWithBothCertificatePathAndInstanceShouldThrow()
        {
            var cert = new X509Certificate2(TestDriverExtension.CertificatePath, TestDriverExtension.CertificatePassword);

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
    }
}
