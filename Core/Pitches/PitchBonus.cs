using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Pitches
{
    // What a premium spot or an event adds to a pitch: its areas, raised to at least its presences (as a zone in
    // add mode does), and its footfall factor. Both can apply at once; their factors multiply.
    public sealed class PitchBonus
    {
        public static readonly PitchBonus None = new PitchBonus(null, 1.0);

        public PitchBonus(AreaMix? areas, double footfall)
        {
            if (double.IsNaN(footfall) || double.IsInfinity(footfall) || footfall < 0) throw new ArgumentOutOfRangeException(nameof(footfall));
            Areas = areas;
            Footfall = footfall;
        }

        public AreaMix? Areas { get; }
        public double Footfall { get; }

        public static PitchBonus Of(PremiumSpot spot) => new PitchBonus(spot.Areas, spot.Footfall);
        public static PitchBonus Of(EventKind kind) => new PitchBonus(kind.Areas, kind.Footfall);

        public PitchBonus And(PitchBonus other)
        {
            if (other == null) throw new ArgumentNullException(nameof(other));
            var areas = Areas == null ? other.Areas : other.Areas == null ? Areas : AreaMix.FromPresences(Areas.Presences.Concat(other.Areas.Presences));
            return new PitchBonus(areas, Footfall * other.Footfall);
        }

        public AreaMix Apply(AreaMix mix)
        {
            if (mix == null) throw new ArgumentNullException(nameof(mix));
            return Areas == null ? mix : AreaMix.FromPresences(mix.Presences.Concat(Areas.Presences));
        }

        // Fills the query's pitch fields: the areas stay apart from the venues' live factor.
        public void ApplyTo(DemandQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            query.PitchAreas = Areas;
            query.PitchFootfall = Footfall;
        }
    }

    public enum PremiumClaim { Held, Taken }

    // Premium spots are first come, first served (design: Pitches). Until AI trucks exist, only the player's own
    // vehicles compete: the first to open on the spot holds it until it closes, and later ones there sell as on
    // any other pitch.
    public static class PremiumSpots
    {
        // The spot whose building entrance is nearest, within the premium radius. `entrances` gives each spot's
        // entrance positions as the game reads them; spots whose building the game doesn't have are skipped.
        public static PremiumSpot? At(PitchTables tables, Func<PremiumSpot, IEnumerable<(double X, double Z)>?> entrances, double x, double z)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (entrances == null) throw new ArgumentNullException(nameof(entrances));
            PremiumSpot? best = null;
            var bestDistance = double.MaxValue;
            foreach (var spot in tables.Spots)
            {
                var doors = entrances(spot);
                if (doors == null) continue;
                foreach (var (dx, dz) in doors)
                {
                    var distance = Math.Sqrt((dx - x) * (dx - x) + (dz - z) * (dz - z));
                    if (distance <= tables.PremiumRadiusMetres && distance < bestDistance)
                    {
                        best = spot;
                        bestDistance = distance;
                    }
                }
            }
            return best;
        }

        // The vehicle opening on the spot at that minute takes it unless another held it then. Returns who holds it.
        public static (PremiumClaim Claim, string Holder) Claim(SaveState state, VehicleState vehicle, string spotId, long minute)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (vehicle.PremiumSpot != null && vehicle.PremiumSpot != spotId) Release(state, vehicle, minute);
            if (!state.PremiumHolds.TryGetValue(spotId, out var holds)) state.PremiumHolds[spotId] = holds = new List<PlaceHold>();
            var holder = Holds.Claim(holds, vehicle.VehicleId, minute, 1);
            if (holder != null)
            {
                vehicle.PremiumSpot = null;
                return (PremiumClaim.Taken, holder);
            }
            vehicle.PremiumSpot = spotId;
            return (PremiumClaim.Held, vehicle.VehicleId);
        }

        // The vehicle gives the spot back at that minute (it closed, left, or is gone).
        public static void Release(SaveState state, VehicleState vehicle, long minute)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            foreach (var holds in state.PremiumHolds.Values) Holds.Release(holds, vehicle.VehicleId, minute);
            vehicle.PremiumSpot = null;
        }

        // Holds that ended before that minute are dropped, and spots nobody holds.
        public static int Prune(SaveState state, long before)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var removed = 0;
            foreach (var pair in state.PremiumHolds.ToList())
            {
                removed += Holds.Prune(pair.Value, before);
                if (pair.Value.Count == 0 && state.PremiumHolds.Remove(pair.Key)) removed++;
            }
            return removed;
        }
    }
}
