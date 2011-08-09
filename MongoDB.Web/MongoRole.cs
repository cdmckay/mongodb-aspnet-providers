using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace DigitalLiberationFront.MongoDB.Web.Security {
    internal sealed class MongoRole {

        [BsonId]
        public ObjectId Id { get; set; }

        public string Name { get; set; }

    }
}
