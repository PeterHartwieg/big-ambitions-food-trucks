#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using Buildings.Office.Headquarters;
using Entities;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Helpers;
using Localizor;
using Streets;
using UI.Smartphone.Apps.BizMan.LogisticsManagers;
using UnityEngine.Events;
using CoreLogistics = FoodTrucks.Core.Commissary.CommissaryLogistics;

namespace FoodTrucks
{
    // Logistics plans deliver to a commissary (Peter, 14 October 2026; design: Commissary supply and crew restock). A
    // commissary where the player rents a space is offered in a warehouse plan's destination dropdown under its own name;
    // at the game's 02:00 run its stock targets are filled from the plan's warehouse into Core's commissary stock. Core's
    // CommissaryLogistics decides how much; this moves the goods with the game's own calls.
    //
    // A commissary destination's address is the game's underground parking street with a negative number per commissary
    // (Core AddressNumber). That street never has a building registration [code, 1.0: BuildingHelper.
    // CanTheAddressHaveARegistration], so every game call on it finds none instead of throwing, and none of the game's
    // own code that later walks the plans touches it: the 02:00 delivery stops at the missing registration
    // (LogisticsManagerPlanDestination.Deliver, the import/export check in DeliverDestination is null-safe), a building
    // changing hands or a business moving resets or moves only destinations at that building's address
    // (LogisticsManagerHelper.CancelAllDeliveriesForAddress, CompatibilityHelper.RemoveLogisticsManagerPlan,
    // BizManTransfer), and the plan's product list comes out empty. So a save keeps loading with the mod removed without
    // the uninstall step: such a destination shows as unassigned and delivers nothing. The real building's address isn't
    // used: the game would then deliver into whoever occupies it, list it twice when the player rents it, and drop the
    // destination when it changes hands.
    internal static class LogisticsCommissary
    {
        // The game's street for underground parking entrances [code, 1.0: UndergroundParkingEntrance], whose numbers
        // are the entrances' own, never negative.
        private const string Street = "ba:street_parking";
        private const string NoSpaceKey = "foodtrucks:logistics_commissary_nospace";

        private static bool ready;
        // Lines said once per load: a destination that delivers nothing, by plan and reason.
        private static readonly HashSet<string> logged = new HashSet<string>(StringComparer.Ordinal);
        private static AccessTools.FieldRef<LogisticsManagerDestinationUI, LogisticsManagerPlanUI>? planUiField;
        private static AccessTools.FieldRef<LogisticsManagerDestinationUI, LogisticsManagerPlan>? planField;
        private static AccessTools.FieldRef<LogisticsManagerDestinationUI, int>? indexField;
        private static AccessTools.FieldRef<LogisticsManagerDestinationUI, UI.Elements.Dropdown>? dropdownField;
        // The game's list of shortfalls for its "not enough stock" text, by plan (LogisticsManagerPlan.AllUndeliveredNoStock).
        private static System.Reflection.FieldInfo? noStockField;

        // The patches the destination needs, by method; commissaries are offered only when all are active.
        private static readonly (Type Type, string Method)[] Needed =
        {
            (typeof(LogisticsManagerPlan), "DeliverDestination"),
            (typeof(LogisticsManagerPlanUI), "GetListOfAvailableProducts"),
            (typeof(LogisticsManagerPlanUI), "IsProductInUse"),
            (typeof(LogisticsManagerDestinationUI), "SetUp")
        };

        // Called once after patching.
        internal static void CheckReady()
        {
            try
            {
                planUiField = AccessTools.FieldRefAccess<LogisticsManagerDestinationUI, LogisticsManagerPlanUI>("_logisticsManagerPlanUI");
                planField = AccessTools.FieldRefAccess<LogisticsManagerDestinationUI, LogisticsManagerPlan>("_currentPlan");
                indexField = AccessTools.FieldRefAccess<LogisticsManagerDestinationUI, int>("_destinationIndex");
                dropdownField = AccessTools.FieldRefAccess<LogisticsManagerDestinationUI, UI.Elements.Dropdown>("businessTargetDropdown");
                noStockField = AccessTools.Field(typeof(LogisticsManagerPlan), "AllUndeliveredNoStock");
                if (noStockField == null) Log.Error("Commissary logistics: the game's shortfall list isn't there; shortfalls go to the log only.");
                var missing = Needed.Where(n => !Patched(n.Type, n.Method)).Select(n => $"{n.Type.Name}.{n.Method}").ToList();
                ready = missing.Count == 0;
                Log.Info(ready
                    ? "Commissary logistics: on (commissaries with a space are logistics plan destinations)."
                    : "Commissary logistics: off (patches not active: " + string.Join(", ", missing) + "); plans can't pick a commissary.");
            }
            catch (Exception ex)
            {
                ready = false;
                Log.Error("Commissary logistics: off, the game's plan screen has changed: " + ex.Message);
            }
        }

