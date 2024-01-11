using Akka.Actor;
using Akka.Persistence.Journal;
using Akka.Persistence.Serialization;
using Akka.Util;

namespace Akka.Persistence.RavenDB.Journal.Types
{
    public class Event
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

        public static Event Serialize(PersistenceMessageSerializer serialization, IPersistentRepresentation message)
        {
            message = new EventSerializeModifications(message).Get(out var tags);

            var payloadType = message.GetType().TypeQualifiedName();
            var payload = serialization.ToBinary(message); // TODO figure out how to deserialize only the payload and not the entire message

            return new Event
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

        public static Persistent Deserialize(PersistenceMessageSerializer serialization, Event @event, IActorRef sender)
        {
            var type = Type.GetType(@event.PayloadType);
            var payloadBytes = Convert.FromBase64String(@event.Payload);
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
                    _message = _message.Update(_message.SequenceNr, _message.PersistenceId, _message.IsDeleted, ActorRefs.NoSender, _message.WriterGuid);

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
}