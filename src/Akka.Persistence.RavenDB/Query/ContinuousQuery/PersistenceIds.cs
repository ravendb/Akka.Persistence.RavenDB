using System.Threading.Channels;
using Akka.Persistence.RavenDB.Journal;
using Akka.Persistence.RavenDB.Journal.Types;
using Raven.Client.Documents.Changes;

namespace Akka.Persistence.RavenDB.Query.ContinuousQuery;

public class PersistenceIds : ContinuousQuery<IndexChange, string>
{
    private RavenDbReadJournal.ChangeVectorOffset _offset;

    public PersistenceIds(JournalRavenDbPersistence ravendb, Channel<string> channel) : base(ravendb, channel)
    {
        _offset = new RavenDbReadJournal.ChangeVectorOffset(string.Empty);
    }

    protected override IChangesObservable<IndexChange> Subscribe(IDatabaseChanges changes)
    {
        return changes.ForIndex(nameof(ActorsByChangeVector));
    }

    protected override async Task Query()
    {
        using var session = Ravendb.OpenAsyncSession();
        var q = session.Advanced.AsyncDocumentQuery<ActorId>(indexName: nameof(ActorsByChangeVector));
        q = _offset.ApplyOffset(q);

        await using var results = await session.Advanced.StreamAsync(q);
        while (await results.MoveNextAsync())
        {
            var id = results.Current.Document.PersistenceId;
            await Channel.Writer.WriteAsync(id);
            _offset = new RavenDbReadJournal.ChangeVectorOffset(results.Current.ChangeVector);
        }
    }
}