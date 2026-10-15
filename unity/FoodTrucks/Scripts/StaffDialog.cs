#nullable enable
using System;
using System.Collections.Generic;
using Localizor;

namespace FoodTrucks
{
    // What the crew-and-route screens share (the BizMan vehicle page and fleet section): the
    // day choices for a route, a stop's spot key and the clock text. The old list-picker dialog that held these went
    // with the vehicle panel's Hires button (15 October 2026).
    internal static class StaffDialog
    {
        internal static readonly (string Key, DayOfWeek[] Days)[] DayChoices =
        {
            ("foodtrucks:hires_days_every", Array.Empty<DayOfWeek>()),
            ("foodtrucks:hires_days_weekdays", new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday }),
            ("foodtrucks:hires_days_monsat", new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday }),
            ("foodtrucks:hires_days_weekends", new[] { DayOfWeek.Saturday, DayOfWeek.Sunday }),
        };

        internal static string DaysKey(List<DayOfWeek>? days)
        {
            var set = new HashSet<DayOfWeek>(days ?? new List<DayOfWeek>());
            foreach (var (key, choice) in DayChoices)
                if (set.SetEquals(choice)) return key;
            return "foodtrucks:hires_days_custom";
        }

        // A stop is a pitch: the same key regulars use (World.PitchKey).
        internal static string SpotKey(float x, float z) => World.PitchKey(FoodTrucksSession.CurrentState, x, z);

        // 13:30; a late stop's closing time past midnight reads "01:00 (after midnight)".
        internal static string Clock(int minute)
        {
            var time = $"{minute / 60 % 24:D2}:{minute % 60:D2}";
            return minute >= 24 * 60 ? string.Format(Text("foodtrucks:hires_after_midnight"), time) : time;
        }

        private static string Text(string key) => LocalizorManager.GetLocalization(key, null);
    }
}
