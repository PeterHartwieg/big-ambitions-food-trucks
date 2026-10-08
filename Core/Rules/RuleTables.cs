using FoodTrucks.Core.Catering;
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
            r.OnlyKeys(root, "schemaVersion", "queue", "health", "regulars", "competition", "fleet", "catering");
            r.SchemaVersion(root, SchemaVersion);
            Queue = QueueRules.Read(r, r.Object(root, "queue"));
            Health = HealthRules.Read(r, r.Object(root, "health"));
            Regulars = RegularsRules.Read(r, r.Object(root, "regulars"));
            Competition = CompetitionRules.Read(r, r.Object(root, "competition"));
            Fleet = FleetRules.Read(r, r.Object(root, "fleet"));
            Catering = CateringRules.Read(r, r.Object(root, "catering"));
        }

        public QueueRules Queue { get; }
        public HealthRules Health { get; }
        public RegularsRules Regulars { get; }
        public CompetitionRules Competition { get; }
        public FleetRules Fleet { get; }
        public CateringRules Catering { get; }

        public static RuleTables Parse(string? json)
        {
            var reader = new DataReader("rules.json");
            return new RuleTables(reader, reader.Parse(json));
        }
    }

    public sealed class QueueRules
    {
        public QueueRules(double patienceMinutes = 20, double patienceSpread = 0.25, double rainPatience = 0.6, int maxQueue = 12)
        {
            PatienceMinutes = patienceMinutes;
            PatienceSpread = patienceSpread;
            RainPatience = rainPatience;
            MaxQueue = maxQueue;
        }

        public double PatienceMinutes { get; }
        // Each customer's patience is PatienceMinutes times a factor drawn from 1 ± PatienceSpread.
        public double PatienceSpread { get; }
        // Multiplies everyone's patience while it rains, so the queue thins as soon as rain starts.
        public double RainPatience { get; }
        public int MaxQueue { get; }

        internal static QueueRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "patienceMinutes", "patienceSpread", "rainPatience", "maxQueue");
            return new QueueRules(
                r.Number(obj, "patienceMinutes", 1, 240),
                r.Number(obj, "patienceSpread", 0, 0.9),
                r.Number(obj, "rainPatience", 0.05, 1),
                r.Integer(obj, "maxQueue", 1, 100));
        }
    }
}
