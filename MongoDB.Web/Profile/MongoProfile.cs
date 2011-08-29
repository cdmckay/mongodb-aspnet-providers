using System;
using System.Collections.Generic;
using System.Web.Profile;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DigitalLiberationFront.MongoDB.Web.Profile {
    internal sealed class MongoProfile {

        [BsonDateTimeOptions(Representation = BsonType.Document)]
        public DateTime LastActivityDate { get; set; }

        [BsonDateTimeOptions(Representation = BsonType.Document)]
        public DateTime LastUpdateDate { get; set; }

        public BsonDocument Properties { get; set; }        

    }
}
