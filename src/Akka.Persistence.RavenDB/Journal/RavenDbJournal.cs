using Akka.Persistence.Journal;
using System.Collections.Immutable;
using System.Diagnostics;
using Akka.Actor;
using Akka.Persistence.RavenDB;
using Akka.Persistence.RavenDB.Query;
using Akka.Persistence.Serialization;
using Akka.Util;
using Raven.Client.Documents.Commands.Batches;
using Raven.Client.Documents.Operations;
using Akka.Streams.Dsl;

namespace Akka.Persistence.RavenDB.Journal
{
    public class RavenDbJournal : AsyncWriteJournal
    {
        private readonly JournalRavenDbPersistence _storage = Context.System.WithExtension<JournalRavenDbPersistence, JournalRavenDbPersistenceProvider>();
        private readonly PersistenceMessageSerializer _serializer = new PersistenceMessageSerializer((ExtendedActorSystem)Context.System);

        public override async Task ReplayMessagesAsync(
            IActorContext context, 
            string persistenceId, 
            long fromSequenceNr, 
            long toSequenceNr, 
            long max, 
            Action<IPersistentRepresentation> recoveryCallback)
        {
            using var session = _storage.OpenAsyncSession();

            var results = await session.Advanced.StreamAsync<JournalEvent>(startsWith: GetEventPrefix(persistenceId), startAfter: GetSequenceId(persistenceId, fromSequenceNr - 1));
            while (max > 0 && await results.MoveNextAsync())
            {
                var message = results.Current.Document;
                if (message.SequenceNr > toSequenceNr)
                    return;

                var persistent = JournalEvent.Deserialize(_serializer, message, context.Sender);
                recoveryCallback(persistent);
                max--;
            }
        }

        public override async Task<long> ReadHighestSequenceNrAsync(string persistenceId, long fromSequenceNr)
        {
            //TODO backward by prefix
            using var session = _storage.OpenAsyncSession();
            var max = -1L;
            var results = await session.Advanced.StreamAsync<JournalEvent>(startsWith: GetEventPrefix(persistenceId), startAfter: GetSequenceId(persistenceId, fromSequenceNr - 1));
            while (await results.MoveNextAsync())
            {
                var message = results.Current.Document;
                max = Math.Max(max, message.SequenceNr);
            }

            if (max == -1L) // no journal found
            {
                var metadataId = GetMetadataId(persistenceId);
                var metadata = await session.LoadAsync<JournalMetadata>(metadataId);
                max = metadata?.MaxSequenceNr ?? 0;
            }

            return max;
        }

        protected override async Task<IImmutableList<Exception?>> WriteMessagesAsync(IEnumerable<AtomicWrite> messages)
        {
            var builder = ImmutableList.CreateBuilder<Exception?>();
            using var cts = RavenDbPersistence.CancellationTokenSource;
            var tasks = new List<Task>();

            foreach (var atomicWrite in messages)
            {
                var t = WriteAtomic(atomicWrite, cts);
                tasks.Add(t);
            }

            await Task.WhenAll(tasks);

            foreach (var task in tasks)
            {
                try
                {
                    if (task.IsCompletedSuccessfully)
                    {
                        builder.Add(null);
                        continue;
                    }

                    await task; // unwrap the exception
                    Debug.Assert(false, "Task supposed to be faulted, but it isn't.");
                }
                catch (Exception e)
                {
                    builder.Add(e);
                }
            }
           
            return builder.ToImmutable();
        }

        private async Task WriteAtomic(AtomicWrite atomicWrite, CancellationTokenSource cts)
        {
            var payload = (IImmutableList<IPersistentRepresentation>)atomicWrite.Payload;
            using var session = _storage.OpenAsyncSession();

            foreach (var representation in payload)
            {
                var id = GetSequenceId(representation.PersistenceId, representation.SequenceNr);
                var journalEvent = JournalEvent.Serialize(_serializer, representation);
                await session.StoreAsync(journalEvent, id, cts.Token);
            }

            var metadataId = GetMetadataId(atomicWrite.PersistenceId);
            session.Advanced.Defer(new PatchCommandData(metadataId, changeVector: null, patch: new PatchRequest
            {
                Script = JournalMetadata.UpdateScript,
                Values = new Dictionary<string, object>
                {
                    [nameof(JournalMetadata.MaxSequenceNr)] = atomicWrite.HighestSequenceNr,
                }
            }, new PatchRequest
            {
                Script = JournalMetadata.CreateNewScript,
                Values = new Dictionary<string, object>
                {
                    [nameof(JournalMetadata.PersistenceId)] = atomicWrite.PersistenceId,
                    [nameof(JournalMetadata.MaxSequenceNr)] = atomicWrite.HighestSequenceNr,
                    ["collection"] = RavenDbPersistence.Instance.Conventions.FindCollectionName(typeof(JournalMetadata)),
                    ["type"] = RavenDbPersistence.Instance.Conventions.FindClrTypeName(typeof(JournalMetadata)),
                    ["collection2"] = RavenDbPersistence.Instance.Conventions.FindCollectionName(typeof(UniqueActor)),
                    ["type2"] = RavenDbPersistence.Instance.Conventions.FindClrTypeName(typeof(UniqueActor))
                }
            }));

            await session.SaveChangesAsync(cts.Token);
        }

