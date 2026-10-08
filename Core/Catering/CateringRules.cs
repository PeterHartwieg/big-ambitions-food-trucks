using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Catering
{
    public sealed class CateringKind
    {
        public CateringKind(string id, IEnumerable<string> areas, (int Min, int Max) start,
            (int Min, int Max) durationMinutes, (int Min, int Max) guests,
            IReadOnlyDictionary<string, double> categories, double weight = 1, IEnumerable<DayOfWeek>? days = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Kind ID must not be empty.", nameof(id));
            Id = id;
            Areas = CateringRuleChecks.Names(areas, nameof(areas));
            CateringRuleChecks.Range(start, 0, 1439, nameof(start));
            CateringRuleChecks.Range(durationMinutes, 1, int.MaxValue, nameof(durationMinutes));
            CateringRuleChecks.Range(guests, 1, 1000, nameof(guests));
            CateringRuleChecks.Number(weight, double.Epsilon, double.MaxValue, nameof(weight));
            var weekdays = (days ?? Enum.GetValues(typeof(DayOfWeek)).Cast<DayOfWeek>()).ToList();
            if (weekdays.Count == 0 || weekdays.Any(d => !Enum.IsDefined(typeof(DayOfWeek), d)))
                throw new ArgumentException("Days must contain known weekdays.", nameof(days));
            if (categories == null || categories.Count == 0) throw new ArgumentException("Categories must not be empty.", nameof(categories));
            var copy = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (var pair in categories)
            {
                if (string.IsNullOrWhiteSpace(pair.Key)) throw new ArgumentException("Category must not be empty.", nameof(categories));
                CateringRuleChecks.Number(pair.Value, double.Epsilon, double.MaxValue, nameof(categories));
                copy.Add(pair.Key, pair.Value);
            }
            Start = start;
            DurationMinutes = durationMinutes;
            Guests = guests;
            Categories = new ReadOnlyDictionary<string, double>(copy);
            Weight = weight;
            Days = weekdays.Distinct().OrderBy(d => d).ToList().AsReadOnly();
        }

        public string Id { get; }
        public IReadOnlyList<string> Areas { get; }
        public IReadOnlyList<DayOfWeek> Days { get; }
        public (int Min, int Max) Start { get; }
        public (int Min, int Max) DurationMinutes { get; }
        public (int Min, int Max) Guests { get; }
        public IReadOnlyDictionary<string, double> Categories { get; }
        public double Weight { get; }
    }

    public sealed class CateringRules
    {
        public CateringRules(IEnumerable<string>? vehicleTypes = null, double offersPerDayAtMaturity = 0.5,
            int maxOffersPerDay = 2, long maturityServed = 500, (int Min, int Max)? leadDays = null,
            int replyDays = 1, int graceMinutes = 10, double unitsPerGuest = 1, double feeMarkup = 1.25,
            double reputationStart = 1, double reputationMin = 0.1, double reputationSuccess = 0.05,
            double reputationFailure = 0.25, IEnumerable<CateringKind>? kinds = null, int arrivalWindowMinutes = 60)
        {
            VehicleTypes = CateringRuleChecks.Names(vehicleTypes ?? new[] { "truck" }, nameof(vehicleTypes));
            CateringRuleChecks.Number(offersPerDayAtMaturity, 0, int.MaxValue, nameof(offersPerDayAtMaturity));
            CateringRuleChecks.Number(maxOffersPerDay, 0, int.MaxValue, nameof(maxOffersPerDay));
            if (maturityServed < 1) throw new ArgumentOutOfRangeException(nameof(maturityServed));
            var lead = leadDays ?? (2, 5);
            CateringRuleChecks.Range(lead, 0, int.MaxValue, nameof(leadDays));
            CateringRuleChecks.Number(replyDays, 0, int.MaxValue, nameof(replyDays));
            CateringRuleChecks.Number(graceMinutes, 0, int.MaxValue, nameof(graceMinutes));
            CateringRuleChecks.Number(unitsPerGuest, double.Epsilon, int.MaxValue / 1000.0, nameof(unitsPerGuest));
            CateringRuleChecks.Number(feeMarkup, 0, double.MaxValue, nameof(feeMarkup));
            CateringRuleChecks.Number(reputationStart, 0, 1, nameof(reputationStart));
            CateringRuleChecks.Number(reputationMin, 0, reputationStart, nameof(reputationMin));
            CateringRuleChecks.Number(reputationSuccess, 0, 1, nameof(reputationSuccess));
            CateringRuleChecks.Number(reputationFailure, 0, 1, nameof(reputationFailure));
            var list = (kinds ?? Defaults()).ToList();
            if (list.Count == 0 || list.Any(k => k == null) || list.Select(k => k.Id).Distinct(StringComparer.Ordinal).Count() != list.Count)
                throw new ArgumentException("Kinds must not be empty and must have unique IDs.", nameof(kinds));
            OffersPerDayAtMaturity = offersPerDayAtMaturity;
            MaxOffersPerDay = maxOffersPerDay;
            MaturityServed = maturityServed;
            LeadDays = lead;
            ReplyDays = replyDays;
            GraceMinutes = graceMinutes;
            CateringRuleChecks.Number(arrivalWindowMinutes, 0, 1440, nameof(arrivalWindowMinutes));
            ArrivalWindowMinutes = arrivalWindowMinutes;
            UnitsPerGuest = unitsPerGuest;
            FeeMarkup = feeMarkup;
            ReputationStart = reputationStart;
            ReputationMin = reputationMin;
            ReputationSuccess = reputationSuccess;
            ReputationFailure = reputationFailure;
            Kinds = list.AsReadOnly();
        }

        public IReadOnlyList<string> VehicleTypes { get; }
        public double OffersPerDayAtMaturity { get; }
        public int MaxOffersPerDay { get; }
        public long MaturityServed { get; }
        public (int Min, int Max) LeadDays { get; }
        public int ReplyDays { get; }
        public int GraceMinutes { get; }
        public int ArrivalWindowMinutes { get; }
        public double UnitsPerGuest { get; }
        public double FeeMarkup { get; }
        public double ReputationStart { get; }
        public double ReputationMin { get; }
        public double ReputationSuccess { get; }
        public double ReputationFailure { get; }
        public IReadOnlyList<CateringKind> Kinds { get; }

        private static IEnumerable<CateringKind> Defaults()
        {
            yield return new CateringKind("office", new[] { "office" }, (690, 750), (60, 90), (40, 120),
                new Dictionary<string, double> { ["fastfood"] = 1, ["pizza"] = 1, ["coffee"] = 0.3 },
                days: new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday });
            yield return new CateringKind("party", new[] { "residential", "nightlife" }, (1080, 1200), (90, 150), (40, 100),
                new Dictionary<string, double> { ["pizza"] = 1, ["fastfood"] = 1, ["icecream"] = 0.5 }, 0.8);
            yield return new CateringKind("event", new[] { "waterfront", "retail" }, (720, 960), (120, 180), (100, 200),
                new Dictionary<string, double> { ["fastfood"] = 1, ["icecream"] = 1, ["colddrinks"] = 0.5, ["pizza"] = 0.5 }, 0.5,
                new[] { DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday });
        }

        internal static CateringRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "vehicleTypes", "offersPerDayAtMaturity", "maxOffersPerDay", "maturityServed", "leadDays",
                "replyDays", "graceMinutes", "arrivalWindowMinutes", "unitsPerGuest", "feeMarkup", "reputation", "kinds");
            var rep = r.Object(obj, "reputation");
            r.OnlyKeys(rep, "start", "min", "success", "failure");
            var start = r.Number(rep, "start", 0, 1);
            var min = r.Number(rep, "min", 0, 1);
            if (min > start) throw r.Fail(DataReader.PathOf(rep, "min"), "must not exceed start");
            var kinds = new List<CateringKind>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var list = r.List(obj, "kinds");
            if (list.Count == 0) throw r.Fail(list.Path, "must not be empty");
            foreach (var token in list)
            {
                var kind = token as JObject ?? throw r.Fail(token.Path, "must be an object");
                r.OnlyKeys(kind, "id", "areas", "days", "start", "durationMinutes", "guests", "categories", "weight");
                var id = ReadText(r, r.Required(kind, "id"));
                if (!ids.Add(id)) throw r.Fail(DataReader.PathOf(kind, "id"), "must be unique");
                var days = new List<DayOfWeek>();
                if (kind["days"] != null)
                {
                    var dayList = r.List(kind, "days");
                    if (dayList.Count == 0) throw r.Fail(dayList.Path, "must not be empty");
                    foreach (var day in dayList)
                    {
                        var name = r.Text(day, day.Path);
                        var index = Array.IndexOf(new[] { "sun", "mon", "tue", "wed", "thu", "fri", "sat" }, name);
                        if (index < 0) throw r.Fail(day.Path, "must be a weekday from sun to sat");
                        days.Add((DayOfWeek)index);
                    }
                }
                var categories = r.Object(kind, "categories");
                var weights = new Dictionary<string, double>(StringComparer.Ordinal);
                foreach (var entry in r.Entries(categories))
                {
                    if (string.IsNullOrWhiteSpace(entry.Name)) throw r.Fail(entry.Path, "must not be blank");
                    weights.Add(entry.Name, Positive(r, entry.Value, entry.Value.Path));
                }
                if (weights.Count == 0) throw r.Fail(categories.Path, "must not be empty");
                kinds.Add(new CateringKind(id, Names(r, kind, "areas"), ReadRange(r, kind, "start", 0, 1439, true),
                    ReadRange(r, kind, "durationMinutes", 1, int.MaxValue), ReadRange(r, kind, "guests", 1, 1000), weights,
                    Positive(r, r.Required(kind, "weight"), DataReader.PathOf(kind, "weight")), kind["days"] == null ? null : days));
            }
            return new CateringRules(Names(r, obj, "vehicleTypes"), r.Number(obj, "offersPerDayAtMaturity", 0, int.MaxValue),
                r.Integer(obj, "maxOffersPerDay", 0, int.MaxValue), r.Integer(obj, "maturityServed", 1, int.MaxValue),
                ReadRange(r, obj, "leadDays", 0, int.MaxValue), r.Integer(obj, "replyDays", 0, int.MaxValue),
                r.Integer(obj, "graceMinutes", 0, int.MaxValue), Positive(r, r.Required(obj, "unitsPerGuest"), DataReader.PathOf(obj, "unitsPerGuest"), int.MaxValue / 1000.0),
                r.Number(obj, "feeMarkup", 0, double.MaxValue), start, min, r.Number(rep, "success", 0, 1), r.Number(rep, "failure", 0, 1), kinds, r.Integer(obj, "arrivalWindowMinutes", 0, 1440));
        }

        private static double Positive(DataReader r, JToken token, string path, double max = double.MaxValue)
        {
            var value = r.Number(token, path, 0, max);
            if (value == 0) throw r.Fail(path, "must be above 0");
            return value;
        }

        private static string ReadText(DataReader r, JToken token)
        {
            var value = r.Text(token, token.Path);
            if (string.IsNullOrWhiteSpace(value)) throw r.Fail(token.Path, "must not be blank");
            return value;
        }

        private static IEnumerable<string> Names(DataReader r, JObject obj, string name)
        {
            var list = r.List(obj, name);
            if (list.Count == 0) throw r.Fail(list.Path, "must not be empty");
            return list.Select(t => ReadText(r, t)).ToList();
        }

        private static (int Min, int Max) ReadRange(DataReader r, JObject obj, string name, int min, int max, bool time = false)
        {
            var list = r.List(obj, name);
            if (list.Count != 2) throw r.Fail(list.Path, "must contain a minimum and maximum");
            var values = new int[2];
            for (var i = 0; i < 2; i++)
            {
                var token = list[i];
                if (time)
                {
                    var text = r.Text(token, token.Path);
                    if (text.Length != 5 || text[2] != ':' || text.Where((c, n) => n != 2).Any(c => c < '0' || c > '9'))
                        throw r.Fail(token.Path, "must be a time HH:MM");
                    var hour = int.Parse(text.Substring(0, 2));
                    var minute = int.Parse(text.Substring(3, 2));
                    if (hour > 23 || minute > 59) throw r.Fail(token.Path, "must be a time HH:MM");
                    values[i] = hour * 60 + minute;
                }
                else
                {
                    if (token.Type != JTokenType.Integer) throw r.Fail(token.Path, "must be a whole number");
                    values[i] = (int)r.Number(token, token.Path, min, max);
                }
            }
            if (values[0] > values[1]) throw r.Fail(list.Path, "minimum must not exceed maximum");
            return (values[0], values[1]);
        }
    }

    internal static class CateringRuleChecks
    {
        internal static void Number(double value, double min, double max, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(name, $"Must be finite and between {min} and {max}.");
        }

        internal static void Range((int Min, int Max) range, int min, int max, string name)
        {
            if (range.Min < min || range.Max > max || range.Min > range.Max)
                throw new ArgumentOutOfRangeException(name, $"Must be an ordered range between {min} and {max}.");
        }

        internal static IReadOnlyList<string> Names(IEnumerable<string> names, string name)
        {
            if (names == null) throw new ArgumentNullException(name);
            var list = names.ToList();
            if (list.Count == 0 || list.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Names must not be empty.", name);
            return list.Distinct(StringComparer.Ordinal).ToList().AsReadOnly();
        }
    }
}
