using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Common;

namespace FoodTrucks.Core.Reputation
{
    public sealed class SpotVisit
    {
        public string SpotKey { get; set; } = string.Empty;
        public int OpenMinutes { get; set; }
    }

    public sealed class Regulars
    {
        private readonly RegularsRules rules;

        public Regulars(RegularsRules rules) => this.rules = rules ?? throw new ArgumentNullException(nameof(rules));

        public void EndOfDay(RegularsState state, int day, IEnumerable<SpotVisit> visits)
        {
            Check(state);
            if (day <= state.LastUpdatedDay) return;
            var weekday = GameDays.Weekday(day);
            if (visits == null) throw new ArgumentNullException(nameof(visits));
            var visited = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var visit in visits)
            {
                if (visit == null || string.IsNullOrEmpty(visit.SpotKey))
                    throw new ArgumentException("Visits need a nonempty spot key.", nameof(visits));
                if (visit.OpenMinutes < 0) throw new ArgumentException("Open minutes must not be negative.", nameof(visits));
                if (visit.OpenMinutes >= rules.MinOpenMinutes) visited.Add(visit.SpotKey);
            }
            if (state.LastUpdatedDay != int.MinValue && rules.MissPenalty > 0)
            {
                // After enough missed weeks every bonus is gone, so a long break costs no extra work.
                var missedDays = Math.Min((long)day - state.LastUpdatedDay - 1,
                    7 * Math.Ceiling(rules.MaxBonus / rules.MissPenalty) + 7);
                for (long offset = 1; offset <= missedDays; offset++)
                    Miss(state, GameDays.Weekday((int)(state.LastUpdatedDay + offset)), null);
            }
            Miss(state, weekday, visited);
            foreach (var key in visited)
            {
                var entry = state.Entries.Find(e => e.Weekday == weekday && string.Equals(e.SpotKey, key, StringComparison.Ordinal));
                if (entry == null)
                {
                    entry = new RegularsEntry { SpotKey = key, Weekday = weekday };
                    state.Entries.Add(entry);
                }
                entry.Bonus = Math.Min(rules.MaxBonus, entry.Bonus + rules.GainPerVisit);
                entry.LastVisitDay = day;
            }
            state.Entries.Sort((a, b) =>
            {
                var spot = StringComparer.Ordinal.Compare(a.SpotKey, b.SpotKey);
                return spot != 0 ? spot : a.Weekday.CompareTo(b.Weekday);
            });
            state.LastUpdatedDay = day;
        }

        public double Bonus(RegularsState state, string spotKey, DayOfWeek weekday)
        {
            Check(state);
            if (string.IsNullOrEmpty(spotKey)) throw new ArgumentException("Spot key must not be empty.", nameof(spotKey));
            CheckWeekday(weekday);
            return state.Entries.FirstOrDefault(e => e.Weekday == weekday && string.Equals(e.SpotKey, spotKey, StringComparison.Ordinal))?.Bonus ?? 0;
        }

        public double DemandFactor(RegularsState state, string spotKey, DayOfWeek weekday) => 1 + Bonus(state, spotKey, weekday);

        private void Miss(RegularsState state, DayOfWeek weekday, ISet<string>? visited)
        {
            foreach (var entry in state.Entries)
                if (entry.Weekday == weekday && (visited == null || !visited.Contains(entry.SpotKey))) entry.Bonus -= rules.MissPenalty;
            state.Entries.RemoveAll(e => e.Weekday == weekday && (visited == null || !visited.Contains(e.SpotKey)) && e.Bonus <= 0);
        }

        private static void Check(RegularsState state)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            ReputationValidation.Validate(state);
        }

        private static void CheckWeekday(DayOfWeek weekday)
        {
            if (!Enum.IsDefined(typeof(DayOfWeek), weekday)) throw new ArgumentOutOfRangeException(nameof(weekday));
        }
    }
}
