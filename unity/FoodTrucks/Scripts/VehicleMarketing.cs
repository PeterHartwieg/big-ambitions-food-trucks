#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Marketing;
using FoodTrucks.Core.Saves;
using Localizor;

namespace FoodTrucks
{
    // Marketing for one cart or truck (design: Marketing). Core's MarketingFees decides what is due; this pays it through
    // the game under the marketing tax label, records it for Econoview and says when a day's fee couldn't be paid.
    internal static class VehicleMarketing
    {
        private const string ExpenseLabel = "foodtrucks:transaction_marketing_label";

        private static bool attached;

        private static MarketingRules? Rules => DemandData.Rules?.Marketing;

        public static void Attach()
        {
            Detach();
            GlobalEvents.onNewDay += OnNewDay;
            attached = true;
            // After a load: today's fees for levels still on.
            Renew();
        }

        public static void Detach()
        {
            if (!attached) return;
            GlobalEvents.onNewDay -= OnNewDay;
            attached = false;
        }

        private static void OnNewDay()
        {
            try { Renew(); }
            catch (Exception ex) { Log.Error("Marketing: " + ex); }
        }

        // Today's fee for every vehicle the player still has with a level on. A refused fee turns the level off.
        internal static void Renew()
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            var today = SaveGameManager.Current.Day;
            if (today < 1) return;
            foreach (var vehicle in state.Vehicles.Where(v => v.MarketingLevel != null).ToList())
            {
                // One vehicle's failure doesn't keep the others from their day's fee.
                try
                {
                    if (BizManVehiclePage.Instance(vehicle.VehicleId) == null) continue;
                    var before = vehicle.MarketingPaidThroughDay;
                    var charge = MarketingFees.Renew(vehicle, rules, today, c => Pay(state, c, showNotification: false));
                    if (charge == null)
                    {
                        // A level priced at 0 runs today without a payment.
                        if (vehicle.MarketingPaidThroughDay != before) FoodTrucksSession.MarkChanged();
                        continue;
                    }
                    FoodTrucksSession.MarkChanged();
                    Log.Info($"Marketing: {World.Short(charge.VehicleId)} {charge.Level} for day {charge.Day}, {CartSelling.Money(charge.Cents)} " +
                             (charge.Paid ? "paid." : "unpaid (not enough money); marketing stopped."));
                    if (!charge.Paid)
                        CartSelling.Notify("foodtrucks:notification_marketing_stopped",
                            ("vehicle", VehicleName(state, charge.VehicleId)), ("fee", CartSelling.Money(charge.Cents)));
                }
                catch (Exception ex) { Log.Error($"Marketing: {World.Short(vehicle.VehicleId)}: {ex}"); }
            }
        }

        // The player's pick on the vehicle page (and the test bridge's marketing command). False when the game refused
        // the payment, with its own "not enough money" notice; the level then stays as it was.
        internal static bool Choose(SaveState state, VehicleState vehicle, string level)
        {
            var rules = Rules ?? throw new InvalidOperationException("The marketing rules aren't loaded.");
            // The minutes not yet sold sell under the old level first, as ServingWork.BeforeLeaving catches up before the
            // server changes: routes decide their closings, then the player's windows. Neither has a per-vehicle catch-up,
            // and selling every vehicle up to now is what their next ticks would do anyway.
            var now = HireCrew.NowExact();
            try { HireRoutes.CatchUpTo((long)Math.Floor(now)); }
            catch (Exception ex) { Log.Error("Marketing, routes catch-up before a level change: " + ex); }
            CartSelling.CatchUpTo(now);
            var today = SaveGameManager.Current.Day;
            var before = vehicle.MarketingLevel;
            var charge = MarketingFees.Choose(vehicle, rules, level, today, c => Pay(state, c, showNotification: true));
            FoodTrucksSession.MarkChanged();
            Log.Info($"Marketing: {World.Short(vehicle.VehicleId)} from {before ?? MarketingRules.None} to {level} on day {today}" +
                     (charge == null ? ", nothing to pay." : $", {CartSelling.Money(charge.Cents)} {(charge.Paid ? "paid" : "refused (not enough money); unchanged")}."));
            return charge == null || charge.Paid;
        }

        // The level running today, for the page and the bridge; null without one.
        internal static MarketingLevel? Active(VehicleState? vehicle)
        {
            var rules = Rules;
            var game = SaveGameManager.Current;
            return rules == null || game == null ? null : MarketingFees.Active(vehicle, rules, game.Day);
        }

        private static bool Pay(SaveState state, MarketingCharge charge, bool showNotification)
        {
            if (!Payments.TryPayOut(charge.Cents, Info(state, charge.VehicleId), showNotification)) return false;
            try { SalesLedger.RecordMarketing(state, charge.Day, charge.VehicleId, charge.Cents); }
            catch (Exception ex) { Log.Error($"Marketing: {World.Short(charge.VehicleId)} paid, but the fee wasn't recorded for Econoview: {ex}"); }
            return true;
        }

        private static TransactionInfo Info(SaveState state, string vehicleId)
        {
            var info = new TransactionInfo("foodtrucks:transaction_marketing", new Dictionary<string, string> { { "vehicle", VehicleName(state, vehicleId) } });
            info.SetTaxDeductibleName(ExpenseLabel);
            return info;
        }

        private static string VehicleName(SaveState state, string vehicleId)
        {
            if (StreetFoodBooks.VehicleNames().TryGetValue(vehicleId, out var name)) return name;
            var kind = StreetSales.Find(state, vehicleId)?.VehicleType;
            return LocalizorManager.GetLocalization(kind == "truck" ? FoodTrucksMod.TruckTypeName : FoodTrucksMod.CartTypeName, null);
        }
    }
}
