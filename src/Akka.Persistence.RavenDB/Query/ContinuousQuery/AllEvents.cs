using System.Threading.Channels;
using Akka.Actor;
using Akka.Persistence.Query;
using Raven.Client.Documents.Changes;

namespace Akka.Persistence.RavenDB.Query.ContinuousQuery;

public class AllEvents : ContinuousQuery<IndexChange>
{
    private RavenDbReadJournal.ChangeVectorOffset _offset;

    public AllEvents(RavenDbReadJournal.ChangeVectorOffset offset, JournalRavenDbPersistence ravendb, Channel<EventEnvelope> channel) : base(ravendb, channel)
    {
        _offset = offset;
    }

    protected override IChangesObservable<IndexChange> Subscribe(IDatabaseChanges changes)
    {
        return changes.ForIndex(nameof(Journal.EventsByTagAndChangeVector));
    }

    protected override async Task Query()
    {
        using var session = Ravendb.OpenAsyncSession();
        var q = session.Advanced.AsyncDocumentQuery<Journal.Types.Event>(nameof(Journal.EventsByTagAndChangeVector));
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