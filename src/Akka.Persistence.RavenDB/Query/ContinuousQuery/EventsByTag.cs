using System.Threading.Channels;
using Akka.Actor;
using Akka.Persistence.Query;
using Akka.Persistence.RavenDB.Journal;
using Raven.Client.Documents.Changes;
using Raven.Client.Documents.Operations;
using Raven.Client.Documents.Session;

namespace Akka.Persistence.RavenDB.Query.ContinuousQuery;

public class EventsByTag : ContinuousQuery<IndexChange>
{
    private readonly string _tag;
    private RavenDbReadJournal.ChangeVectorOffset _offset;

    public EventsByTag(string tag, RavenDbReadJournal.ChangeVectorOffset offset, JournalRavenDbPersistence ravendb, Channel<EventEnvelope> channel) : base(ravendb, channel)
    {
        _tag = tag;
        _offset = offset;
    }

    protected override IChangesObservable<IndexChange> Subscribe(IDatabaseChanges changes)
    {
        return changes.ForIndex(nameof(Journal.EventsByTagAndChangeVector));
    }

    protected override async Task Query()
    {
        /*var stats = await RavenDbPersistence.Instance.Maintenance.ForDatabase(Ravendb.Database).SendAsync(new GetStatisticsOperation());
        var databaseChangeVector = ChangeVectorAnalyzer.ToList(stats.DatabaseChangeVector);
        */

        using var session = Ravendb.OpenAsyncSession();
        session.Advanced.SessionInfo.SetContext(_tag);

        var q = session.Advanced.AsyncDocumentQuery<Journal.Types.Event>(nameof(EventsByTagAndChangeVector)).ContainsAny(e => e.Tags, new[] { _tag });
        q = _offset.ApplyOffset(q);

        await using var results = await session.Advanced.StreamAsync(q);
        while (await results.MoveNextAsync())
        {
            var @event = results.Current.Document;
            var persistent = Journal.Types.Event.Deserialize(Ravendb.Serialization, @event, ActorRefs.NoSender);
            _offset = new RavenDbReadJournal.ChangeVectorOffset(results.Current.ChangeVector);
            var e = new EventEnvelope(_offset, @event.PersistenceId, @event.SequenceNr, persistent.Payload,
                @event.Timestamp, @event.Tags);
            await Channel.Writer.WriteAsync(e);

        }
    }
}