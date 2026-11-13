using System;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Fleet
{
    public enum LicenceProblem
    {
        None,
        NoCourse,
        TooEarlyToRenew
    }

    public enum OpenProblem
    {
        None,
        UnknownVehicle,
        NoLicence,
        LicenceExpired,
        NoPermit,
        NoCrew,
        InTransit
    }

    public enum PermitProblem
    {
        None,
        UnknownVehicle,
        NoLicence,
        AlreadyHasPermit
    }

    public enum LicenceReminder
    {
        None,
        RenewalOpen,
        Expired
    }

    public sealed class Licensing
    {
        private readonly FleetRules rules;

        public Licensing(FleetRules rules) => this.rules = rules ?? throw new ArgumentNullException(nameof(rules));

        public static bool IsValid(LicenceState licence, int today) =>
            licence != null && licence.HasVendorLicence && today <= licence.LicenceExpiresOnDay;

        public static int DaysLeft(LicenceState licence, int today) =>
            IsValid(licence, today) ? licence.LicenceExpiresOnDay - today + 1 : 0;

        public LicenceProblem CanBuy(LicenceState licence, int today, bool passedFoodSafety)
        {
            if (licence == null) throw new ArgumentNullException(nameof(licence));
            if (!passedFoodSafety) return LicenceProblem.NoCourse;
            if (IsValid(licence, today) && DaysLeft(licence, today) > rules.RenewWindowDays) return LicenceProblem.TooEarlyToRenew;
            return LicenceProblem.None;
        }

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

        public LicenceReminder ReminderFor(LicenceState licence, int today)
        {
            if (licence == null || !licence.HasVendorLicence) return LicenceReminder.None;
            if (today == licence.LicenceExpiresOnDay + 1) return LicenceReminder.Expired;
            if (DaysLeft(licence, today) == rules.RenewWindowDays && rules.RenewWindowDays > 0) return LicenceReminder.RenewalOpen;
            return LicenceReminder.None;
        }

        public static PermitProblem CanBuyPermit(SaveState state, string vehicleId, int today)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            var vehicle = StreetSales.Find(state, vehicleId);
            if (vehicle == null) return PermitProblem.UnknownVehicle;
            if (!IsValid(state.Licence, today)) return PermitProblem.NoLicence;
            if (vehicle.HasPermit) return PermitProblem.AlreadyHasPermit;
            return PermitProblem.None;
        }

        public static PermitProblem BuyPermit(SaveState state, string vehicleId, int today)
        {
            var problem = CanBuyPermit(state, vehicleId, today);
            if (problem == PermitProblem.None) StreetSales.Find(state, vehicleId)!.HasPermit = true;
            return problem;
        }

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

        public static int Crew(VehicleInfo vehicle, bool playerAtWindow, int hires)
        {
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            return Math.Min(vehicle.Staff, (playerAtWindow ? 1 : 0) + Math.Max(0, hires));
        }
    }
}
