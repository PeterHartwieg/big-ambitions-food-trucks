#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using Localizor;
using Parking.UndergroundParking;
using UI;
using UI.ItemPanel;
using UI.Notification;
using UnityEngine;

namespace FoodTrucks
{
    // Selling: Open parks the cart where it stands, if the spot is legal and the fitted equipment gives a
    // menu. While it is open and the player stays close, customers come at the rate the demand model
    // gives for the spot and the hour (CartDemand). Each customer buys one main item and sometimes a cold
    // drink; each item takes one unit of cargo and pays the game's market price with the mod's label.
    // Grabbing the cart closes it.
    internal sealed class CartSelling : MonoBehaviour
    {
        // The player works the window, so sales pause when they walk further away than this.
        private const float WorkingDistance = 8f;

        // Real-time throttle for the tick; sales themselves follow in-game minutes.
        private const float TickSeconds = 0.25f;

        // In-game minute up to which each open cart has been simulated. Not saved: after a load the
        // first tick starts counting again, so time spent in the menu never turns into sales.
        private readonly Dictionary<string, float> simulatedUntil = new Dictionary<string, float>();
        private readonly HashSet<string> pausedNoticeShown = new HashSet<string>();
        private readonly Dictionary<string, (int Sales, long Cents)> sinceOpen = new Dictionary<string, (int, long)>();
        // The spot each open cart stands on, read at opening and again each new day.
        private readonly Dictionary<string, Spot> spots = new Dictionary<string, Spot>();
        private readonly Dictionary<string, double> addOnProgress = new Dictionary<string, double>();
        private readonly System.Random random = new System.Random();
        private float nextTick;
        // From the time machine's start event to its end event (sleep, skips, paid activities).
        // Its component turns off a moment before the end event, so isRunning alone misses the last minutes.
        private bool skipping;

        private static CartSelling? instance;

        private void OnEnable()
        {
            instance = this;
            GlobalEvents.onEnterVehicle += OnEnterVehicle;
            GlobalEvents.onTimeMachineStarted += OnTimeMachineStarted;
            GlobalEvents.onTimeMachineEnded += OnTimeMachineEnded;
        }

        private void OnDisable()
        {
            GlobalEvents.onEnterVehicle -= OnEnterVehicle;
            GlobalEvents.onTimeMachineStarted -= OnTimeMachineStarted;
            GlobalEvents.onTimeMachineEnded -= OnTimeMachineEnded;
            if (instance == this) instance = null;
        }

        private void OnTimeMachineStarted() => skipping = true;

        // Skipped time is not working time: every open cart starts counting again from now.
        private void OnTimeMachineEnded()
        {
            skipping = false;
            var now = TimeHelper.NowInMinutes();
            foreach (var id in simulatedUntil.Keys.ToList()) simulatedUntil[id] = now;
        }

        // Called by the item panel's Open button while the player pushes a cart.
        public static void OpenHere(ItemPanelUI panel)
        {
            try { instance?.Open(panel); }
            catch (Exception ex)
            {
                Log.Error("Open failed: " + ex);
                Notify("foodtrucks:notification_open_failed");
            }
        }

        private void Open(ItemPanelUI panel)
        {
            var state = FoodTrucksSession.CurrentState;
            var cart = VehicleHelper.GetCurrentVehicle();
            if (cart == null || cart.vehicleTypeName != FoodTrucksMod.CartTypeName) return;
            if (state == null || !FoodTrucksSession.CanWrite)
            {
                Notify("foodtrucks:notification_data_readonly");
                return;
            }
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking)
            {
                Notify("foodtrucks:notification_open_outside");
                return;
            }
            var tables = DemandData.Tables;
            if (tables == null)
            {
                Notify("foodtrucks:notification_open_no_data");
                return;
            }
            if (cart.cargoInstances.Any(c => !c.paid))
            {
                Notify("foodtrucks:notification_open_unpaid");
                return;
            }
            var menu = CartDemand.MenuOf(tables, cart);
            if (menu.Problem == MenuProblem.TooMuchEquipment)
            {
                Notify("foodtrucks:notification_open_too_much_equipment", ("slots", (tables.Vehicle(CartDemand.Kind)?.EquipmentSlots ?? 0).ToString()));
                return;
            }
            if (menu.Problem != MenuProblem.None || menu.Menu.Items.Count == 0)
            {
                Log.Info($"Open: cart {cart.id} has no usable equipment ({menu.Problem} {menu.Detail}).");
                Notify("foodtrucks:notification_open_no_equipment");
                return;
            }
            if (CartDemand.StockOf(cart, menu.Menu).Count == 0)
            {
                Notify("foodtrucks:notification_open_no_stock");
                return;
            }
            var controller = VehicleHelper.GetCurrentVehicleBase();
            var position = controller != null ? controller.transform.position : PlayerHelper.GetPosition();
            var legality = Legality.Check(position);
            Log.Info($"Open: cart {cart.id} at {position}: {legality.Detail}; road layer below: {(legality.RoadBelow ? "yes" : "no")}.");
            if (!legality.Legal)
            {
                Notify(legality.RuleKey!);
                return;
            }