        private static bool Patched(Type type, string method)
        {
            var original = AccessTools.Method(type, method);
            var info = original == null ? null : Harmony.GetPatchInfo(original);
            return info != null && info.Prefixes.Concat(info.Postfixes).Any(p => p.PatchMethod.DeclaringType == typeof(LogisticsCommissary));
        }

        // On load: what the plans hold for commissaries.
        internal static void Attach()
        {
            logged.Clear();
            var plans = SaveGameManager.Current?.logisticsManagerPlans;
            if (plans == null) return;
            var ours = plans.Where(p => p?.destinations != null)
                .SelectMany(p => p.destinations.Where(d => Ours(d?.deliveryTargetAddress)).Select(d => (Plan: p, Destination: d))).ToList();
            if (ours.Count == 0) return;
            Log.Info($"Commissary logistics: {ours.Count} plan destinations at commissaries: " +
                     string.Join("; ", ours.Select(o => $"{IdOf(o.Destination.deliveryTargetAddress) ?? "unknown " + o.Destination.deliveryTargetAddress.streetNumber} " +
                                                       $"from {SourceName(o.Plan)}, {o.Destination.stockTargets?.Count(t => t != null && t.targetAmount > 0) ?? 0} targets")) + ".");
        }

        private static bool Ours(Address? address) => address != null && address.streetName == Street && address.streetNumber < 0;

        private static IEnumerable<CommissarySite> Sites => DemandData.Commissaries?.Sites ?? Enumerable.Empty<CommissarySite>();

        private static string? IdOf(Address? address) =>
            Ours(address) ? CoreLogistics.IdFor(Sites.Select(s => s.Id), address!.streetNumber) : null;

        private static Address AddressOf(CommissarySite site) => new Address(Street, CoreLogistics.AddressNumber(site.Id));

        // For the test bridge: whether commissaries are offered as destinations, the address a commissary stands for in a
        // plan (what its dropdown entry sets), and the commissary an address stands for.
        internal static bool Ready => ready;
        internal static Address DestinationFor(CommissarySite site) => AddressOf(site);
        internal static string? SiteIdOf(Address? address) => IdOf(address);

        private static string SourceName(LogisticsManagerPlan plan)
        {
            if (plan.targetAddress.IsUndefined()) return "no warehouse";
            var registration = BuildingHelper.GetBuildingRegistration(plan.targetAddress);
            return string.IsNullOrEmpty(registration?.BusinessName) ? plan.targetAddress.ToString() : registration!.BusinessName;
        }

        private static void Once(string key, string message)
        {
            if (logged.Add(key)) Log.Info(message);
        }

        // ---- The 02:00 delivery ----

        // Prefix on LogisticsManagerPlan.DeliverDestination: a commissary destination is filled here and the game's own
        // delivery skipped. Its shortfall goes into the game's "not enough stock" text, as a shop's does.
        internal static bool DeliverPrefix(LogisticsManagerPlan __instance, LogisticsManagerPlanDestination destination)
        {
            if (destination == null || !Ours(destination.deliveryTargetAddress)) return true;
            try { Deliver(__instance, destination); }
            catch (Exception ex) { Log.Error("Commissary logistics, delivery: " + ex); }
            // As the game's own DeliverDestination after a delivery: the game refreshes what depends on stock once the
            // run's work is done [code, 1.0: LogisticsManagerPlan.DeliverDestination, GameManager.shouldUpdateAfterDeliveries].
            try
            {
                var game = InstanceBehavior<GameManager>.Instance;
                if (game != null) game.shouldUpdateAfterDeliveries = true;
            }
            catch (Exception ex) { Log.Error("Commissary logistics, update after deliveries: " + ex.Message); }
            return false;
        }

