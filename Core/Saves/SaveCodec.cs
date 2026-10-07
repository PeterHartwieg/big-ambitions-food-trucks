using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace FoodTrucks.Core.Saves
{
    public sealed class SaveCodec
    {
        private readonly int currentVersion;
        private readonly Dictionary<int, Func<JObject, JObject>> migrations;
        private readonly JsonSerializerSettings settings = new JsonSerializerSettings
        {
            ContractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy
                {
                    ProcessExtensionDataNames = false
                }
            },
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore,
            DateParseHandling = DateParseHandling.None,
            MetadataPropertyHandling = MetadataPropertyHandling.Ignore
        };

        public SaveCodec(int currentVersion = SaveState.CurrentSchemaVersion,
            IReadOnlyDictionary<int, Func<JObject, JObject>>? migrations = null)
        {
            if (currentVersion < 1)
                throw new ArgumentOutOfRangeException(nameof(currentVersion));

            this.currentVersion = currentVersion;
            this.migrations = new Dictionary<int, Func<JObject, JObject>>();
            if (migrations != null)
            {
                foreach (var migration in migrations)
                    this.migrations.Add(migration.Key, migration.Value);
            }
        }

        public string Serialize(SaveState state)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));

            var document = JObject.FromObject(state, JsonSerializer.Create(settings));
            document["schemaVersion"] = currentVersion;
            return document.ToString(Formatting.None);
        }

        public LoadResult Load(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return new LoadResult(LoadStatus.Fresh, new SaveState { SchemaVersion = currentVersion });

            try
            {
                using var reader = new JsonTextReader(new StringReader(json))
                {
                    DateParseHandling = DateParseHandling.None
                };
                var document = JObject.Load(reader);
                if (reader.TokenType != JsonToken.EndObject || reader.Read())
                    throw new JsonReaderException("Incomplete JSON object or additional content after save state.");

                var versionToken = document["schemaVersion"];
                if (versionToken == null || versionToken.Type != JTokenType.Integer)
                    throw new JsonSerializationException("Missing or invalid schemaVersion: expected a non-negative integer.");

                var version = versionToken.Value<int>();
                if (version < 0)
                    throw new JsonSerializationException("Invalid schemaVersion: expected a non-negative integer.");

                if (version > currentVersion)
                    return new LoadResult(LoadStatus.NewerVersion, rawJson: json);

                var status = version == currentVersion ? LoadStatus.Loaded : LoadStatus.Migrated;
                while (version < currentVersion)
                {
                    if (!migrations.TryGetValue(version, out var migrate))
                        throw new JsonSerializationException($"Missing migration from schema version {version} to {version + 1}.");

                    document = migrate(document)
                        ?? throw new JsonSerializationException($"Migration from schema version {version} returned null.");
                    document["schemaVersion"] = ++version;
                }

                var state = document.ToObject<SaveState>(JsonSerializer.Create(settings))
                    ?? throw new JsonSerializationException("Save state must be a JSON object.");
                ValidateState(state);
                return new LoadResult(status, state);
            }
            catch (Exception exception)
            {
                return new LoadResult(LoadStatus.Corrupt, rawJson: json, error: exception.Message);
            }
        }

        internal static void ValidateState(SaveState state)
        {
            if (state.Licence == null)
                throw new JsonSerializationException("Licence must not be null.");
            if (state.SaveCount < 0)
                throw new JsonSerializationException("SaveCount must not be negative.");

            foreach (var vehicle in state.Vehicles)
            {
                if (vehicle == null)
                    throw new JsonSerializationException("Vehicles must not contain null entries.");
                if (vehicle.LifetimeSales < 0 || vehicle.LifetimeRevenueCents < 0)
                    throw new JsonSerializationException("Vehicle LifetimeSales and LifetimeRevenueCents must not be negative.");
            }

            var days = new HashSet<int>();
            foreach (var sale in state.DailySales)
            {
                if (sale == null)
                    throw new JsonSerializationException("DailySales must not contain null entries.");
                if (sale.RevenueCents < 0)
                    throw new JsonSerializationException("DailySales RevenueCents must not be negative.");
                if (!days.Add(sale.Day))
                    throw new JsonSerializationException($"DailySales contains duplicate Day {sale.Day}.");
            }
        }
    }
}