            // The game's own Park button: it checks the panel state and refuses where parking is not allowed.
            panel.ClickPark();
            if (SaveGameManager.Current.ActiveVehicleId == cart.id)
            {
                Log.Info($"Open: cart {cart.id} could not be parked here.");
                Notify("foodtrucks:notification_open_cannot_park");
                return;
            }

            var vehicle = StreetSales.GetOrAdd(state, cart.id);
            vehicle.IsOpen = true;
            vehicle.SaleProgress = 0;
            simulatedUntil[cart.id] = TimeHelper.NowInMinutes();
            pausedNoticeShown.Remove(cart.id);
            sinceOpen[cart.id] = (0, 0);
            addOnProgress[cart.id] = 0;
            var spot = World.ReadSpot(tables, position);
            spots[cart.id] = spot;
            FoodTrucksSession.MarkChanged();
            var plan = CartDemand.Plan(tables, cart, spot);
            Log.Info($"Cart {cart.id} open on day {TimeHelper.CurrentDay} at {TimeHelper.CurrentHour:D2}:{(int)TimeHelper.CurrentMinute:D2}: {World.Describe(spot)}; {CartDemand.Describe(plan)}");
            Notify("foodtrucks:notification_open", ("units", plan.StockUnits.ToString()), ("rate", plan.SalesPerHour.ToString("F0")));
        }

