using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
using VrFencing.Settings;

namespace VrFencing.Session
{
    /// <summary>What a JSONL file held: every line that parsed, and how many non-empty lines did not.</summary>
    public sealed class JsonLinesReadResult<T>
    {
        public JsonLinesReadResult(IReadOnlyList<T> items, int skippedLineCount)
        {
            Items = items;
            SkippedLineCount = skippedLineCount;
        }

        public IReadOnlyList<T> Items { get; }

        /// <summary>Lines that were not valid JSON or missed a required field, such as a line cut short when the app was killed.</summary>
        public int SkippedLineCount { get; }
    }

    /// <summary>
    /// Append-only JSON Lines files shared by the rep log and the session history: one JSON object per line,
    /// so a crash can only damage the line being written, and reading skips damaged lines instead of failing.
    /// </summary>
    internal static class JsonLinesFile
    {
        private static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            ContractResolver = new DefaultContractResolver { NamingStrategy = new CamelCaseNamingStrategy() },
            Converters = { new StringEnumConverter(), new TrackedPoseJsonConverter() },
            DateParseHandling = DateParseHandling.DateTimeOffset,
            NullValueHandling = NullValueHandling.Include,
            MissingMemberHandling = MissingMemberHandling.Ignore,
        };

        /// <summary>Appends one line, creating the file and its folder if needed.</summary>
        public static void Append<T>(string path, T item)
        {
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // A line cut short by a crash has no newline; start a fresh line so the new item stays readable.
            var separator = EndsWithNewline(path) ? string.Empty : "\n";
            File.AppendAllText(path, separator + JsonConvert.SerializeObject(item, SerializerSettings) + "\n");
        }

        /// <summary>Reads every line that parses. A missing file reads as empty; blank lines are ignored.</summary>
        public static JsonLinesReadResult<T> Read<T>(string path)
            where T : class
        {
            var items = new List<T>();
            if (!File.Exists(path))
            {
                return new JsonLinesReadResult<T>(items, 0);
            }

            var skippedLineCount = 0;
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                var item = TryParseLine<T>(line);
                if (item == null)
                {
                    skippedLineCount++;
                }
                else
                {
                    items.Add(item);
                }
            }

            return new JsonLinesReadResult<T>(items, skippedLineCount);
        }

        /// <summary>The parsed line, or null when it is not valid JSON or fails the type's own validation.</summary>
        private static T TryParseLine<T>(string line)
            where T : class
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(line, SerializerSettings);
            }
            catch (JsonException)
            {
                return null;
            }
            catch (ArgumentException)
            {
                // A constructor rejected the values (for example an empty id).
                return null;
            }
            catch (TargetInvocationException error) when (error.InnerException is ArgumentException)
            {
                return null;
            }
        }

        private static bool EndsWithNewline(string path)
        {
            if (!File.Exists(path))
            {
                return true;
            }

            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (stream.Length == 0)
                {
                    return true;
                }

                stream.Seek(-1, SeekOrigin.End);
                return stream.ReadByte() == '\n';
            }
        }
    }

    /// <summary>Writes a <see cref="TrackedPose"/> as <c>{"position":[x,y,z],"rotation":[x,y,z,w]}</c>, metres and a unit quaternion.</summary>
    internal sealed class TrackedPoseJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(TrackedPose) || objectType == typeof(TrackedPose?);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            var pose = (TrackedPose)value;
            writer.WriteStartObject();
            writer.WritePropertyName("position");
            WriteNumbers(writer, pose.Position.X, pose.Position.Y, pose.Position.Z);
            writer.WritePropertyName("rotation");
            WriteNumbers(writer, pose.Rotation.X, pose.Rotation.Y, pose.Rotation.Z, pose.Rotation.W);
            writer.WriteEndObject();
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                if (objectType == typeof(TrackedPose))
                {
                    throw new JsonSerializationException("A pose cannot be null here.");
                }

                return null;
            }

            var json = JObject.Load(reader);
            var position = ReadNumbers(json, "position", 3);
            var rotation = ReadNumbers(json, "rotation", 4);
            return new TrackedPose(
                new Vector3(position[0], position[1], position[2]),
                new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]));
        }

        private static void WriteNumbers(JsonWriter writer, params float[] numbers)
        {
            writer.WriteStartArray();
            foreach (var number in numbers)
            {
                writer.WriteValue(number);
            }

            writer.WriteEndArray();
        }

        private static float[] ReadNumbers(JObject json, string name, int expectedCount)
        {
            if (!(json[name] is JArray array) || array.Count != expectedCount)
            {
                throw new JsonSerializationException($"A pose needs '{name}' as an array of {expectedCount} numbers.");
            }

            var numbers = new float[expectedCount];
            for (var index = 0; index < expectedCount; index++)
            {
                var token = array[index];
                if (token.Type != JTokenType.Float && token.Type != JTokenType.Integer)
                {
                    throw new JsonSerializationException($"A pose's '{name}' must hold numbers only.");
                }

                numbers[index] = token.Value<float>();
            }

            return numbers;
        }
    }
}
