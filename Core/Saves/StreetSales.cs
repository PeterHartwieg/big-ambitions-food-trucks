using System;

namespace FoodTrucks.Core.Saves
{
    public static class StreetSales
    {
        public static bool CanRecord(SaveState state, string vehicleId, int day, long priceCents, int count) =>
            CanRecordTotal(state, vehicleId, day, priceCents, count, perUnit: true);

        // A sum paid for several units at once (a catering gig's fee for its portions).
        public static bool CanRecordTotal(SaveState state, string vehicleId, int day, long revenueCents, int count) =>
            CanRecordTotal(state, vehicleId, day, revenueCents, count, perUnit: false);

        private static bool CanRecordTotal(SaveState state, string vehicleId, int day, long cents, int count, bool perUnit)
        {
            try
            {
                PrepareRecording(state, vehicleId, day, cents, count, perUnit, out _, out _, out _);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (OverflowException) { return false; }
        }

        public static void Record(SaveState state, string vehicleId, int day, long priceCents, int count) =>
            Record(state, vehicleId, day, priceCents, count, perUnit: true);

        public static void RecordTotal(SaveState state, string vehicleId, int day, long revenueCents, int count) =>
            Record(state, vehicleId, day, revenueCents, count, perUnit: false);

        private static void Record(SaveState state, string vehicleId, int day, long cents, int count, bool perUnit)
        {
            PrepareRecording(state, vehicleId, day, cents, count, perUnit,
                out var revenueCents, out var lifetimeSales, out var lifetimeRevenueCents);

            var vehicle = GetOrAdd(state, vehicleId);
            SalesLedger.Record(state, day, revenueCents);
            // A sum for several units at once is a catering fee; Econoview shows it apart from street sales.
            SalesLedger.RecordLine(state, day, vehicleId, perUnit ? LedgerLine.Sales : LedgerLine.Catering, revenueCents);
            vehicle.LifetimeSales = lifetimeSales;
            vehicle.LifetimeRevenueCents = lifetimeRevenueCents;
        }

        public static VehicleState GetOrAdd(SaveState state, string vehicleId)
        {
            ValidateVehicleInput(state, vehicleId);
            var vehicle = Find(state, vehicleId);
            if (vehicle == null)
            {
                vehicle = new VehicleState { VehicleId = vehicleId };
                state.Vehicles.Add(vehicle);
            }
            return vehicle;
        }

        public static VehicleState? Find(SaveState? state, string vehicleId)
        {
            if (state == null) return null;
            if (state.Vehicles == null)
                throw new InvalidOperationException("Vehicles must not be null.");
            VehicleState? found = null;
            foreach (var vehicle in state.Vehicles)
            {
                if (vehicle == null)
                    throw new InvalidOperationException("Vehicles must not contain null entries.");
                if (string.Equals(vehicle.VehicleId, vehicleId, StringComparison.Ordinal))
                {
                    if (found != null)
                        throw new InvalidOperationException("Vehicles contains duplicate vehicle ids.");
                    found = vehicle;
                }
            }
            return found;
        }

        private static void ValidateVehicleInput(SaveState state, string vehicleId)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            if (string.IsNullOrEmpty(vehicleId)) throw new ArgumentException("Vehicle id must not be empty.", nameof(vehicleId));
        }

        // perUnit: cents is each unit's price; otherwise it is the sum for all of them.
        private static void PrepareRecording(SaveState state, string vehicleId, int day, long cents, int count, bool perUnit,
            out long revenueCents, out int lifetimeSales, out long lifetimeRevenueCents)
        {
            ValidateVehicleInput(state, vehicleId);
            if (cents < 0) throw new ArgumentOutOfRangeException(nameof(cents));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));

            var vehicle = Find(state, vehicleId);
            if (vehicle != null && (vehicle.LifetimeSales < 0 || vehicle.LifetimeRevenueCents < 0))
                throw new InvalidOperationException("Vehicle lifetime totals must not be negative.");

            revenueCents = perUnit ? checked(cents * count) : cents;
            lifetimeSales = checked((vehicle?.LifetimeSales ?? 0) + count);
            lifetimeRevenueCents = checked((vehicle?.LifetimeRevenueCents ?? 0) + revenueCents);
            if (!SalesLedger.CanRecord(state, day, revenueCents))
                throw new OverflowException("Sales ledger cannot record the revenue without overflow or invalid data.");
        }
    }
}
