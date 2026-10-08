using System;
using FoodTrucks.Core.Common;

namespace FoodTrucks.Core.Reputation
{
    public readonly struct InspectionResult
    {
        internal InspectionResult(Grade grade, int points)
        {
            Grade = grade;
            Points = points;
        }

        public Grade Grade { get; }
        public int Points { get; }
    }

    public sealed class HealthGrades
    {
        private readonly HealthRules rules;

        public HealthGrades(HealthRules rules) => this.rules = rules ?? throw new ArgumentNullException(nameof(rules));

        public static HealthState NewVehicle(HealthRules rules, int today, long seed)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (today < 1) throw new ArgumentOutOfRangeException(nameof(today));
            return new HealthState { FirstSeenDay = today, RandomState = seed };
        }

        // Only open days are recorded. Replacing a mark keeps a repeated callback harmless.
        public void RecordDay(HealthState state, int day, bool cleaned, bool soldStaleStock)
        {
            Check(state);
            var mark = state.Marks.Find(m => m.Day == day);
            if (mark == null)
            {
                mark = new HealthMark { Day = day };
                state.Marks.Add(mark);
            }
            mark.Points = (cleaned ? 0 : rules.UncleanedDayPoints) + (soldStaleStock ? rules.StaleStockPoints : 0);
            state.Marks.Sort((a, b) => a.Day.CompareTo(b.Day));
            if (state.Marks.Count > rules.MemoryDays)
                state.Marks.RemoveRange(0, state.Marks.Count - rules.MemoryDays);
        }

        public int Points(HealthState state)
        {
            Check(state);
            long points = 0;
            foreach (var mark in state.Marks)
                points += mark.Points;
            // Corrupt or hand-edited saves cannot overflow the grade calculation.
            return (int)Math.Min(int.MaxValue, points);
        }

        public InspectionResult? OnOpen(HealthState state, int today)
        {
            Check(state);
            var previous = state.LastInspectionDay ?? state.FirstSeenDay;
            if ((long)today - previous < rules.MinimumGapDays || (state.LastInspectionDay.HasValue && today <= previous)) return null;
            if (state.LastDrawDay.HasValue && today <= state.LastDrawDay.Value) return null;
            var weeks = state.Grade == Grade.Ungraded ? rules.FirstInspectionWeeks : rules.Grades[(int)state.Grade - 1].InspectionWeeks;
            var daily = 1.0 / Math.Max(1, weeks * rules.DaysPerWeek - rules.MinimumGapDays);
            // The inspector may have come by on any day since the last draw; a closed vehicle is only
            // found on the next open day. So the intervals average the grade's calendar weeks, however
            // often the vehicle opens.
            var firstChanceDay = (long)previous + rules.MinimumGapDays;
            var from = Math.Max(firstChanceDay, state.LastDrawDay.HasValue ? state.LastDrawDay.Value + 1L : firstChanceDay);
            var days = Math.Max(1, today - from + 1);
            var chance = 1 - Math.Pow(1 - daily, days);
            var random = new SeededRandom(state.RandomState);
            var inspect = random.Chance(chance);
            state.RandomState = random.State;
            state.LastDrawDay = today;
            if (!inspect) return null;
            var points = Points(state);
            var grade = GradeFor(points);
            state.Grade = grade;
            state.LastInspectionDay = today;
            return new InspectionResult(grade, points);
        }

        public double DemandFactor(HealthState state)
        {
            Check(state);
            return state.Grade == Grade.Ungraded ? 1 : rules.Grades[(int)state.Grade - 1].Demand;
        }

        public Grade GradeFor(int points)
        {
            if (points < 0) throw new ArgumentOutOfRangeException(nameof(points));
            foreach (var band in rules.Grades)
                if (!band.MaxPoints.HasValue || points <= band.MaxPoints.Value) return band.Grade;
            return Grade.C;
        }

        private static void Check(HealthState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            ReputationValidation.Validate(state);
        }
    }
}
