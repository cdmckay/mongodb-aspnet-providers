#region License
// Copyright 2011 Cameron McKay
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
#endregion

using System;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Options;

namespace DigitalLiberationFront.MongoDB.Web {    
    internal static class SerializationHelper {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nominalType"></param>
        /// <param name="value"></param>
        /// <param name="options"></param>
        /// <returns></returns>
        public static BsonDocument Serialize(Type nominalType, object value, IBsonSerializationOptions options) {
            var document = new BsonDocument();
            var writerSettings = new BsonDocumentWriterSettings();
            var writer = new BsonDocumentWriter(document, writerSettings);
            BsonSerializer.Serialize(writer, nominalType, value, options);
            return document;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="nominalType"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        public static BsonDocument Serialize(Type nominalType, object value) {
            return Serialize(nominalType, value, null);
        }

        /// <summary>
        /// Serializes the DateTime value such that it matches the way DateTimes are serialized by classes.
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public static BsonDocument SerializeDateTime(DateTime value) {
            return Serialize(typeof (DateTime), value, new DateTimeSerializationOptions(DateTimeKind.Utc, BsonType.Document));
        }

    }
}
