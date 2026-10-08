#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Characters.Skills;
using BigAmbitions.DayNightCycle;
using BigAmbitions.Items;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Rules;
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
    // menu. While it is open, Core's SalesSimulator runs every in-game minute: customers arrive at the rate
    // the demand model gives for the spot and the hour (CartDemand), queue at the window and give up after
    // a while. The player serves while they stay close, at the speed of their customer service skill;
    // away from the window nobody serves, so the queue waits and thins out. Each customer buys one main
    // item and sometimes a cold drink; each item takes one unit of cargo and pays the cart's price with
    // the mod's label. CartQueueView shows the queue as people near the player. Grabbing the cart closes it.
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
        // Customers since opening who left without buying, by reason, for the close log line.
        private readonly Dictionary<string, Dictionary<CustomerExit, int>> lostSinceOpen = new Dictionary<string, Dictionary<CustomerExit, int>>();
        // The spot each open cart stands on, read at opening and again each new day.
        private readonly Dictionary<string, Spot> spots = new Dictionary<string, Spot>();
        private SalesSimulator? simulator;
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

        private void OnTimeMachineStarted()
        {
            skipping = true;
            CartQueueView.ClearAll();
        }

        // Skipped time is not working time: every open cart starts counting again from now, and the people
        // who were waiting have long gone.
        private void OnTimeMachineEnded()
        {
            skipping = false;
            var now = TimeHelper.NowInMinutes();
            foreach (var id in simulatedUntil.Keys.ToList()) simulatedUntil[id] = now;
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen && v.Service != null))
            {
                var gone = SalesSimulator.Close(vehicle.Service!);
                if (gone.Left.Count > 0)
                {
                    CountLost(vehicle.VehicleId, gone);
                    FoodTrucksSession.MarkChanged();
                }
            }
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
            if (tables == null || DemandData.Rules == null)
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
            vehicle.Service = SalesSimulator.Open(SeededRandom.SeedFor(cart.id, SaveGameManager.Current.Day));
            simulatedUntil[cart.id] = TimeHelper.NowInMinutes();
            pausedNoticeShown.Remove(cart.id);
            sinceOpen[cart.id] = (0, 0);
            lostSinceOpen.Remove(cart.id);
            var spot = World.ReadSpot(tables, position);
            spots[cart.id] = spot;
            FoodTrucksSession.MarkChanged();
            var plan = CartDemand.Plan(tables, cart, spot);
            var service = Service(DemandData.Rules);
            Log.Info($"Cart {cart.id} open on day {TimeHelper.CurrentDay} at {TimeHelper.CurrentHour:D2}:{(int)TimeHelper.CurrentMinute:D2}: {World.Describe(spot)}; {CartDemand.Describe(plan)} " +
                     $"Window: customer service skill {service.Skill:F0}, speed {service.Speed:F2}, {plan.Capacity * service.Speed:F0} an hour at most, customers {service.Mood.ToString().ToLowerInvariant()}; queue limit {service.QueueLimit}.");
            Notify("foodtrucks:notification_open", ("units", plan.StockUnits.ToString()), ("rate", ExpectedSales(plan, service.Speed).ToString("F0")));
        }

        // The window as the player works it: their customer service skill sets the speed (Core: ServiceRules).
        // The player has no satisfaction of their own in the game, so their skill counts in full.
        private readonly struct Window
        {
            public Window(double skill, double speed, ServiceMood mood, int queueLimit)
            {
                Skill = skill;
                Speed = speed;
                Mood = mood;
                QueueLimit = queueLimit;
            }

            public double Skill { get; }
            public double Speed { get; }
            public ServiceMood Mood { get; }
            public int QueueLimit { get; }
        }

        private static Window Service(RuleTables rules)
        {
            var skill = ServiceRules.EffectiveSkill(PlayerSkill(), 100);
            return new Window(skill, rules.Service.Speed(skill), rules.Service.Mood(skill),
                rules.Service.QueueLimit(CartDemand.Kind, rules.Queue.MaxQueue));
        }

        // The player's customer service skill [code, unverified in game]: the first character in the save is
        // the player, and the game gives everyone 50 when the skill is missing (InitializePlayerSkills).
        private static double PlayerSkill()
        {
            var skills = SaveGameManager.Current?.charactersData?.FirstOrDefault()?.skills;
            var skill = skills?.Find(s => s != null && s.name == CustomerServiceSkill);
            return skill != null ? skill.value : 50;
        }

        private const string CustomerServiceSkill = "ba:skill_customerservice";

        // Sales an hour while the player serves: the customers who buy, up to what the window can make.
        private static double ExpectedSales(CartPlan plan, double speed) =>
            plan.Estimate == null ? 0 : Math.Min(plan.Estimate.CustomersPerHour * plan.Conversion, plan.Capacity * speed);

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
            if (vehicle.Service != null) CountLost(vehicle.VehicleId, SalesSimulator.Close(vehicle.Service));
            vehicle.Service = null;
            CartQueueView.Closed(vehicle.VehicleId);
            simulatedUntil.Remove(vehicle.VehicleId);
            pausedNoticeShown.Remove(vehicle.VehicleId);
            sinceOpen.TryGetValue(vehicle.VehicleId, out var totals);
            sinceOpen.Remove(vehicle.VehicleId);
            lostSinceOpen.TryGetValue(vehicle.VehicleId, out var lost);
            lostSinceOpen.Remove(vehicle.VehicleId);
            spots.Remove(vehicle.VehicleId);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Cart {vehicle.VehicleId} closed ({messageKey}): {totals.Sales} sales, {Money(totals.Cents)} since opening; " +
                     $"walked away: {Lost(lost, CustomerExit.GaveUp)} tired of waiting, {Lost(lost, CustomerExit.QueueFull)} at a full queue, " +
                     $"{Lost(lost, CustomerExit.NothingLeft)} found nothing left, {Lost(lost, CustomerExit.Closed)} at closing; " +
                     $"lifetime {vehicle.LifetimeSales} sales, {Money(vehicle.LifetimeRevenueCents)}.");
            Notify(messageKey, ("sales", totals.Sales.ToString()), ("revenue", Money(totals.Cents)));
        }

        private static int Lost(Dictionary<CustomerExit, int>? lost, CustomerExit exit) =>
            lost != null && lost.TryGetValue(exit, out var count) ? count : 0;

        private void CountLost(string vehicleId, SalesResult result)
        {
            if (result.Left.Count == 0) return;
            if (!lostSinceOpen.TryGetValue(vehicleId, out var lost)) lostSinceOpen[vehicleId] = lost = new Dictionary<CustomerExit, int>();
            foreach (var (_, exit) in result.Left)
            {
                lost.TryGetValue(exit, out var count);
                lost[exit] = count + 1;
            }
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
                vehicle.Service = null;
            }
            CartQueueView.ClearAll();
            instance?.simulatedUntil.Clear();
            instance?.sinceOpen.Clear();
            instance?.lostSinceOpen.Clear();
            instance?.spots.Clear();
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
            var atWindow = PlayerIsAtCart(cart);
            if (!atWindow)
            {
                if (pausedNoticeShown.Add(vehicle.VehicleId)) Notify("foodtrucks:notification_paused");
            }
            else pausedNoticeShown.Remove(vehicle.VehicleId);

            var tables = DemandData.Tables;
            var rules = DemandData.Rules;
            if (tables == null || rules == null) return;
            var spot = SpotFor(tables, cart);
            if (spot == null) return;
            var plan = CartDemand.Plan(tables, cart, spot);
            if (!plan.Usable)
            {
                // A cart opened under 0.1.0 has food but no equipment; it isn't sold out.
                Close(vehicle, "foodtrucks:notification_closed_no_equipment");
                return;
            }
            if (plan.StockUnits == 0)
            {
                Close(vehicle, "foodtrucks:notification_sold_out");
                return;
            }
            if (elapsed > SalesTick.MaxCatchUpMinutes) return;

            // A cart opened before 0.4.0 has no queue yet; it starts one now.
            vehicle.Service ??= SalesSimulator.Open(SeededRandom.SeedFor(cart.id, SaveGameManager.Current.Day));
            simulator ??= new SalesSimulator(tables, rules.Queue);
            var window = Service(rules);
            var result = simulator.Advance(vehicle.Service, Input(tables, plan, atWindow ? 1 : 0, window), elapsed);
            CountLost(vehicle.VehicleId, result);
            var failed = false;
            // One notification per customer: their main item and the drink that came with it.
            foreach (var customer in result.Sales.GroupBy(s => s.CustomerId))
            {
                if (!ServeCustomer(state, vehicle, cart, plan, customer.Select(s => s.Item).ToList()))
                {
                    failed = true;
                    break;
                }
            }
            CartQueueView.Record(vehicle.VehicleId, result, window.Mood);
            if (result.Sales.Count > 0 || result.Joined.Count > 0 || result.Left.Count > 0) FoodTrucksSession.MarkChanged();
            if (failed) Close(vehicle, "foodtrucks:notification_closed_error");
            else if (result.SoldOut || CartDemand.StockOf(cart, plan.Menu.Menu).Count == 0) Close(vehicle, "foodtrucks:notification_sold_out");
        }

        // What the simulator needs for these minutes, from the cart's plan. Customers who buy arrive at the
        // model's rate times the price conversion and pick a category by the buyer shares; each category
        // sells the item customers accept best (plan.SaleItems), and food buyers add the plan's drink.
        private static SalesInput Input(DemandTables tables, CartPlan plan, int servers, Window window)
        {
            var mainShare = plan.BuyerShares.Where(p => tables.Category(p.Key).AddOnFactor <= 0).Sum(p => p.Value);
            return new SalesInput
            {
                Menu = plan.Stock.Keys.OrderBy(k => k, StringComparer.Ordinal).ToList(),
                Stock = plan.Stock,
                CustomersPerHour = plan.Estimate?.CustomersPerHour ?? 0,
                Conversion = plan.Conversion,
                CustomerShares = plan.BuyerShares,
                CategoryItems = plan.SaleItems,
                // Core spreads add-ons over all buyers and divides by the food buyers' share again.
                AddOnsPerCustomer = plan.AddOnItem == null ? 0 : plan.AddOnsPerFoodBuyer * mainShare,
                Servers = servers,
                Raining = RainHelper.isRaining,
                ServiceSpeed = window.Speed,
                QueueLimit = window.QueueLimit
            };
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

        // One customer's order as Core sold it: each item takes a unit and is paid at the cart's price.
        // One notification per customer.
        private bool ServeCustomer(SaveState state, VehicleState vehicle, VehicleInstance cart, CartPlan plan, List<string> items)
        {
            var labels = new List<string>();
            long cents = 0;
            foreach (var item in items)
            {
                var sold = SellItem(state, vehicle, cart, item);
                // The main item failing stops selling; a drink that fails only leaves the order without it.
                if (sold < 0)
                {
                    if (labels.Count == 0) return false;
                    break;
                }
                labels.Add(LocalizorManager.GetLocalization(item, null));
                cents += sold;
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
            spots.TryGetValue(cart.id, out var spot);
            var cents = CartPricing.PriceCents(vehicle, itemName, spot?.Neighbourhood);
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
            // What this unit cost at the wholesaler, for the profit shown in the day summary and Econoview.
            SalesLedger.RecordCost(state, day, (long)Math.Round(Math.Max(0f, entry.pricePerUnit) * 100.0));
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
