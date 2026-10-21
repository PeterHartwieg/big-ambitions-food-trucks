#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BigAmbitions.Items;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using Localizor;
using UI;
using UI.InteriorDesigner;

namespace FoodTrucks
{
    // Commissary stock, the vehicle panel's button in any commissary's bay (plan, "UI flow: the vehicle panel holds only
    // cargo"; Peter, 15 October 2026). It replaces Restock with a two-way transfer the player chooses in the game's list
    // picker: "Fill up" loads food as Restock did, "Take …" loads one item from the commissary's stock (a piece of
    // equipment is fitted by loading it), "Return …" gives food or a fitted piece back, and a piece that leaves takes the
    // food that no longer fits without it. Food asks how many with the game's text prompt. Core's StockTransfer decides;
    // this moves the cargo and the stock, says what happened and logs every refusal.
    internal sealed partial class Commissary
    {
        // A vehicle standing in a commissary's bay, checked, with what a transfer needs.
        private sealed class AtBay
        {
            public AtBay(SaveState state, DemandTables tables, VehicleInstance vehicle, string kind, CommissarySite site)
            {
                State = state;
                Tables = tables;
                Vehicle = vehicle;
                Kind = kind;
                Site = site;
            }

            public SaveState State { get; }
            public DemandTables Tables { get; }
            public VehicleInstance Vehicle { get; }
            public string Kind { get; }
            public CommissarySite Site { get; }
            public string Who => $"{Kind} {World.Short(Vehicle.id)}";
        }

        // The checks before any transfer: data writable and the vehicle in a commissary's bay (any commissary, space or
        // not). A refusal shows its notice and logs why; null then. Standing at the bay unlocks its contact.
        private static AtBay? CheckAtBay(VehicleInstance? vehicle)
        {
            var state = FoodTrucksSession.CurrentState;
            var tables = DemandData.Tables;
            var kind = FoodTrucksMod.KindOf(vehicle?.vehicleTypeName);
            if (state == null || tables == null || Rules == null || vehicle == null || kind == null || uninstalled)
            {
                Log.Info("Commissary stock: refused, the mod's data isn't loaded (or uninstalled) or this isn't a food vehicle.");
                return null;
            }
            if (!FoodTrucksSession.CanWrite)
            {
                Log.Info("Commissary stock: refused, the mod's data is read-only.");
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return null;
            }
            var site = SiteOf(vehicle);
            if (site == null)
            {
                Log.Info($"Commissary stock: {kind} {World.Short(vehicle.id)} isn't in a commissary's bay; refused.");
                SayNotInBay(vehicle, kind);
                return null;
            }
            Unlock(state, site, "vehicle at the bay");
            return new AtBay(state, tables, vehicle, kind, site);
        }

        // One line of the list: "fill" or "nostock", or a take or return of one item.
        private sealed class StockOption
        {
            public StockOption(string op, string label, TransferChoice? choice = null)
            {
                Op = op;
                Label = label;
                Choice = choice;
            }

            public string Op { get; }
            public string Label { get; }
            public TransferChoice? Choice { get; }
        }

        // Fill up first, then the takes (foods, then pieces) and the returns (foods, then pieces), each group by name.
        private static List<StockOption> StockOptions(AtBay at)
        {
            var stock = InStock(at.State, at.Site.Id);
            var foodHere = stock.Where(p => at.Tables.Product(p.Key) != null).Sum(p => (long)p.Value);
            var options = new List<StockOption>
            {
                foodHere > 0
                    ? new StockOption("fill", Format("foodtrucks:commissary_stock_fill", Count(foodHere)))
                    : new StockOption("nostock", Format("foodtrucks:commissary_stock_fill_none", Name(at.Site)))
            };
            var choices = StockTransfer.Choices(at.Tables, at.Kind, CargoLimit.Lines(at.Vehicle), stock, Returnable(at.Vehicle));
            var labels = new HashSet<string>(options.Select(o => o.Label), StringComparer.Ordinal);
            foreach (var group in choices.GroupBy(c => (c.Kind, c.IsPiece)))
                foreach (var choice in group.OrderBy(c => Text(c.Item), StringComparer.CurrentCulture))
                {
                    var key = choice.Kind == TransferKind.Take ? "foodtrucks:commissary_stock_take" : "foodtrucks:commissary_stock_return";
                    var label = Format(key, Count(choice.Units), Text(choice.Item));
                    // Two items the game names alike: the item key tells them apart.
                    if (!labels.Add(label)) labels.Add(label = $"{label} ({choice.Item})");
                    options.Add(new StockOption(choice.Kind == TransferKind.Take ? "take" : "return", label, choice));
                }
            return options;
        }

