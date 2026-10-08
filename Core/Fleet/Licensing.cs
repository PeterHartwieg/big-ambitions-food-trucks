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
        AlreadyHasPermit
    }

    // What the city writes to the player about the licence on a new day.
    public enum LicenceReminder
    {
        None,
        // The renewal window has just opened.
        RenewalOpen,
        // The licence ran out yesterday.
        Expired
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
        // The caller charges rules.LicenceFeeCents first.
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

        // The message due today, if any. Called once per new day, so each reminder comes once.
        public LicenceReminder ReminderFor(LicenceState licence, int today)
        {
            if (licence == null || !licence.HasVendorLicence) return LicenceReminder.None;
            if (today == licence.LicenceExpiresOnDay + 1) return LicenceReminder.Expired;
            if (DaysLeft(licence, today) == rules.RenewWindowDays && rules.RenewWindowDays > 0) return LicenceReminder.RenewalOpen;
            return LicenceReminder.None;
        }

        // Every vehicle needs its own permit; there is no cap on how many (Peter, 8 October 2026).
        public static PermitProblem CanBuyPermit(SaveState state, string vehicleId, int today)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var vehicle = StreetSales.Find(state, vehicleId);
            if (vehicle == null) return PermitProblem.UnknownVehicle;
            if (!IsValid(state.Licence, today)) return PermitProblem.NoLicence;
            if (vehicle.HasPermit) return PermitProblem.AlreadyHasPermit;
            return PermitProblem.None;
        }

        // The caller charges rules.PermitFeeCents first. A permit stays with its vehicle.
        public static PermitProblem BuyPermit(SaveState state, string vehicleId, int today)
        {
            var problem = CanBuyPermit(state, vehicleId, today);
            if (problem == PermitProblem.None) StreetSales.Find(state, vehicleId)!.HasPermit = true;
            return problem;
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
