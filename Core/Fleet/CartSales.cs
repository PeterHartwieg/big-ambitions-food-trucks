using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Pitches;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Fleet
{
    public class CartSale
    {
        public string VehicleId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Cents { get; set; }
        public int Day { get; set; }
        public long Minute { get; set; }
        [JsonExtensionData]
        public IDictionary<string, JToken> ExtensionData { get; set; } = new Dictionary<string, JToken>();
    }

    public static class CartSales
    {
        // The hand-vehicle restriction is deliberately absent; use the game's resale multiplier and cargo valuation.
        public static long Quote(double purchasePrice, double multiplier, double repairCost, IEnumerable<double> cargoPrices,
            bool zeroSellingPrice = false)
        {
            var price = (zeroSellingPrice ? 0 : Math.Max(0, purchasePrice * multiplier - repairCost)) + cargoPrices.Sum();
            if (double.IsNaN(price) || double.IsInfinity(price) || price < 0 || price > long.MaxValue / 100.0)
                throw new ArgumentOutOfRangeException(nameof(purchasePrice));
            return checked((long)Math.Round(price * 100, MidpointRounding.AwayFromZero));
        }

        // The adapter settles wages and releases native employees first. Keep historical sales, wages and vehicle
        // numbers for accounts; end only this vehicle's future operation. Shared stock, orders and event tickets stay.
        public static void Close(SaveState state, string id, long minute)
        {
            var vehicle = state.Vehicles.FirstOrDefault(v => v.VehicleId == id);
            if (vehicle != null)
            {
                vehicle.IsOpen = false;
                vehicle.HasPermit = false;
                vehicle.Crew.Clear();
                vehicle.Schedule = null;
                vehicle.Shifts.Clear();
                vehicle.Transit = null;
                vehicle.Arrival = null;
                vehicle.Skipped = null;
                vehicle.HireStop = null;
                vehicle.ReturnPending = false;
                vehicle.CrewWindowLost = false;
                vehicle.Service = null;
                vehicle.OpenPitch = null;
                vehicle.PremiumSpot = null;
                vehicle.StockBatches.Clear();
            }
            Commissaries.Release(state.Commissary, id);
            state.Commissary.PendingCleaning.Remove(id);
            state.Partnerships.RemoveAll(p => p.VehicleId == id);
            foreach (var holds in state.PremiumHolds.Values) Holds.Release(holds, id, minute);
            foreach (var cityEvent in state.Events.Events) Holds.Release(cityEvent.Holds, id, minute);
        }
    }
}
