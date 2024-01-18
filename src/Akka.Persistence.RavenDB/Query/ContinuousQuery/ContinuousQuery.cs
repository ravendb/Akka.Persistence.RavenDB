using System.Threading.Channels;
using Akka.Persistence.Query;
using Nito.AsyncEx;
using Raven.Client.Documents.Changes;

namespace Akka.Persistence.RavenDB.Query.ContinuousQuery;

public abstract class ContinuousQuery<TChange, TInput> where TChange : DatabaseChange
{
    protected readonly JournalRavenDbPersistence Ravendb;
    protected readonly Channel<TInput> Channel;

    protected ContinuousQuery(JournalRavenDbPersistence ravendb, Channel<TInput> channel)
    {
        Ravendb = ravendb;
        Channel = channel;
    }

    protected abstract IChangesObservable<TChange> Subscribe(IDatabaseChanges changes);

    protected abstract Task Query();

    public async Task Run()
    {
        // TODO add stop token
        try
        {
            var mre = new AsyncManualResetEvent(false);
            using var changes = await RavenDbPersistence.Instance.Changes(Ravendb.Database).EnsureConnectedNow();
            var observable = Subscribe(changes);
            await observable.EnsureSubscribedNow();
            using var sub = observable.Subscribe(x => mre.Set()); // TODO on error need to reconnect

            while (true)
            {
                mre.Reset();

                await Query();

                //await Task.Delay(TimeSpan.FromSeconds(1));
                await mre.WaitAsync();
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
    protected ContinuousQuery(JournalRavenDbPersistence ravendb, Channel<EventEnvelope> channel) : base(ravendb, channel)
    {
    }
}