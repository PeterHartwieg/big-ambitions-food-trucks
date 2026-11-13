using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Fleet
{
    public static class FleetRecruitment
    {
        public const string Skill = "ba:skill_customerservice";

        public static IReadOnlyList<string> Skills(int foodVehicles, IEnumerable<string>? agencySkills)
        {
            if (foodVehicles <= 0 || agencySkills == null) return Array.Empty<string>();
            return agencySkills.Contains(Skill, StringComparer.Ordinal) ? new[] { Skill } : Array.Empty<string>();
        }

        // What an unfinished fleet campaign gives back when the mod is uninstalled: the price of the candidates not found
        // yet, rounded down to the cent. A campaign the game would keep without the mod can't be shown by it.
        public static long RefundCents(long priceCents, int candidates, int found)
        {
            if (priceCents <= 0 || candidates <= 0) return 0;
            var left = Math.Max(0, Math.Min(candidates, candidates - found));
            return priceCents * left / candidates;
        }
    }
}
