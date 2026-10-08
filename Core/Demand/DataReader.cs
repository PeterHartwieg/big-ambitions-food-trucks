using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Demand
{
    public sealed class DemandDataException : Exception
    {
        public DemandDataException(string message) : base(message) { }
    }

    // Strict reads over a parsed data file. Every failure names the file and the JSON path.
    // Keys starting with an underscore are notes and never read.
    internal sealed class DataReader
    {
        private readonly string file;

        public DataReader(string file) => this.file = file;

        public JObject Parse(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw Fail("", "is empty");
            try
            {
                using var reader = new JsonTextReader(new StringReader(json!))
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Double
                };
                var settings = new JsonLoadSettings
                {
                    DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    CommentHandling = CommentHandling.Ignore
                };
                if (!(JToken.ReadFrom(reader, settings) is JObject root))
                    throw Fail("", "must be a JSON object");
                while (reader.Read())
                {
                    if (reader.TokenType != JsonToken.Comment) throw Fail("", "has content after the top-level object");
                }
                return root;
            }
            catch (JsonException ex)
            {
                throw new DemandDataException($"{file}: not valid JSON: {ex.Message}");
            }
        }

        public DemandDataException Fail(string path, string problem) =>
            new DemandDataException(string.IsNullOrEmpty(path) ? $"{file}: {problem}." : $"{file}: {path} {problem}.");

        public static string PathOf(JToken parent, string name)
        {
            var prefix = parent.Path;
            var segment = IsPlainName(name) ? name : $"['{name}']";
            if (prefix.Length == 0) return segment;
            return segment.StartsWith("[", StringComparison.Ordinal) ? prefix + segment : prefix + "." + segment;
        }

        private static bool IsPlainName(string name)
        {
            if (name.Length == 0) return false;
            foreach (var c in name)
                if (!char.IsLetterOrDigit(c) && c != '_') return false;
            return true;
        }

        // Rejects keys the schema does not know, so a typo fails instead of silently reading 1.0.
        public void OnlyKeys(JObject obj, params string[] allowed)
        {
            foreach (var property in obj.Properties())
            {
                if (property.Name.StartsWith("_", StringComparison.Ordinal)) continue;
                if (Array.IndexOf(allowed, property.Name) < 0)
                    throw Fail(PathOf(obj, property.Name), "is not a known field (expected one of: " + string.Join(", ", allowed) + ")");
            }
        }

        // Properties that are data, in file order, without notes.
        public IEnumerable<JProperty> Entries(JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                if (property.Name.Length == 0) throw Fail(PathOf(obj, property.Name), "must not be an empty name");
                if (!property.Name.StartsWith("_", StringComparison.Ordinal)) yield return property;
            }
        }

        public JToken Required(JObject parent, string name)
        {
            var token = parent[name];
            if (token == null || token.Type == JTokenType.Null) throw Fail(PathOf(parent, name), "is missing");
            return token;
        }

        public JObject Object(JObject parent, string name)
        {
            var token = Required(parent, name);
            if (token is JObject obj) return obj;
            throw Fail(PathOf(parent, name), "must be an object");
        }

        public JObject? OptionalObject(JObject parent, string name)
        {
            var token = parent[name];
            if (token == null) return null;
            if (token is JObject obj) return obj;
            throw Fail(PathOf(parent, name), "must be an object");
        }

        public JArray List(JObject parent, string name)
        {
            var token = Required(parent, name);
            if (token is JArray array) return array;
            throw Fail(PathOf(parent, name), "must be an array");
        }

        public JArray? OptionalArray(JObject parent, string name)
        {
            var token = parent[name];
            if (token == null) return null;
            if (token is JArray array) return array;
            throw Fail(PathOf(parent, name), "must be an array");
        }

        public double Number(JObject parent, string name, double min, double max) =>
            Number(Required(parent, name), PathOf(parent, name), min, max);

        public double Number(JToken token, string path, double min, double max)
        {
            if (token.Type != JTokenType.Integer && token.Type != JTokenType.Float)
                throw Fail(path, "must be a number");
            // Integers beyond long arrive as BigInteger, which Value<double>() can't convert.
            if (!(((JValue)token).Value is IConvertible))
                throw Fail(path, $"must be between {Format(min)} and {Format(max)}");
            var value = token.Value<double>();
            if (double.IsNaN(value) || double.IsInfinity(value))
                throw Fail(path, "must be a finite number");
            if (value < min || value > max)
                throw Fail(path, $"must be between {Format(min)} and {Format(max)}, got {Format(value)}");
            return value;
        }

        public int Integer(JObject parent, string name, int min, int max)
        {
            var token = Required(parent, name);
            var path = PathOf(parent, name);
            if (token.Type != JTokenType.Integer) throw Fail(path, "must be a whole number");
            if (!(((JValue)token).Value is long value)) throw Fail(path, $"must be between {min} and {max}");
            if (value < min || value > max) throw Fail(path, $"must be between {min} and {max}, got {value}");
            return (int)value;
        }

        public string Text(JObject parent, string name) => Text(Required(parent, name), PathOf(parent, name));

        public string Text(JToken token, string path)
        {
            if (token.Type != JTokenType.String) throw Fail(path, "must be a string");
            var value = token.Value<string>() ?? string.Empty;
            if (value.Length == 0) throw Fail(path, "must not be empty");
            return value;
        }

        public void SchemaVersion(JObject root, int supported)
        {
            var version = Integer(root, "schemaVersion", 0, int.MaxValue);
            if (version != supported)
                throw Fail("schemaVersion", $"is {version}; this mod version reads schema {supported}");
        }

        private static string Format(double value) =>
            value == double.MaxValue ? "any" : value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
