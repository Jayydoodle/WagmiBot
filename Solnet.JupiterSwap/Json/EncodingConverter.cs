﻿using Newtonsoft.Json;
using Solnet.Rpc.Types;

namespace Solnet.JupiterSwap.Json
{
    public class EncodingConverter : JsonConverter<BinaryEncoding>
    {
        public override void WriteJson(JsonWriter writer, BinaryEncoding value, JsonSerializer serializer)
        {
            if (value == BinaryEncoding.JsonParsed)
            {
                writer.WriteValue("jsonParsed");
            }
            else if (value == BinaryEncoding.Base64Zstd)
            {
                writer.WriteValue("base64+zstd");
            }
            else
            {
                writer.WriteValue("base64");
            }
        }

        /// <inheritdoc />
        public override BinaryEncoding ReadJson(JsonReader reader, Type objectType, BinaryEncoding existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
