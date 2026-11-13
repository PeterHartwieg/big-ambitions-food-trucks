using System;
using System.Linq;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Pitches
{
    public enum PartnerProblem { None, VehicleHasPartner, VenueHasPartner }

    public static class Partnerships
    {
        public static PartnershipState? Of(SaveState state, string vehicleId) =>
            state?.Partnerships.FirstOrDefault(p => p.VehicleId == vehicleId);

        public static PartnershipState? ForVenue(SaveState state, string venue) =>
            state?.Partnerships.FirstOrDefault(p => p.Venue == venue);

        public static PartnerProblem CanSign(SaveState state, string vehicleId, string venue)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (Of(state, vehicleId) != null) return PartnerProblem.VehicleHasPartner;
            if (ForVenue(state, venue) != null) return PartnerProblem.VenueHasPartner;
            return PartnerProblem.None;
        }

        public static PartnerProblem Sign(SaveState state, string vehicleId, string venue, int day)
        {
            var problem = CanSign(state, vehicleId, venue);
            if (problem != PartnerProblem.None) return problem;
            state.Partnerships.Add(new PartnershipState { VehicleId = vehicleId, Venue = venue, SignedDay = day });
            return PartnerProblem.None;
        }

        public static bool End(SaveState state, string vehicleId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            return state.Partnerships.RemoveAll(p => p.VehicleId == vehicleId) > 0;
        }

        public static int Prune(SaveState state, Func<PartnershipState, bool> keep)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (keep == null) throw new ArgumentNullException(nameof(keep));
            return state.Partnerships.RemoveAll(p => !keep(p));
        }

        public static void Worked(PartnershipState partnership, PartnershipRules rules, int day, int minutes)
        {
            if (partnership == null) throw new ArgumentNullException(nameof(partnership));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (minutes <= 0) return;
            if (partnership.WorkingDay != day)
            {
                partnership.WorkingDay = day;
                partnership.WorkedMinutes = 0;
            }
            partnership.WorkedMinutes = (int)Math.Min(int.MaxValue, (long)partnership.WorkedMinutes + minutes);
            if (partnership.WorkedMinutes >= rules.MinWorkedMinutes && (partnership.LastWorkedDay ?? int.MinValue) < day)
                partnership.LastWorkedDay = day;
        }

        public static bool BonusDue(PartnershipState partnership, int today) =>
            partnership?.LastWorkedDay is { } worked && partnership.ScoredDay == today && worked == today - 1 &&
            worked > (partnership.BonusForDay ?? int.MinValue);

        public static void BonusGiven(PartnershipState partnership)
        {
            if (partnership == null) throw new ArgumentNullException(nameof(partnership));
            partnership.BonusForDay = partnership.LastWorkedDay;
        }

        public static (int Facility, int Overall) WithBonus(int customerService, int pricing, int cleanliness, int facility, PartnershipRules rules)
        {
            var raised = Math.Min(100, facility + rules.SatisfactionBonus);
            return (raised, (int)((customerService + (double)pricing + cleanliness + raised) / 4.0));
        }
    }
}
