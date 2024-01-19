using System;
using System.Collections.Generic;
using System.Text;

namespace Akka.Persistence.RavenDB.Query.ContinuousQuery
{
    public static class ObservableExtensions
    {
        private class ActionObserver<T> : IObserver<T>
        {
            private readonly Action<T> _onNext;
            private readonly Action<Exception> _onError;
            private readonly Action _onCompleted;

            public ActionObserver(Action<T> onNext, Action<Exception> onError = null, Action onCompleted = null)
            {
                _onNext = onNext;
                _onError = onError;
                _onCompleted = onCompleted;
            }

            public void OnCompleted()
            {
                _onCompleted?.Invoke();
            }

            public void OnError(Exception error)
            {
                _onError?.Invoke(error);
            }

            public void OnNext(T value)
            {
                _onNext(value);
            }
        }

        public static IDisposable Subscribe<T>(this IObservable<T> self, Action<T> action)
        {
            return self.Subscribe(new ActionObserver<T>(action));
        }

        public static IDisposable Subscribe<T>(this IObservable<T> self, Action<T> onNext, Action<Exception> onError)
        {
            return self.Subscribe(new ActionObserver<T>(onNext, onError));
        }

        public static IDisposable Subscribe<T>(this IObservable<T> self, Action<T> onNext, Action<Exception> onError, Action onCompleted)
        {
            return self.Subscribe(new ActionObserver<T>(onNext, onError, onCompleted));
        }
    }
}
