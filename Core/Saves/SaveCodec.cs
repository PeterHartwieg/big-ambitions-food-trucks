using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

        // Applies the saved count and mod version only after serialization succeeds.
        public string SerializeForSave(SaveState state, string modVersion)
        {
            if (state == null)
                throw new ArgumentNullException(nameof(state));
            if (!ModVersion.IsValid(modVersion))
                throw new ArgumentException("Mod version must be a valid SemVer version.", nameof(modVersion));

            var saveCount = state.SaveCount < int.MaxValue ? state.SaveCount + 1 : int.MaxValue;
            var document = JObject.FromObject(state, JsonSerializer.Create(settings));
            document["schemaVersion"] = currentVersion;
            document["saveCount"] = saveCount;
            document["writtenByModVersion"] = modVersion;
            var json = document.ToString(Formatting.None);

            state.SaveCount = saveCount;
            state.WrittenByModVersion = modVersion;
            return json;
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
            if (!string.IsNullOrEmpty(state.WrittenByModVersion) && !ModVersion.IsValid(state.WrittenByModVersion))
                throw new JsonSerializationException("WrittenByModVersion must be a valid SemVer version or empty.");

            var vehicleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var vehicle in state.Vehicles)
            {
                if (vehicle == null)
                    throw new JsonSerializationException("Vehicles must not contain null entries.");
                if (vehicle.LifetimeSales < 0 || vehicle.LifetimeRevenueCents < 0)
                    throw new JsonSerializationException("Vehicle LifetimeSales and LifetimeRevenueCents must not be negative.");
                if (string.IsNullOrEmpty(vehicle.VehicleId))
                    throw new JsonSerializationException("VehicleId must not be empty.");
                if (!vehicleIds.Add(vehicle.VehicleId))
                    throw new JsonSerializationException($"Vehicles contains duplicate VehicleId {vehicle.VehicleId}.");
                if (double.IsNaN(vehicle.SaleProgress) || double.IsInfinity(vehicle.SaleProgress) ||
                    vehicle.SaleProgress < 0 || vehicle.SaleProgress >= 1)
                    throw new JsonSerializationException("Vehicle SaleProgress must be finite and in [0, 1).");
                if (vehicle.CustomPriceCents == null || vehicle.CustomPriceCents.Any(price =>
                        string.IsNullOrEmpty(price.Key) || price.Value <= 0 || price.Value > 1_000_000))
                    throw new JsonSerializationException("Vehicle CustomPriceCents must name items with prices from 1 to 1,000,000 cents.");
                if (vehicle.Service != null) ValidateService(vehicle.Service);
                if (vehicle.Health != null) FoodTrucks.Core.Reputation.ReputationValidation.Validate(vehicle.Health);
                if (vehicle.VehicleType == null)
                    throw new JsonSerializationException("VehicleType must not be null.");
                if (vehicle.Schedule != null && (vehicle.Schedule.Days == null || vehicle.Schedule.Stops == null ||
                                                 vehicle.Schedule.Stops.Any(s => s == null || s.SpotKey == null)))
                    throw new JsonSerializationException("Schedule Days and Stops must be lists without null entries.");
                if (vehicle.Transit != null && (vehicle.Transit.ToSpotKey == null || vehicle.Transit.ArrivesAt < vehicle.Transit.DepartsAt))
                    throw new JsonSerializationException("Transit must name its spot and arrive no earlier than it departs.");
                if (vehicle.Crew == null || vehicle.Crew.Any(string.IsNullOrEmpty) || vehicle.Crew.Distinct(StringComparer.Ordinal).Count() != vehicle.Crew.Count)
                    throw new JsonSerializationException("Crew must list distinct, non-empty employee IDs.");
                if (vehicle.Shifts == null || vehicle.Shifts.Any(p => p == null || p.Legs == null || p.Legs.Any(l =>
                        l == null || !Enum.IsDefined(typeof(FoodTrucks.Core.Fleet.LegKind), l.Kind) || l.StopIndex < 0 || l.End < l.Start)))
                    throw new JsonSerializationException("Shifts must be plans with legs that end no earlier than they start.");
                if ((vehicle.Arrival != null && vehicle.Arrival.SpotKey == null) || (vehicle.Skipped != null && vehicle.Skipped.SpotKey == null))
                    throw new JsonSerializationException("Arrival and Skipped must name their spot.");
                if (vehicle.Days == null || vehicle.Days.Any(d => d == null || d.Visits == null ||
                        d.Visits.Any(v => v == null || string.IsNullOrEmpty(v.SpotKey) || v.Minutes < 0)) ||
                    vehicle.Days.Select(d => d.Day).Distinct().Count() != vehicle.Days.Count)
                    throw new JsonSerializationException("Days must be one record per day, with named spots and minutes not negative.");
                if (vehicle.StockBatches == null || vehicle.StockBatches.Any(p => string.IsNullOrEmpty(p.Key) || p.Value == null || p.Value.Any(b => b == null || b.Units <= 0)))
                    throw new JsonSerializationException("StockBatches must name their items and hold batches of at least one unit.");
                if (vehicle.HireStop < 0)
                    throw new JsonSerializationException("HireStop must not be negative.");
            }

            if (state.Regulars == null)
                throw new JsonSerializationException("Regulars must not be null.");
            FoodTrucks.Core.Reputation.ReputationValidation.Validate(state.Regulars);
            if (state.Catering == null)
                throw new JsonSerializationException("Catering must not be null.");
            FoodTrucks.Core.Catering.CateringValidation.Validate(state.Catering);
            if (state.Events == null || state.Events.Events == null || state.Events.Events.Any(e =>
                    e == null || e.Kind == null || e.PlaceId == null || !ValidHolds(e.Holds) ||
                    e.Booked < 0 || e.Places < 0 || e.FeeCents < 0 || e.Start < 0 || e.End < e.Start) ||
                state.Events.Events.Select(e => e.Id).Distinct().Count() != state.Events.Events.Count)
                throw new JsonSerializationException("Events must be distinct events with their kind, place and vehicles named and counts not negative.");
            if (state.PremiumHolds == null || state.PremiumHolds.Any(p => string.IsNullOrEmpty(p.Key) || !ValidHolds(p.Value)))
                throw new JsonSerializationException("PremiumHolds must name their spots and vehicles, each hold ending no earlier than it starts.");
            if (state.Partnerships == null || state.Partnerships.Any(p => p == null || string.IsNullOrEmpty(p.VehicleId) || string.IsNullOrEmpty(p.Venue)) ||
                state.Partnerships.Select(p => p.VehicleId).Distinct(StringComparer.Ordinal).Count() != state.Partnerships.Count ||
                state.Partnerships.Select(p => p.Venue).Distinct(StringComparer.Ordinal).Count() != state.Partnerships.Count)
                throw new JsonSerializationException("Partnerships must be one per vehicle and one per venue, each named.");

            var days = new HashSet<int>();
            long total = 0;
            foreach (var sale in state.DailySales)
            {
                if (sale == null)
                    throw new JsonSerializationException("DailySales must not contain null entries.");
                if (sale.CostCents < 0)
                    throw new JsonSerializationException("DailySales CostCents must not be negative.");
                if (sale.RevenueCents < 0)
                    throw new JsonSerializationException("DailySales RevenueCents must not be negative.");
                if (!days.Add(sale.Day))
                    throw new JsonSerializationException($"DailySales contains duplicate Day {sale.Day}.");
                total = checked(total + sale.RevenueCents);
            }
        }

        private static bool ValidHolds(List<FoodTrucks.Core.Pitches.PlaceHold>? holds) =>
            holds != null && holds.All(h => h != null && !string.IsNullOrEmpty(h.VehicleId) && (h.Until == null || h.Until >= h.From));

        private static void ValidateService(ServiceState service)
        {
            if (service.Queue == null || service.Window == null)
                throw new JsonSerializationException("Service Queue and Window must not be null.");
            if (service.Minute < 0 || service.NextCustomerId < 1)
                throw new JsonSerializationException("Service Minute must not be negative and NextCustomerId must be at least 1.");
            var ids = new HashSet<long>();
            foreach (var customer in service.Window.Concat(service.Queue))
            {
                if (customer == null)
                    throw new JsonSerializationException("Service customers must not be null.");
                if (customer.Id < 1 || customer.Id >= service.NextCustomerId || !ids.Add(customer.Id))
                    throw new JsonSerializationException("Service customer IDs must be unique and below NextCustomerId.");
                if (customer.ArrivedAtMinute < 0 || customer.ArrivedAtMinute > service.Minute)
                    throw new JsonSerializationException("Service customer ArrivedAtMinute must lie between 0 and Minute.");
                if (double.IsNaN(customer.Patience) || double.IsInfinity(customer.Patience) || customer.Patience <= 0 ||
                    double.IsNaN(customer.MinutesLeft) || double.IsInfinity(customer.MinutesLeft) || customer.MinutesLeft < 0)
                    throw new JsonSerializationException("Service customer Patience must be positive and MinutesLeft not negative, both finite.");
            }
        }
    }
}
