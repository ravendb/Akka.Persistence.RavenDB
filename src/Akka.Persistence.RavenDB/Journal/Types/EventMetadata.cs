namespace Akka.Persistence.RavenDb.Journal.Types
{
    // we need this metadata to keep track of the actor even when all events are deleted
    public class EventMetadata
    {
        public string Id;
        public string PersistenceId;
        public long MaxSequenceNr;
        public DateTime Timestamp = DateTime.UtcNow;
       
        public class ScriptArgs
        {
            public string? MetadataCollection;
            public string? UniqueActorCollection;
            public string? MetadataType;
            public string? UniqueActorType;
            public string? ConcurrencyCheck;
        }

        public static string CreateNewScript =
            @$"
this['{Raven.Client.Constants.Documents.Metadata.Key}'] = {{}};
this['{Raven.Client.Constants.Documents.Metadata.Key}']['{Raven.Client.Constants.Documents.Metadata.Collection}'] = args.{nameof(ScriptArgs.MetadataCollection)};
this['{Raven.Client.Constants.Documents.Metadata.Key}']['{Raven.Client.Constants.Documents.Metadata.RavenClrType}'] = args.{nameof(ScriptArgs.MetadataType)};
this['{nameof(MaxSequenceNr)}'] = args.{nameof(MaxSequenceNr)};
this['{nameof(PersistenceId)}'] = args.{nameof(PersistenceId)};

let uid = 'UniqueActors/' + args.{nameof(PersistenceId)}; 
if(load(uid) == null)
put(uid,
{{ {nameof(UniqueActor.PersistenceId)} : args.{nameof(PersistenceId)}, 
   {nameof(UniqueActor.CreatedAt)} : new Date().toISOString(), 
    '{Raven.Client.Constants.Documents.Metadata.Key}' : {{ 
        '{Raven.Client.Constants.Documents.Metadata.Collection}' : args.{nameof(ScriptArgs.UniqueActorCollection)},
        '{Raven.Client.Constants.Documents.Metadata.RavenClrType}' : args.{nameof(ScriptArgs.UniqueActorType)}
    }} 
}});
";

        public static string UpdateScript =
            @$"
var x = this['{nameof(MaxSequenceNr)}'];
if (x != args.{nameof(ScriptArgs.ConcurrencyCheck)})
  throw new Error('Concurrency check failed. expected ' + args.{nameof(ScriptArgs.ConcurrencyCheck)}+ ', but got ' + x);

this['{nameof(MaxSequenceNr)}'] = Math.max(x, args.{nameof(MaxSequenceNr)});
";
    }
}