        private static void Deliver(LogisticsManagerPlan plan, LogisticsManagerPlanDestination destination)
        {
            // As the game: no warehouse or one the player no longer rents delivers nothing [code, 1.0: DeliverDestination].
            if (plan.targetAddress.IsUndefined()) return;
            if (!(BuildingHelper.GetBuildingRegistration(plan.targetAddress) is Warehouse warehouse) || !warehouse.RentedByPlayer) return;
            var number = destination.deliveryTargetAddress.streetNumber;
            var siteId = IdOf(destination.deliveryTargetAddress);
            var site = DemandData.Commissaries?.Site(siteId);
            var key = plan.id + "|" + number;
            var state = FoodTrucksSession.CurrentState;
            if (Commissary.Uninstalled || state == null || !FoodTrucksSession.CanWrite)
            {
                Once(key + "|state", $"Commissary logistics: the delivery from {SourceName(plan)} to {siteId ?? "a commissary"} skipped (the mod's save data can't be written now).");
                return;
            }
            if (site == null)
            {
                Once(key + "|site", $"Commissary logistics: a destination of {SourceName(plan)} names commissary number {number}, which commissaries.json doesn't have; nothing delivered.");
                return;
            }
            var prices = Commissary.Catalogue(false).GroupBy(c => c.Item, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().UnitCents, StringComparer.Ordinal);
            var targets = (destination.stockTargets ?? new List<ItemAmountTarget>()).Where(t => t != null && !string.IsNullOrEmpty(t.itemName))
                .Select(t => new KeyValuePair<string, int>(t.itemName, t.targetAmount)).ToList();
            var run = CoreLogistics.Plan(state.Commissary, site.Id, targets, prices);
            if (!run.Accepted)
            {
                Once(key + "|space", $"Commissary logistics: {site.Id} has no rented space, so the delivery from {SourceName(plan)} brings nothing (said once until it has a space again).");
                return;
            }
            logged.Remove(key + "|space");
            var day = SaveGameManager.Current.Day;
            var from = warehouse.Address?.ToString() ?? plan.targetAddress.ToString();
            var moved = new List<(string Item, int Units)>();
            var shortItems = new List<ItemAmountTarget>();
            foreach (var line in run.Lines)
            {
                var got = Withdraw(warehouse, state, site.Id, line, day, from);
                if (got > 0) moved.Add((line.ItemName, got));
                if (got < line.Units) shortItems.Add(new ItemAmountTarget(line.ItemName, line.Units - got));
            }
            if (moved.Count > 0)
            {
                FoodTrucksSession.MarkChanged();
                // The warehouse's delivery history, as for a shop: goods out, negative [code, 1.0: LogisticsManagerPlanDestination.Deliver].
                warehouse.deliveryTransactions ??= new List<DeliveryTransaction>();
                var transaction = new DeliveryTransaction();
                transaction.SetItems(moved.Select(m => new DeliveryItem(m.Item, -m.Units)));
                warehouse.deliveryTransactions.Add(transaction);
                if (warehouse.deliveryTransactions.Count > 60) warehouse.deliveryTransactions.RemoveRange(0, warehouse.deliveryTransactions.Count - 60);
            }
            if (shortItems.Count > 0 && !plan.isFactory) ReportShort(plan, Commissary.Name(site), shortItems);
            Log.Info($"Commissary logistics: {SourceName(plan)} to {site.Id}: " +
                     (moved.Count == 0 ? "nothing brought" : string.Join(", ", moved.Select(m => $"{m.Units} {m.Item}"))) +
                     $" ({moved.Sum(m => (long)m.Units)} units)" +
                     (shortItems.Count > 0 ? "; short in the warehouse: " + string.Join(", ", shortItems.Select(s => $"{s.targetAmount} {s.itemName}")) : "") +
                     (run.Unpriced.Count > 0 ? "; not in the commissary's catalogue, left alone: " + string.Join(", ", run.Unpriced) : "") + ".");
        }

