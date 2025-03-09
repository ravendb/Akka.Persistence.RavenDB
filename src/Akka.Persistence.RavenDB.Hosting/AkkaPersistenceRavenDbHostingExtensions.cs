﻿using System.IO;
using System.Security.Cryptography.X509Certificates;
using Akka.Actor;
using Akka.Hosting;
using Akka.Persistence.Hosting;
using Raven.Client.Documents.Conventions;

namespace Akka.Persistence.RavenDb.Hosting
{
    public static class AkkaPersistenceRavenDbHostingExtensions
    {
        /// <summary>
        ///     Adds Akka.Persistence.RavenDb support to this <see cref="ActorSystem"/>.
        /// </summary>
        /// <param name="builder">
        ///     The builder instance being configured.
        /// </param>
        /// <param name="urls">
        ///     An array of server urls where the RavenDb database is stored
        /// </param>
        /// <param name="autoInitialize">
        ///     <para>
        ///         Should the RavenDb database be created automatically.
        ///         If the database already exists, will do nothing.
        ///     </para>
        ///     <i>Default</i>: <c>true</c>
        /// </param>
        /// <param name="databaseName">
        ///     The name of the database where the persistence data should be stored
        /// </param>
        /// <param name="certificatePath">
        ///     Path to the client certificate of the secure RavenDB database.
        ///     <i>Default</i>: <c>null</c>
        /// </param>
        /// <param name="certificatePassword">
        ///     Password of the certificate.
        ///     <i>Default</i>: <c>null</c>
        /// </param>
        /// <param name="modifyDocumentConventions">
        ///     Allow to configure Conventions for the RavenDB client.
        ///     Conventions set here take precedence over other options in this class.
        ///     <i>Default</i>: <c>null</c>
        /// </param>
        /// <param name="mode">
        ///     <para>
        ///         Determines which settings should be added by this method call.
        ///     </para>
        ///     <i>Default</i>: <see cref="PersistenceMode.Both"/>
        /// </param>
        /// <param name="journalBuilder">
        ///     <para>
        ///         An <see cref="Action{T}"/> used to configure an <see cref="AkkaPersistenceJournalBuilder"/> instance.
        ///     </para>
        ///     <i>Default</i>: <c>null</c>
        /// </param>
        /// <param name="pluginIdentifier">
        ///     <para>
        ///         The configuration identifier for the plugins
        ///     </para>
        ///     <i>Default</i>: <c>"ravendb"</c>
        /// </param>
        /// <param name="isDefaultPlugin">
        ///     <para>
        ///         A <c>bool</c> flag to set the plugin as the default persistence plugin for the <see cref="ActorSystem"/>
        ///     </para>
        ///     <b>Default</b>: <c>true</c>
        /// </param>
        /// <returns>
        ///     The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.
        /// </returns>
        /// <exception cref="ArgumentOutOfRangeException">
        ///     Thrown when <see cref="journalBuilder"/> is set and <see cref="mode"/> is set to
        ///     <see cref="PersistenceMode.SnapshotStore"/>
        /// </exception>
        public static AkkaConfigurationBuilder WithRavenDbPersistence(
            this AkkaConfigurationBuilder builder,
            string[] urls,
            string databaseName,
            string certificatePath,
            string certificatePassword = null,
            Action<DocumentConventions>? modifyDocumentConventions = null,
            PersistenceMode mode = PersistenceMode.Both,
            Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
            bool autoInitialize = true,
            string pluginIdentifier = "ravendb",
            bool isDefaultPlugin = true)
        {
            if (mode == PersistenceMode.SnapshotStore && journalBuilder is { })
                throw new Exception($"{nameof(journalBuilder)} can only be set when {nameof(mode)} is set to either {PersistenceMode.Both} or {PersistenceMode.Journal}");

            var journalOpt = new RavenDbJournalOptions(isDefaultPlugin, pluginIdentifier)
            {
                Urls = urls,
                Name = databaseName,
                CertificatePath = certificatePath,
                ModifyDocumentConventions = modifyDocumentConventions,
                AutoInitialize = autoInitialize,
            };

            var adapters = new AkkaPersistenceJournalBuilder(journalOpt.Identifier, builder);
            journalBuilder?.Invoke(adapters);
            journalOpt.Adapters = adapters;

            var snapshotOpt = new RavenDbSnapshotOptions(isDefaultPlugin, pluginIdentifier)
            {
                Urls = urls,
                Name = databaseName,
                CertificatePath = certificatePath,
                ModifyDocumentConventions = modifyDocumentConventions,
                AutoInitialize = autoInitialize,
            };
            
            if (string.IsNullOrEmpty(certificatePassword) == false)
                Environment.SetEnvironmentVariable(RavenDbConfiguration.CertificatePasswordVariable, certificatePassword);

            return mode switch
            {
                PersistenceMode.Journal => builder.WithRavenDbPersistence(journalOpt, null),
                PersistenceMode.SnapshotStore => builder.WithRavenDbPersistence(null, snapshotOpt),
                PersistenceMode.Both => builder.WithRavenDbPersistence(journalOpt, snapshotOpt),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid PersistenceMode defined.")
            };
        }

