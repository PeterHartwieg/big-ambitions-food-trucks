#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
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
    // Milestone 1 selling: Open parks the cart where it stands; while it is open and the player stays
    // close, customers buy at a constant rate. Each sale takes one unit of cargo and pays the game's
    // market price with the mod's transaction label. Grabbing the cart closes it.
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
            if (cart.cargoInstances.Any(c => !c.paid))
            {
                Notify("foodtrucks:notification_open_unpaid");
                return;
            }
            if (StockByItem(cart).Count == 0)
            {
                Notify("foodtrucks:notification_open_no_stock");
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
            FoodTrucksSession.MarkChanged();
            Log.Info($"Cart {cart.id} open on day {TimeHelper.CurrentDay} at {TimeHelper.CurrentHour:D2}:{(int)TimeHelper.CurrentMinute:D2}, stock {StockUnits(cart)}.");
            Notify("foodtrucks:notification_open", ("units", StockUnits(cart).ToString()));
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

            var result = SalesTick.Advance(vehicle.SaleProgress, elapsed, CartTuning.SalesPerHour, StockUnits(cart));
            if (result.Skipped) return;
            vehicle.SaleProgress = result.Progress;
            var sold = 0;
            for (var i = 0; i < result.Sales; i++)
            {
                if (!SellOne(state, vehicle, cart)) break;
                sold++;
            }
            if (sold > 0 || result.Sales > 0) FoodTrucksSession.MarkChanged();
            if (sold < result.Sales || StockUnits(cart) == 0)
                Close(vehicle, StockUnits(cart) == 0 ? "foodtrucks:notification_sold_out" : "foodtrucks:notification_closed_error");
        }

        private static bool PlayerIsAtCart(VehicleInstance cart)
        {
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking) return false;
            if (SaveGameManager.Current.ActiveVehicleId == cart.id) return false;
            var controller = VehicleHelper.GetVehicleController(cart);
            if (controller == null) return false;
            return Vector3.Distance(PlayerHelper.GetPosition(), controller.transform.position) <= WorkingDistance;
        }

        // One customer: the menu item with the most stock, at the game's market price.
        private static bool SellOne(SaveState state, VehicleState vehicle, VehicleInstance cart)
        {
            var itemName = CartMenu.PickNext(StockByItem(cart));
            if (itemName == null) return false;
            var item = ItemsGetter.GetByName(itemName, suppressError: true);
            if (item == null) return false;
            var cents = (long)Math.Round(item.DefaultMarketPrice * CartTuning.PriceMultiplier * 100);
            if (cents <= 0)
            {
                Log.Error($"Sale refused: {itemName} has no market price.");
                return false;
            }
            var day = SaveGameManager.Current.Day;
            // Check the record can take the sale before any money moves.
            if (!StreetSales.CanRecord(state, vehicle.VehicleId, day, cents, 1))
            {
                Log.Error("Sale refused: the street food record is full.");
                return false;
            }
            var entry = cart.cargoInstances
                .Where(c => c.itemName == itemName && c.paid && !c.IsSealed && c.amount > 0)
                .OrderBy(c => c.amount)
                .First();
            var label = LocalizorManager.GetLocalization(itemName, null);
            var info = new TransactionInfo("foodtrucks:transaction_sale", new Dictionary<string, string> { { "itemName", label } });
            if (!Payments.PayIn(cents, info))
            {
                Log.Error($"Sale refused by the game: {itemName} for {Money(cents)}.");
                return false;
            }
            // Record before touching the cargo: emptying a stack runs the game's cargo listeners,
            // and the ledger must not miss a paid sale if one of them throws. Neither call can fail here.
            StreetSales.Record(state, vehicle.VehicleId, day, cents, 1);
            if (instance != null)
            {
                instance.sinceOpen.TryGetValue(vehicle.VehicleId, out var totals);
                instance.sinceOpen[vehicle.VehicleId] = (totals.Sales + 1, totals.Cents + cents);
            }
            try
            {
                // Emptying a stack already runs the game's cargo listeners; a throwing one must not stop selling.
                cart.ReduceFromCargo(entry, 1);
                // ReduceFromCargo only signals when a stack empties; the HUD needs every change.
                cart.OnItemsInCargoUpdated()?.Invoke();
                GameEvent.Invoke("ba:gameevent_itemcargochanged");
            }
            catch (Exception ex) { Log.Error("Cargo update listeners: " + ex); }
            var left = StockUnits(cart);
            Log.Info($"Sale: {itemName} for {Money(cents)} on day {day}, {left} units left.");
            Notify("foodtrucks:notification_sale", ("item", label), ("price", Money(cents)), ("units", left.ToString()));
            return true;
        }

        // Paid, unsealed menu items in the cart, in units.
        internal static Dictionary<string, int> StockByItem(VehicleInstance cart)
        {
            var stock = new Dictionary<string, int>();
            foreach (var entry in cart.cargoInstances)
            {
                if (entry == null || !entry.paid || entry.IsSealed || entry.amount <= 0 || !CartMenu.IsOnMenu(entry.itemName)) continue;
                stock.TryGetValue(entry.itemName, out var units);
                stock[entry.itemName] = units + entry.amount;
            }
            return stock;
        }

        internal static int StockUnits(VehicleInstance cart) => StockByItem(cart).Values.Sum();

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
