# Akka.Persistence.RavenDB

Akka Persistence journal and snapshot store backed by RavenDB database.

### Setup

To activate the journal plugin, add the following lines to actor system configuration file:

```
akka.persistence.journal.plugin = "akka.persistence.journal.ravendb"
akka.persistence.journal.ravendb.urls= ["<urls to the ravendb instace>"]
akka.persistence.journal.ravendb.name = "<database name for journals>"
```

Similar configuration may be used to setup a RavenDB snapshot store:

```
akka.persistence.snapshot-store.plugin = "akka.persistence.snapshot-store.ravendb"
akka.persistence.snapshot-store.ravendb.urls= ["<urls to the ravendb instace>"]
akka.persistence.snapshot-store.ravendb.name = "<database name for snapshots>"
```

The `urls` and the `name` can be identical but the configuration must be provided separately to Journal and Snapshot Store.

### Configuration

Both journal and snapshot store share the same configuration keys (however they resides in separate scopes, so they are definied distinctly for either journal or snapshot store):

## The Easy Way, Using `Akka.Hosting`
```csharp
var host = new HostBuilder()
    .ConfigureServices((context, services) => {
        services.AddAkka("my-system-name", (builder, provider) =>
        {
            builder.WithRavenDbPersistence(
                urls: new[] { "http://localhost:8080" },
                databaseName: "AkkaStorage");
        });
    })
```

## The Classic Way, Using HOCON
```hocon
akka.persistence {
	journal {
		ravendb {
			# qualified type name of the RavenDB persistence journal actor
			class = "Akka.Persistence.RavenDb.Journal.RavenDbJournal, Akka.Persistence.RavenDb"

			# dispatcher used to drive journal actor
			plugin-dispatcher = "akka.actor.default-dispatcher"

			# urls to the ravendb cluster
			urls = ["http://localhost:8080"]
		
			# database name where journal events will be stored
			name = "AkkaStorage"
			
			# create the database if it doesn't exists
			auto-initialize = false

			# Location of a client certificate to access a secure RavenDB database
			# if password required it should be stored in `RAVEN_CERTIFICATE_PASSWORD` env variable
			#certificate-path = "\\path\\to\\cert.pfx"

			# Timeout for 'save' requests sent to RavenDB, such as writing or deleting
			# as opposed to stream operations which may take longer and have a different timeout (12h).
			# Client will fail requests that take longer than this.
			# default: 30s
			#save-changes-timeout = 30s

			# Http version for the RavenDB client to use in communication with the server
			# default: 2.0
			#http-version = "2.0"

			# Determines whether to compress the data sent in the clinet-server TCP communication
			# default: false
			#disable-tcp-compression = false
		}
	}
	
	snapshot-store {
		ravendb {
			# qualified type name of the RavenDB persistence snapshot actor
			class = "Akka.Persistence.RavenDb.Snapshot.RavenDbSnapshotStore, Akka.Persistence.RavenDb"

			# dispatcher used to drive snapshot storage actor
			plugin-dispatcher = "akka.actor.default-dispatcher"

			# urls to the ravendb cluster
			urls = ["http://localhost:8080"]
		
			# database name where snapshots will be stored
			name = "AkkaStorage"
			
			# create the database if it doesn't exists
			auto-initialize = false

			# Location of a client certificate to access a secure RavenDB database
			# if password required it should be stored in `RAVEN_CERTIFICATE_PASSWORD` env variable
			#certificate-path = "\\path\\to\\cert.pfx"

			# Timeout for 'save' requests sent to RavenDB, such as writing or deleting
			# as opposed to stream operations which may take longer and have a different timeout (12h).
			# Client will fail requests that take longer than this.
			# default: 30s
			#save-changes-timeout = 30s

			# Http version for the RavenDB client to use in communication with the server
			# default: 2.0
			#http-version = "2.0"

			# Determines whether to compress the data sent in the clinet-server TCP communication
			# default: false
			#disable-tcp-compression = false
		}
	}

	query {
        ravendb {
            # Implementation class of the EventStore ReadJournalProvider
            class = "Akka.Persistence.RavenDb.Query.RavenDbReadJournalProvider, Akka.Persistence.RavenDb"

            # The interval at which to check for new ids/events
			# deafult: 3s
            #refresh-interval = 3s
  
            # The number of events to keep buffered while querying until they
            # are delivered downstreams.
			# default: 65536
            #max-buffer-size = 65536
        }
    }
}
```