        ///<inheritdoc cref="WithRavenDbPersistence(AkkaConfigurationBuilder, string[], string, string, string,Action{DocumentConventions}, PersistenceMode,Action{AkkaPersistenceJournalBuilder},bool,string,bool)"/>
        public static AkkaConfigurationBuilder WithRavenDbPersistence(
            this AkkaConfigurationBuilder builder,
            string[] urls,
            string databaseName,
            X509Certificate2 certificate,
            Action<DocumentConventions>? modifyDocumentConventions = null,
            PersistenceMode mode = PersistenceMode.Both,
            Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
            bool autoInitialize = true,
            string pluginIdentifier = "ravendb",
            bool isDefaultPlugin = true)
        {
            if (mode == PersistenceMode.SnapshotStore && journalBuilder is { })
                throw new Exception($"{nameof(journalBuilder)} can only be set when {nameof(mode)} is set to either {PersistenceMode.Both} or {PersistenceMode.Journal}");
            
            var journalOpt = new RavenDbJournalOptions(isDefaultPlugin, pluginIdentifier)
            {
                Urls = urls,
                Name = databaseName,
                Certificate = certificate,
                ModifyDocumentConventions = modifyDocumentConventions,
                AutoInitialize = autoInitialize,
            };

            var adapters = new AkkaPersistenceJournalBuilder(journalOpt.Identifier, builder);
            journalBuilder?.Invoke(adapters);
            journalOpt.Adapters = adapters;

            var snapshotOpt = new RavenDbSnapshotOptions(isDefaultPlugin, pluginIdentifier)
            {
                Urls = urls,
                Name = databaseName,
                Certificate = certificate,
                ModifyDocumentConventions = modifyDocumentConventions,
                AutoInitialize = autoInitialize,
            };

            return mode switch
            {
                PersistenceMode.Journal => builder.WithRavenDbPersistence(journalOpt, null),
                PersistenceMode.SnapshotStore => builder.WithRavenDbPersistence(null, snapshotOpt),
                PersistenceMode.Both => builder.WithRavenDbPersistence(journalOpt, snapshotOpt),
                _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Invalid PersistenceMode defined.")
            };
        }

        ///<inheritdoc cref="WithRavenDbPersistence(AkkaConfigurationBuilder, string[], string, string, string,Action{DocumentConventions}, PersistenceMode,Action{AkkaPersistenceJournalBuilder},bool,string,bool)"/>
        public static AkkaConfigurationBuilder WithRavenDbPersistence(
            this AkkaConfigurationBuilder builder,
            string[] urls,
            string databaseName,
            Action<DocumentConventions>? modifyDocumentConventions = null,
            PersistenceMode mode = PersistenceMode.Both,
            Action<AkkaPersistenceJournalBuilder>? journalBuilder = null,
            bool autoInitialize = true,
            string pluginIdentifier = "ravendb",
            bool isDefaultPlugin = true)
        {
            return WithRavenDbPersistence(builder, urls, databaseName, certificate: null, modifyDocumentConventions, mode, journalBuilder,
                autoInitialize, pluginIdentifier, isDefaultPlugin);
        }

        /// <summary>
        ///     Adds Akka.Persistence.RavenDb support to this <see cref="ActorSystem"/>. At least one of the
        ///     configurator delegate needs to be populated else this method will throw an exception.
        /// </summary>
        /// <param name="builder">
        ///     The builder instance being configured.
        /// </param>
        /// <param name="journalOptionConfigurator">
        ///     <para>
        ///         An <see cref="Action{T}"/> that modifies an instance of <see cref="RavenDbJournalOptions"/>,
        ///         used to configure the journal plugin
        ///     </para>
        ///     <i>Default</i>: <c>null</c>
        /// </param>
        /// <param name="snapshotOptionConfigurator">
        ///     <para>
        ///         An <see cref="Action{T}"/> that modifies an instance of <see cref="RavenDbSnapshotOptions"/>,
        ///         used to configure the snapshot store plugin
        ///     </para>
        ///     <i>Default</i>: <c>null</c>
        /// </param>
        /// <param name="isDefaultPlugin">
        ///     <para>
        ///         A <c>bool</c> flag to set the plugin as the default persistence plugin for the <see cref="ActorSystem"/>
        ///     </para>
        ///     <b>Default</b>: <c>true</c>
        /// </param>
        /// <returns>
        ///     The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.
        /// </returns>
        /// <exception cref="ArgumentException">
        ///     Thrown when both <paramref name="journalOptionConfigurator"/> and <paramref name="snapshotOptionConfigurator"/> are null.
        /// </exception>
        public static AkkaConfigurationBuilder WithRavenDbPersistence(
            this AkkaConfigurationBuilder builder,
            Action<RavenDbJournalOptions>? journalOptionConfigurator = null,
            Action<RavenDbSnapshotOptions>? snapshotOptionConfigurator = null,
            bool isDefaultPlugin = true)
        {
            if (journalOptionConfigurator is null && snapshotOptionConfigurator is null)
                throw new ArgumentException($"{nameof(journalOptionConfigurator)} and {nameof(snapshotOptionConfigurator)} could not both be null");

            RavenDbJournalOptions? journalOptions = null;
            if (journalOptionConfigurator is { })
            {
                journalOptions = new RavenDbJournalOptions(isDefaultPlugin);
                journalOptionConfigurator(journalOptions);
            }

            RavenDbSnapshotOptions? snapshotOptions = null;
            if (snapshotOptionConfigurator is { })
            {
                snapshotOptions = new RavenDbSnapshotOptions(isDefaultPlugin);
                snapshotOptionConfigurator(snapshotOptions);
            }

            return builder.WithRavenDbPersistence(journalOptions, snapshotOptions);
        }

