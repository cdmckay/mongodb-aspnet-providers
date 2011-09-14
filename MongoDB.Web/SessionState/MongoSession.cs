using System;
using System.Web.SessionState;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DigitalLiberationFront.MongoDB.Web.SessionState {
    internal sealed class MongoSession {

        [BsonId]
        public string Id { get; set; }

        [BsonDateTimeOptions(Representation = BsonType.Document, Kind = DateTimeKind.Local)]
        public DateTime CreatedDate { get; set; }

        [BsonDateTimeOptions(Representation = BsonType.Document, Kind = DateTimeKind.Local)]
        public DateTime ExpiresDate { get; set; }

        public ObjectId LockId { get; set; }

        [BsonDateTimeOptions(Representation = BsonType.Document, Kind = DateTimeKind.Local)]
        public DateTime LockedDate { get; set; }

        public bool IsLocked { get; set; }
        public int Timeout { get; set; }
        public BsonDocument Properties { get; set; }
        public SessionStateActions Actions { get; set; }

    }
}
