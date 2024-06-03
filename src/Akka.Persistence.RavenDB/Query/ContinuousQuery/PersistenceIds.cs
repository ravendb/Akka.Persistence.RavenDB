using System.Threading.Channels;
using Akka.Persistence.RavenDb.Journal;
using Akka.Persistence.RavenDb.Journal.Types;
using Raven.Client.Documents.Changes;

namespace Akka.Persistence.RavenDb.Query.ContinuousQuery;

public class PersistenceIds : ContinuousQuery<IndexChange, string>
{
    private ChangeVectorOffset _offset;

    public PersistenceIds(RavenDbReadJournal ravendb, Channel<string> channel) : base(ravendb, channel)
    {
        _offset = new ChangeVectorOffset(string.Empty);
    }

    protected override IChangesObservable<IndexChange> Subscribe(IDatabaseChanges changes)
    {
        return changes.ForIndex(nameof(ActorsByChangeVector));
    }

    protected override async Task QueryAsync()
    {
        using var session = Ravendb.Store.Instance.OpenAsyncSession();
        
        var q = session.Advanced.AsyncDocumentQuery<UniqueActor>(indexName: nameof(ActorsByChangeVector));
        q = _offset.ApplyOffset(q);

        using var cts = Ravendb.Store.GetReadCancellationTokenSource();
        await using var results = await session.Advanced.StreamAsync(q, cts.Token).ConfigureAwait(false);
        while (await results.MoveNextAsync().ConfigureAwait(false))
        {
            var id = results.Current.Document.PersistenceId;
            await Channel.Writer.WriteAsync(id, cts.Token).ConfigureAwait(false);
            _offset.Merge(results.Current.ChangeVector);
        }
    }
}