        /// <summary>
        ///     Adds Akka.Persistence.RavenDb support to this <see cref="ActorSystem"/>. At least one of the options
        ///     have to be populated else this method will throw an exception.
        /// </summary>
        /// <param name="builder">
        ///     The builder instance being configured.
        /// </param>
        /// <param name="journalOptions">
        ///     <para>
        ///         An instance of <see cref="RavenDbJournalOptions"/>, used to configure the journal plugin
        ///     </para>
        ///     <i>Default</i>: <c>null</c>
        /// </param>
        /// <param name="snapshotOptions">
        ///     <para>
        ///         An instance of <see cref="RavenDbSnapshotOptions"/>, used to configure the snapshot store plugin
        ///     </para>
        ///     <i>Default</i>: <c>null</c>
        /// </param>
        /// <returns>
        ///     The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.
        /// </returns>
        /// <exception cref="ArgumentException">
        ///     Thrown when both <paramref name="journalOptions"/> and <paramref name="snapshotOptions"/> are null.
        /// </exception>
        public static AkkaConfigurationBuilder WithRavenDbPersistence(
            this AkkaConfigurationBuilder builder,
            RavenDbJournalOptions? journalOptions = null,
            RavenDbSnapshotOptions? snapshotOptions = null)
        {
            if (journalOptions is null && snapshotOptions is null)
                throw new ArgumentException($"{nameof(journalOptions)} and {nameof(snapshotOptions)} could not both be null");

            return (journalOptions, snapshotOptions) switch
            {
                (null, null) =>
                    throw new ArgumentException($"{nameof(journalOptions)} and {nameof(snapshotOptions)} could not both be null"),

                (_, null) =>
                    builder.WithRavenDbPersistence(journalOptions),

                (null, _) =>
                    builder.WithRavenDbPersistence(snapshotOptions),

                (_, _) =>
                    builder
                        .WithRavenDbPersistence(journalOptions)
                        .WithRavenDbPersistence(snapshotOptions)
            };
        }

        /// <summary>
        ///     Add an RavenDB journal Akka.Persistence implementations for a given <see cref="ActorSystem"/>.
        /// </summary>
        /// <param name="builder">
        ///     The <see cref="AkkaConfigurationBuilder"/> builder instance being configured.
        /// </param>
        /// <param name="options">
        ///     An <see cref="RavenDbJournalOptions"/> instance that will be used to set up
        ///     the RavenDb journal.
        /// </param>
        /// <returns>
        ///     The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.
        /// </returns>
        public static AkkaConfigurationBuilder WithRavenDbPersistence(
            this AkkaConfigurationBuilder builder,
            RavenDbJournalOptions options)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));

            builder.AddHocon(options.ToConfig(), HoconAddMode.Prepend);
            options.Apply(builder);
            builder.AddHocon(options.DefaultConfig, HoconAddMode.Append);

            // Need to add persistence default settings to make sure that persistence message serializer is loaded properly
            builder.AddHocon(RavenDbPersistence.DefaultConfiguration(), HoconAddMode.Append);

            return builder;
        }

        /// <summary>
        ///     Add an RavenDB snapshot store Akka.Persistence implementations for a given <see cref="ActorSystem"/>.
        /// </summary>
        /// <param name="builder">
        ///     The <see cref="AkkaConfigurationBuilder"/> builder instance being configured.
        /// </param>
        /// <param name="options">
        ///     An <see cref="RavenDbSnapshotOptions"/> instance that will be used to set up
        ///     the RavenDb snapshot store.
        /// </param>
        /// <returns>
        ///     The same <see cref="AkkaConfigurationBuilder"/> instance originally passed in.
        /// </returns>
        public static AkkaConfigurationBuilder WithRavenDbPersistence(
            this AkkaConfigurationBuilder builder,
            RavenDbSnapshotOptions options)
        {
            if (options is null)
                throw new ArgumentNullException(nameof(options));

            builder.AddHocon(options.ToConfig(), HoconAddMode.Prepend);
            options.Apply(builder);
            builder.AddHocon(options.DefaultConfig, HoconAddMode.Append);

            // Need to add persistence default settings to make sure that persistence message serializer is loaded properly
            builder.AddHocon(RavenDbPersistence.DefaultConfiguration(), HoconAddMode.Append);

            return builder;
        }
    }
}