        private void OnEnterVehicle(VehicleController controller)
        {
            try
            {
                var cart = controller?.vehicleInstance;
                if (cart == null || cart.vehicleTypeName != FoodTrucksMod.CartTypeName) return;
                PrefabFixes.ApplyTo(controller);
                var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, cart.id);
                if (vehicle != null && vehicle.IsOpen && FoodTrucksSession.CanWrite) Close(vehicle, "foodtrucks:notification_closed");
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private void Close(VehicleState vehicle, string messageKey)
        {
            vehicle.IsOpen = false;
            vehicle.SaleProgress = 0;
            simulatedUntil.Remove(vehicle.VehicleId);
            pausedNoticeShown.Remove(vehicle.VehicleId);
            sinceOpen.TryGetValue(vehicle.VehicleId, out var totals);
            sinceOpen.Remove(vehicle.VehicleId);
            spots.Remove(vehicle.VehicleId);
            addOnProgress.Remove(vehicle.VehicleId);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Cart {vehicle.VehicleId} closed ({messageKey}): {totals.Sales} sales, {Money(totals.Cents)} since opening; lifetime {vehicle.LifetimeSales} sales, {Money(vehicle.LifetimeRevenueCents)}.");
            Notify(messageKey, ("sales", totals.Sales.ToString()), ("revenue", Money(totals.Cents)));
        }

        // Closes every open cart without a message, for the uninstall step.
        public static void CloseAllQuietly()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen))
            {
                vehicle.IsOpen = false;
                vehicle.SaleProgress = 0;
            }
            instance?.simulatedUntil.Clear();
            instance?.sinceOpen.Clear();
            instance?.spots.Clear();
            instance?.addOnProgress.Clear();
            FoodTrucksSession.MarkChanged();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextTick) return;
            nextTick = Time.unscaledTime + TickSeconds;
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            try
            {
                foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen).ToList())
                    Tick(state, vehicle);
            }
            catch (Exception ex)
            {
                Log.Error("Sales tick: " + ex);
                enabled = false;
                Notify("foodtrucks:notification_tick_failed");
            }
        }

        private void Tick(SaveState state, VehicleState vehicle)
        {
            var cart = SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId);
            if (cart == null)
            {
                // Sold, deleted or lost: nothing left to sell from.
                Close(vehicle, "foodtrucks:notification_closed_missing");
                return;
            }

            var now = TimeHelper.NowInMinutes();
            if (!simulatedUntil.TryGetValue(vehicle.VehicleId, out var from))
            {
                simulatedUntil[vehicle.VehicleId] = now;
                return;
            }
            // Sleeping and other time skips run the clock without the player at the window.
            var timeMachine = InstanceBehavior<UIs>.Instance?.timeMachine;
            if (skipping || (timeMachine != null && timeMachine.isRunning))
            {
                simulatedUntil[vehicle.VehicleId] = now;
                return;
            }
            var elapsed = (int)Math.Floor(now - from);
            if (elapsed < 1) return;
            simulatedUntil[vehicle.VehicleId] = from + elapsed;
            if (!PlayerIsAtCart(cart))
            {
                if (pausedNoticeShown.Add(vehicle.VehicleId)) Notify("foodtrucks:notification_paused");
                return;
            }
            pausedNoticeShown.Remove(vehicle.VehicleId);

            var tables = DemandData.Tables;
            if (tables == null) return;
            var spot = SpotFor(tables, cart);
            if (spot == null) return;
            var plan = CartDemand.Plan(tables, cart, spot);
            if (!plan.Usable)
            {
                // A cart opened under 0.1.0 has food but no equipment; it isn't sold out.
                Close(vehicle, "foodtrucks:notification_closed_no_equipment");
                return;
            }
            if (elapsed > SalesTick.MaxCatchUpMinutes) return;

            // Walk through the elapsed minutes customer by customer: after each sale the stock, and so the
            // rate, may change (a category that sells out stops drawing customers for the rest of the time).
            var progress = vehicle.SaleProgress;
            if (double.IsNaN(progress) || progress < 0 || progress >= 1) progress = 0;
            double minutesLeft = elapsed;
            var served = 0;
            var failed = false;
            while (minutesLeft > 0 && plan.StockUnits > 0)
            {
                var perMinute = plan.SalesPerHour / 60.0;
                // Nobody buys at these prices right now: no sales, and no stock-only fallback.
                if (perMinute <= 0 || plan.Conversion <= 0) break;
                var needed = (1 - progress) / perMinute;
                if (needed > minutesLeft)
                {
                    progress = Math.Min(progress + minutesLeft * perMinute, 0.999999);
                    break;
                }
                minutesLeft -= needed;
                progress = 0;
                if (!ServeCustomer(state, vehicle, cart, tables, plan))
                {
                    failed = true;
                    break;
                }
                served++;
                plan = CartDemand.Plan(tables, cart, spot);
            }
            if (served > 0 || progress != vehicle.SaleProgress) FoodTrucksSession.MarkChanged();
            vehicle.SaleProgress = progress;
            if (failed || plan.StockUnits == 0)
                Close(vehicle, plan.StockUnits == 0 ? "foodtrucks:notification_sold_out" : "foodtrucks:notification_closed_error");
        }

        // The spot is read again on a new day, in case the city around the cart changed.
        private Spot? SpotFor(DemandTables tables, VehicleInstance cart)
        {
            if (spots.TryGetValue(cart.id, out var spot) && spot.Day == SaveGameManager.Current.Day) return spot;
            var controller = VehicleHelper.GetVehicleController(cart);
            if (controller == null) return spot;
            spot = World.ReadSpot(tables, controller.transform.position);
            spots[cart.id] = spot;
            Log.Info($"Cart {cart.id} spot on day {spot.Day}: {World.Describe(spot)}.");
            return spot;
        }

        // One customer: a main item chosen by the categories' shares of customers, plus the cold drinks
        // the add-on rate gives. One notification per customer.
        private bool ServeCustomer(SaveState state, VehicleState vehicle, VehicleInstance cart, DemandTables tables, CartPlan plan)
        {
            var stock = CartDemand.StockOf(cart, plan.Menu.Menu);
            var main = CustomerPick.MainItem(tables, plan.BuyerShares, stock, random.NextDouble());
            if (main == null) return false;
            var labels = new List<string>();
            long cents = 0;
            var sold = SellItem(state, vehicle, cart, main);
            if (sold < 0) return false;
            labels.Add(LocalizorManager.GetLocalization(main, null));
            cents += sold;

            // Drinks ride only with customers who came for food; a drink customer doesn't buy a second one.
            var drinkCustomer = tables.Category(tables.Product(main)!.Category).AddOnFactor > 0;
            addOnProgress.TryGetValue(vehicle.VehicleId, out var progress);
            var (addOns, next) = drinkCustomer ? (0, progress) : CustomerPick.AdvanceAddOns(progress, plan.AddOnsPerFoodBuyer);
            addOnProgress[vehicle.VehicleId] = next;
            for (var i = 0; i < addOns; i++)
            {
                var drink = CustomerPick.AddOnItem(tables, CartDemand.StockOf(cart, plan.Menu.Menu));
                if (drink == null) break;
                var drinkCents = SellItem(state, vehicle, cart, drink);
                if (drinkCents < 0) break;
                labels.Add(LocalizorManager.GetLocalization(drink, null));
                cents += drinkCents;
            }
            var left = CartDemand.StockOf(cart, plan.Menu.Menu).Values.Sum();
            Notify("foodtrucks:notification_sale", ("item", string.Join(" + ", labels)), ("price", Money(cents)), ("units", left.ToString()));
            return true;
        }

        private static bool PlayerIsAtCart(VehicleInstance cart)
        {
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking) return false;
            if (SaveGameManager.Current.ActiveVehicleId == cart.id) return false;
            var controller = VehicleHelper.GetVehicleController(cart);
            if (controller == null) return false;
            return Vector3.Distance(PlayerHelper.GetPosition(), controller.transform.position) <= WorkingDistance;
        }

        // One item at the cart's price. Returns the cents paid, or -1 if nothing was sold.
        private long SellItem(SaveState state, VehicleState vehicle, VehicleInstance cart, string itemName)
        {
            var cents = (long)Math.Round(CartDemand.PriceOf(itemName) * 100.0);
            if (cents <= 0)
            {
                Log.Error($"Sale refused: {itemName} has no market price.");
                return -1;
            }
            var day = SaveGameManager.Current.Day;
            // Check the record can take the sale before any money moves.
            if (!StreetSales.CanRecord(state, vehicle.VehicleId, day, cents, 1))
            {
                Log.Error("Sale refused: the street food record is full.");
                return -1;
            }
            var entry = cart.cargoInstances
                .Where(c => c.itemName == itemName && c.paid && !c.IsSealed && c.amount > 0 &&
                            (c.nestedCargoInstances == null || c.nestedCargoInstances.Count == 0))
                .OrderBy(c => c.amount)
                .FirstOrDefault();
            if (entry == null) return -1;
            var label = LocalizorManager.GetLocalization(itemName, null);
            var info = new TransactionInfo("foodtrucks:transaction_sale", new Dictionary<string, string> { { "itemName", label } });
            if (!Payments.PayIn(cents, info))
            {
                Log.Error($"Sale refused by the game: {itemName} for {Money(cents)}.");
                return -1;
            }
            // Record before touching the cargo: emptying a stack runs the game's cargo listeners,
            // and the ledger must not miss a paid sale if one of them throws. Neither call can fail here.
            StreetSales.Record(state, vehicle.VehicleId, day, cents, 1);
            sinceOpen.TryGetValue(vehicle.VehicleId, out var totals);
            sinceOpen[vehicle.VehicleId] = (totals.Sales + 1, totals.Cents + cents);
            try
            {
                // Emptying a stack already runs the game's cargo listeners; a throwing one must not stop selling.
                cart.ReduceFromCargo(entry, 1);
                // ReduceFromCargo only signals when a stack empties; the HUD needs every change.
                cart.OnItemsInCargoUpdated()?.Invoke();
                GameEvent.Invoke("ba:gameevent_itemcargochanged");
            }
            catch (Exception ex) { Log.Error("Cargo update listeners: " + ex); }
            Log.Info($"Sale: {itemName} for {Money(cents)} on day {day}, {cart.GetAmountByItemName(itemName)} of it left.");
            return cents;
        }

        // Paid food units in the cart, whatever the menu (for the status readout).
        internal static int StockUnits(VehicleInstance cart)
        {
            var tables = DemandData.Tables;
            return tables == null ? 0 : CargoRules.FoodUnits(tables, CargoLimit.Lines(cart));
        }

        internal static string Money(long cents) =>
            TaxCalculationHelper.ToCurrencyFormat(TaxCalculationHelper.RoundCurrency(cents / 100f));

        internal static void Notify(string key, params (string Key, string Value)[] data)
        {
            var values = new Dictionary<string, string>();
            foreach (var (k, v) in data) values[k] = v;
            Notifications.Show(NotificationType.Info, key, values, 5f, notificationSound: false, trackOnSaveGame: false);
        }
    }
}
