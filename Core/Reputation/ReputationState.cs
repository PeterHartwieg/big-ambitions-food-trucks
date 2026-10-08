using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Reputation
{
    public enum Grade { Ungraded, A, B, C }

    public class HealthState
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Grade Grade { get; set; }
        public int? LastInspectionDay { get; set; }
        public int FirstSeenDay { get; set; } = 1;
        // The last day the inspector's chance was drawn, so a vehicle opened twice in a day draws once.
        public int? LastDrawDay { get; set; }
        public long RandomState { get; set; }
        public List<HealthMark> Marks { get; set; } = new List<HealthMark>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class HealthMark
    {
        public int Day { get; set; }
        public int Points { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class RegularsState
    {
        public int LastUpdatedDay { get; set; } = int.MinValue;
        public List<RegularsEntry> Entries { get; set; } = new List<RegularsEntry>();

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public class RegularsEntry
    {
        public string SpotKey { get; set; } = string.Empty;
        [JsonConverter(typeof(StringEnumConverter))]
        public DayOfWeek Weekday { get; set; }
        public double Bonus { get; set; }
        public int LastVisitDay { get; set; }

        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public static class ReputationValidation
    {
        internal static void Validate(HealthState state)
        {
            if (state == null) throw new JsonSerializationException("Health state must not be null.");
            if (!Enum.IsDefined(typeof(Grade), state.Grade)) throw new JsonSerializationException("Health grade is not known.");
            if (state.Marks == null) throw new JsonSerializationException("Health marks must not be null.");
            var days = new HashSet<int>();
            foreach (var mark in state.Marks)
            {
                if (mark == null) throw new JsonSerializationException("Health marks must not contain null entries.");
                if (!days.Add(mark.Day)) throw new JsonSerializationException($"Health marks repeat day {mark.Day}.");
                if (mark.Points < 0) throw new JsonSerializationException($"Health points on day {mark.Day} must not be negative.");
            }
        }

        internal static void Validate(RegularsState state)
        {
            if (state == null) throw new JsonSerializationException("Regulars state must not be null.");
            if (state.Entries == null) throw new JsonSerializationException("Regulars entries must not be null.");
            var keys = new HashSet<(string, DayOfWeek)>();
            foreach (var entry in state.Entries)
            {
                if (entry == null) throw new JsonSerializationException("Regulars entries must not contain null entries.");
                if (string.IsNullOrEmpty(entry.SpotKey)) throw new JsonSerializationException("Regulars spot key must not be empty.");
                if (!Enum.IsDefined(typeof(DayOfWeek), entry.Weekday)) throw new JsonSerializationException("Regulars weekday is not known.");
                if (double.IsNaN(entry.Bonus) || double.IsInfinity(entry.Bonus) || entry.Bonus < 0 || entry.Bonus > 5)
                    throw new JsonSerializationException("Regulars bonus must be finite and between 0 and 5.");
                if (!keys.Add((entry.SpotKey, entry.Weekday)))
                    throw new JsonSerializationException($"Regulars repeat spot '{entry.SpotKey}' on {entry.Weekday}.");
            }
        }
    }
}
