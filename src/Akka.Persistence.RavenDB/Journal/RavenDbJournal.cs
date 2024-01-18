using System.Collections.Concurrent;
using Akka.Persistence.Journal;
using System.Collections.Immutable;
using System.Diagnostics;
using Akka.Actor;
using Akka.Persistence.Serialization;
using Raven.Client.Documents.Commands.Batches;
using Raven.Client.Documents.Operations;
using Akka.Persistence.RavenDB.Journal.Types;
using Nito.AsyncEx;
using Raven.Client;
using Raven.Client.Documents.Queries;
using Akka.Persistence.RavenDB.Query.ContinuousQuery;

namespace Akka.Persistence.RavenDB.Journal
{
    public class RavenDbJournal : AsyncWriteJournal
    {
        private readonly JournalRavenDbPersistence _storage = Context.System.WithExtension<JournalRavenDbPersistence, JournalRavenDbPersistenceProvider>();
        private readonly Akka.Serialization.Serialization _serialization = Context.System.Serialization;

        //requests for the highest sequence number may be made concurrently to writes executing for the same persistenceId.
        private readonly ConcurrentDictionary<string, AsyncReaderWriterLock> _lockPerActor = new ConcurrentDictionary<string, AsyncReaderWriterLock>();

        public override async Task ReplayMessagesAsync(
            IActorContext context, 
            string persistenceId, 
            long fromSequenceNr, 
            long toSequenceNr, 
            long max, 
            Action<IPersistentRepresentation> recoveryCallback)
        {
            using var session = _storage.OpenAsyncSession();
            using var cts = RavenDbPersistence.CancellationTokenSource;
            session.Advanced.SessionInfo.SetContext(persistenceId);
            
            await using var results = await session.Advanced.StreamAsync<Types.Event>(startsWith: GetEventPrefix(persistenceId), startAfter: GetSequenceId(persistenceId, fromSequenceNr - 1), token: cts.Token);
            while (max > 0 && await results.MoveNextAsync())
            {
                var message = results.Current.Document;
                if (message.SequenceNr > toSequenceNr)
                    return;

                var persistent = Types.Event.Deserialize(_serialization, message, context.Sender);
                recoveryCallback(persistent);
                max--;
            }
        }

        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            using (await GetLocker(persistenceId).WriterLockAsync())
            {
                using var session = _storage.OpenAsyncSession();
                using var cts = RavenDbPersistence.CancellationTokenSource;
                session.Advanced.SessionInfo.SetContext(persistenceId);

                var metadata = await session.LoadAsync<Metadata>(GetMetadataId(persistenceId), cts.Token);
                return metadata?.MaxSequenceNr ?? 0;

                // TODO read last event with the prefix of 'persistenceId' 
            }
        }

        protected override async Task<IImmutableList<Exception?>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var builder = ImmutableList.CreateBuilder<Exception?>();
            using var cts = RavenDbPersistence.CancellationTokenSource;
            var writes = new Dictionary<string, Task>();
            var original = messages.ToList();

            // we can have multiple atomic writes with the _same_ actor :(
            foreach (var atomicWrites in original.GroupBy(m => m.PersistenceId))
            {
                var t = AtomicWriteForActor(atomicWrites, cts);
                writes.Add(atomicWrites.Key, t);
            }

            foreach (var atomicWrite in original)
            {
                try
                {
                    await writes[atomicWrite.PersistenceId]; // unwrap the exception if needed
                    builder.Add(null);
                }
                catch (Exception e)
                {
                    builder.Add(e);
                }
            }
           
            return builder.ToImmutable();
        }

        // we group multiple atomic writes for a given actor into a single atomic write
        private async Task AtomicWriteForActor(IGrouping<string, AtomicWrite> atomicWrites, CancellationTokenSource cts)
        {
            var persistenceId = atomicWrites.Key;
            using var _ = await GetLocker(persistenceId).ReaderLockAsync(cts.Token);
            using var session = _storage.OpenAsyncSession();
            session.Advanced.SessionInfo.SetContext(persistenceId);
            
            var highest = long.MinValue;
            var lowest = long.MaxValue;

            foreach (var atomicWrite in atomicWrites)
            {
                lowest = Math.Min(lowest, atomicWrite.LowestSequenceNr);
                highest = Math.Max(highest, atomicWrite.HighestSequenceNr);

                var payload = (IImmutableList<IPersistentRepresentation>)atomicWrite.Payload;

                foreach (var representation in payload)
                {
                    var id = GetSequenceId(representation.PersistenceId, representation.SequenceNr);
                    var journalEvent = Types.Event.Serialize(_serialization, representation);
                    // events are immutable and should always be new 
                    await session.StoreAsync(journalEvent, changeVector: string.Empty, id, cts.Token);
                }
            }

            var metadataId = GetMetadataId(persistenceId);
            session.Advanced.Defer(new PatchCommandData(metadataId, changeVector: null, patch: new PatchRequest
            {
                Script = Metadata.UpdateScript,
                Values = new Dictionary<string, object>
                {
                    [nameof(Metadata.MaxSequenceNr)] = highest,
                    ["check"] = lowest - 1,
                }
            }, new PatchRequest
            {
                Script = Metadata.CreateNewScript,
                Values = new Dictionary<string, object>
                {
                    [nameof(Metadata.PersistenceId)] = persistenceId,
                    [nameof(Metadata.MaxSequenceNr)] = highest,
                    ["collection"] = EventsMetadataCollection,
                    ["type"] = RavenDbPersistence.Instance.Conventions.FindClrTypeName(typeof(Metadata)),
                    ["collection2"] = RavenDbPersistence.Instance.Conventions.FindCollectionName(typeof(ActorId)),
                    ["type2"] = RavenDbPersistence.Instance.Conventions.FindClrTypeName(typeof(ActorId))
                }
            }));

            // session.Advanced.WaitForIndexesAfterSaveChanges(); // TODO uncomment to make the query test pass
            await session.SaveChangesAsync(cts.Token);
        }

        protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            // TODO batch this ?
            using var cts = RavenDbPersistence.CancellationTokenSource;
            using var session = _storage.OpenAsyncSession();
            await using var results = await session.Advanced.StreamAsync<Types.Event>(startsWith: GetEventPrefix(persistenceId), token: cts.Token);
            while (await results.MoveNextAsync())
            {
                var current = results.Current.Document;
                if (current.SequenceNr > toSequenceNr)
                    break;

                session.Delete(current.Id);
            }

            await session.SaveChangesAsync(cts.Token);
        }

        private AsyncReaderWriterLock GetLocker(string persistenceId) => _lockPerActor.GetOrAdd(persistenceId, new AsyncReaderWriterLock());

        private static string GetMetadataId(string persistenceId) => $"{EventsMetadataCollection}/{persistenceId}";

        public static string GetEventPrefix(string persistenceId) => $"{EventsCollection}/{persistenceId}/";

        public static string GetSequenceId(string persistenceId, long sequenceNr)
        {
            if (sequenceNr <= 0) 
                sequenceNr = 0;

            return $"{GetEventPrefix(persistenceId)}{sequenceNr.ToLeadingZerosFormat()}";
        }

        private static string EventsCollection = RavenDbPersistence.Instance.Conventions.FindCollectionName(typeof(Types.Event));
        private static string EventsMetadataCollection = RavenDbPersistence.Instance.Conventions.FindCollectionName(typeof(Metadata));
    }
}