        // The panel's Commissary stock button.
        public static void ShowStock()
        {
            var at = CheckAtBay(VehicleHelper.GetCurrentVehicle());
            if (at == null) return;
            // The game's list picker belongs to the interior designer and registers itself when that UI starts.
            if (DropdownSelector.onShow == null)
            {
                Log.Error("Commissary stock: the game's list picker isn't available in this scene.");
                CartSelling.Notify("foodtrucks:notification_commissary_stock_unavailable");
                return;
            }
            var options = StockOptions(at);
            var vehicleId = at.Vehicle.id;
            Log.Info($"Commissary stock at {at.Site.Id}: {at.Who} lists {options.Count} choices " +
                     $"({string.Join(", ", options.Select(o => o.Choice == null ? o.Op : $"{o.Op} {o.Choice.Item} {o.Choice.Units}"))}).");
            DropdownSelector.Show(options.Select(o => o.Label).ToList(), chosen =>
            {
                try
                {
                    var option = options.FirstOrDefault(o => o.Label == chosen);
                    if (option == null) Log.Info("Commissary stock: the list closed with nothing chosen.");
                    else Choose(vehicleId, option);
                }
                catch (Exception ex) { Log.Error("Commissary stock: " + ex); }
            }, "foodtrucks:commissary_stock_header", "foodtrucks:commissary_stock_body");
        }

        private static VehicleInstance? VehicleById(string vehicleId) =>
            SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v != null && v.id == vehicleId);

        // A line chosen. Food asks how many first; a piece moves one at a time. Every step checks the bay again, since
        // the list and the prompt can stay open while the game runs.
        private static void Choose(string vehicleId, StockOption option)
        {
            var choice = option.Choice;
            switch (option.Op)
            {
                case "fill":
                    FillUp(VehicleById(vehicleId));
                    return;
                case "nostock":
                    var at = CheckAtBay(VehicleById(vehicleId));
                    if (at == null) return;
                    Log.Info($"Commissary stock at {at.Site.Id}: {at.Who} chose Fill up with no food in stock here.");
                    CartSelling.Say("foodtrucks:notification_commissary_nostock", at.Kind, ("commissary", Name(at.Site)));
                    return;
            }
            if (choice == null) return;
            var take = choice.Kind == TransferKind.Take;
            // A piece, or a food none of which fits: no question, the transfer itself says why.
            if (choice.IsPiece || (take && choice.Most <= 0))
            {
                if (take) Take(VehicleById(vehicleId), choice.Item, choice.IsPiece ? 1 : choice.Units);
                else GiveBack(VehicleById(vehicleId), choice.Item, 1);
                return;
            }
            var input = InstanceBehavior<UIs>.Instance?.textInputUi;
            if (input == null)
            {
                Log.Error("Commissary stock: the game's text prompt isn't available; nothing moved.");
                CartSelling.Notify("foodtrucks:notification_commissary_stock_unavailable");
                return;
            }
            var most = choice.Most;
            input.Show(take ? "foodtrucks:commissary_stock_take_input" : "foodtrucks:commissary_stock_return_input",
                take ? "foodtrucks:commissary_stock_take_button" : "foodtrucks:commissary_stock_return_button", text =>
                {
                    try
                    {
                        var units = ParseUnits(text, most);
                        if (units == null)
                        {
                            Log.Info($"Commissary stock: '{text}' isn't a number of {choice.Item}; nothing moved.");
                            CartSelling.Notify("foodtrucks:notification_commissary_stock_invalid");
                            return;
                        }
                        if (take) Take(VehicleById(vehicleId), choice.Item, units.Value);
                        else GiveBack(VehicleById(vehicleId), choice.Item, units.Value);
                    }
                    catch (Exception ex) { Log.Error("Commissary stock: " + ex); }
                }, initialText: most.ToString(CultureInfo.InvariantCulture), allowEmpty: true, characterLimit: 7);
        }

        // Empty means all (`most`); otherwise a whole number above 0.
        private static int? ParseUnits(string? text, int most)
        {
            if (string.IsNullOrWhiteSpace(text)) return most;
            return int.TryParse(text!.Trim(), NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var units) && units > 0 ? units : (int?)null;
        }

