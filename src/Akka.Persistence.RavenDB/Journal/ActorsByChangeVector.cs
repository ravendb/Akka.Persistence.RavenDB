using Akka.Persistence.RavenDb.Journal.Types;
using Akka.Persistence.RavenDb.Query;
using Raven.Client;
using Raven.Client.Documents.Indexes;

namespace Akka.Persistence.RavenDb.Journal;

public class ActorsByChangeVector : AbstractIndexCreationTask<UniqueActor>
{
    public ActorsByChangeVector()
    {
        Map = actors => 
            from actor in actors 
            let changeVector = MetadataFor(actor).Value<string>(Constants.Documents.Metadata.ChangeVector)
            select new
            {
                PersistenceId = actor.PersistenceId,
                _ = ChangeVectorAnalyzer.ToDictionary(changeVector).Select(x => CreateField(x.Key, x.Value)),
            };

        AdditionalSources = new Dictionary<string, string>()
        {
            {"ChangeVectorAnalyzer",ChangeVectorOffset.Code}
        };
    }
}