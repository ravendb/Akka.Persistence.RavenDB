# Akka.Persistence.RavenDB

Akka.Persistence.RavenDB is a persistence plugin for Akka.NET that integrates RavenDB as a durable storage backend.  
It allows persisting journal events and snapshots of your Akka.NET actors to a RavenDB database.  
Querying is supported through the Akka.Persistence query interface, with RavenDB serving as the underlying storage engine.  

## Configuration

To activate and use the Akka.Persistence.RavenDB plugin, you have the following configuration options:
  * Configure using Akka.Hosting (the easy way)
  * Configure directly in your actor system configuration file using HOCON (the classic way).

Note:  
When configuring the plugin using both Akka.Hosting and HOCON, in cases where parameters overlap,  
the configuration provided via Akka.Hosting take precedence and will override the corresponding HOCON settings.

### Configure with `Akka.Hosting`

Using _Akka.Hosting_, you can easily set up the plugin within your application's startup configuration.  
Here is a basic example:  

```csharp
using Akka.Hosting;
using Akka.Persistence;
using Akka.Persistence.Hosting;
using Akka.Persistence.RavenDb.Hosting;

var host = new HostBuilder().ConfigureServices((context, services) => {
    services.AddAkka("my-actor-system-name", (builder, provider) =>
    {
        builder.WithRavenDbPersistence(
            urls: new[] { "http://localhost:8080" },
            databaseName: "AkkaStorage");
    });
})
    
var app = builder.Build();
app.Run();
```

### Configure with `HOCON`

While both the journal and snapshot-store have the same configuration keys, they reside in separate scopes.  
So when configuring using _HOCON_, the settings for the journal and snapshot-store must be provided separately.  
For example, properties `urls` and `name` can have the same values for both stores, but they still need to be defined distinctly within their respective sections.

```hocon
akka.persistence {
    # Setup the RavenDB journal store:
    journal {
        plugin = "akka.persistence.journal.ravendb"
        ravendb {
            # Qualified type name of the RavenDB persistence journal actor
            class = "Akka.Persistence.RavenDb.Journal.RavenDbJournal, Akka.Persistence.RavenDb" 

            # Dispatcher used to drive journal actor
            plugin-dispatcher = "akka.actor.default-dispatcher"

            # URLs to the RavenDB cluster
            urls = ["http://localhost:8080"]
            
            # Database name where journal events will be stored
            name = "AkkaStorage"
            
            # Create the database if it doesn't exist
            auto-initialize = false

            # Location of a client certificate to access a secure RavenDB database.
            # If a password is required, it should be stored in the `RAVEN_CERTIFICATE_PASSWORD` env variable.
            #certificate-path = "\\path\\to\\cert.pfx"

            # Timeout for 'save' requests sent to RavenDB, such as writing or deleting
            # as opposed to stream operations which may take longer and have a different timeout (12h).
            # Client will fail requests that take longer than this.
            # default: 30s
            #save-changes-timeout = 30s

            # Http version for the RavenDB client to use in communication with the server
            # default: 2.0
            #http-version = "2.0"

            # Determines whether to compress the data sent in the client-server TCP communication
            # default: false
            #disable-tcp-compression = false
        }
    }
    
    # Setup the RavenDB snapshot store:
    snapshot-store {
        plugin = "akka.persistence.snapshot-store.ravendb"
        ravendb {
            # Qualified type name of the RavenDB persistence snapshot actor
            class = "Akka.Persistence.RavenDb.Snapshot.RavenDbSnapshotStore, Akka.Persistence.RavenDb"

            # Dispatcher used to drive snapshot storage actor
            plugin-dispatcher = "akka.actor.default-dispatcher"

            # URLs to the RavenDB cluster
            urls = ["http://localhost:8080"]
            
            # Database name where snapshots will be stored
            name = "AkkaStorage"
            
            # Create the database if it doesn't exist
            auto-initialize = false

            # Location of a client certificate to access a secure RavenDB database.
            # If a password is required, it should be stored in the `RAVEN_CERTIFICATE_PASSWORD` env variable.
            #certificate-path = "\\path\\to\\cert.pfx"

            # Timeout for 'save' requests sent to RavenDB, such as writing or deleting
            # as opposed to stream operations which may take longer and have a different timeout (12h).
            # Client will fail requests that take longer than this.
            # default: 30s
            #save-changes-timeout = 30s

            # Http version for the RavenDB client to use in communication with the server
            # default: 2.0
            #http-version = "2.0"

            # Determines whether to compress the data sent in the client-server TCP communication
            # default: false
            #disable-tcp-compression = false
        }
    }
    
    query {
        # Configure RavenDB as the underlying storage engine for querying:
        ravendb {
            # Implementation class of the EventStore ReadJournalProvider
            class = "Akka.Persistence.RavenDb.Query.RavenDbReadJournalProvider, Akka.Persistence.RavenDb"

            # The interval at which to check for new ids/events
            # default: 3s
            #refresh-interval = 3s
  
            # The number of events to keep buffered while querying until they are delivered downstream.
            # default: 65536
            #max-buffer-size = 65536
        }
    }
}
```