using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Fleet
{
    // The game's recruitment agency recruiting for the street food fleet (Peter, 17 October 2026): a player with only
    // carts and trucks has no building business, which the agency otherwise wants. The game's Basic HR course stays
    // required. A fleet campaign has no business address, so its candidates arrive unassigned, ready for a vehicle's crew.
    public static class FleetRecruitment
    {
        // The one skill the fleet hires for: who serves at the window.
        public const string Skill = "ba:skill_customerservice";

        // The skills the agency offers for the fleet: customer service, if this agency recruits it and the player owns a
        // food vehicle. Empty otherwise, and then the fleet isn't offered.
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
