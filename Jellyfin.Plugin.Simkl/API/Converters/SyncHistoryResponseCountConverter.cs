using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.Simkl.API.Responses;

namespace Jellyfin.Plugin.Simkl.API.Converters
{
    /// <summary>
    /// Custom JSON converter for SyncHistoryResponseCount that handles both integer and array responses.
    /// SIMKL API sometimes returns counts as integers and sometimes as arrays.
    /// </summary>
    public class SyncHistoryResponseCountConverter : JsonConverter<SyncHistoryResponseCount>
    {
        /// <inheritdoc />
        public override SyncHistoryResponseCount Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var result = new SyncHistoryResponseCount();

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected StartObject token");
            }

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    return result;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected PropertyName token");
                }

                var propertyName = reader.GetString();
                reader.Read();

                switch (propertyName?.ToLowerInvariant())
                {
                    case "movies":
                        result.Movies = ReadIntOrArrayCount(ref reader);
                        break;
                    case "shows":
                        result.Shows = ReadIntOrArrayCount(ref reader);
                        break;
                    case "episodes":
                        result.Episodes = ReadIntOrArrayCount(ref reader);
                        break;
                    default:
                        reader.Skip();
                        break;
                }
            }

            return result;
        }

        /// <inheritdoc />
        public override void Write(Utf8JsonWriter writer, SyncHistoryResponseCount value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteNumber("movies", value.Movies);
            writer.WriteNumber("shows", value.Shows);
            writer.WriteNumber("episodes", value.Episodes);
            writer.WriteEndObject();
        }

        private static int ReadIntOrArrayCount(ref Utf8JsonReader reader)
        {
            if (reader.TokenType == JsonTokenType.Number)
            {
                return reader.GetInt32();
            }

            if (reader.TokenType == JsonTokenType.StartArray)
            {
                int count = 0;
                int depth = 0;
                const int maxIterations = 10000; // Safety limit to prevent infinite loops
                int iterations = 0;

                while (reader.Read() && iterations < maxIterations)
                {
                    iterations++;

                    if (reader.TokenType == JsonTokenType.EndArray && depth == 0)
                    {
                        break;
                    }

                    // Count any non-null item at the top level of the array
                    if (depth == 0 && reader.TokenType != JsonTokenType.EndArray)
                    {
                        count++;
                    }

                    // Track depth for nested structures
                    if (reader.TokenType == JsonTokenType.StartObject || reader.TokenType == JsonTokenType.StartArray)
                    {
                        depth++;
                    }
                    else if (reader.TokenType == JsonTokenType.EndObject || reader.TokenType == JsonTokenType.EndArray)
                    {
                        depth--;
                    }
                }

                return count;
            }

            // If null or other token type, return 0
            return 0;
        }
    }
}
