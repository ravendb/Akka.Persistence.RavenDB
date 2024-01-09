using Akka.Persistence.TestKit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akka.Persistence.RavenDB.Tests
{
    
    public class CounterActorTests : PersistenceTestKit, IClassFixture<RavenDbFixture>
    {
        public CounterActorTests()
        {
            
        }

        [Fact]
        public async Task CounterActor_internal_state_will_be_lost_if_underlying_persistence_store_is_not_available()
        {
            await WithJournalWrite(write => write.Fail(), () =>
            {
                var actor = ActorOf(() => new CounterActor("test"), "counter");
                actor.Tell("inc", TestActor);
                actor.Tell("read", TestActor);

                var value = ExpectMsg<int>(TimeSpan.FromSeconds(3));
                Assert.Equal(0, value);
            });
        }

        /*[Fact]
        public async Task actor_must_fail_when_journal_will_fail_saving_message()
        {
            await WithJournalWrite(write => write.Fail(), () =>
            {
                var actor = ActorOf(() => new PersistActor());
                Watch(actor);

                actor.Tell("write", TestActor);
                ExpectTerminated(actor);
            });
        }*/
    }

    public class CounterActor : UntypedPersistentActor
    {
        public CounterActor(string id)
        {
            PersistenceId = id;
        }

        private int value = 0;

        public override string PersistenceId { get; }

        protected override void OnCommand(object message)
        {
            switch (message as string)
            {
                case "inc":
                    value++;
                    Persist(message, _ => { });
                    break;

                case "dec":
                    value++;
                    Persist(message, _ => { });
                    break;

                case "read":
                    Sender.Tell(value, Self);
                    break;

                default:
                    return;
            }
        }

        protected override void OnRecover(object message)
        {
            switch (message as string)
            {
                case "inc":
                    value++;
                    break;

                case "dec":
                    value++;
                    break;

                default:
                    return;
            }
        }
    }
}
