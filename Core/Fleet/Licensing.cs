using System;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Fleet
{
    public enum LicenceProblem
    {
        None,
        // The player hasn't passed the game's Food Safety course (EducationHelper.HasCompletedDiploma).
        NoCourse,
        // Renewal opens RenewWindowDays before expiry.
        TooEarlyToRenew
    }

    public enum OpenProblem
    {
        None,
        UnknownVehicle,
        NoLicence,
        LicenceExpired,
        NoPermit,
        // Nobody at the window: the player isn't there and no hire is assigned.
        NoCrew,
        // Driving between stops.
        InTransit
    }

    public enum PermitProblem
    {
        None,
        UnknownVehicle,
        NoLicence,
        AlreadyHasPermit,
        // Every permit the player may hold right now is in use.
        CapReached
    }

    // The vendor licence (the player) and the vehicle permits (one per cart or truck, covering its crew).
    // design: Licence and permits.
    public sealed class Licensing
    {
        private readonly FleetRules rules;

        public Licensing(FleetRules rules) => this.rules = rules ?? throw new ArgumentNullException(nameof(rules));

        public static bool IsValid(LicenceState licence, int today) =>
            licence != null && licence.HasVendorLicence && today <= licence.LicenceExpiresOnDay;

        // Days left including today; 0 once expired or never bought.
        public static int DaysLeft(LicenceState licence, int today) =>
            IsValid(licence, today) ? licence.LicenceExpiresOnDay - today + 1 : 0;

        public LicenceProblem CanBuy(LicenceState licence, int today, bool passedFoodSafety)
        {
            if (licence == null) throw new ArgumentNullException(nameof(licence));
            if (!passedFoodSafety) return LicenceProblem.NoCourse;
            if (IsValid(licence, today) && DaysLeft(licence, today) > rules.RenewWindowDays) return LicenceProblem.TooEarlyToRenew;
            return LicenceProblem.None;
        }

        // Buys or renews. A renewal adds a full period to the old expiry, so renewing early loses nothing.
        // The caller charges Economy.Price("licence") first.
        public LicenceProblem Buy(LicenceState licence, int today, bool passedFoodSafety)
        {
            var problem = CanBuy(licence, today, passedFoodSafety);
            if (problem != LicenceProblem.None) return problem;
            var from = IsValid(licence, today) ? licence.LicenceExpiresOnDay : today - 1;
            licence.HasVendorLicence = true;
            licence.LicenceExpiresOnDay = checked(from + rules.LicenceDays);
            licence.FirstLicensedDay ??= today;
            return LicenceProblem.None;
        }

        // Days since the first licence, the clock for permit unlocks. 0 before any licence.
        public static int DaysInBusiness(LicenceState licence, int today) =>
            licence?.FirstLicensedDay is int first ? Math.Max(0, today - first) : 0;

        // Permits the player may hold now: one, plus one per unlock reached by days or by items sold
        // across the fleet, at most maxPermits (the mod setting, default rules.MaxPermits). Sales count
        // only the vehicles still in the fleet, so the cap reached is remembered in PermitsUnlocked and
        // never drops; lowering the setting still lowers the ceiling. The game side should call it once a
        // day as well as on the permit screen, so an unlock is recorded before the vehicle that earned it leaves.
        public int PermitCap(SaveState state, int today, int? maxPermits = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var cap = Math.Max(1, maxPermits ?? rules.MaxPermits);
            var days = DaysInBusiness(state.Licence, today);
            var sales = state.Vehicles.Sum(v => (long)v.LifetimeSales);
            var permits = Math.Max(state.Licence.PermitsUnlocked, 1 + rules.Unlocks.Count(u => days >= u.Days || sales >= u.LifetimeSales));
            state.Licence.PermitsUnlocked = permits;
            return Math.Min(cap, permits);
        }

        // Vehicles from before Milestone 4 (no vehicle type) were bought before permits existed: each
        // gets a permit so an existing cart isn't locked out, and its type is set to cart, the only
        // vehicle on sale then. The player still needs the course and a licence. Call once after loading;
        // returns how many vehicles it changed.
        // Only saves written before firstVersionWithTypes (the first mod version that sets vehicle types)
        // qualify, so a vehicle a later version forgot to type never gets a free permit.
        public static int GrandfatherOldVehicles(SaveState state, string firstVersionWithTypes)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (!ModVersion.IsValid(firstVersionWithTypes))
                throw new ArgumentException("Must be a valid SemVer version.", nameof(firstVersionWithTypes));
            if (ModVersion.Compare(state.WrittenByModVersion, firstVersionWithTypes) >= 0) return 0;
            var changed = 0;
            foreach (var vehicle in state.Vehicles)
            {
                if (!string.IsNullOrEmpty(vehicle.VehicleType)) continue;
                vehicle.VehicleType = "cart";
                vehicle.HasPermit = true;
                changed++;
            }
            return changed;
        }

        public int PermitsInUse(SaveState state) => state.Vehicles.Count(v => v.HasPermit);

        public PermitProblem CanBuyPermit(SaveState state, string vehicleId, int today, int? maxPermits = null)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var vehicle = StreetSales.Find(state, vehicleId);
            if (vehicle == null) return PermitProblem.UnknownVehicle;
            if (!IsValid(state.Licence, today)) return PermitProblem.NoLicence;
            if (vehicle.HasPermit) return PermitProblem.AlreadyHasPermit;
            if (PermitsInUse(state) >= PermitCap(state, today, maxPermits)) return PermitProblem.CapReached;
            return PermitProblem.None;
        }

        // The caller charges Economy.Price("permit") first. A permit stays with its vehicle until the
        // vehicle leaves the fleet (ReleasePermit), and then frees a place under the cap.
        public PermitProblem BuyPermit(SaveState state, string vehicleId, int today, int? maxPermits = null)
        {
            var problem = CanBuyPermit(state, vehicleId, today, maxPermits);
            if (problem == PermitProblem.None) StreetSales.Find(state, vehicleId)!.HasPermit = true;
            return problem;
        }

        // Records the cap first, so the vehicle's sales still count towards the unlocks it earned.
        public void ReleasePermit(SaveState state, string vehicleId, int today)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            PermitCap(state, today);
            var vehicle = StreetSales.Find(state, vehicleId);
            if (vehicle != null) vehicle.HasPermit = false;
        }

        // Whether the vehicle may open, before the spot's own rules (legality is the game side's).
        // The permit covers the crew, so hires need neither the course nor a licence of their own;
        // the player's licence still has to be valid.
        public static OpenProblem CanOpen(SaveState state, string vehicleId, int today, bool playerAtWindow, int hires)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var vehicle = StreetSales.Find(state, vehicleId);
            if (vehicle == null) return OpenProblem.UnknownVehicle;
            if (!state.Licence.HasVendorLicence) return OpenProblem.NoLicence;
            if (!IsValid(state.Licence, today)) return OpenProblem.LicenceExpired;
            if (!vehicle.HasPermit) return OpenProblem.NoPermit;
            if (vehicle.Transit != null) return OpenProblem.InTransit;
            if (!playerAtWindow && hires <= 0) return OpenProblem.NoCrew;
            return OpenProblem.None;
        }

        // Workers at the window: the player and the hires, up to the vehicle's staff limit.
        public static int Crew(VehicleInfo vehicle, bool playerAtWindow, int hires)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            return Math.Min(vehicle.Staff, (playerAtWindow ? 1 : 0) + Math.Max(0, hires));
        }
    }
}
