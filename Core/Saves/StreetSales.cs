using System;

namespace FoodTrucks.Core.Saves
{
    public static class StreetSales
    {
        public static bool CanRecord(SaveState state, string vehicleId, int day, long priceCents, int count)
        {
            try
            {
                PrepareRecording(state, vehicleId, day, priceCents, count, out _, out _, out _);
                return true;
            }
            catch (ArgumentException) { return false; }
            catch (InvalidOperationException) { return false; }
            catch (OverflowException) { return false; }
        }

        public static void Record(SaveState state, string vehicleId, int day, long priceCents, int count)
        {
            PrepareRecording(state, vehicleId, day, priceCents, count,
                out var revenueCents, out var lifetimeSales, out var lifetimeRevenueCents);

            var vehicle = GetOrAdd(state, vehicleId);
            SalesLedger.Record(state, day, revenueCents);
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

        private static void PrepareRecording(SaveState state, string vehicleId, int day, long priceCents, int count,
            out long revenueCents, out int lifetimeSales, out long lifetimeRevenueCents)
        {
            ValidateVehicleInput(state, vehicleId);
            if (priceCents < 0) throw new ArgumentOutOfRangeException(nameof(priceCents));
            if (count < 1) throw new ArgumentOutOfRangeException(nameof(count));

            var vehicle = Find(state, vehicleId);
            if (vehicle != null && (vehicle.LifetimeSales < 0 || vehicle.LifetimeRevenueCents < 0))
                throw new InvalidOperationException("Vehicle lifetime totals must not be negative.");

            revenueCents = checked(priceCents * count);
            lifetimeSales = checked((vehicle?.LifetimeSales ?? 0) + count);
            lifetimeRevenueCents = checked((vehicle?.LifetimeRevenueCents ?? 0) + revenueCents);
            if (!SalesLedger.CanRecord(state, day, revenueCents))
                throw new OverflowException("Sales ledger cannot record the revenue without overflow or invalid data.");
        }
    }
}
