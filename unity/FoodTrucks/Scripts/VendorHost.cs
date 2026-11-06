#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Vendor;
using FoodTrucks.ModApi;

namespace FoodTrucks
{
    // The game side of FoodTrucks.ModApi.VendorApi (docs/vendor-api.md): the loaded save, the clock, each food vehicle
    // as the window sees it, and Street Food's own one-item sale. Core's VendorService decides; this only reads and
    // calls. Attached with each save (FoodTrucksSession.Attach); unused until another mod calls the API.
    internal sealed class StreetFoodVendorHost : IVendorHost
    {
        public VendorApiStatus Status
        {
            get
            {
                var game = SaveGameManager.Current;
                if (game == null || !FoodTrucksSession.IsAttachedTo(game)) return VendorApiStatus.NotYet;
                if (FoodTrucksSession.CurrentState == null || !FoodTrucksSession.CanWrite || Commissary.Uninstalled || !CartSelling.Running)
                    return VendorApiStatus.Off;
                return DemandData.Tables == null || DemandData.Rules == null ? VendorApiStatus.NotYet : VendorApiStatus.Ready;
            }
        }

        public SaveState? State => FoodTrucksSession.CanWrite ? FoodTrucksSession.CurrentState : null;

        public long Now => HireCrew.Now();

        public IReadOnlyList<VendorFacts> Facts()
        {
            var tables = DemandData.Tables;
            var rules = DemandData.Rules;
            var state = State;
            if (tables == null || rules == null || state == null) return Array.Empty<VendorFacts>();
            return FoodVehicles().Select(cart => CartSelling.VendorFactsOf(cart, StreetSales.Find(state, cart.id), tables, rules)).ToList();
        }

        public VendorFacts? FactsOf(string vehicleId)
        {
            var tables = DemandData.Tables;
            var rules = DemandData.Rules;
            var state = State;
            if (tables == null || rules == null || state == null) return null;
            var cart = FoodVehicles().FirstOrDefault(v => v.id == vehicleId);
            return cart == null ? null : CartSelling.VendorFactsOf(cart, StreetSales.Find(state, cart.id), tables, rules);
        }

        public long SellOne(string vehicleId, string item, long minute) => CartSelling.VendorSellOne(vehicleId, item, minute);

        public void Changed() => FoodTrucksSession.MarkChanged();

        public void Log(string line) => global::FoodTrucks.Log.Info(line);

        // The player's food vehicles, with the private driver's too, once each, in ordinal ID order.
        private static IEnumerable<VehicleInstance> FoodVehicles()
        {
            var game = SaveGameManager.Current;
            if (game == null) return Enumerable.Empty<VehicleInstance>();
            return (game.VehicleInstances ?? new List<VehicleInstance>())
                .Concat(game.privateDriverVehicleInstances ?? new List<VehicleInstance>())
                .Where(v => v != null && FoodTrucksMod.KindOf(v.vehicleTypeName) != null && VendorBook.ValidVehicleId(v.id))
                .GroupBy(v => v.id, StringComparer.Ordinal).Select(g => g.First())
                .OrderBy(v => v.id, StringComparer.Ordinal);
        }
    }
}
