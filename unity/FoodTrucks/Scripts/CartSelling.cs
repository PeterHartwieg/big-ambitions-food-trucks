#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Characters.Skills;
using BigAmbitions.DayNightCycle;
using BigAmbitions.Items;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Fleet;
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
    // Selling: Open parks the cart where it stands (or the truck where it stopped, in a legal parking space),
    // if the player has a vendor licence, the vehicle a permit, and the fitted equipment gives a menu. While it is open, Core's SalesSimulator runs every in-game minute: customers arrive at the rate
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
        // Sales Core has made whose customer is still on the way to the window in the visible queue, per cart.
        // They are paid when that customer pays (CartQueueView calls Paid), so the money and the message
        // come with the person at the window. Their units already count as gone for Core.
        private readonly Dictionary<string, List<HeldSale>> held = new Dictionary<string, List<HeldSale>>();
        // Carts where paying a held sale failed; the next tick closes them.
        private readonly HashSet<string> heldFailed = new HashSet<string>();
        private SalesSimulator? simulator;
        private float nextTick;

        // A held sale is paid anyway after this many in-game minutes, whatever the view does.
        private const double MaxHeldMinutes = 60;

        private sealed class HeldSale
        {
            public HeldSale(long customerId, List<string> items, double minute)
            {
                CustomerId = customerId;
                Items = items;
                Minute = minute;
            }

            public long CustomerId { get; }
            public List<string> Items { get; }
            public double Minute { get; }
        }
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
            PayAllHeld();
            skipping = true;
            CartQueueView.Suspended = true;
            CartQueueView.ClearAll();
        }

        // Skipped time is not the player's working time: every cart the player runs starts counting again from
        // now, and the people who were waiting have long gone. A crew kept selling through it.
        private void OnTimeMachineEnded()
        {
            skipping = false;
            CartQueueView.Suspended = false;
            var now = TimeHelper.NowInMinutes();
            var state = FoodTrucksSession.CurrentState;
            var crewed = new HashSet<string>(state?.Vehicles.Where(Crewed).Select(v => v.VehicleId) ?? Enumerable.Empty<string>());
            foreach (var id in simulatedUntil.Keys.ToList())
                if (!crewed.Contains(id)) simulatedUntil[id] = now;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            // Not counted as walking away at closing: the cart stays open.
            foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen && v.Service != null && !crewed.Contains(v.VehicleId)))
            {
                if (SalesSimulator.Close(vehicle.Service!).Left.Count > 0) FoodTrucksSession.MarkChanged();
            }
        }

        // A crew is at the window now.
        private static bool Crewed(VehicleState vehicle) => HireCrew.OnShift(vehicle, HireCrew.Now()).Count > 0;

        // Called by the item panel's Open button while the player pushes a cart or sits in a stopped truck.
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
            var kind = FoodTrucksMod.KindOf(cart?.vehicleTypeName);
            if (cart == null || kind == null) return;
            if (state == null || !FoodTrucksSession.CanWrite)
            {
                Notify("foodtrucks:notification_data_readonly");
                return;
            }
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking)
            {
                Say("foodtrucks:notification_open_outside", kind);
                return;
            }
            var tables = DemandData.Tables;
            var rules = DemandData.Rules;
            if (tables == null || rules == null)
            {
                Notify("foodtrucks:notification_open_no_data");
                return;
            }
            if (cart.cargoInstances.Any(c => !c.paid))
            {
                Say("foodtrucks:notification_open_unpaid", kind);
                return;
            }
            var menu = CartDemand.MenuOf(tables, cart);
            if (menu.Problem == MenuProblem.TooMuchEquipment)
            {
                Say("foodtrucks:notification_open_too_much_equipment", kind, ("slots", (tables.Vehicle(kind)?.EquipmentSlots ?? 0).ToString()));
                return;
            }
            if (menu.Problem != MenuProblem.None || menu.Menu.Items.Count == 0)
            {
                Log.Info($"Open: {kind} {cart.id} has no usable equipment ({menu.Problem} {menu.Detail}).");
                Say("foodtrucks:notification_open_no_equipment", kind);
                return;
            }
            if (CartDemand.StockOf(cart, menu.Menu).Count == 0)
            {
                Say("foodtrucks:notification_open_no_stock", kind);
                return;
            }

            // The city's rules come before the spot's: the player's vendor licence and the vehicle's permit.
            var vehicle = StreetSales.GetOrAdd(state, cart.id);
            if (string.IsNullOrEmpty(vehicle.VehicleType)) vehicle.VehicleType = kind;
            var crew = HireCrew.OnShift(vehicle, HireCrew.Now()).Count;
            var licensing = Licensing.CanOpen(state, cart.id, SaveGameManager.Current.Day, playerAtWindow: true, hires: crew);
            if (licensing != OpenProblem.None)
            {
                Log.Info($"Open: {kind} {cart.id} refused: {licensing}; licence valid to day {state.Licence.LicenceExpiresOnDay}, permit {vehicle.HasPermit}.");
                Say(LicensingKey(licensing), kind);
                return;
            }

            var problem = PitchProblem(panel, cart);
            if (problem != null)
            {
                Notify(problem);
                return;
            }
            var controller = VehicleHelper.GetCurrentVehicleBase();
            var position = controller != null ? controller.transform.position : PlayerHelper.GetPosition();

            // The game's own Park button: it checks the panel state and refuses where parking is not allowed.
            // For a truck it is the way out of the driver's seat.
            panel.ClickPark();
            if (SaveGameManager.Current.ActiveVehicleId == cart.id)
            {
                Log.Info($"Open: {kind} {cart.id} could not be parked here.");
                Say("foodtrucks:notification_open_cannot_park", kind);
                return;
            }

            var plan = StartSelling(vehicle, cart, controller, position, kind, hireStop: null);
            var service = Service(rules, kind, vehicle, true);
            Say("foodtrucks:notification_open", kind, ("units", plan.StockUnits.ToString()), ("rate", ExpectedSales(plan, service).ToString("F0")));
        }

        // Where the player stands with the vehicle now, as a pitch: null when it could open here, else the
        // message saying why not. A truck needs a legal parking space and pavement by its hatch, a cart a legal
        // pavement spot. Shared by Open and the route's "add a stop here".
        internal static string? PitchProblem(ItemPanelUI panel, VehicleInstance cart)
        {
            var kind = FoodTrucksMod.KindOf(cart.vehicleTypeName) ?? "cart";
            var controller = VehicleHelper.GetCurrentVehicleBase();
            var position = controller != null ? controller.transform.position : PlayerHelper.GetPosition();
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking) return "foodtrucks:notification_open_outside";
            if (kind == "truck")
            {
                // A truck sells from a legal parking space: the game's own check, the one parking fees use.
                var parking = panel.vehicleInfo != null ? panel.vehicleInfo.currentParkingState : ParkingState.NotAvailable;
                Log.Info($"Pitch: truck {cart.id} at {position}: parking {parking}.");
                if (parking != ParkingState.Legal)
                    return parking == ParkingState.NotAvailable ? "foodtrucks:legality_truck_moving" : "foodtrucks:legality_truck_parking";
                // The hatch is on the right: with it towards the road, nobody could reach the window.
                if (controller != null && !QueueLine.HasWindowPlace(controller.transform, kind))
                {
                    Log.Info($"Pitch: truck {cart.id} has no pavement beside its hatch.");
                    return "foodtrucks:legality_truck_hatch";
                }
                return null;
            }
            var legality = Legality.Check(position);
            Log.Info($"Pitch: cart {cart.id} at {position}: {legality.Detail}; road layer below: {(legality.RoadBelow ? "yes" : "no")}.");
            return legality.Legal ? null : legality.RuleKey;
        }

        // The window opens: a fresh queue, the spot read, the hatch up. hireStop is the route's stop when the
        // crew opened it, null when the player did.
        private CartPlan StartSelling(VehicleState vehicle, VehicleInstance cart, VehicleController? controller, Vector3 position, string kind, int? hireStop)
        {
            var tables = DemandData.Tables!;
            var rules = DemandData.Rules!;
            vehicle.IsOpen = true;
            vehicle.HireStop = hireStop;
            vehicle.SaleProgress = 0;
            vehicle.Service = SalesSimulator.Open(Seed(cart.id));
            simulatedUntil[cart.id] = TimeHelper.NowInMinutes();
            pausedNoticeShown.Remove(cart.id);
            sinceOpen[cart.id] = (0, 0);
            lostSinceOpen.Remove(cart.id);
            var spot = World.ReadSpot(tables, position);
            spots[cart.id] = spot;
            TruckHatch.Set(controller, true);
            FoodTrucksSession.MarkChanged();
            var plan = CartDemand.Plan(tables, cart, spot);
            var service = Service(rules, kind, vehicle, hireStop == null);
            Log.Info($"{Title(kind)} {cart.id} open{(hireStop != null ? $" by its crew at stop {hireStop + 1}" : "")} on day {TimeHelper.CurrentDay} at {TimeHelper.CurrentHour:D2}:{(int)TimeHelper.CurrentMinute:D2}: {World.Describe(spot)}; {CartDemand.Describe(plan)} " +
                     $"Window: {service.Servers} serving, customer service skill {service.Skill:F0}, speed {service.Speed:F2}, {plan.CapacityPerServer * service.Speed * Math.Max(1, service.Servers):F0} an hour at most, customers {service.Mood.ToString().ToLowerInvariant()}; queue limit {service.QueueLimit}.");
            return plan;
        }

        // The crew opens the window at a route stop, where the vehicle has just been placed. Null when it
        // opened; otherwise the message saying why not (the route tells the player once a day per stop).
        internal static string? OpenByCrew(VehicleState vehicle, int stop)
        {
            var self = instance;
            var state = FoodTrucksSession.CurrentState;
            var tables = DemandData.Tables;
            var rules = DemandData.Rules;
            var cart = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId);
            var kind = FoodTrucksMod.KindOf(cart?.vehicleTypeName);
            if (self == null || state == null || tables == null || rules == null || cart == null || kind == null || !FoodTrucksSession.CanWrite)
                return "foodtrucks:notification_open_failed";
            if (cart.cargoInstances.Any(c => !c.paid)) return "foodtrucks:notification_open_unpaid";
            var menu = CartDemand.MenuOf(tables, cart);
            if (menu.Problem != MenuProblem.None || menu.Menu.Items.Count == 0) return "foodtrucks:notification_open_no_equipment";
            if (CartDemand.StockOf(cart, menu.Menu).Count == 0) return "foodtrucks:notification_hire_no_stock";
            var crew = HireCrew.OnShift(vehicle, HireCrew.Now()).Count;
            var licensing = Licensing.CanOpen(state, cart.id, SaveGameManager.Current!.Day, playerAtWindow: false, hires: crew);
            if (licensing != OpenProblem.None) return LicensingKey(licensing);
            var controller = VehicleHelper.GetVehicleController(cart);
            if (controller == null) return "foodtrucks:notification_open_failed";
            var position = controller.transform.position;
            if (kind == "cart")
            {
                var legality = Legality.Check(position);
                if (!legality.Legal) return legality.RuleKey;
            }
            else if (!QueueLine.HasWindowPlace(controller.transform, kind)) return "foodtrucks:legality_truck_hatch";
            self.StartSelling(vehicle, cart, controller, position, kind, stop);
            return null;
        }

        // The crew closes the window at the end of a stop's hours.
        internal static void CloseByCrew(VehicleState vehicle)
        {
            if (instance == null) return;
            instance.Close(vehicle, "foodtrucks:notification_hire_closed");
        }

        private static string LicensingKey(OpenProblem problem)
        {
            switch (problem)
            {
                case OpenProblem.NoLicence: return "foodtrucks:notification_open_no_licence";
                case OpenProblem.LicenceExpired: return "foodtrucks:notification_open_licence_expired";
                case OpenProblem.NoPermit: return "foodtrucks:notification_open_no_permit";
                default: return "foodtrucks:notification_open_failed";
            }
        }

        // The window as the player works it: their customer service skill sets the speed (Core: ServiceRules).
        // The player has no satisfaction of their own in the game, so their skill counts in full.
        private readonly struct Window
        {
            public Window(int servers, double skill, double speed, ServiceMood mood, int queueLimit)
            {
                Servers = servers;
                Skill = skill;
                Speed = speed;
                Mood = mood;
                QueueLimit = queueLimit;
            }

            public int Servers { get; }
            public double Skill { get; }
            public double Speed { get; }
            public ServiceMood Mood { get; }
            public int QueueLimit { get; }
        }

        // Everyone at the window: the crew on shift, each at skill × satisfaction / 100, and the player at
        // their own skill when they stand there. They serve at the team's mean speed (ServiceRules.Team).
        private static Window Service(RuleTables rules, string kind, VehicleState vehicle, bool playerAtWindow)
        {
            var skills = HireCrew.OnShift(vehicle, HireCrew.Now()).Select(HireCrew.Effective).ToList();
            if (playerAtWindow) skills.Add(ServiceRules.EffectiveSkill(PlayerSkill(), 100));
            var team = rules.Service.Team(skills);
            return new Window(skills.Count, team.Skill, team.Speed, team.Mood, rules.Service.QueueLimit(kind, rules.Queue.MaxQueue));
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

        // Each opening gets its own customers: the cart and the minute it opened.
        private static long Seed(string vehicleId) => SeededRandom.SeedFor(vehicleId, (long)Math.Floor(TimeHelper.NowInMinutes()));

        // Sales an hour with this window: the customers who buy, up to what the workers can make.
        private static double ExpectedSales(CartPlan plan, Window window) =>
            plan.Estimate == null ? 0 : Math.Min(plan.Estimate.CustomersPerHour * plan.Conversion, plan.CapacityPerServer * window.Speed * Math.Max(1, window.Servers));

        private void OnEnterVehicle(VehicleController controller)
        {
            try
            {
                var cart = controller?.vehicleInstance;
                if (cart == null || FoodTrucksMod.KindOf(cart.vehicleTypeName) == null) return;
                PrefabFixes.ApplyTo(controller);
                // Driving off or grabbing the cart closes the window.
                TruckHatch.Set(controller, false);
                var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, cart.id);
                if (vehicle != null && vehicle.IsOpen && FoodTrucksSession.CanWrite) Close(vehicle, "foodtrucks:notification_closed");
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private void Close(VehicleState vehicle, string messageKey)
        {
            PayHeld(vehicle, null);
            heldFailed.Remove(vehicle.VehicleId);
            vehicle.IsOpen = false;
            vehicle.HireStop = null;
            vehicle.SaleProgress = 0;
            if (vehicle.Service != null) CountLost(vehicle.VehicleId, SalesSimulator.Close(vehicle.Service));
            vehicle.Service = null;
            CartQueueView.Closed(vehicle.VehicleId);
            TruckHatch.Set(ControllerOf(vehicle.VehicleId), false);
            simulatedUntil.Remove(vehicle.VehicleId);
            pausedNoticeShown.Remove(vehicle.VehicleId);
            sinceOpen.TryGetValue(vehicle.VehicleId, out var totals);
            sinceOpen.Remove(vehicle.VehicleId);
            lostSinceOpen.TryGetValue(vehicle.VehicleId, out var lost);
            lostSinceOpen.Remove(vehicle.VehicleId);
            spots.Remove(vehicle.VehicleId);
            FoodTrucksSession.MarkChanged();
            var kind = KindOf(vehicle);
            Log.Info($"{Title(kind)} {vehicle.VehicleId} closed ({messageKey}): {totals.Sales} sales, {Money(totals.Cents)} since opening; " +
                     $"walked away: {Lost(lost, CustomerExit.GaveUp)} tired of waiting, {Lost(lost, CustomerExit.QueueFull)} at a full queue, " +
                     $"{Lost(lost, CustomerExit.NothingLeft)} found nothing left, {Lost(lost, CustomerExit.Closed)} at closing; " +
                     $"lifetime {vehicle.LifetimeSales} sales, {Money(vehicle.LifetimeRevenueCents)}.");
            Say(messageKey, kind, ("sales", totals.Sales.ToString()), ("revenue", Money(totals.Cents)));
        }

        private static string KindOf(VehicleState vehicle) =>
            string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType;

        private static string Title(string kind) => kind == "truck" ? "Truck" : "Cart";

        private static VehicleController? ControllerOf(string vehicleId)
        {
            var found = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicleId);
            return found == null ? null : VehicleHelper.GetVehicleController(found);
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
            instance?.PayAllHeld();
            foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen))
            {
                vehicle.IsOpen = false;
                vehicle.HireStop = null;
                vehicle.SaleProgress = 0;
                vehicle.Service = null;
                TruckHatch.Set(ControllerOf(vehicle.VehicleId), false);
            }
            CartQueueView.ClearAll();
            instance?.simulatedUntil.Clear();
            instance?.sinceOpen.Clear();
            instance?.lostSinceOpen.Clear();
            instance?.spots.Clear();
            instance?.held.Clear();
            instance?.heldFailed.Clear();
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
                // Selling stops; what customers already bought is still paid.
                try { PayAllHeld(); }
                catch (Exception payEx) { Log.Error("Paying held sales: " + payEx); }
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
            // Sleeping and other time skips run the clock without the player at the window; a crew keeps selling.
            var timeMachine = InstanceBehavior<UIs>.Instance?.timeMachine;
            var inSkip = skipping || (timeMachine != null && timeMachine.isRunning);
            if (inSkip && !Crewed(vehicle))
            {
                simulatedUntil[vehicle.VehicleId] = now;
                return;
            }
            if (heldFailed.Remove(vehicle.VehicleId))
            {
                Close(vehicle, "foodtrucks:notification_closed_error");
                return;
            }
            // The licence ran out while open (a new day began).
            if (!Licensing.IsValid(state.Licence, SaveGameManager.Current.Day))
            {
                Close(vehicle, state.Licence.HasVendorLicence ? "foodtrucks:notification_closed_licence" : "foodtrucks:notification_closed_no_licence");
                return;
            }
            // A truck loaded open, or respawned near the player, shows its hatch open.
            TruckHatch.Set(VehicleHelper.GetVehicleController(cart), true);
            // Customers nobody can see any more, or who never reached the window, pay now.
            if (!CartQueueView.Showing(vehicle.VehicleId)) PayHeld(vehicle, null);
            else PayHeld(vehicle, h => now - h.Minute > MaxHeldMinutes);
            var elapsed = (int)Math.Floor(now - from);
            if (elapsed < 1) return;
            simulatedUntil[vehicle.VehicleId] = from + elapsed;
            var tables = DemandData.Tables;
            var rules = DemandData.Rules;
            if (tables == null || rules == null) return;
            var atWindow = !inSkip && PlayerIsAtCart(cart);
            var window = Service(rules, CartDemand.KindOf(cart), vehicle, atWindow);
            if (window.Servers == 0)
            {
                if (pausedNoticeShown.Add(vehicle.VehicleId)) Say("foodtrucks:notification_paused", KindOf(vehicle));
            }
            else pausedNoticeShown.Remove(vehicle.VehicleId);
            var spot = SpotFor(tables, cart);
            if (spot == null) return;
            // Units held for customers on their way to the window are sold already: the plan leaves them out.
            var plan = CartDemand.Plan(tables, cart, spot, Reserved(vehicle.VehicleId));
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
            vehicle.Service ??= SalesSimulator.Open(Seed(cart.id));
            simulator ??= new SalesSimulator(tables, rules.Queue);
            var result = simulator.Advance(vehicle.Service, Input(tables, plan, window.Servers, window), elapsed);
            // Sales the player can't see (a crew elsewhere, or asleep) come without a message each.
            var quiet = !atWindow && (inSkip || !PlayerNear(cart));
            CountLost(vehicle.VehicleId, result);
            var failed = false;
            var showing = CartQueueView.Showing(vehicle.VehicleId);
            // One notification per customer: their main item and the drink that came with it. With the queue
            // in view, the sale waits for that customer to pay at the window.
            foreach (var customer in result.Sales.GroupBy(s => s.CustomerId))
            {
                var items = customer.Select(s => s.Item).ToList();
                if (showing)
                {
                    if (!held.TryGetValue(vehicle.VehicleId, out var list)) held[vehicle.VehicleId] = list = new List<HeldSale>();
                    list.Add(new HeldSale(customer.Key, items, now));
                }
                else if (!ServeCustomer(state, vehicle, cart, plan.Menu.Menu, items, quiet))
                {
                    failed = true;
                    break;
                }
            }
            CartQueueView.Record(vehicle.VehicleId, result, window.Mood);
            // Every simulated minute changes the saved queue (its clock, random state, orders in progress).
            FoodTrucksSession.MarkChanged();
            if (failed) Close(vehicle, "foodtrucks:notification_closed_error");
            else if (result.SoldOut) Close(vehicle, "foodtrucks:notification_sold_out");
        }

        // Called by the queue view when a customer starts paying at the window, or leaves the view unseen.
        public static void Paid(string vehicleId, long customerId)
        {
            var self = instance;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId);
            if (self == null || vehicle == null || !FoodTrucksSession.CanWrite) return;
            try { self.PayHeld(vehicle, h => h.CustomerId == customerId); }
            catch (Exception ex)
            {
                Log.Error("Paying a held sale: " + ex);
                self.heldFailed.Add(vehicleId);
            }
        }

        // Pays every held sale; before saving and time skips, so the save never misses a sale Core made.
        public static void PayAllHeldNow() => instance?.PayAllHeld();

        private void PayAllHeld()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            foreach (var id in held.Keys.ToList())
            {
                var vehicle = StreetSales.Find(state, id);
                if (vehicle != null) PayHeld(vehicle, null);
                else held.Remove(id);
            }
        }

        // Pays the cart's held sales that match (all when `which` is null), in the order Core made them, one
        // at a time: each leaves the list just before it is paid, so a failure or exception costs only that
        // one and a callback during payment can never pay it twice.
        private void PayHeld(VehicleState vehicle, Func<HeldSale, bool>? which)
        {
            if (!held.TryGetValue(vehicle.VehicleId, out var list) || list.Count == 0) return;
            var state = FoodTrucksSession.CurrentState;
            var cart = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId);
            var tables = DemandData.Tables;
            if (state == null || cart == null || tables == null)
            {
                var gone = which == null ? list.Count : list.Count(which);
                if (which == null) held.Remove(vehicle.VehicleId);
                else list.RemoveAll(h => which(h));
                if (gone > 0) Log.Error($"Cart {vehicle.VehicleId}: {gone} held sales dropped, the cart or the data is gone.");
                return;
            }
            var menu = CartDemand.MenuOf(tables, cart).Menu;
            var paid = false;
            while (true)
            {
                var sale = which == null ? list.FirstOrDefault() : list.FirstOrDefault(which);
                if (sale == null) break;
                list.Remove(sale);
                paid = true;
                // A sale that can't be paid (its stock taken out by hand, say) is dropped and logged; the cart
                // closes with the error message on its next tick, as a failed sale always did.
                bool ok;
                try { ok = ServeCustomer(state, vehicle, cart, menu, sale.Items, quiet: false); }
                catch (Exception ex)
                {
                    Log.Error($"Cart {vehicle.VehicleId}: paying held sale for customer {sale.CustomerId}: {ex}");
                    ok = false;
                }
                if (ok) continue;
                Log.Error($"Cart {vehicle.VehicleId}: held sale for customer {sale.CustomerId} ({string.Join(", ", sale.Items)}) not fully paid; dropped.");
                heldFailed.Add(vehicle.VehicleId);
            }
            if (list.Count == 0) held.Remove(vehicle.VehicleId);
            if (paid) FoodTrucksSession.MarkChanged();
        }

        // Units per item held for customers on their way to the window; null when none are.
        private Dictionary<string, int>? Reserved(string vehicleId)
        {
            if (!held.TryGetValue(vehicleId, out var list) || list.Count == 0) return null;
            var reserved = new Dictionary<string, int>();
            foreach (var item in list.SelectMany(h => h.Items))
            {
                reserved.TryGetValue(item, out var units);
                reserved[item] = units + 1;
            }
            return reserved;
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
                AddOnItem = plan.AddOnItem,
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
        private bool ServeCustomer(SaveState state, VehicleState vehicle, VehicleInstance cart, Menu menu, List<string> items, bool quiet)
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
                    Log.Info($"Cart {vehicle.VehicleId}: {item} not sold with the order, none left to take.");
                    break;
                }
                labels.Add(LocalizorManager.GetLocalization(item, null));
                cents += sold;
            }
            if (quiet) return true;
            var left = CartDemand.StockOf(cart, menu).Values.Sum();
            Notify("foodtrucks:notification_sale", ("item", string.Join(" + ", labels)), ("price", Money(cents)), ("units", left.ToString()));
            return true;
        }

        // Close enough to see the window: the queue view's range, outdoors.
        private static bool PlayerNear(VehicleInstance cart)
        {
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking) return false;
            var controller = VehicleHelper.GetVehicleController(cart);
            return controller != null && Vector3.Distance(PlayerHelper.GetPosition(), controller.transform.position) <= 100f;
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

        // A message about one vehicle: {vehicle} in the text is "cart" or "truck".
        internal static void Say(string key, string kind, params (string Key, string Value)[] data) =>
            Notify(key, data.Concat(new[] { ("vehicle", LocalizorManager.GetLocalization("foodtrucks:noun_" + kind, null)) }).ToArray());

        internal static void Notify(string key, params (string Key, string Value)[] data)
        {
            var values = new Dictionary<string, string>();
            foreach (var (k, v) in data) values[k] = v;
            Notifications.Show(NotificationType.Info, key, values, 5f, notificationSound: false, trackOnSaveGame: false);
        }
    }
}
