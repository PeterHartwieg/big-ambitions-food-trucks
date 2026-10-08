using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Reputation
{
    public sealed class GradeBand
    {
        public GradeBand(Grade grade = Grade.A, int? maxPoints = 13, double demand = 1, int inspectionWeeks = 12)
        {
            if (grade < Grade.A || grade > Grade.C) throw new ArgumentOutOfRangeException(nameof(grade));
            if (maxPoints.HasValue) ReputationRuleChecks.Range(maxPoints.Value, 0, int.MaxValue, nameof(maxPoints));
            ReputationRuleChecks.Range(demand, 0, 2, nameof(demand));
            if (demand == 0) throw new ArgumentOutOfRangeException(nameof(demand));
            ReputationRuleChecks.Range(inspectionWeeks, 1, 520, nameof(inspectionWeeks));
            Grade = grade;
            MaxPoints = maxPoints;
            Demand = demand;
            InspectionWeeks = inspectionWeeks;
        }

        public Grade Grade { get; }
        public int? MaxPoints { get; }
        public double Demand { get; }
        public int InspectionWeeks { get; }
    }

    public sealed class HealthRules
    {
        public HealthRules(int memoryDays = 14, int uncleanedDayPoints = 3, int staleStockPoints = 4,
            IEnumerable<GradeBand>? grades = null, int firstInspectionWeeks = 2, int minimumGapDays = 7, int daysPerWeek = 7)
        {
            ReputationRuleChecks.Range(memoryDays, 1, 365, nameof(memoryDays));
            ReputationRuleChecks.Range(uncleanedDayPoints, 0, 100, nameof(uncleanedDayPoints));
            ReputationRuleChecks.Range(staleStockPoints, 0, 100, nameof(staleStockPoints));
            ReputationRuleChecks.Range(firstInspectionWeeks, 1, 520, nameof(firstInspectionWeeks));
            ReputationRuleChecks.Range(minimumGapDays, 0, 365, nameof(minimumGapDays));
            ReputationRuleChecks.Range(daysPerWeek, 1, 365, nameof(daysPerWeek));
            var bands = (grades ?? new[]
            {
                new GradeBand(), new GradeBand(Grade.B, 27, 0.9, 6), new GradeBand(Grade.C, null, 0.75, 4)
            }).ToList();
            if (bands.Count != 3 || bands.Where((b, i) => b == null || b.Grade != (Grade)(i + 1)).Any())
                throw new ArgumentException("Grades must be exactly A, B, C in that order.", nameof(grades));
            if (!bands[0].MaxPoints.HasValue || !bands[1].MaxPoints.HasValue || bands[1].MaxPoints <= bands[0].MaxPoints)
                throw new ArgumentException("A and B need rising maxPoints.", nameof(grades));
            if (bands[2].MaxPoints.HasValue) throw new ArgumentException("C must have no maxPoints.", nameof(grades));
            MemoryDays = memoryDays;
            UncleanedDayPoints = uncleanedDayPoints;
            StaleStockPoints = staleStockPoints;
            Grades = bands.AsReadOnly();
            FirstInspectionWeeks = firstInspectionWeeks;
            MinimumGapDays = minimumGapDays;
            DaysPerWeek = daysPerWeek;
        }

        public int MemoryDays { get; }
        public int UncleanedDayPoints { get; }
        public int StaleStockPoints { get; }
        public IReadOnlyList<GradeBand> Grades { get; }
        public int FirstInspectionWeeks { get; }
        public int MinimumGapDays { get; }
        public int DaysPerWeek { get; }

        internal static HealthRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "memoryDays", "uncleanedDayPoints", "staleStockPoints", "grades", "firstInspectionWeeks", "minimumGapDays", "daysPerWeek");
            var list = r.List(obj, "grades");
            if (list.Count != 3) throw r.Fail(list.Path, "must list exactly A, B, C in that order");
            var bands = new List<GradeBand>();
            for (var i = 0; i < list.Count; i++)
            {
                var band = list[i] as JObject ?? throw r.Fail(list[i].Path, "must be an object");
                r.OnlyKeys(band, "grade", "maxPoints", "demand", "inspectionWeeks");
                var grade = (Grade)(i + 1);
                if (r.Text(band, "grade") != grade.ToString())
                    throw r.Fail(DataReader.PathOf(band, "grade"), $"must be {grade}");
                int? maxPoints = null;
                if (i < 2)
                {
                    maxPoints = r.Integer(band, "maxPoints", 0, int.MaxValue);
                    if (i > 0 && maxPoints <= bands[i - 1].MaxPoints)
                        throw r.Fail(DataReader.PathOf(band, "maxPoints"), "must be above the previous band's maxPoints");
                }
                else if (band["maxPoints"] != null)
                    throw r.Fail(DataReader.PathOf(band, "maxPoints"), "must be omitted for the last band");
                var demand = r.Number(band, "demand", 0, 2);
                if (demand == 0) throw r.Fail(DataReader.PathOf(band, "demand"), "must be above 0");
                bands.Add(new GradeBand(grade, maxPoints, demand, r.Integer(band, "inspectionWeeks", 1, 520)));
            }
            return new HealthRules(
                r.Integer(obj, "memoryDays", 1, 365),
                r.Integer(obj, "uncleanedDayPoints", 0, 100),
                r.Integer(obj, "staleStockPoints", 0, 100), bands,
                r.Integer(obj, "firstInspectionWeeks", 1, 520),
                r.Integer(obj, "minimumGapDays", 0, 365),
                r.Integer(obj, "daysPerWeek", 1, 365));
        }
    }

    public sealed class RegularsRules
    {
        public RegularsRules(double gainPerVisit = 0.05, double maxBonus = 0.30, double missPenalty = 0.075, int minOpenMinutes = 60)
        {
            ReputationRuleChecks.Range(gainPerVisit, 0, 5, nameof(gainPerVisit));
            ReputationRuleChecks.Range(maxBonus, 0, 5, nameof(maxBonus));
            ReputationRuleChecks.Range(missPenalty, 0, 5, nameof(missPenalty));
            ReputationRuleChecks.Range(minOpenMinutes, 0, 1440, nameof(minOpenMinutes));
            GainPerVisit = gainPerVisit;
            MaxBonus = maxBonus;
            MissPenalty = missPenalty;
            MinOpenMinutes = minOpenMinutes;
        }

        public double GainPerVisit { get; }
        public double MaxBonus { get; }
        public double MissPenalty { get; }
        public int MinOpenMinutes { get; }

        internal static RegularsRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "gainPerVisit", "maxBonus", "missPenalty", "minOpenMinutes");
            return new RegularsRules(r.Number(obj, "gainPerVisit", 0, 5), r.Number(obj, "maxBonus", 0, 5),
                r.Number(obj, "missPenalty", 0, 5), r.Integer(obj, "minOpenMinutes", 0, 1440));
        }
    }

    public sealed class CompetitionRules
    {
        public CompetitionRules(double nearbyMetres = 80, double footfallPerNeighbour = 0.10, double maxFootfallBonus = 0.30)
        {
            ReputationRuleChecks.Range(nearbyMetres, 1, 10000, nameof(nearbyMetres));
            ReputationRuleChecks.Range(footfallPerNeighbour, 0, 5, nameof(footfallPerNeighbour));
            ReputationRuleChecks.Range(maxFootfallBonus, 0, 5, nameof(maxFootfallBonus));
            NearbyMetres = nearbyMetres;
            FootfallPerNeighbour = footfallPerNeighbour;
            MaxFootfallBonus = maxFootfallBonus;
        }

        public double NearbyMetres { get; }
        public double FootfallPerNeighbour { get; }
        public double MaxFootfallBonus { get; }

        internal static CompetitionRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "nearbyMetres", "footfallPerNeighbour", "maxFootfallBonus");
            return new CompetitionRules(r.Number(obj, "nearbyMetres", 1, 10000),
                r.Number(obj, "footfallPerNeighbour", 0, 5), r.Number(obj, "maxFootfallBonus", 0, 5));
        }
    }

    internal static class ReputationRuleChecks
    {
        internal static void Range(double value, double min, double max, string name)
        {
            if (double.IsNaN(value) || double.IsInfinity(value) || value < min || value > max)
                throw new ArgumentOutOfRangeException(name, $"Must be finite and between {min} and {max}.");
        }
    }
}