        // Takes up to the line's units out of the warehouse's pallets with the game's own calls, as its delivery to a shop
        // does [code, 1.0: LogisticsManagerPlanDestination.ProcessStockTarget], and adds them to the commissary's stock.
        // Returns the units moved.
        private static int Withdraw(Warehouse warehouse, SaveState state, string siteId, CommissaryLogisticsLine line, int day, string from)
        {
            var cargo = new List<CargoInstance>();
            BuildingHelper.GetItemsWithStock(warehouse, line.ItemName, cargo);
            // WithdrawFromCargo invokes each pallet's listeners after reducing its amount. A listener can throw
            // before the helper returns the withdrawn cargo. Keep the original amounts until Core accepts it, so
            // either failure restores the same stacks without running the throwing listeners again.
            var before = cargo.Select(c => (Cargo: c, Amount: c.amount)).ToList();
            CargoInstance? taken;
            try { taken = BuildingHelper.WithdrawFromCargo(cargo, line.Units); }
            catch (Exception ex)
            {
                foreach (var entry in before) entry.Cargo.amount = entry.Amount;
                Log.Error($"Commissary logistics: withdrawal of {line.ItemName} for {siteId} threw; warehouse stock restored: {ex}");
                return 0;
            }
            var got = taken?.amount ?? 0;
            if (got > 0)
            {
                // Priced at what the warehouse paid (the withdrawal's mean price), not the catalogue's.
                try { CoreLogistics.Receive(state.Commissary, siteId, line.ItemName, got, CoreLogistics.PaidCents(taken!.pricePerUnit), day, from); }
                catch (Exception ex)
                {
                    Log.Error($"Commissary logistics: {got} {line.ItemName} for {siteId} not taken into stock, back to the warehouse: {ex}");
                    foreach (var entry in before) entry.Cargo.amount = entry.Amount;
                    return 0;
                }
            }
            // Stock has already reached Core. Empty-stack cleanup only updates the warehouse's presentation;
            // its listeners must not abort delivery bookkeeping or the remaining products in the plan.
            try { BuildingHelper.RemoveEmptyCargo(cargo); }
            catch (Exception ex) { Log.Error("Commissary logistics, empty cargo cleanup: " + ex); }
            return got;
        }

        // Into the list the game texts from after the run (LogisticsManagerPlan.OnFinishedDeliveries), named by the commissary.
        private static void ReportShort(LogisticsManagerPlan plan, string name, List<ItemAmountTarget> shortItems)
        {
            try
            {
                if (!(noStockField?.GetValue(null) is Dictionary<LogisticsManagerPlan, List<BusinessDeliveryInfo>> all)) return;
                if (!all.TryGetValue(plan, out var list)) all[plan] = list = new List<BusinessDeliveryInfo>();
                list.Add(new BusinessDeliveryInfo(name, shortItems));
            }
            catch (Exception ex) { Log.Error("Commissary logistics, shortfall text: " + ex.Message); }
        }

        // ---- The plan screen ----

        // Prefix on LogisticsManagerPlanUI.GetListOfAvailableProducts: a commissary lists the foods of its catalogue the
        // game keeps on warehouse pallets (retail products, as the game's own delivery list; no service products, no
        // machines), and any other item that still has a target, as the game keeps those [code, 1.0:
        // AddStockTargetsWithPositiveAmounts].
        internal static bool ProductsPrefix(LogisticsManagerPlanDestination destination, ref List<string> __result)
        {
            if (destination == null || !Ours(destination.deliveryTargetAddress)) return true;
            try { __result = Products(destination); }
            catch (Exception ex)
            {
                Log.Error("Commissary logistics, product list: " + ex);
                __result = new List<string>();
            }
            return false;
        }

        private static List<string> Products(LogisticsManagerPlanDestination destination)
        {
            var list = Commissary.Catalogue(false).Select(c => c.Item).Where(name =>
            {
                var item = ItemsGetter.GetByName(name, suppressError: true);
                return item != null && (item.type & ItemType.RetailProduct) != 0 && (item.type & ItemType.ServiceProduct) == 0;
            }).ToList();
            foreach (var target in destination.stockTargets ?? new List<ItemAmountTarget>())
                if (target != null && target.targetAmount > 0 && !list.Contains(target.itemName) &&
                    ItemsGetter.GetByName(target.itemName, suppressError: true) != null)
                    list.Add(target.itemName);
            return list;
        }

