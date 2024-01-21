namespace Akka.Persistence.RavenDB.Journal.Types
{
    // we need this metadata to keep track of the actor even when all events are deleted
    public class Metadata
    {
        public string Id;
        public string PersistenceId;
        public long MaxSequenceNr;

        public static string CreateNewScript =
            @$"
this['{Raven.Client.Constants.Documents.Metadata.Key}'] = {{}};
this['{Raven.Client.Constants.Documents.Metadata.Key}']['{Raven.Client.Constants.Documents.Metadata.Collection}'] = args.collection;
this['{Raven.Client.Constants.Documents.Metadata.Key}']['{Raven.Client.Constants.Documents.Metadata.RavenClrType}'] = args.type;
this['{nameof(MaxSequenceNr)}'] = args.{nameof(MaxSequenceNr)};
this['{nameof(PersistenceId)}'] = args.{nameof(PersistenceId)};

let uid = 'UniqueActors/' + args.{nameof(PersistenceId)}; 
if(load(uid) == null)
put(uid,
{{ {nameof(ActorId.PersistenceId)} : args.{nameof(PersistenceId)}, 
   {nameof(ActorId.CreatedAt)} : new Date().toISOString(), 
    '{Raven.Client.Constants.Documents.Metadata.Key}' : {{ 
        '{Raven.Client.Constants.Documents.Metadata.Collection}' : args.collection2,
        '{Raven.Client.Constants.Documents.Metadata.RavenClrType}' : args.type2
    }} 
}});
";

        public static string UpdateScript =
            @$"
var x = this['{nameof(MaxSequenceNr)}'];
if (x != args.check)
  throw new Error('Concurrency check failed. expected ' + args.check + ', but got ' + x);

this['{nameof(MaxSequenceNr)}'] = Math.max(x, args.{nameof(MaxSequenceNr)});
";
    }
}
