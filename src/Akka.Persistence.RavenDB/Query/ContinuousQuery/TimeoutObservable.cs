using Raven.Client.Documents.Changes;

namespace Akka.Persistence.RavenDb.Query.ContinuousQuery;

public class TimeoutObservable : IChangesObservable<TimeoutChange>
{
    private readonly TimeSpan _timeout;

    public TimeoutObservable(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public IDisposable Subscribe(IObserver<TimeoutChange> observer)
    {
        return new Timer((o) => observer.OnNext((TimeoutChange)o), new TimeoutChange(), _timeout, _timeout);
    }

    public Task EnsureSubscribedNow() => Task.CompletedTask;
}

public class TimeoutChange : DatabaseChange
{

}