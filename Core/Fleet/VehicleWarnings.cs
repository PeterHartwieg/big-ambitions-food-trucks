using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;

namespace FoodTrucks.Core.Fleet
{
    public enum VehicleWarningKind
    {
        LowStock = 1,
        EmptyStock = 2,
        MissingCrew = 3,
        RouteProblem = 4,
        NoPermit = 5,
        NoCommissarySpace = 6,
        HealthGradeC = 7,
        LicenceRunningOut = 8,
        LicenceExpired = 9,
        NoEquipment = 10
    }

    public readonly struct VehicleWarning : IEquatable<VehicleWarning>
    {
        public VehicleWarning(VehicleWarningKind kind, string? vehicleId, string? itemName = null)
        {
            Kind = kind;
            VehicleId = vehicleId;
            ItemName = itemName;
        }

        public VehicleWarningKind Kind { get; }
        public string? VehicleId { get; }
        public string? ItemName { get; }

        public bool IsFleet => VehicleWarnings.IsFleetKind(Kind);

        public bool Equals(VehicleWarning other) =>
            Kind == other.Kind && string.Equals(VehicleId, other.VehicleId, StringComparison.Ordinal) &&
            string.Equals(ItemName, other.ItemName, StringComparison.Ordinal);

        public override bool Equals(object? obj) => obj is VehicleWarning other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Kind;
                hash = hash * 397 ^ (VehicleId == null ? 0 : StringComparer.Ordinal.GetHashCode(VehicleId));
                return hash * 397 ^ (ItemName == null ? 0 : StringComparer.Ordinal.GetHashCode(ItemName));
            }
        }

        public override string ToString() => $"{Kind} {VehicleId ?? "fleet"}{(ItemName == null ? "" : " " + ItemName)}";
    }

    public static class VehicleWarnings
    {
        public static bool IsFleetKind(VehicleWarningKind kind) =>
            kind == VehicleWarningKind.LicenceRunningOut || kind == VehicleWarningKind.LicenceExpired;

        public static IReadOnlyList<VehicleWarning> For(DemandTables tables, FleetRules rules, SaveState state, VehicleState vehicle,
            IEnumerable<CargoLine> cargo, int? crew = null, Func<string, bool>? itemExists = null, double gameSpeed = 1.0,
            (double X, double Z)? home = null, IReadOnlyCollection<int>? bayStops = null)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (vehicle == null) throw new ArgumentNullException(nameof(vehicle));
            if (cargo == null) throw new ArgumentNullException(nameof(cargo));
            var id = vehicle.VehicleId;
            var warnings = new List<VehicleWarning>();
            var lines = cargo.ToList();

            var view = Storage.Of(tables, vehicle.VehicleType, lines);
            if (view != null)
                foreach (var food in StockLevels.Stocked(tables, view, vehicle, itemExists))
                {
                    if (food.Level == StockLevel.Empty) warnings.Add(new VehicleWarning(VehicleWarningKind.EmptyStock, id, food.Item));
                    else if (food.Level == StockLevel.Low) warnings.Add(new VehicleWarning(VehicleWarningKind.LowStock, id, food.Item));
                }

            var hasRoute = WeekPlan.RunsAnyDay(vehicle.Schedule);
            var info = tables.Vehicle(vehicle.VehicleType);
            var hires = Math.Max(0, crew ?? vehicle.Crew?.Count ?? 0);
            if (hasRoute && info != null && hires < info.Staff)
                warnings.Add(new VehicleWarning(VehicleWarningKind.MissingCrew, id));
            if (hasRoute && new HireRunner(rules).RouteProblem(vehicle, gameSpeed, home, bayStops) != ScheduleProblem.None)
                warnings.Add(new VehicleWarning(VehicleWarningKind.RouteProblem, id));
            if (hasRoute && view != null && view.Pools.Count == 0)
                warnings.Add(new VehicleWarning(VehicleWarningKind.NoEquipment, id));

            if (!vehicle.HasPermit) warnings.Add(new VehicleWarning(VehicleWarningKind.NoPermit, id));
            var spaces = state.Commissary?.Spaces;
            if (spaces == null || !spaces.Any(s => string.Equals(s.VehicleId, id, StringComparison.Ordinal)))
                warnings.Add(new VehicleWarning(VehicleWarningKind.NoCommissarySpace, id));
            if (vehicle.Health?.Grade == Grade.C) warnings.Add(new VehicleWarning(VehicleWarningKind.HealthGradeC, id));
            return warnings;
        }

        public static IReadOnlyList<VehicleWarning> Fleet(FleetRules rules, LicenceState licence, int today)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            if (licence == null || !licence.HasVendorLicence) return Array.Empty<VehicleWarning>();
            if (!Licensing.IsValid(licence, today)) return new[] { new VehicleWarning(VehicleWarningKind.LicenceExpired, null) };
            if (rules.RenewWindowDays > 0 && Licensing.DaysLeft(licence, today) <= rules.RenewWindowDays)
                return new[] { new VehicleWarning(VehicleWarningKind.LicenceRunningOut, null) };
            return Array.Empty<VehicleWarning>();
        }

        public static (IReadOnlyList<VehicleWarning> Add, IReadOnlyList<VehicleWarning> Complete) Reconcile(
            IEnumerable<VehicleWarning> wanted, IEnumerable<VehicleWarning> existing)
        {
            if (wanted == null) throw new ArgumentNullException(nameof(wanted));
            if (existing == null) throw new ArgumentNullException(nameof(existing));
            var wantedList = wanted.ToList();
            var want = new HashSet<VehicleWarning>(wantedList);
            var seen = new HashSet<VehicleWarning>();
            var complete = new List<VehicleWarning>();
            foreach (var warning in existing)
                if (!want.Contains(warning) || !seen.Add(warning)) complete.Add(warning);
            var add = wantedList.Where(w => seen.Add(w)).ToList();
            return (add, complete);
        }
    }
}
