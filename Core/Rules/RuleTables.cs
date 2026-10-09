using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Catering;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Reputation;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Rules
{
    // The validated contents of data/rules.json. Parse throws DemandDataException naming the field
    // of the first problem, like DemandTables.
    public sealed class RuleTables
    {
        public const int SchemaVersion = 1;

        private RuleTables(DataReader r, JObject root)
        {
            r.OnlyKeys(root, "schemaVersion", "queue", "service", "health", "regulars", "competition", "fleet", "catering", "liveCrowds", "commissary");
            r.SchemaVersion(root, SchemaVersion);
            Queue = QueueRules.Read(r, r.Object(root, "queue"));
            Service = ServiceRules.Read(r, r.Object(root, "service"));
            Health = HealthRules.Read(r, r.Object(root, "health"));
            Regulars = RegularsRules.Read(r, r.Object(root, "regulars"));
            Competition = CompetitionRules.Read(r, r.Object(root, "competition"));
            Fleet = FleetRules.Read(r, r.Object(root, "fleet"));
            Catering = CateringRules.Read(r, r.Object(root, "catering"));
            Commissary = CommissaryRules.Read(r, r.Object(root, "commissary"));
            LiveCrowds = LiveCrowds.Read(r, r.Object(root, "liveCrowds"));
        }

        public QueueRules Queue { get; }
        public ServiceRules Service { get; }
        public HealthRules Health { get; }
        public RegularsRules Regulars { get; }
        public CompetitionRules Competition { get; }
        public FleetRules Fleet { get; }
        public CateringRules Catering { get; }
        public CommissaryRules Commissary { get; }
        public LiveCrowds LiveCrowds { get; }

        public static RuleTables Parse(string? json)
        {
            var reader = new DataReader("rules.json");
            return new RuleTables(reader, reader.Parse(json));
        }
    }

    public enum ServiceMood { Complaining, Neutral, Pleased }

    public sealed class ServiceRules
    {
        public ServiceRules(double baseSkill = 50, double slowestTimeFactor = 1.5, double fastestSpeed = 1.3,
            double complainBelowSkill = 25, double pleasedFromSkill = 75, IReadOnlyDictionary<string, int>? queueLimits = null)
        {
            BaseSkill = baseSkill;
            SlowestTimeFactor = slowestTimeFactor;
            FastestSpeed = fastestSpeed;
            ComplainBelowSkill = complainBelowSkill;
            PleasedFromSkill = pleasedFromSkill;
            QueueLimits = queueLimits ?? new Dictionary<string, int>(StringComparer.Ordinal) { ["cart"] = 6, ["truck"] = 10 };
        }

        public double BaseSkill { get; }
        public double SlowestTimeFactor { get; }
        public double FastestSpeed { get; }
        public double ComplainBelowSkill { get; }
        public double PleasedFromSkill { get; }
        public IReadOnlyDictionary<string, int> QueueLimits { get; }

        // Order time is divided by this speed; BaseSkill keeps the item's base service time.
        public double Speed(double skill)
        {
            skill = ClampSkill(skill, BaseSkill);
            if (skill == BaseSkill) return 1.0;
            if (skill == 100) return FastestSpeed;
            var slowestSpeed = 1.0 / SlowestTimeFactor;
            return skill < BaseSkill
                ? slowestSpeed + (1.0 - slowestSpeed) * skill / BaseSkill
                : 1.0 + (FastestSpeed - 1.0) * (skill - BaseSkill) / (100.0 - BaseSkill);
        }

        // The game's worker measure. The player uses their skill alone.
        public static double EffectiveSkill(double skill, double satisfaction) =>
            ClampSkill(skill, 0) * ClampSkill(satisfaction, 0) / 100.0;

        // Several workers at one window: the simulator takes one speed, so each serves at the team's mean
        // speed, and customers react to the team's mean effective skill. An empty team serves at base speed.
        public (double Speed, ServiceMood Mood, double Skill) Team(IEnumerable<double> effectiveSkills)
        {
            var skills = (effectiveSkills ?? Array.Empty<double>()).Select(s => ClampSkill(s, BaseSkill)).ToList();
            if (skills.Count == 0) return (1.0, Mood(BaseSkill), BaseSkill);
            var skill = skills.Average();
            return (skills.Average(Speed), Mood(skill), skill);
        }

        // A hook for customer reactions; it does not change sales yet.
        public ServiceMood Mood(double skill) => skill < ComplainBelowSkill ? ServiceMood.Complaining
            : skill >= PleasedFromSkill ? ServiceMood.Pleased : ServiceMood.Neutral;

        public int QueueLimit(string vehicleKind, int maxQueue) =>
            QueueLimits.TryGetValue(vehicleKind, out var limit) ? Math.Min(limit, maxQueue) : maxQueue;

        private static double ClampSkill(double value, double whenNaN) =>
            double.IsNaN(value) ? whenNaN : Math.Max(0, Math.Min(100, value));

        internal static ServiceRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "baseSkill", "slowestTimeFactor", "fastestSpeed", "complainBelowSkill", "pleasedFromSkill", "queueLimits");
            var baseSkill = r.Number(obj, "baseSkill", 1, 99);
            var slowestTimeFactor = r.Number(obj, "slowestTimeFactor", 1, 5);
            var fastestSpeed = r.Number(obj, "fastestSpeed", 1, 3);
            var complainBelowSkill = r.Number(obj, "complainBelowSkill", 0, 100);
            var pleasedFromSkill = r.Number(obj, "pleasedFromSkill", 0, 100);
            if (pleasedFromSkill <= complainBelowSkill)
                throw r.Fail(DataReader.PathOf(obj, "pleasedFromSkill"), "must be above complainBelowSkill");
            var limits = r.Object(obj, "queueLimits");
            var queueLimits = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(limits)) queueLimits.Add(entry.Name, r.Integer(limits, entry.Name, 1, 100));
            return new ServiceRules(baseSkill, slowestTimeFactor, fastestSpeed, complainBelowSkill, pleasedFromSkill, queueLimits);
        }
    }

    public sealed class QueueRules
    {
        public QueueRules(double patienceMinutes = 20, double patienceSpread = 0.25, double rainPatience = 0.6, int maxQueue = 12,
            int walkInMinMinutes = 0, int walkInMaxMinutes = 0)
        {
            if (walkInMinMinutes < 0 || walkInMaxMinutes < walkInMinMinutes)
                throw new ArgumentOutOfRangeException(nameof(walkInMaxMinutes), "The walk-in range must be from 0 up.");
            PatienceMinutes = patienceMinutes;
            PatienceSpread = patienceSpread;
            RainPatience = rainPatience;
            MaxQueue = maxQueue;
            WalkInMinMinutes = walkInMinMinutes;
            WalkInMaxMinutes = walkInMaxMinutes;
        }

        public double PatienceMinutes { get; }
        // Each customer's patience is PatienceMinutes times a factor drawn from 1 ± PatienceSpread.
        public double PatienceSpread { get; }
        // Multiplies everyone's patience while it rains, so the queue thins as soon as rain starts.
        public double RainPatience { get; }
        public int MaxQueue { get; }
        // Whole minutes an arriving customer walks before joining the queue, drawn evenly from this range.
        // 0 to 0 joins at once, as the queue did before the walk-in.
        public int WalkInMinMinutes { get; }
        public int WalkInMaxMinutes { get; }

        internal static QueueRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "patienceMinutes", "patienceSpread", "rainPatience", "maxQueue", "walkInMinutes");
            var walk = r.List(obj, "walkInMinutes");
            if (walk.Count != 2) throw r.Fail(walk.Path, "must contain a minimum and maximum");
            var walkMin = WholeMinutes(r, walk[0]);
            var walkMax = WholeMinutes(r, walk[1]);
            if (walkMin > walkMax) throw r.Fail(walk.Path, "minimum must not exceed maximum");
            return new QueueRules(
                r.Number(obj, "patienceMinutes", 1, 240),
                r.Number(obj, "patienceSpread", 0, 0.9),
                r.Number(obj, "rainPatience", 0.05, 1),
                r.Integer(obj, "maxQueue", 1, 100),
                walkMin,
                walkMax);
        }

        private static int WholeMinutes(DataReader r, JToken token)
        {
            if (token.Type != JTokenType.Integer) throw r.Fail(token.Path, "must be a whole number");
            return (int)r.Number(token, token.Path, 0, 60);
        }
    }
}
