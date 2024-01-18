using Akka.Persistence.RavenDB.Journal.Types;
using Raven.Client;
using Raven.Client.Documents.Indexes;
using static Akka.Persistence.RavenDB.Query.RavenDbReadJournal;

namespace Akka.Persistence.RavenDB.Journal;

public class ActorsByChangeVector : AbstractIndexCreationTask<ActorId>
{
    public ActorsByChangeVector()
    {
        Map = actors => 
            from actor in actors 
            let changeVector = MetadataFor(actor).Value<string>(Constants.Documents.Metadata.ChangeVector)
            select new
            {
                PersistenceId = actor.PersistenceId,
                _ = ChangeVectorAnalyzer.ToList(changeVector).Select(x => CreateField(x.DatabaseId, x.Etag)),
            };

        AdditionalSources = new Dictionary<string, string>()
        {
            {"ChangeVectorAnalyzer",ChangeVectorOffset.Code}
        };
    }
}