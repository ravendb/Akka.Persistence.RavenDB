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

namespace Akka.Persistence.RavenDB.Journal
{
    public class RavenDbJournal : AsyncWriteJournal
    {
        private readonly JournalRavenDbPersistence _storage = Context.System.WithExtension<JournalRavenDbPersistence, JournalRavenDbPersistenceProvider>();
        private readonly PersistenceMessageSerializer _serializer = new PersistenceMessageSerializer((ExtendedActorSystem)Context.System);

        //requests for the highest sequence number may be made concurrently to writes executing for the same persistenceId.
        private readonly AsyncReaderWriterLock _writeMessagesLock = new AsyncReaderWriterLock();

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
                    return; // TODO 

                var persistent = Types.Event.Deserialize(_serializer, message, context.Sender);
                recoveryCallback(persistent);
                max--;
            }
        }


        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            using (await _writeMessagesLock.WriterLockAsync())
            {
                using var session = _storage.OpenAsyncSession();
                using var cts = RavenDbPersistence.CancellationTokenSource;
                session.Advanced.SessionInfo.SetContext(persistenceId);

                var metadata = await session.LoadAsync<Metadata>(GetMetadataId(persistenceId), cts.Token);
                return metadata?.MaxSequenceNr ?? 0;
            }
        }

        protected override async Task<IImmutableList<Exception?>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var builder = ImmutableList.CreateBuilder<Exception?>();
            using var cts = RavenDbPersistence.CancellationTokenSource;
            var tasks = new List<Task>();

            using (await _writeMessagesLock.ReaderLockAsync(cts.Token))
            {
                foreach (var atomicWrite in messages)
                {
                    var t = WriteAtomic(atomicWrite, cts);
                    tasks.Add(t);
                }

                await Task.WhenAll(tasks);
            }

            foreach (var task in tasks)
            {
                try
                {
                    await task; // unwrap the exception if needed
                    builder.Add(null);
                }
                catch (Exception e)
                {
                    builder.Add(e);
                }
            }
           
            return builder.ToImmutable();
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

        private async Task WriteAtomic(AtomicWrite atomicWrite, CancellationTokenSource cts)
        {
            var payload = (IImmutableList<IPersistentRepresentation>)atomicWrite.Payload;
            using var session = _storage.OpenAsyncSession();
            session.Advanced.SessionInfo.SetContext(atomicWrite.PersistenceId);

            foreach (var representation in payload)
            {
                var id = GetSequenceId(representation.PersistenceId, representation.SequenceNr);
                var journalEvent = Types.Event.Serialize(_serializer, representation);
                // events are immutable and should always be new 
                await session.StoreAsync(journalEvent, changeVector: string.Empty, id, cts.Token);
            }

            var metadataId = GetMetadataId(atomicWrite.PersistenceId);
            session.Advanced.Defer(new PatchCommandData(metadataId, changeVector: null, patch: new PatchRequest
            {
                Script = Metadata.UpdateScript, // TODO : add conflict resolution to prefer the max
                Values = new Dictionary<string, object>
                {
                    [nameof(Metadata.MaxSequenceNr)] = atomicWrite.HighestSequenceNr,
                }
            }, new PatchRequest
            {
                Script = Metadata.CreateNewScript,
                Values = new Dictionary<string, object>
                {
                    [nameof(Metadata.PersistenceId)] = atomicWrite.PersistenceId,
                    [nameof(Metadata.MaxSequenceNr)] = atomicWrite.HighestSequenceNr,
                    ["collection"] = EventsMetadataCollection,
                    ["type"] = RavenDbPersistence.Instance.Conventions.FindClrTypeName(typeof(Metadata)),
                    ["collection2"] = RavenDbPersistence.Instance.Conventions.FindCollectionName(typeof(ActorId)),
                    ["type2"] = RavenDbPersistence.Instance.Conventions.FindClrTypeName(typeof(ActorId))
                }
            }));

            await session.SaveChangesAsync(cts.Token);
        }

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
        private static string DeletionScript = 
            $@"
from {EventsCollection} where startsWith(id(), $prefix) 
update {{
    if (this.SequenceNr > $toSequenceNr)
        return; // need to stop the patch
    
    del(id(this));
}}
";
    }
}