        protected override async Task DeleteMessagesToAsync(string persistenceId, long toSequenceNr)
        {
            //TODO delete by prefix (upto)
            using var session = _storage.OpenAsyncSession();
            using var cts = RavenDbPersistence.CancellationTokenSource;
            var results = await session.Advanced.StreamAsync<JournalEvent>(startsWith: GetEventPrefix(persistenceId), token: cts.Token);
            var maxDeleted = 0L;
            while (await results.MoveNextAsync())
            {
                var current = results.Current.Document;
                if (current.SequenceNr > toSequenceNr)
                    break;

                maxDeleted = Math.Max(maxDeleted, current.SequenceNr);
                session.Delete(results.Current.Id);
            }

            await session.SaveChangesAsync(cts.Token);

        }

        public class JournalMetadata
        {
            public string Id;
            public string PersistenceId;
            public long MaxSequenceNr;
            
            public static string CreateNewScript = 
@$"
this['{Raven.Client.Constants.Documents.Metadata.Key}'] = {{}};
this['{Raven.Client.Constants.Documents.Metadata.Key}']['{Raven.Client.Constants.Documents.Metadata.Collection}'] = args.collection;
this['{Raven.Client.Constants.Documents.Metadata.Key}']['{Raven.Client.Constants.Documents.Metadata.RavenClrType}'] = args.type;
this['{nameof(MaxSequenceNr)}'] = args.{nameof(MaxSequenceNr)};
this['{nameof(PersistenceId)}'] = args.{nameof(PersistenceId)};

let uid = 'UniqueActors/' + args.{nameof(PersistenceId)}; 
if(load(uid) == null)
put(uid,
{{ {nameof(UniqueActor.PersistenceId)} : args.{nameof(PersistenceId)}, 
    '{Raven.Client.Constants.Documents.Metadata.Key}' : {{ 
        '{Raven.Client.Constants.Documents.Metadata.Collection}' : args.collection2,
        '{Raven.Client.Constants.Documents.Metadata.RavenClrType}' : args.type2
    }} 
}});
";

            public static string UpdateScript = 
@$"
var x = this['{nameof(MaxSequenceNr)}'];
this['{nameof(MaxSequenceNr)}'] = Math.max(x, args.{nameof(MaxSequenceNr)});
";
        }

        public class UniqueActor
        {
            public string PersistenceId;
        }

        public class JournalEvent
        {
            public string Id;
            public string PersistenceId;
            public long SequenceNr;
            public string Payload; // base64
            public string PayloadType;
            public long Timestamp;
            public string WriterGuid;
            public bool IsDeleted;
            public string Manifest;
            public string[] Tags;

            public static JournalEvent Serialize(PersistenceMessageSerializer serialization, IPersistentRepresentation message)
            {
                message = new EventSerializeModifications(message).Get(out var tags);

                var payloadType = message.GetType().TypeQualifiedName();
                var payload = serialization.ToBinary(message); // TODO figure out how to deserialize only the payload and not the entire message

                return new JournalEvent
                {
                    PersistenceId = message.PersistenceId,
                    Timestamp = message.Timestamp,
                    SequenceNr = message.SequenceNr,
                    WriterGuid = message.WriterGuid,
                    IsDeleted = message.IsDeleted,
                    Manifest = message.Manifest,
                    Payload = Convert.ToBase64String(payload),
                    PayloadType = payloadType,
                    Tags = tags
                };
            }

            public static Persistent Deserialize(PersistenceMessageSerializer serialization, JournalEvent journalEvent, IActorRef sender)
            {
                var type = Type.GetType(journalEvent.PayloadType);
                var payloadBytes = Convert.FromBase64String(journalEvent.Payload);
                return serialization.FromBinary<Persistent>(payloadBytes);
            }

            private struct EventSerializeModifications
            {
                private IPersistentRepresentation _message;
                private readonly bool _nullifySender;
                private readonly bool _addTimestamp;
                private readonly bool _tagged;

                public EventSerializeModifications(IPersistentRepresentation message)
                {
                    _message = message;
                    _nullifySender = message.Sender != null;
                    _addTimestamp = message.Timestamp == 0;
                    _tagged = message.Payload is Tagged;
                }

                public IPersistentRepresentation Get(out string[]? tags)
                {
                    tags = null;

                    if (_nullifySender)
                        _message =  _message.Update(_message.SequenceNr, _message.PersistenceId, _message.IsDeleted, ActorRefs.NoSender, _message.WriterGuid);

                    if (_addTimestamp)
                        _message = _message.WithTimestamp(DateTime.UtcNow.Ticks);

                    if (_tagged)
                    {
                        var tagged = (Tagged)_message.Payload;
                        tags = tagged.Tags.ToArray();
                        _message = _message.WithPayload(tagged.Payload);
                    }

                    return _message;
                }
            }
        }

        private static string GetMetadataId(string persistenceId) => $"EventsMetadata/{persistenceId}";

        public static string GetEventPrefix(string persistenceId) => $"Events/{persistenceId}/";

        public static string GetSequenceId(string persistenceId, long sequenceNr)
        {
            if (sequenceNr <= 0) 
                sequenceNr = 0;

            return $"{GetEventPrefix(persistenceId)}{sequenceNr.ToLeadingZerosFormat()}";
        }
    }
}
