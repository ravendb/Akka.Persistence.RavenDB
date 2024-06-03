using Akka.Persistence.RavenDb.Query;
using Raven.Client;
using Raven.Client.Documents.Indexes;

namespace Akka.Persistence.RavenDb.Journal;

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
                _ = ChangeVectorAnalyzer.ToDictionary(changeVector).Select(x => CreateField(x.Key, x.Value))
            };

        AdditionalSources = new Dictionary<string, string>()
        {
            {"ChangeVectorAnalyzer",ChangeVectorOffset.Code}
        };
    }
}