using Akka.Actor;
using Akka.Persistence.Query;
using Raven.Client.Documents.Changes;
using System.Threading.Channels;

namespace Akka.Persistence.RavenDb.Query.ContinuousQuery;

public class AllEvents : ContinuousQuery<TimeoutChange>
{
    private ChangeVectorOffset _offset;

    public AllEvents(RavenDbReadJournal ravendb, Channel<EventEnvelope> channel, ChangeVectorOffset offset) : base(ravendb, channel)
    {
        _offset = offset;
    }

    protected override IChangesObservable<TimeoutChange> Subscribe(IDatabaseChanges changes)
    {
        return new TimeoutObservable(Ravendb.Storage.QueryConfiguration.RefreshInterval);
    }

    protected override async Task QueryAsync()
    {
        using var session = Ravendb.Store.Instance.OpenAsyncSession();
        var q = session.Advanced.AsyncDocumentQuery<Journal.Types.Event>(nameof(Journal.EventsByTagAndChangeVector));
        q = _offset.ApplyOffset(q);
        using var cts = Ravendb.Store.GetReadCancellationTokenSource();
        await using var results = await session.Advanced.StreamAsync(q, cts.Token).ConfigureAwait(false);
        while (await results.MoveNextAsync().ConfigureAwait(false))
        {
            var @event = results.Current.Document;
            var persistent = Journal.Types.Event.Deserialize(Ravendb.Storage.Serialization, @event, ActorRefs.NoSender);
            var offset = _offset.Merge(results.Current.ChangeVector);
            var e = new EventEnvelope(offset, @event.PersistenceId, @event.SequenceNr, persistent.Payload,
                @event.Timestamp.Ticks, @event.Tags);
            await Channel.Writer.WriteAsync(e, cts.Token).ConfigureAwait(false);
        }
    }
}