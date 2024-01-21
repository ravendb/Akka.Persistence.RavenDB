using System.Threading.Channels;
using Akka.Actor;
using Akka.Persistence.Query;
using Akka.Persistence.RavenDb.Journal;
using Raven.Client.Documents.Changes;

namespace Akka.Persistence.RavenDb.Query.ContinuousQuery;

public class EventsByPersistenceId : ContinuousQuery<DocumentChange>
{
    private readonly string _persistenceId;
    private long _fromSequenceNr;
    private readonly long _toSequenceNr;

    public EventsByPersistenceId(string persistenceId, long fromSequenceNr, long toSequenceNr, Channel<EventEnvelope> channel, RavenDbReadJournal ravendb) : base(ravendb, channel)
    {
        _persistenceId = persistenceId;
        _fromSequenceNr = fromSequenceNr - 1;
        _toSequenceNr = toSequenceNr;
    }

    protected override IChangesObservable<DocumentChange> Subscribe(IDatabaseChanges changes)
    {
        var prefix = RavenDbJournal.GetEventPrefix(_persistenceId);
        return changes.ForDocumentsStartingWith(prefix);
    }

    protected override async Task Query()
    {
        using var session = Ravendb.Storage.OpenAsyncSession();
        session.Advanced.SessionInfo.SetContext(_persistenceId);

        await using var results = await session.Advanced.StreamAsync<Journal.Types.Event>(startsWith: RavenDbJournal.GetEventPrefix(_persistenceId),
            startAfter: RavenDbJournal.GetSequenceId(_persistenceId, _fromSequenceNr));
        while (await results.MoveNextAsync())
        {
            var @event = results.Current.Document;
            if (results.Current.Document.SequenceNr > _toSequenceNr)
                break;

            var persistent = Journal.Types.Event.Deserialize(Ravendb.Storage.Serialization, @event, ActorRefs.NoSender);
            var e = new EventEnvelope(new Sequence(@event.Timestamp), @event.PersistenceId, @event.SequenceNr, persistent.Payload, @event.Timestamp,
                @event.Tags);
            await Channel.Writer.WriteAsync(e);
            _fromSequenceNr = e.SequenceNr;
        }

        
        if (_fromSequenceNr >= _toSequenceNr)
        {
            Channel.Writer.TryComplete();
        }
    }
}