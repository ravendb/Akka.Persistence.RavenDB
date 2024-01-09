using Akka.Event;

namespace Akka.Persistence.RavenDB.Query
{
    public class SubscriptionDroppedException : Exception, IDeadLetterSuppression
    {

        public SubscriptionDroppedException() : this("Unknown error", null)
        {

        }

        public SubscriptionDroppedException(string message, Exception inner) : base(message, inner)
        {

        }
    }
}