        // Prefix on LogisticsManagerPlanUI.IsProductInUse: for a commissary, "in use" is what it holds in stock (Core).
        internal static bool InUsePrefix(Address address, string product, ref bool __result)
        {
            if (!Ours(address)) return true;
            try
            {
                var state = FoodTrucksSession.CurrentState;
                var id = IdOf(address);
                __result = state != null && id != null && CoreLogistics.Held(state.Commissary, id, product) > 0;
            }
            catch (Exception ex)
            {
                Log.Error("Commissary logistics, in use: " + ex.Message);
                __result = false;
            }
            return false;
        }

        // Postfix on LogisticsManagerDestinationUI.SetUp: the destination dropdown gets the commissaries with a rented
        // space after the player's businesses, named as the commissary (not whoever occupies its building), and the
        // one this destination already names if its space was given up. Its choice handler is replaced by one that
        // knows those entries; for a business it does what the game's does.
        internal static void SetUpPostfix(LogisticsManagerDestinationUI __instance)
        {
            if (!ready || Commissary.Uninstalled) return;
            try
            {
                var ui = planUiField!(__instance);
                var plan = planField!(__instance);
                var dropdown = dropdownField!(__instance);
                var index = indexField!(__instance);
                if (ui == null || plan == null || dropdown == null || plan.isFactory || index < 0 || index >= plan.destinations.Count) return;
                var state = FoodTrucksSession.CurrentState;
                var current = plan.destinations[index]?.deliveryTargetAddress;
                var currentId = IdOf(current);
                var entries = new List<(Address Address, string Label)>();
                foreach (var site in Sites)
                {
                    var accepts = state != null && CoreLogistics.Accepts(state.Commissary, site.Id);
                    if (accepts) entries.Add((AddressOf(site), Commissary.Name(site)));
                    else if (site.Id == currentId)
                        entries.Add((AddressOf(site), string.Format(LocalizorManager.GetLocalization(NoSpaceKey, null), Commissary.Name(site))));
                }
                if (entries.Count == 0) return;
                var businesses = ui.Destinations ?? new List<BuildingRegistration>();
                var labels = businesses.Select(b => b.GetDisplayName()).Concat(entries.Select(e => e.Label)).ToList();
                var selected = businesses.FindIndex(b => b.Address == current);
                if (currentId != null)
                {
                    var ours = entries.FindIndex(e => e.Address == current);
                    selected = ours < 0 ? -1 : businesses.Count + ours;
                }
                dropdown.SetOptions(labels, localize: false, selected);
                dropdown.onOptionSelected.RemoveAllListeners();
                dropdown.onOptionSelected.AddListener((UnityAction<int>)(option =>
                {
                    try
                    {
                        var list = ui.Destinations ?? new List<BuildingRegistration>();
                        Address? address = option >= 0 && option < list.Count ? list[option].Address
                            : option >= list.Count && option - list.Count < entries.Count ? entries[option - list.Count].Address : null;
                        if (address == null) return;
                        ui.UpdateSelectedBusiness(indexField!(__instance), address);
                        var id = IdOf(address);
                        if (id != null) Log.Info($"Commissary logistics: destination {indexField!(__instance) + 1} of the plan from {SourceName(plan)} set to {id}.");
                    }
                    catch (Exception ex) { Log.Error("Commissary logistics, destination choice: " + ex); }
                }));
            }
            catch (Exception ex) { Log.Error("Commissary logistics, destination dropdown: " + ex); }
        }

        // ---- The uninstall step ----

        // Every commissary destination leaves every plan, so the plans read as the game made them.
        internal static void RemoveFromPlans()
        {
            var removed = 0;
            foreach (var plan in SaveGameManager.Current?.logisticsManagerPlans ?? new List<LogisticsManagerPlan>())
                if (plan?.destinations != null) removed += plan.destinations.RemoveAll(d => d != null && Ours(d.deliveryTargetAddress));
            if (removed > 0)
            {
                SaveGameManager.MarkChange();
                Log.Info($"Uninstall: {removed} commissary destinations removed from logistics plans.");
            }
        }
    }
}
