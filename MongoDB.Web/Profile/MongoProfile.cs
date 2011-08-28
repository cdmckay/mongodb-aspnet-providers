using System;
using System.Collections.Generic;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DigitalLiberationFront.MongoDB.Web.Profile {
    internal sealed class MongoProfile {

        [BsonDateTimeOptions(Representation = BsonType.String)]
        public DateTime LastActivityDate { get; set; }

        [BsonDateTimeOptions(Representation = BsonType.String)]
        public DateTime LastUpdateDate { get; set; }

        public BsonDocument Properties { get; set; }

        public MongoProfile() {
        }

    }
}
