using System.Text;
using Akka.Actor;
using Akka.IO;
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
        public object Payload; // base64
        public int SerializationId;
        public long Timestamp;
        public string WriterGuid;
        public bool IsDeleted;
        public string Manifest;
        public string[] Tags;

        public enum Serialization
        {
            Default,
            Embedded
        }

        public static Event Serialize(Akka.Serialization.Serialization serialization, IPersistentRepresentation message)
        {
            var e = new Event
            {
                PersistenceId = message.PersistenceId,
                SequenceNr = message.SequenceNr,
                WriterGuid = message.WriterGuid,
                IsDeleted = message.IsDeleted,
                Manifest = message.Manifest,
                Payload = message.Payload,
            };

            message = new EventSerializeModifications(message).Get(out var tags);
            e.Tags = tags;
            e.Timestamp = message.Timestamp;

            var serializer = serialization.FindSerializerFor(message.Payload);

            if (serializer.Identifier == 1) // inject our own serializer instead
            {
                e.Payload = message.Payload;
                e.SerializationId = 1;
            }
            else
            {
                e.Payload = serialization.Serialize(message);
                e.SerializationId = serialization.FindSerializerFor(message).Identifier;
            }

            return e;
        }

        public static Persistent Deserialize(Akka.Serialization.Serialization serialization, Event @event, IActorRef sender)
        {
            if (@event.SerializationId == 1)
                return new Persistent(@event.Payload, @event.SequenceNr, @event.PersistenceId, @event.Manifest, @event.IsDeleted, sender, @event.WriterGuid,
                    @event.Timestamp);

            var r = (Persistent)serialization.Deserialize((byte[])@event.Payload, @event.SerializationId, @event.Manifest);
            return r;
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