using System.Threading.Channels;
using Akka.Persistence.Query;
using Nito.AsyncEx;
using Raven.Client.Documents.Changes;

namespace Akka.Persistence.RavenDb.Query.ContinuousQuery;

public abstract class ContinuousQuery<TChange, TInput> where TChange : DatabaseChange
{
    protected readonly RavenDbReadJournal Ravendb;
    protected readonly Channel<TInput> Channel;

    protected ContinuousQuery(RavenDbReadJournal ravendb, Channel<TInput> channel)
    {
        Ravendb = ravendb;
        Channel = channel;
    }

    protected abstract IChangesObservable<TChange> Subscribe(IDatabaseChanges changes);

    protected abstract Task QueryAsync();

    public async Task Run()
    {
        // TODO add stop token
        try
        {
            var mre = new AsyncManualResetEvent(false);
            using var changes = await Ravendb.Store.Instance.Changes(Ravendb.Store.Configuration.Name).EnsureConnectedNow().ConfigureAwait(false);
            var observable = Subscribe(changes);
            using var sub = observable.Subscribe(x => mre.Set()); // TODO on error need to reconnect

            await observable.EnsureSubscribedNow().ConfigureAwait(false);

            while (true)
            {
                mre.Reset();

                await QueryAsync().ConfigureAwait(false);

                await mre.WaitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception e)
        {
            Channel.Writer.TryComplete(e);
        }
    }
}

public abstract class ContinuousQuery<TChange> : ContinuousQuery<TChange, EventEnvelope> where TChange : DatabaseChange
{
    protected ContinuousQuery(RavenDbReadJournal ravendb, Channel<EventEnvelope> channel) : base(ravendb, channel)
    {
    }
}