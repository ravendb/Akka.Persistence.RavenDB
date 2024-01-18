using Raven.Client;
using Raven.Client.Documents.Indexes;
using static Akka.Persistence.RavenDB.Query.RavenDbReadJournal;

namespace Akka.Persistence.RavenDB.Journal;

public class EventsByTagAndChangeVector : AbstractIndexCreationTask<Types.Event>
{
    public EventsByTagAndChangeVector()
    {
        Map = events => 
            from e in events 
            //where e.Tags != null && e.Tags.Length > 0
            let changeVector = MetadataFor(e).Value<string>(Constants.Documents.Metadata.ChangeVector)
            select new
            {
                e.PersistenceId,
                e.Tags,
                e.Timestamp,
                _ = ChangeVectorAnalyzer.ToList(changeVector).Select(x => CreateField(x.DatabaseId, x.Etag))
            };

        AdditionalSources = new Dictionary<string, string>()
        {
            {"ChangeVectorAnalyzer",ChangeVectorOffset.Code}
        };
    }
}