        // "1,200": counts in the list read as in the plan's examples.
        private static string Count(long units) => units.ToString("#,0", CultureInfo.InvariantCulture);

        private static string Text(string key) => LocalizorManager.GetLocalization(key, null);

        // What can go back from the vehicle, by item: paid stacks, not sealed, as selling takes them. Food goes back from
        // loose stacks only; a fitted piece also when it holds stock, which goes back with it (Unload).
        private static Dictionary<string, int> Returnable(VehicleInstance vehicle)
        {
            var units = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var entry in ReturnableStacks(vehicle, null))
                units[entry.itemName] = (int)Math.Min(int.MaxValue, (units.TryGetValue(entry.itemName, out var n) ? n : 0) + (long)entry.amount);
            return units;
        }

        private static IEnumerable<CargoInstance> ReturnableStacks(VehicleInstance vehicle, string? item)
        {
            var tables = DemandData.Tables;
            return vehicle.cargoInstances.Where(c => c != null && !string.IsNullOrEmpty(c.itemName) && (item == null || c.itemName == item) &&
                                                     c.paid && !c.IsSealed && c.amount > 0 &&
                                                     (!Nested(c) || tables?.EquipmentForGameItem(c.itemName) != null)).ToList();
        }

        // ---- Fill up ----

        // Food from this commissary's stock into the vehicle, as Restock loaded it (LoadFromStock). Null when it ran (its
        // notice says what loaded), otherwise why not, for the test bridge.
        private static string? FillUp(VehicleInstance? vehicle)
        {
            var at = CheckAtBay(vehicle);
            if (at == null) return "refused";
            var state = at.State;
            var tables = at.Tables;
            var kind = at.Kind;
            var siteId = at.Site.Id;
            vehicle = at.Vehicle;
            if (!InStock(state, siteId).Any(p => tables.Product(p.Key) != null && p.Value > 0))
            {
                Log.Info($"Commissary fill up: {at.Who} at {siteId}: no food in stock here.");
                CartSelling.Say("foodtrucks:notification_commissary_nostock", kind, ("commissary", Name(at.Site)));
                return null;
            }
            var result = LoadFromStock(state, tables, vehicle, kind, siteId);
            var loaded = result.Items;
            var left = result.Left;
            state.Commissary.Stock.TryGetValue(siteId, out var rest);
            var view = Storage.Of(tables, kind, CargoLimit.Lines(vehicle));
            Log.Info($"Commissary fill up: {at.Who} at {siteId} loaded {(loaded.Count == 0 ? "nothing" : string.Join(", ", loaded.Select(p => $"{p.Value} {p.Key}")))}; " +
                     (result.NotHeld.Count == 0 ? "" : $"no piece for {string.Join(", ", result.NotHeld)}; ") + $"{result.Back} units refused by the cargo and kept; " +
                     $"{left} units left in stock; storage {(view == null ? "?" : StoragePanel.Summary(vehicle, view))}.");
            var foodText = string.Join(", ", FoodParts(tables, vehicle, view, loaded, rest));
            var notHeld = string.Join(", ", result.NotHeld.Select(Text));
            // Foods that stayed here because their piece is full, "Tacos: the Griddle is full (240/240)": each full piece
            // none of whose foods went in this time (a piece that took some says so in the loaded list).
            var fullParts = view == null ? new List<string>()
                : view.Pools.Where(p => Capped(tables, p, rest) && !p.Piece.Products.Any(loaded.ContainsKey)).Select(p => PieceFull(vehicle, p, rest)).ToList();
            var full = fullParts.Count == 0 ? "" : " " + string.Join("; ", fullParts) + ".";
            if (fullParts.Count > 0) Log.Info($"Commissary fill up: full pieces:{full}");
            if (loaded.Count == 0 && result.NotHeld.Count == 0 && fullParts.Count > 0)
                CartSelling.Say("foodtrucks:notification_commissary_restock_full_why", kind, ("full", full), ("left", left.ToString()));
            else if (loaded.Count == 0 && result.NotHeld.Count == 0)
                CartSelling.Say("foodtrucks:notification_commissary_restock_full", kind, ("left", left.ToString()));
            else if (loaded.Count == 0)
                CartSelling.Say("foodtrucks:notification_commissary_restock_notheld", kind, ("notheld", notHeld), ("full", full), ("left", left.ToString()));
            else if (result.NotHeld.Count == 0)
                CartSelling.Say("foodtrucks:notification_commissary_restocked", kind, ("loaded", foodText), ("full", full), ("left", left.ToString()));
            else
                CartSelling.Say("foodtrucks:notification_commissary_restocked_notheld", kind, ("loaded", foodText), ("notheld", notHeld), ("full", full), ("left", left.ToString()));
            return null;
        }

        // ---- Take and give back ----

        // Loads up to `units` of one item from this commissary's stock: no more than is here, and no more than fits
        // (Core StockTransfer.Take, the cargo's own rule). Nothing fitting is refused with the cargo's own notice. Null
        // when something loaded, otherwise why not.
        private static string? Take(VehicleInstance? vehicle, string item, int units)
        {
            var at = CheckAtBay(vehicle);
            if (at == null) return "refused";
            vehicle = at.Vehicle;
            var siteId = at.Site.Id;
            var piece = at.Tables.EquipmentForGameItem(item) != null;
            var here = InStock(at.State, siteId).TryGetValue(item, out var h) ? h : 0;
            if (here <= 0)
            {
                Log.Info($"Commissary stock: {at.Who} at {siteId} can't take {item}: none in stock here.");
                CartSelling.Say("foodtrucks:notification_commissary_stock_none_here", at.Kind, ("item", Text(item)), ("commissary", Name(at.Site)));
                return "none in stock here";
            }
            var wanted = Math.Min(piece ? 1 : Math.Max(1, units), here);
            var check = StockTransfer.Take(at.Tables, at.Kind, CargoLimit.Lines(vehicle), item, wanted);
            if (check.Allowed <= 0)
            {
                Log.Info($"Commissary stock: {at.Who} at {siteId} can't take {item}: {check.Refusal}.");
                CargoLimit.Refused(vehicle, item, check);
                return "refused by the cargo: " + check.Refusal;
            }
            var loaded = new Dictionary<string, int>(StringComparer.Ordinal);
            var back = Load(at.State, vehicle, siteId, new Dictionary<string, int>(StringComparer.Ordinal) { { item, check.Allowed } }, loaded);
            FoodTrucksSession.MarkChanged();
            CargoChanged(vehicle);
            var got = loaded.TryGetValue(item, out var g) ? g : 0;
            var left = InStock(at.State, siteId).TryGetValue(item, out var l) ? l : 0;
            var view = Storage.Of(at.Tables, at.Kind, CargoLimit.Lines(vehicle));
            Log.Info($"Commissary stock: {at.Who} at {siteId} took {got} {item} (asked {units}, {check.Allowed} fit" +
                     (check.Refusal == LoadRefusal.None ? "" : $", {check.Refusal}") + $"); {back} refused by the cargo and kept; {left} left here; " +
                     $"storage {(view == null ? "?" : StoragePanel.Summary(vehicle, view))}.");
            if (got == 0)
            {
                CartSelling.Say("foodtrucks:notification_commissary_stock_took_none", at.Kind, ("item", Text(item)));
                return "the cargo took none";
            }
            var pool = check.Refusal == LoadRefusal.PieceFull ? view?.PoolFor(at.Tables, item) : null;
            CartSelling.Say(piece ? "foodtrucks:notification_commissary_stock_took_piece" : "foodtrucks:notification_commissary_stock_took", at.Kind,
                ("units", got.ToString()), ("item", Text(item)), ("left", left.ToString()), ("capped", pool == null ? "" : " " + CapNote(vehicle, pool)));
            return null;
        }

        // Gives `units` of a food back to this commissary's stock, or one fitted piece and with it the food that no longer
        // fits without it (Core StockTransfer.FoodLeavingWith). Each batch goes back at what the vehicle paid for it.
        // Null when something went back, otherwise why not.
        private static string? GiveBack(VehicleInstance? vehicle, string item, int units)
        {
            var at = CheckAtBay(vehicle);
            if (at == null) return "refused";
            vehicle = at.Vehicle;
            var siteId = at.Site.Id;
            var piece = at.Tables.EquipmentForGameItem(item) != null;
            var onBoard = Returnable(vehicle).TryGetValue(item, out var b) ? b : 0;
            if (onBoard <= 0)
            {
                Log.Info($"Commissary stock: {at.Who} at {siteId} can't give back {item}: none on board that can go back.");
                CartSelling.Say("foodtrucks:notification_commissary_stock_none_on_board", at.Kind, ("item", Text(item)));
                return "none on board";
            }
            // A piece goes one at a time, unless the only ones left hold stock in one stack, which goes back whole (Unload).
            var first = piece ? ReturnableStacks(vehicle, item).OrderBy(c => Nested(c) ? 1 : 0).ThenBy(c => c.amount).First() : null;
            var count = piece ? (Nested(first!) ? first!.amount : 1) : Math.Min(Math.Max(1, units), onBoard);
            // Decided before the piece leaves, from the cargo as it stands.
            var food = piece ? StockTransfer.FoodLeavingWith(at.Tables, at.Kind, CargoLimit.Lines(vehicle), item, count) : new Dictionary<string, int>();
            var day = Math.Max(1, SaveGameManager.Current.Day);
            var moved = Unload(at, item, count, day);
            var foodBack = new Dictionary<string, int>(StringComparer.Ordinal);
            if (moved > 0)
                foreach (var pair in food)
                {
                    var m = Unload(at, pair.Key, pair.Value, day);
                    if (m > 0) foodBack[pair.Key] = m;
                    if (m < pair.Value)
                        Log.Info($"Commissary stock: {pair.Value - m} {pair.Key} that no longer fit without the {item} couldn't go back (unpaid, sealed or packed) and stay on board.");
                }
            FoodTrucksSession.MarkChanged();
            CargoChanged(vehicle);
            var view = Storage.Of(at.Tables, at.Kind, CargoLimit.Lines(vehicle));
            var inStock = InStock(at.State, siteId).TryGetValue(item, out var s) ? s : 0;
            Log.Info($"Commissary stock: {at.Who} at {siteId} gave back {moved} {item} (asked {units})" +
                     (foodBack.Count == 0 ? "" : $" and with it {string.Join(", ", foodBack.Select(p => $"{p.Value} {p.Key}"))}") +
                     $"; {inStock} {item} in stock here now; storage {(view == null ? "?" : StoragePanel.Summary(vehicle, view))}.");
            if (moved == 0)
            {
                CartSelling.Say("foodtrucks:notification_commissary_stock_none_on_board", at.Kind, ("item", Text(item)));
                return "nothing went back";
            }
            if (!piece)
                CartSelling.Say("foodtrucks:notification_commissary_stock_returned", at.Kind, ("units", moved.ToString()), ("item", Text(item)), ("here", inStock.ToString()));
            else if (foodBack.Count == 0)
                CartSelling.Say("foodtrucks:notification_commissary_stock_returned_piece", at.Kind, ("item", Text(item)));
            else
                CartSelling.Say("foodtrucks:notification_commissary_stock_returned_piece_food", at.Kind, ("item", Text(item)),
                    ("food", string.Join(", ", foodBack.Select(p => $"{p.Value} {Text(p.Key)}"))));
            return null;
        }

        // Takes up to `units` of an item off the vehicle, loose stacks first and smallest first, and adds each stack's share
        // to this commissary's stock as a new batch at the stack's price per unit (Core Commissaries.ReturnFromVehicle,
        // which keeps the share that came from commissary orders apart, for the tax label, and the day the food first came
        // on board). A fitted piece holding stock (a stocked drinks fridge) goes back whole, its stack and what it holds:
        // the contents become stock of their own, so no unit or value is lost (review round 1 of the UI flow, Astra). The
        // stock is booked before the cargo changes: emptying a stack runs the game's cargo listeners, and one that throws
        // must not lose the units. Food leaves the health grade's record oldest first, as a sale does. Returns the units moved.
        private static int Unload(AtBay at, string item, int units, int day)
        {
            var vehicle = at.Vehicle;
            var moved = 0;
            foreach (var entry in ReturnableStacks(vehicle, item).OrderBy(c => Nested(c) ? 1 : 0).ThenBy(c => c.amount))
            {
                if (moved >= units) break;
                var whole = Nested(entry);
                var n = whole ? entry.amount : Math.Min(units - moved, entry.amount);
                if (whole)
                {
                    Log.Info($"Commissary stock: {entry.amount} {item} on {World.Short(vehicle.id)} hold stock; they go back whole with it.");
                    foreach (var inner in Contents(entry))
                        Book(at, inner.itemName, inner.amount, inner.pricePerUnit, day);
                }
                Book(at, item, n, entry.pricePerUnit, day);
                moved += n;
                try { vehicle.ReduceFromCargo(entry, n); }
                catch (Exception ex) { Log.Error($"Commissary stock, taking {n} {item} off {vehicle.id}: {ex}"); }
            }
            return moved;
        }

        private static bool Nested(CargoInstance entry) => entry.nestedCargoInstances != null && entry.nestedCargoInstances.Count > 0;

        // What a stack holds, with units (the game nests one level: NestedCargoInstance).
        private static IEnumerable<NestedCargoInstance> Contents(CargoInstance stack) =>
            (stack.nestedCargoInstances ?? new List<NestedCargoInstance>()).Where(n => n != null && n.amount > 0 && !string.IsNullOrEmpty(n.itemName));

        // One stack's share into this commissary's stock. The price goes through decimal, so the game's float price per
        // unit comes back as the cents it was loaded at (review round 1 of the UI flow, Opus). The fleet's units of the
        // item are counted before this vehicle's leave, as Core's ordered pool is cut to them.
        private static void Book(AtBay at, string item, int units, float pricePerUnit, int day)
        {
            var food = at.Tables.Product(item) != null;
            var saved = StreetSales.Find(at.State, at.Vehicle.id);
            var fleetOnBoard = Fleet().Values.Sum(v => (long)UnitsOnBoard(v, item));
            var loadedDay = food && saved != null ? ReputationBook.TakeOutOldest(saved, item, units) : null;
            var cents = (long)Math.Round((decimal)Math.Max(0f, pricePerUnit) * 100m);
            var lines = Commissaries.ReturnFromVehicle(at.State.Commissary, at.Site.Id, item, units, cents, day, at.Vehicle.id,
                (int)Math.Min(int.MaxValue, fleetOnBoard), loadedDay);
            Log.Info($"Commissary stock: {units} {item} from {World.Short(at.Vehicle.id)} into stock at {at.Site.Id}, {CartSelling.Money(cents)} each " +
                     $"({string.Join(", ", lines.Select(l => $"{l.Units} {(Commissaries.Deducted(l) ? "from commissary orders" : "of other origin")}"))})" +
                     (loadedDay == null ? "." : $", on board since day {loadedDay}."));
        }

        // An item's units in a vehicle's cargo, inside other stacks too, as the health grade counts food on board.
        internal static int UnitsOnBoard(VehicleInstance vehicle, string item)
        {
            long units = 0;
            foreach (var stack in vehicle.cargoInstances)
            {
                if (stack == null) continue;
                if (stack.itemName == item && stack.amount > 0) units += stack.amount;
                foreach (var inner in Contents(stack))
                    if (inner.itemName == item) units += inner.amount;
            }
            return (int)Math.Min(int.MaxValue, units);
        }

        // ---- The test bridge (docs/test-bridge.md) ----

        // The vehicle op restock: Commissary stock's Fill up, with its checks and notices. Null when it ran (or said
        // there is no food here); otherwise why not.
        internal static string? FillUpForTesting(VehicleInstance vehicle) =>
            FillUp(vehicle) == null ? null : "the Commissary stock button refused (see the notice)";

        // The vehicle op transfer: Commissary stock's take or return of one item, by key or by name; `units` for food
        // (null: as many as fit, or all on board), a piece moves one. Null when something moved; otherwise why not.
        internal static string? TransferForTesting(VehicleInstance vehicle, string direction, string item, int? units)
        {
            var at = CheckAtBay(vehicle);
            if (at == null) return "the Commissary stock button refused (see the notice)";
            var take = direction == "take";
            if (!take && direction != "return") return $"direction is take or return, not '{direction}'";
            var choices = StockTransfer.Choices(at.Tables, at.Kind, CargoLimit.Lines(vehicle), InStock(at.State, at.Site.Id), Returnable(vehicle))
                .Where(c => c.Kind == (take ? TransferKind.Take : TransferKind.Return)).ToList();
            var choice = choices.FirstOrDefault(c => c.Item == item) ??
                         choices.FirstOrDefault(c => string.Equals(Text(c.Item), item, StringComparison.OrdinalIgnoreCase));
            if (choice == null)
                return $"Commissary stock offers no {(take ? "take" : "return")} of '{item}' here ({(choices.Count == 0 ? "none" : string.Join(", ", choices.Select(c => c.Item)))})";
            var count = choice.IsPiece ? 1 : units ?? (take ? Math.Max(1, choice.Most) : choice.Units);
            return take ? Take(vehicle, choice.Item, count) : GiveBack(vehicle, choice.Item, count);
        }
    }
}
