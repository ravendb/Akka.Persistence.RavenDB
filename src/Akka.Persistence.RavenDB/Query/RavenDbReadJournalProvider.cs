using Akka.Actor;
using Akka.Configuration;
using Akka.Persistence.Query;

namespace Akka.Persistence.RavenDB.Query
{
    public class RavenDbReadJournalProvider : IReadJournalProvider
    {
        private readonly ExtendedActorSystem _system;
        private readonly Config _config;

        /// <summary>
        /// 
        /// </summary>
        /// <param name="system">instance of actor system at which read journal should be started</param>
        /// <param name="config"></param>
        public RavenDbReadJournalProvider(ExtendedActorSystem system, Config config)
        {
            _system = system;
            _config = config;
        }

        /// <summary>
        /// Returns instance of EventStoreReadJournal
        /// </summary>
        /// <returns></returns>
        public IReadJournal GetReadJournal()
        {
            var journal = new RavenDbReadJournal(_system, _config);
            journal.PreStart();
            return journal;
        }
    }
}
