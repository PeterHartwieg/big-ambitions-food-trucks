#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Characters.Skills;
using BigAmbitions.DayNightCycle;
using BigAmbitions.Items;
using Entities;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Pitches;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using Localizor;
using Parking.UndergroundParking;
using UI;
using UI.Notification;
using UnityEngine;

namespace FoodTrucks
{
    // Selling: "Open for business" in the parked vehicle's on-foot menu opens the cart where it stands (or the truck in
    // a legal parking space), if the player has a vendor licence, the vehicle a permit, and the fitted equipment gives a menu. While it is open, Core's SalesSimulator runs every in-game minute: customers arrive at the rate
    // the demand model gives for the spot and the hour (CartDemand), queue at the window and give up after
    // a while. The player serves at the speed of their customer service skill while they hold a cart serving stance or
    // stand at a truck's hatch; otherwise only a crew serves, and without one the queue waits and thins out. Each customer buys one main
    // item and sometimes a cold drink; each item takes one unit of cargo and pays the cart's price with
    // the mod's label. CartQueueView shows the queue as people near the player. Grabbing the cart closes it.
    internal sealed class CartSelling : MonoBehaviour
    {
        // The player works a cart's window, so its sales pause when they walk further away than this. A truck's
        // window is worked only from its hatch (TruckWindow), however close the player stands outside.
        private const float WorkingDistance = 8f;

        // Real-time throttle for the tick; sales themselves follow in-game minutes.
        private const float TickSeconds = 0.25f;

        // In-game minute up to which each open cart has been simulated. Not saved: after a load the
        // first tick starts counting again, so time spent in the menu never turns into sales.
        private readonly Dictionary<string, double> simulatedUntil = new Dictionary<string, double>();
        private readonly HashSet<string> pausedNoticeShown = new HashSet<string>();
        private readonly Dictionary<string, (int Sales, long Cents)> sinceOpen = new Dictionary<string, (int, long)>();
        // The crew's sales in the step being sold, per vehicle: one cargo update per step (FlushCrewSales).
        private readonly Dictionary<string, (int Customers, int Items, long Cents)> crewBatch = new Dictionary<string, (int, int, long)>();
        // The crew's sales of the game hour being sold, per vehicle, for one log line an hour.
        private readonly Dictionary<string, (long Hour, int Customers, int Items, long Cents)> crewHour = new Dictionary<string, (long, int, int, long)>();
        // Customers since opening who left without buying, by reason, for the close log line.
        private readonly Dictionary<string, Dictionary<CustomerExit, int>> lostSinceOpen = new Dictionary<string, Dictionary<CustomerExit, int>>();
        // The spot each open cart stands on, read at opening and again each new day.
        private readonly Dictionary<string, Spot> spots = new Dictionary<string, Spot>();
        // The pitch an open vehicle stands on, fixed when it opened; null when closed.
        // The earliest game minute an open window still has to sell from, or a route still has to run from (it may
        // open a window there); now when none lags behind.
        internal static long SoldThrough()
        {
            var now = HireCrew.Now();
            if (HireRoutes.RoutedThrough() is { } routed && routed < now) now = routed;
            var self = instance;
            var state = FoodTrucksSession.CurrentState;
            if (self == null || state == null) return now;
            foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen))
                if (self.simulatedUntil.TryGetValue(vehicle.VehicleId, out var until) && until < now) now = (long)Math.Floor(until);
            return now;
        }

        // Where an open window's spot was last read, null when closed.
        internal static Spot? SpotOf(string vehicleId) =>
            instance != null && instance.spots.TryGetValue(vehicleId, out var spot) ? spot : null;

        // How far an open window has sold, null when closed or not started.
        internal static double? SoldUntil(string vehicleId) =>
            instance != null && instance.simulatedUntil.TryGetValue(vehicleId, out var until) ? until : (double?)null;

        internal static string? PitchOf(string vehicleId) =>
            StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId)?.OpenPitch;

        // Sales Core has made whose customer is still at (or on the way to) the window in the visible queue, per
        // cart. They are booked when that customer picks up and turns to leave (CartQueueView calls Paid).
        // The money waits for midnight. Their units already count as gone for Core.
        private readonly Dictionary<string, List<HeldSale>> held = new Dictionary<string, List<HeldSale>>();
        // Carts where paying a held sale failed; the next tick closes them.
        private readonly HashSet<string> heldFailed = new HashSet<string>();
        private SalesSimulator? simulator;
        private float nextTick;
        private readonly CartServing cartServing = new CartServing();

        // A held sale is paid anyway after this many in-game minutes, whatever the view does.
        private const double MaxHeldMinutes = 60;

        private sealed class HeldSale
        {
            public HeldSale(long customerId, List<string> items, double minute, bool inPerson)
            {
                CustomerId = customerId;
                Items = items;
                Minute = minute;
                InPerson = inPerson;
            }

            public long CustomerId { get; }
            public List<string> Items { get; }
            // The game minute Core sold it at: its day is the day it is booked on, whenever it is paid (GameDays.SaleDay).
            public double Minute { get; }
            // Sold with the player at the window: per-sale log and cargo feedback; money waits for midnight either way.
            public bool InPerson { get; }
        }
        // From the time machine's start event to its end event (sleep, skips, paid activities).
        // Its component turns off a moment before the end event, so isRunning alone misses the last minutes.
        private bool skipping;

        private static CartSelling? instance;

        // A time skip is running (sleep, paid activities): the crew's routes then step every frame.
        internal static bool Skipping => instance != null && instance.skipping ||
                                         (InstanceBehavior<UIs>.Instance?.timeMachine?.isRunning ?? false);

        private void OnEnable()
        {
            instance = this;
            GlobalEvents.onEnterVehicle += OnEnterVehicle;
            GlobalEvents.onTimeMachineStarted += OnTimeMachineStarted;
            GlobalEvents.onTimeMachineEnded += OnTimeMachineEnded;
        }

        private void OnDisable()
        {
            ServingWork.Reset();
            cartServing.Release("session ended");
            GlobalEvents.onEnterVehicle -= OnEnterVehicle;
            GlobalEvents.onTimeMachineStarted -= OnTimeMachineStarted;
            GlobalEvents.onTimeMachineEnded -= OnTimeMachineEnded;
            if (instance == this) instance = null;
        }

        private void OnTimeMachineStarted()
        {
            skipping = true;
            ServingWork.SkipStarted();
            if (cartServing.VehicleId is { } servingId && !ServingWork.IsSkipping(servingId)) cartServing.Release("time skip");
            CartQueueView.Suspended = true;
            try
            {
                PayAllHeld();
                CartQueueView.ClearAll();
            }
            catch (Exception ex) { Log.Error("Time skip start: " + ex); }
        }

        // Unrelated skipped time is not player working time: those windows restart from now. A selected work
        // skip and crew windows retain their clocks and queues, having sold through the elapsed minutes.
        private void OnTimeMachineEnded()
        {
            CatchUpTo(HireCrew.NowExact());
            skipping = false;
            CartQueueView.Suspended = false;
            try { ResumeAfterSkip(); }
            catch (Exception ex) { Log.Error("Time skip end: " + ex); }
            ServingWork.SkipEnded();
        }

        private void ResumeAfterSkip()
        {
            var now = HireCrew.NowExact();
            var state = FoodTrucksSession.CurrentState;
            var crewed = new HashSet<string>(state?.Vehicles.Where(v => RunByRoute(v) || Crewed(v)).Select(v => v.VehicleId) ?? Enumerable.Empty<string>());
            foreach (var id in simulatedUntil.Keys.ToList())
                if (!crewed.Contains(id) && !ServingWork.WasWorkSkip(id)) simulatedUntil[id] = SalesClock.Start(now);
            if (state == null || !FoodTrucksSession.CanWrite) return;
            // Not counted as walking away at closing: the cart stays open.
            foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen && v.Service != null && !crewed.Contains(v.VehicleId) && !ServingWork.WasWorkSkip(v.VehicleId)))
            {
                if (SalesSimulator.Close(vehicle.Service!).Left.Count > 0) FoodTrucksSession.MarkChanged();
            }
        }

        // A crew is at the window now.
        private static bool Crewed(VehicleState vehicle) => HireCrew.OnShift(vehicle, HireCrew.Now()).Count > 0;

        // A window the crew opened is run by HireRoutes, step by step through time, so its sales stop at the
        // stop's closing time even across a time skip.
        private static bool RunByRoute(VehicleState vehicle) => vehicle.HireStop != null;

        // The on-foot menu's "Open for business" (OverlayButtons) and the test bridge's open: the vehicle stands parked,
        // the player is on foot. The vehicle's own saved parking state and position decide, not the item panel.
        public static void OpenHere(VehicleController controller)
        {
            try { instance?.Open(controller); }
            catch (Exception ex)
            {
                Log.Error("Open failed: " + ex);
                Notify("foodtrucks:notification_open_failed");
            }
        }

        // Whether the on-foot menu offers "Open for business" for this vehicle: a food vehicle, closed and not on the
        // road with its crew, the player on foot. The checks themselves come with the click (Open).
        internal static bool CanOfferOpen(VehicleInstance? cart)
        {
            if (cart == null || FoodTrucksMod.KindOf(cart.vehicleTypeName) == null) return false;
            if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId)) return false;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, cart.id);
            return vehicle == null || (!vehicle.IsOpen && vehicle.Transit == null);
        }

        // Whether the on-foot menu offers "Close": an open food vehicle, the player on foot and not at its hatch (there
        // the hatch panel closes it).
        internal static bool CanOfferClose(VehicleInstance? cart)
        {
            if (cart == null || FoodTrucksMod.KindOf(cart.vehicleTypeName) == null) return false;
            if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId) || TruckWindow.PlayerAt(cart.id)) return false;
            return StreetSales.Find(FoodTrucksSession.CurrentState, cart.id)?.IsOpen == true;
        }

        internal static bool CanWorkCart(VehicleInstance? cart) => cart != null && CartDemand.KindOf(cart) == "cart" &&
            instance != null && !instance.cartServing.PlayerAt(cart.id) && !PlayerActivity.PlayerActivityUI.IsPanelOpen &&
            string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId) && !Skipping &&
            StreetSales.Find(FoodTrucksSession.CurrentState, cart.id)?.IsOpen == true;

        internal static void WorkCart(VehicleController controller)
        {
            if (CanWorkCart(controller.vehicleInstance)) instance!.cartServing.Open(controller);
        }

        private void Open(VehicleController controller)
        {
            var state = FoodTrucksSession.CurrentState;
            var cart = controller.vehicleInstance;
            var kind = FoodTrucksMod.KindOf(cart?.vehicleTypeName);
            if (cart == null || kind == null) return;
            if (!string.IsNullOrEmpty(SaveGameManager.Current.ActiveVehicleId))
            {
                Log.Info($"Open: {kind} {cart.id} refused: the player is in a vehicle ({SaveGameManager.Current.ActiveVehicleId}).");
                return;
            }
            if (state == null || !FoodTrucksSession.CanWrite)
            {
                Notify("foodtrucks:notification_data_readonly");
                return;
            }
            if (StreetSales.Find(state, cart.id) is { } already && (already.IsOpen || already.Transit != null))
            {
                Log.Info($"Open: {kind} {cart.id} refused: {(already.IsOpen ? "open already" : "on the road with its crew")}.");
                return;
            }
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking || !string.IsNullOrEmpty(cart.streetName))
            {
                Log.Info($"Open: {kind} {cart.id} refused: {(string.IsNullOrEmpty(cart.streetName) ? "the player is indoors" : "the vehicle is in a building or garage")}.");
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

            var problem = PitchProblem(cart, controller);
            if (problem != null)
            {
                Notify(problem);
                return;
            }
            var position = controller.transform.position;
            // A running city event: only vehicles with a booked place sell there.
            var eventProblem = CityPitches.EventProblem(state, cart.id, position, HireCrew.Now());
            if (eventProblem != null)
            {
                Say(eventProblem, kind);
                return;
            }

            // Where the crew parked it stops counting as at its stop, as when the player takes the vehicle.
            HireRoutes.PlayerTook(vehicle);
            var (plan, pitch) = StartSelling(vehicle, cart, controller, position, kind, hireStop: null);
            // No notice for opening (Peter, blind run 3): the open window shows it, and how busy a place is the player
            // learns by watching. The log keeps the line.
            var serving = HireCrew.Serving(vehicle, HireCrew.Now());
            Log.Info($"Open: {kind} {cart.id} opened by the player with {plan.StockUnits} items; serving: {(serving.Count == 0 ? "the player" : HireCrew.Names(serving))}.");
            if (pitch != null) Say(pitch.Value.Key, kind, pitch.Value.Data);
            cartServing.Release("another window opened");
            // To the hatch: the player serves from inside the truck (placed there when beside it, else walked to its rear door).
            if (kind == "truck") TruckWindow.EnterFromOutside(controller);
            else cartServing.Open(controller);
        }

        // Where a parked vehicle stands now, as a pitch: null when it could open here, else the message saying why not. A
        // truck needs a legal parking space by its saved parking state (the game sets it on parking; the crew's opening
        // reads it too) and pavement by its hatch, a cart a legal pavement spot. Shared by Open and the vehicle page's "Add
        // the spot where it stands". The player's own whereabouts are the caller's to check.
        internal static string? PitchProblem(VehicleInstance cart, VehicleController? controller)
        {
            var kind = FoodTrucksMod.KindOf(cart.vehicleTypeName) ?? "cart";
            controller ??= VehicleHelper.GetVehicleController(cart);
            var position = controller != null ? controller.transform.position : (Vector3)cart.position;
            if (!string.IsNullOrEmpty(cart.streetName))
            {
                Log.Info($"Pitch: {kind} {cart.id}: in a building or garage ({cart.streetName}).");
                return "foodtrucks:notification_open_outside";
            }
            if (SaveGameManager.Current?.ActiveVehicleId == cart.id)
            {
                Log.Info($"Pitch: {kind} {cart.id} at {position}: the player is driving or pushing it.");
                return "foodtrucks:legality_truck_moving";
            }
            if (kind == "truck")
            {
                // A truck sells from a legal parking space: the game's own check, the one parking fees use.
                var parking = cart.parkingState;
                if (parking != ParkingState.Legal)
                {
                    Log.Info($"Pitch: truck {cart.id} at {position}: saved parking state {parking}.");
                    return parking == ParkingState.NotAvailable ? "foodtrucks:legality_truck_moving" : "foodtrucks:legality_truck_parking";
                }
                // The hatch is on the right: with it towards the road, the line would form in the traffic.
                if (controller == null)
                {
                    Log.Info($"Pitch: truck {cart.id} at {position}: parking {parking}; hatch not checked (no vehicle controller).");
                    return null;
                }
                var truck = controller.transform;
                var hatch = HatchCheck.Probe(truck);
                Log.Info($"Pitch: truck {cart.id} at {truck.position}, heading {truck.eulerAngles.y:F0}: parking {parking}; " +
                         $"hatch {hatch}; {(hatch.Opens ? "may open" : "refused")}.");
                return hatch.RefusalKey;
            }
            var legality = Legality.Check(position);
            Log.Info($"Pitch: cart {cart.id} at {position}: {legality.Detail}; road layer below: {(legality.RoadBelow ? "yes" : "no")}.");
            return legality.Legal ? null : legality.RuleKey;
        }

        // The window opens: a fresh queue, the spot read, the hatch up. hireStop is the route's stop when the
        // crew opened it, null when the player did.
        private (CartPlan Plan, (string Key, (string Key, string Value)[] Data)? Pitch) StartSelling(VehicleState vehicle, VehicleInstance cart,
            VehicleController? controller, Vector3 position, string kind, int? hireStop, double? openedAt = null)
        {
            var tables = DemandData.Tables!;
            var rules = DemandData.Rules!;
            vehicle.IsOpen = true;
            vehicle.HireStop = hireStop;
            vehicle.SaleProgress = 0;
            vehicle.Service = SalesSimulator.Open(Seed(cart.id));
            // On a whole game minute, so its steps land on midnight exactly (SalesClock).
            simulatedUntil[cart.id] = SalesClock.Start(openedAt ?? HireCrew.NowExact());
            pausedNoticeShown.Remove(cart.id);
            sinceOpen[cart.id] = (0, 0);
            lostSinceOpen.Remove(cart.id);
            var state = FoodTrucksSession.CurrentState!;
            var spot = World.ReadSpot(tables, position, partnerVenue: Partners.VenueOf(cart.id));
            spots[cart.id] = spot;
            // The premium spot and a running event's place go to the first vehicle there.
            var pitch = CityPitches.Opened(state, vehicle, position, (long)Math.Floor(openedAt ?? HireCrew.NowExact()));
            TruckHatch.Set(controller, true);
            Inspect(vehicle, kind, (long)Math.Floor(openedAt ?? HireCrew.NowExact()));
            // The pitch is fixed for this opening, so its minutes stay together.
            vehicle.OpenPitch = World.PitchKey(FoodTrucksSession.CurrentState, spot.X, spot.Z);
            FoodTrucksSession.MarkChanged();
            var plan = CartDemand.Plan(tables, cart, spot);
            var service = Service(rules, kind, vehicle, hireStop == null);
            Log.Info($"{Title(kind)} {cart.id} open{(hireStop != null ? $" by its crew at stop {hireStop + 1}" : "")} on day {TimeHelper.CurrentDay} at {TimeHelper.CurrentHour:D2}:{(int)TimeHelper.CurrentMinute:D2}: {World.Describe(spot)}; {CartDemand.Describe(plan)} " +
                     $"Window: {service.Servers} serving, customer service skill {service.Skill:F0}, speed {service.Speed:F2}, {plan.CapacityPerServer * service.Speed * Math.Max(1, service.Servers):F0} an hour at most, customers {service.Mood.ToString().ToLowerInvariant()}; queue limit {service.QueueLimit}. " +
                     $"Reputation: {Reputation(tables, cart, spot, vehicle, plan)}; pitch {vehicle.OpenPitch}" +
                     $"{(vehicle.PremiumSpot != null ? $", premium spot {vehicle.PremiumSpot}" : "")}{(Partners.VenueOf(cart.id) is { } partner ? $", partner {partner}" : "")}.");
            return (plan, pitch);
        }

        private static string Reputation(DemandTables tables, VehicleInstance cart, Spot spot, VehicleState vehicle, CartPlan plan)
        {
            var (parts, shares) = CartDemand.Reputation(tables, cart, spot, vehicle, plan.Stock.Keys.ToList());
            var split = shares == null || shares.Count == 0 || shares.Values.All(v => v >= 1) ? "" :
                "; customers left by neighbours " + string.Join(", ", shares.Select(p => $"{p.Key} {p.Value:P0}"));
            return parts + split;
        }

        // The vehicle opens, or works into a new day: the inspector may come by (design: Health grades).
        // A catering gig is a day the truck served food: it counts as opened for health, inspector included.
        internal static void ServedAt(VehicleState vehicle, string kind, long minute) => Inspect(vehicle, kind, minute);

        private static void Inspect(VehicleState vehicle, string kind, long minute, bool opening = true)
        {
            var book = CartDemand.Book();
            var state = FoodTrucksSession.CurrentState;
            if (book == null || state == null) return;
            var day = ServiceDay(minute);
            if (day <= ReputationBook.ClosedThrough(state)) return;
            var result = book.Opened(vehicle, day, SeededRandom.SeedFor(vehicle.VehicleId, day), opening);
            if (result == null) return;
            Log.Info($"Health inspection: {kind} {vehicle.VehicleId} on day {day}: grade {result.Value.Grade}, {result.Value.Points} points.");
            Say("foodtrucks:notification_inspection", kind, ("grade", result.Value.Grade.ToString()), ("points", result.Value.Points.ToString()));
        }

        internal static string GradeText(VehicleState? vehicle) =>
            vehicle?.Health == null || vehicle.Health.Grade == Core.Reputation.Grade.Ungraded
                ? LocalizorManager.GetLocalization("foodtrucks:grade_pending", null)
                : string.Format(LocalizorManager.GetLocalization("foodtrucks:grade_letter", null), vehicle.Health.Grade);

        // The working day this minute belongs to: the night counts for the evening before (until the demand
        // model's night ends, 05:00), so a late stop is one working day.
        internal static int ServiceDay(long absoluteMinute) =>
            ReputationBook.ServiceDay(absoluteMinute, DemandData.Tables?.NightEndsMinute ?? 5 * 60);

        internal static int ServiceDayNow() => ServiceDay(HireCrew.Now());

        // After every Update of the frame (sales and routes have caught up to now), when the hour has changed
        // and once after a load: finished working days go into health marks and the fleet's regulars, and a
        // vehicle open into a new working day gets that day's inspector's chance.
        private void LateUpdate()
        {
            cartServing.LateUpdate();
            ServingWork.UpdateCart(cartServing);
            if (FoodTrucksSession.CurrentState == null || SaveGameManager.Current == null) return;
            var hour = HireCrew.Now() / 60;
            if (hour == closedAtHour) return;
            closedAtHour = hour;
            try { CloseDays(); }
            catch (Exception ex) { Log.Error("Reputation, closing days: " + ex); }
        }

        private long closedAtHour = -1;

        private static void CloseDays()
        {
            var state = FoodTrucksSession.CurrentState;
            var book = CartDemand.Book();
            if (state == null || book == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            var today = ServiceDayNow();
            var before = state.Vehicles.Sum(v => v.Days?.Count ?? 0);
            var lastCounted = state.Regulars.LastUpdatedDay;
            // A working day is counted once the next one is over too, so a late shift can still be cleaned
            // up the morning after and a window open past 05:00 finishes its day first.
            var dropped = book.CloseDaysBefore(state, today - 1);
            var after = state.Vehicles.Sum(v => v.Days?.Count ?? 0);
            if (dropped > 0) Log.Error($"Reputation: {dropped} visits came after their day was counted; dropped.");
            if (before != after || lastCounted != state.Regulars.LastUpdatedDay)
            {
                FoodTrucksSession.MarkChanged();
                Log.Info($"Reputation through day {today - 2}: {before - after} vehicle days counted; regulars at {state.Regulars.Entries.Count} pitches and weekdays; " +
                         string.Join(", ", state.Vehicles.Where(v => v.Health != null).Select(v => $"{v.VehicleId} {v.Health!.Grade} ({book.Points(v)} points)")) + ".");
            }
            // Open through the night into a new working day: that day counts as opened, inspector included.
            foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen && (v.Health == null || (v.Health.LastDrawDay ?? 0) < today)))
                Inspect(vehicle, string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType, HireCrew.Now(), opening: false);
        }

        // The crew opens the window at a route stop, where the vehicle has just been placed. Null when it
        // opened; otherwise the message saying why not (the route tells the player once a day per stop).
        internal static string? OpenByCrew(VehicleState vehicle, int stop, long minute)
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
            var crew = HireCrew.OnShift(vehicle, minute).Count;
            // By the day of the opening, which lags the live day in a catch-up.
            var licensing = Licensing.CanOpen(state, cart.id, (int)(minute / 1440) + 1, playerAtWindow: false, hires: crew);
            if (licensing != OpenProblem.None) return LicensingKey(licensing);
            var controller = VehicleHelper.GetVehicleController(cart);
            if (controller == null) return "foodtrucks:notification_open_failed";
            var position = controller.transform.position;
            if (kind == "cart")
            {
                var legality = Legality.Check(position);
                if (!legality.Legal) return legality.RuleKey;
            }
            else if (cart.parkingState != ParkingState.Legal) return "foodtrucks:legality_truck_parking";
            else
            {
                // The route parks its truck with the hatch to the pavement and moves one that stands the wrong
                // way (HireRoutes.Where), so a refusal here is mostly a stop with no pavement at all. The route
                // logs the reading with its refusal, once a day.
                var hatch = HireRoutes.Hatch(vehicle.VehicleId, controller.transform);
                if (!hatch.Opens) return hatch.RefusalKey;
            }
            var eventProblem = CityPitches.EventProblem(state, cart.id, position, minute);
            if (eventProblem != null) return eventProblem;
            self.StartSelling(vehicle, cart, controller, position, kind, stop, minute);
            return null;
        }

        // The crew closes the window at the end of a stop's hours, after selling up to that minute.
        internal static void CloseByCrew(VehicleState vehicle, long minute)
        {
            if (instance == null) return;
            AdvanceTo(vehicle, minute);
            if (!vehicle.IsOpen) return;
            instance.Close(vehicle, "foodtrucks:notification_hire_closed", minute);
        }

        // Sells every window the player opened up to that game minute, exactly as the frames would have: called inside
        // the game's tick before crew pay is decided for the hour that just ended, so a sell-out or a closing before it
        // counts. Windows the crew opened are the route's to catch up.
        internal static void CatchUpTo(double minute)
        {
            var self = instance;
            var state = FoodTrucksSession.CurrentState;
            if (self == null || state == null || !FoodTrucksSession.CanWrite || !self.enabled) return;
            try
            {
                foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen && !RunByRoute(v)).ToList())
                    self.Advance(state, vehicle, minute, int.MaxValue);
            }
            catch (Exception ex) { Log.Error("Sales catch-up before crew pay: " + ex); }
        }

        // The crew who serve at the window now (the player at the window may take a place); all on shift while closed.
        internal static List<EmployeeInstance> ServingNow(VehicleState vehicle, string kind)
        {
            var now = HireCrew.Now();
            if (!vehicle.IsOpen) return HireCrew.OnShift(vehicle, now);
            var cart = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId);
            var atWindow = cart != null && (!Skipping || ServingWork.IsSkipping(cart.id)) && PlayerIsAtCart(cart);
            return HireCrew.Team(vehicle, now, atWindow ? ServiceRules.EffectiveSkill(PlayerSkill(), 100) : (double?)null, kind).Crew;
        }

        // Sells at a window the crew runs up to that game minute, in steps the simulator takes at once.
        internal static void AdvanceTo(VehicleState vehicle, double minute)
        {
            var self = instance;
            var state = FoodTrucksSession.CurrentState;
            if (self == null || state == null || !FoodTrucksSession.CanWrite || !vehicle.IsOpen) return;
            // The route acts (closes, leaves) right after this, so the window must be sold up to `minute` first,
            // however many steps that takes.
            self.Advance(state, vehicle, minute, int.MaxValue);
        }

        // Sells up to that game minute one game minute at a time, so every minute gets its own demand, venue
        // weights and surges, as the live clock would give it. A live tick is about a minute anyway; only a
        // catch-up after a skip takes several steps. A window the player runs takes at most maxSteps a frame and
        // the rest next frame; a crew's window is caught up in full before its route acts (AdvanceTo).
        private void Advance(SaveState state, VehicleState vehicle, double until, int maxSteps = 200)
        {
            for (var i = 0; i < maxSteps && vehicle.IsOpen; i++)
            {
                if (!simulatedUntil.TryGetValue(vehicle.VehicleId, out var from))
                {
                    simulatedUntil[vehicle.VehicleId] = SalesClock.Start(until);
                    return;
                }
                if (!(SalesClock.NextStep(from, until, StepMinutes) is { } end)) return;
                Tick(state, vehicle, end);
                // A tick that left the clock where it was (paused, or closed) ends the catch-up.
                if (!simulatedUntil.TryGetValue(vehicle.VehicleId, out var after) || after <= from) return;
            }
        }

        private const double StepMinutes = 1;

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
            public Window(int servers, double skill, double speed, ServiceMood mood, int queueLimit, List<EmployeeInstance> crew)
            {
                Crew = crew;
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
            // The crew among those serving (the player may take a place): they alone are paid for serving.
            public List<EmployeeInstance> Crew { get; }
        }

        // Everyone at the window: the crew on shift, each at skill × satisfaction / 100, and the player at
        // their own skill when they stand there. They serve at the team's mean speed (ServiceRules.Team).
        private static Window Service(RuleTables rules, string kind, VehicleState vehicle, bool playerAtWindow, long? minute = null)
        {
            // No more than the vehicle's staff fit at the window: the best of them serve.
            var (crew, skills) = HireCrew.Team(vehicle, minute ?? HireCrew.Now(), playerAtWindow ? ServiceRules.EffectiveSkill(PlayerSkill(), 100) : (double?)null, kind);
            var team = rules.Service.Team(skills);
            return new Window(skills.Count, team.Skill, team.Speed, team.Mood, rules.Service.QueueLimit(kind, rules.Queue.MaxQueue), crew);
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
        private static long Seed(string vehicleId) => SeededRandom.SeedFor(vehicleId, (long)Math.Floor(HireCrew.NowExact()));

        private void OnEnterVehicle(VehicleController controller)
        {
            cartServing.Release("entered a vehicle");
            try
            {
                var cart = controller?.vehicleInstance;
                if (cart == null || FoodTrucksMod.KindOf(cart.vehicleTypeName) == null) return;
                PrefabFixes.ApplyTo(controller);
                // Driving off or grabbing the cart closes the window.
                TruckHatch.Set(controller, false);
                var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, cart.id);
                // Where the crew parked it stops counting as at its stop, whether or not it has a crew or a route now.
                if (vehicle != null) HireRoutes.PlayerTook(vehicle);
                if (vehicle != null && vehicle.IsOpen && FoodTrucksSession.CanWrite) Close(vehicle, "foodtrucks:notification_closed");
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        // The player closes the window: from the truck's hatch, or with Close in the vehicle's on-foot menu.
        internal static void CloseByPlayer(string vehicleId)
        {
            ServingWork.BeforeLeaving(vehicleId);
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId);
            if (instance == null || vehicle == null || !vehicle.IsOpen || !FoodTrucksSession.CanWrite)
            {
                Log.Info($"Close: {vehicleId} not closed: {(instance == null ? "no runtime" : vehicle == null || !vehicle.IsOpen ? "not open" : "the mod's data is read-only")}.");
                return;
            }
            instance.Close(vehicle, "foodtrucks:notification_closed");
        }

        // at: the game minute the window closes, when the sales clock is catching up behind the live one.
        private void Close(VehicleState vehicle, string messageKey, double? at = null)
        {
            ServingWork.WindowClosed(vehicle.VehicleId);
            cartServing.Closed(vehicle.VehicleId);
            // Closing doesn't clean, for the crew either: cleaning happens only in the vehicle's own commissary bay (the
            // on-foot menu's Clean, or standing there at the 04:00 check).
            var state = FoodTrucksSession.CurrentState;
            var closedAt = (long)Math.Floor(at ?? HireCrew.NowExact());
            var crewWindow = vehicle.HireStop != null || HireCrew.OnShift(vehicle, closedAt).Count > 0;
            vehicle.OpenPitch = null;
            if (state != null)
            {
                PremiumSpots.Release(state, vehicle, closedAt);
                CityEvents.Release(state.Events, vehicle.VehicleId, closedAt);
            }
            // A crew window closed by a failing sale stays closed for the rest of that opening.
            if (vehicle.HireStop != null && messageKey == "foodtrucks:notification_closed_error")
                HireRunner.GiveUp(vehicle, vehicle.HireStop.Value, (long)Math.Floor(at ?? HireCrew.NowExact()));
            PayHeld(vehicle, null);
            LogCrewHour(vehicle.VehicleId, SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId));
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
            // A crew's routine close (the stop's hours over, an event taking the place) far from the player or during a skip
            // goes in the log only, like its sales: a night slept through would otherwise end in a pile of closing notices.
            // A close the player has to act on (sold out, the licence ended, the vehicle gone, an error) always shows.
            var closedCart = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId);
            if (crewWindow && RoutineClose(messageKey) && (Skipping || closedCart == null || !PlayerNear(closedCart)))
            {
                Log.Info($"{Title(kind)} {vehicle.VehicleId}: closing notice not shown (the crew's window, the player away or asleep).");
                return;
            }
            Say(messageKey, kind, ("sales", totals.Sales.ToString()), ("revenue", Money(totals.Cents)));
        }

        private static bool RoutineClose(string messageKey) =>
            messageKey == "foodtrucks:notification_closed" || messageKey == "foodtrucks:notification_hire_closed" ||
            messageKey == "foodtrucks:notification_event_closed";

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
            instance?.crewBatch.Clear();
            instance?.crewHour.Clear();
            instance?.lostSinceOpen.Clear();
            instance?.spots.Clear();
            instance?.held.Clear();
            instance?.heldFailed.Clear();
            FoodTrucksSession.MarkChanged();
        }

        private void Update()
        {
            // During a skip every frame counts, so a crew's sales follow the clock a frame at a time.
            if (Time.unscaledTime < nextTick && !Skipping && !ServingWork.Accelerated) return;
            nextTick = Time.unscaledTime + TickSeconds;
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            try
            {
                TrackStock(state);
                foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen && !RunByRoute(v)).ToList())
                    Advance(state, vehicle, HireCrew.NowExact());
                // Midnight pays the day's sales in (SettleClosingDay); retry leftovers and loaded takings here.
                // A sold/deleted vehicle is paid back now, including today's sales.
                PayTakings(state, all: false);
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

        private void Tick(SaveState state, VehicleState vehicle, double until)
        {
            var cart = SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId);
            if (cart == null)
            {
                // Sold, deleted or lost: nothing left to sell from.
                Close(vehicle, "foodtrucks:notification_closed_missing");
                return;
            }

            var now = until;
            if (!simulatedUntil.TryGetValue(vehicle.VehicleId, out var from))
            {
                simulatedUntil[vehicle.VehicleId] = SalesClock.Start(now);
                return;
            }
            // Sleeping and other time skips run the clock without the player at the window; a crew keeps selling.
            var timeMachine = InstanceBehavior<UIs>.Instance?.timeMachine;
            var inSkip = skipping || (timeMachine != null && timeMachine.isRunning);
            if (inSkip && !ServingWork.IsSkipping(vehicle.VehicleId) && !RunByRoute(vehicle) && !Crewed(vehicle))
            {
                simulatedUntil[vehicle.VehicleId] = SalesClock.Start(now);
                return;
            }
            if (heldFailed.Remove(vehicle.VehicleId))
            {
                Close(vehicle, "foodtrucks:notification_closed_error", from);
                return;
            }
            // The licence ran out while open (a new day began), by the day being sold, which lags in a catch-up.
            if (!Licensing.IsValid(state.Licence, (int)((long)Math.Floor(from) / 1440) + 1))
            {
                Close(vehicle, state.Licence.HasVendorLicence ? "foodtrucks:notification_closed_licence" : "foodtrucks:notification_closed_no_licence", from);
                return;
            }
            // A truck loaded open, or respawned near the player, shows its hatch open.
            TruckHatch.Set(VehicleHelper.GetVehicleController(cart), true);
            // Customers nobody can see any more, or who never reached the window, pay now.
            if (ServingWork.Accelerated || inSkip || !CartQueueView.Showing(vehicle.VehicleId)) PayHeld(vehicle, null);
            else PayHeld(vehicle, h => now - h.Minute > MaxHeldMinutes);
            var elapsed = SalesClock.Elapsed(from, now);
            if (elapsed < 1) return;
            simulatedUntil[vehicle.VehicleId] = from + elapsed;
            var tables = DemandData.Tables;
            var rules = DemandData.Rules;
            if (tables == null || rules == null) return;
            var atWindow = (!inSkip || ServingWork.IsSkipping(vehicle.VehicleId)) && PlayerIsAtCart(cart);
            var window = Service(rules, CartDemand.KindOf(cart), vehicle, atWindow, (long)Math.Floor(from));
            if (window.Servers == 0)
            {
                if (pausedNoticeShown.Add(vehicle.VehicleId))
                {
                    var kind = KindOf(vehicle);
                    if (kind == "truck")
                    {
                        Say("foodtrucks:notification_paused_truck", kind);
                        Log.Info($"Window {vehicle.VehicleId}: nobody serving the truck; {(inSkip ? "a time skip" : TruckWindow.WhyNotAtHatch(vehicle.VehicleId))}.");
                    }
                    else Say("foodtrucks:notification_paused", kind);
                }
            }
            else
            {
                pausedNoticeShown.Remove(vehicle.VehicleId);
                // Minutes someone worked the window here count for the regulars, by working day.
                // Windows opened before 0.7.0 get their pitch now; it is saved, so a load keeps it.
                if (vehicle.OpenPitch == null && spots.TryGetValue(vehicle.VehicleId, out var openSpot))
                    vehicle.OpenPitch = World.PitchKey(state, openSpot.X, openSpot.Z);
                var pitch = vehicle.OpenPitch;
                if (pitch != null)
                    ReputationBook.AddOpenMinutes(vehicle, (long)Math.Floor(from), (long)Math.Floor(from) + elapsed, pitch, tables.NightEndsMinute, ReputationBook.ClosedThrough(state));
            }
            var spot = SpotFor(tables, cart, from);
            if (spot == null) return;
            // An event starting here: a booked place, or the window closes until it ends.
            if (!CityPitches.Tick(state, vehicle, spot, (long)Math.Floor(from)))
            {
                Close(vehicle, "foodtrucks:notification_event_closed", from);
                return;
            }
            // Units held for customers on their way to the window are sold already: the plan leaves them out.
            // Demand at the start of this step's minutes: during a catch-up, not the live clock's.
            var plan = CartDemand.Plan(tables, cart, spot, Reserved(vehicle.VehicleId), from);
            if (!plan.Usable)
            {
                // A cart opened under 0.1.0 has food but no equipment; it isn't sold out.
                Close(vehicle, "foodtrucks:notification_closed_no_equipment", from);
                return;
            }
            if (plan.StockUnits == 0)
            {
                Close(vehicle, "foodtrucks:notification_sold_out", from);
                return;
            }
            if (elapsed > SalesTick.MaxCatchUpMinutes) return;
            // Minutes sold outside a partner venue count towards its day.
            if (window.Servers > 0) Partners.Worked(state, vehicle, spot, (long)Math.Floor(from), elapsed);
            // The crew serving a window the player opened, noted as the sales reach this minute: a minute of an hour's
            // first half earns that hour, decided when it is over (HireCrew.CountHour, BeforePayroll).
            HireCrew.Served(state, vehicle, window.Crew, (long)Math.Floor(from));

            // A cart opened before 0.4.0 has no queue yet; it starts one now.
            vehicle.Service ??= SalesSimulator.Open(Seed(cart.id));
            simulator ??= new SalesSimulator(tables, rules.Queue);
            var startMinute = vehicle.Service.Minute;
            var result = simulator.Advance(vehicle.Service, Input(tables, plan, window.Servers, window), elapsed);
            // Player and crew sales share the saved midnight pot. In-person sales keep their per-sale log and cargo
            // feedback; neither kind moves money or shows a money notice here.
            var inPerson = atWindow;
            CountLost(vehicle.VehicleId, result);
            var failed = false;
            var showing = !ServingWork.Accelerated && !inSkip && CartQueueView.Showing(vehicle.VehicleId);
            // One order per customer: their main item and the drink that came with it. With the queue in view,
            // booking waits for that customer to leave the window; midnight flushes any still held.
            foreach (var customer in result.Sales.GroupBy(s => s.CustomerId))
            {
                var items = customer.Select(s => s.Item).ToList();
                var soldAt = from + (customer.Max(s => s.Minute) - startMinute);
                if (showing)
                {
                    if (!held.TryGetValue(vehicle.VehicleId, out var list)) held[vehicle.VehicleId] = list = new List<HeldSale>();
                    list.Add(new HeldSale(customer.Key, items, soldAt, inPerson));
                }
                // Each order at the game minute it was made: its day for the books, and the working day of any stale food in it.
                else if (!ServeCustomer(state, vehicle, cart, plan.Menu.Menu, items, inPerson, soldAt))
                {
                    failed = true;
                    break;
                }
            }
            FlushCrewSales(vehicle, cart, from);
            CartQueueView.Record(vehicle.VehicleId, result, window.Mood);
            // Every simulated minute changes the saved queue (its clock, random state, orders in progress).
            FoodTrucksSession.MarkChanged();
            if (failed) Close(vehicle, "foodtrucks:notification_closed_error", now);
            // At the step's game minute, so a catch-up frees a shared place when it really sold out.
            else if (result.SoldOut) Close(vehicle, "foodtrucks:notification_sold_out", from + elapsed);
        }

        // Called by the queue view when a served customer leaves the window, or leaves the view unseen.
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
                try { ok = ServeCustomer(state, vehicle, cart, menu, sale.Items, sale.InPerson, sale.Minute); }
                catch (Exception ex)
                {
                    Log.Error($"Cart {vehicle.VehicleId}: paying held sale for customer {sale.CustomerId}: {ex}");
                    ok = false;
                }
                if (ok) continue;
                Log.Error($"Cart {vehicle.VehicleId}: held sale for customer {sale.CustomerId} ({string.Join(", ", sale.Items)}) not fully paid; dropped.");
                heldFailed.Add(vehicle.VehicleId);
            }
            FlushCrewSales(vehicle, cart, HireCrew.NowExact());
            if (list.Count == 0) held.Remove(vehicle.VehicleId);
            if (paid) FoodTrucksSession.MarkChanged();
        }

        // Units already sold to customers still walking to the window (catering checks its stock without them).
        internal static Dictionary<string, int> HeldUnits(string vehicleId) =>
            instance?.Reserved(vehicleId) ?? new Dictionary<string, int>();

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

        // The spot is read again each game hour: live venue crowds change, and the city around it may have.
        private Spot? SpotFor(DemandTables tables, VehicleInstance cart, double at)
        {
            // Live venue crowds change by the hour, so the spot is read again for each game hour sold in.
            var absolute = (long)Math.Floor(at);
            var day = (int)(absolute / 1440) + 1;
            var hour = (int)(absolute % 1440 / 60);
            if (spots.TryGetValue(cart.id, out var spot) && spot.Day == day && spot.Hour == hour && !spot.Provisional) return spot;
            // A crew's vehicle far from the player may have no controller: read it where it stood.
            var controller = VehicleHelper.GetVehicleController(cart);
            if (controller == null && spot == null) return null;
            spot = World.ReadSpot(tables, controller != null ? controller.transform.position : new Vector3(spot!.X, spot.Y, spot.Z), at, Partners.VenueOf(cart.id));
            var again = spots.TryGetValue(cart.id, out var before) && before.Day == spot.Day && before.Hour == spot.Hour;
            spots[cart.id] = spot;
            // "Truck" or "Cart" by the vehicle (live session 9: a truck's spot line said "Cart").
            var noun = FoodTrucksMod.KindOf(cart.vehicleTypeName) == "truck" ? "Truck" : "Cart";
            if (!again || !spot.Provisional) Log.Info($"{noun} {cart.id} spot on day {spot.Day}, {spot.Hour:D2}:00{(spot.Provisional ? " (venues still being simulated)" : "")}: {World.Describe(spot)}.");
            return spot;
        }

        // One customer's order as Core sold it: each item takes a unit at the cart's price and accrues for midnight.
        // The in-person cash sound, cargo feedback and per-sale log stay; no per-sale money notice.
        private bool ServeCustomer(SaveState state, VehicleState vehicle, VehicleInstance cart, Menu menu, List<string> items, bool inPerson, double? at = null)
        {
            var labels = new List<string>();
            long cents = 0;
            foreach (var item in items)
            {
                var sold = SellItem(state, vehicle, cart, item, inPerson, at);
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
            if (!inPerson)
            {
                crewBatch.TryGetValue(vehicle.VehicleId, out var batch);
                crewBatch[vehicle.VehicleId] = (batch.Customers + 1, batch.Items + labels.Count, batch.Cents + cents);
                return true;
            }
            var left = CartDemand.StockOf(cart, menu).Values.Sum();
            Log.Info($"Cart {vehicle.VehicleId}: sold {string.Join(" + ", labels)} for {Money(cents)}; {left} left.");
            return true;
        }

        // After a step's crew sales: the HUD's cargo update once, and the sales added to the vehicle's hour, logged as one
        // line when the hour is over or the window closes (LogCrewHour) instead of one line per sale.
        private void FlushCrewSales(VehicleState vehicle, VehicleInstance cart, double minute)
        {
            if (!crewBatch.TryGetValue(vehicle.VehicleId, out var batch)) return;
            crewBatch.Remove(vehicle.VehicleId);
            try
            {
                cart.OnItemsInCargoUpdated()?.Invoke();
                GameEvent.Invoke("ba:gameevent_itemcargochanged");
            }
            catch (Exception ex) { Log.Error("Cargo update listeners: " + ex); }
            var hour = (long)Math.Floor(minute / 60);
            if (crewHour.TryGetValue(vehicle.VehicleId, out var open) && open.Hour != hour) LogCrewHour(vehicle.VehicleId, cart);
            crewHour.TryGetValue(vehicle.VehicleId, out open);
            crewHour[vehicle.VehicleId] = (hour, open.Customers + batch.Customers, open.Items + batch.Items, open.Cents + batch.Cents);
        }

        private void LogCrewHour(string vehicleId, VehicleInstance? cart)
        {
            if (!crewHour.TryGetValue(vehicleId, out var h)) return;
            crewHour.Remove(vehicleId);
            Log.Info($"Crew sales: {vehicleId}, day {h.Hour / 24 + 1} {h.Hour % 24:D2}:00 hour: {h.Customers} customers, {h.Items} items, {Money(h.Cents)}, " +
                     $"paid in after midnight{(cart == null ? "" : $"; {CartStock(cart)} units left")}.");
        }

        private static int CartStock(VehicleInstance cart)
        {
            try { return StockUnits(cart); }
            catch { return -1; }
        }

        // All window takings paid in, one transaction per vehicle and day, into the day sold, as the game
        // pays its shops their day's revenue at midnight (BusinessHelper.ProcessDailyOrders). A day is paid once that
        // vehicle's sales have passed its end (SoldThroughOf). A payment the game refuses waits for the next tick.
        private static void PayTakings(SaveState state, bool all)
        {
            var today = SaveGameManager.Current.Day;
            var self = instance;
            // With the sales tick switched off nothing sells any more: whatever is waiting is paid.
            Func<string, double>? soldThrough = null;
            if (self != null && self.enabled) soldThrough = self.SoldThroughOf;
            // A vehicle with the private driver is still owned, as is a crew vehicle in transit.
            var owned = new HashSet<string>(SaveGameManager.Current.VehicleInstances
                .Concat(SaveGameManager.Current.privateDriverVehicleInstances ?? Enumerable.Empty<VehicleInstance>()).Select(v => v.id));
            var due = all ? CrewTakings.All(state) : CrewTakings.Due(state, today, soldThrough, owned.Contains);
            if (due.Count == 0) return;
            var names = StreetFoodBooks.VehicleNames();
            foreach (var taking in due)
            {
                if (taking.Cents <= 0)
                {
                    CrewTakings.Settled(state, taking);
                    FoodTrucksSession.MarkChanged();
                    continue;
                }
                var name = names.TryGetValue(taking.VehicleId, out var known) ? known : taking.VehicleId;
                var info = new TransactionInfo("foodtrucks:transaction_crew_sales", new Dictionary<string, string>
                {
                    { "vehicle", name }, { "day", taking.Day.ToString() }, { "sales", taking.Sales.ToString() }
                });
                // ChangeMoneySafe doesn't refuse money in; a payment that threw before booking is tried again next tick.
                if (!Payments.PayIn(taking.Cents, info, taking.Day)) continue;
                CrewTakings.Settled(state, taking);
                FoodTrucksSession.MarkChanged();
                Log.Info($"Street food sales paid in: {name} ({taking.VehicleId}), day {taking.Day}: {taking.Sales} items, {Money(taking.Cents)}.");
            }
        }

        // The absolute game minute this vehicle has sold up to: its open window's sales clock, the route that may still
        // open a window, and the earliest held sale still to be booked. Infinity when nothing lags.
        private double SoldThroughOf(string vehicleId)
        {
            var through = double.PositiveInfinity;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId);
            if (vehicle != null && vehicle.IsOpen && simulatedUntil.TryGetValue(vehicleId, out var until)) through = Math.Min(through, until);
            if (HireRoutes.RoutedUntil(vehicleId) is { } routed) through = Math.Min(through, routed);
            if (held.TryGetValue(vehicleId, out var list) && list.Count > 0) through = Math.Min(through, list.Min(h => h.Minute));
            return through;
        }

        // Inside the game's midnight processing (GameManager.NewDay, the day already turned): every route and every open
        // window sells up to midnight, and the sales whose customers are still walking up are booked, so the closing
        // day's books are complete before the game pays wages, runs the day summary and taxes and records the
        // midnight bank balance. Called before payroll (HireCrew.BeforePayroll) and again before shop revenue
        // (SettleClosingDay); the second call finds nothing left to do.
        internal static void CatchUpDay(long midnight)
        {
            var self = instance;
            var state = FoodTrucksSession.CurrentState;
            if (self == null || state == null || !FoodTrucksSession.CanWrite) return;
            try { HireRoutes.CatchUpTo(midnight); }
            catch (Exception ex) { Log.Error("Routes catch-up at midnight: " + ex); }
            CatchUpTo(midnight);
            try
            {
                foreach (var id in self.held.Keys.ToList())
                {
                    var vehicle = StreetSales.Find(state, id);
                    if (vehicle != null) self.PayHeld(vehicle, h => h.Minute < midnight);
                }
            }
            catch (Exception ex) { Log.Error("Held sales at midnight: " + ex); }
        }

        // BusinessHelper.RunDaily prefix (Patches), where the game pays its shops their day's revenue: the closing
        // day's player and crew takings together, before the daily summary and the midnight bank balance. Runs
        // even when the sales tick has switched itself off; the tick's own check only retries leftovers.
        internal static void SettleClosingDay()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            CatchUpDay(HireSchedules.Absolute(TimeHelper.CurrentDay, 0));
            PayTakings(state, all: false);
        }

        // Every taking paid in now, today's too (the uninstall step). Refuse cleanup while a payment is still owed.
        internal static bool PayAllTakingsNow()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null)
            {
                // A newer mod version's block can hold unpaid takings this version can't read: that version settles them.
                if (FoodTrucksSession.LastLoadStatus == FoodTrucks.Core.Saves.LoadStatus.NewerVersion)
                {
                    Log.Info("Uninstall: this save was written by a newer Food Trucks version, whose unpaid takings this version can't read; cleanup blocked (uninstall with that version).");
                    return false;
                }
                Log.Info("Uninstall: no readable window sales state; no takings to settle.");
                return true;
            }
            if (!FoodTrucksSession.CanWrite)
            {
                var nothingOwed = !state.CrewTakings.Any(t => t.Cents > 0);
                Log.Info(nothingOwed
                    ? "Uninstall: read-only window sales state has no unpaid takings; cleanup can continue."
                    : "Uninstall: read-only window sales state has unpaid takings; cleanup blocked.");
                return nothingOwed;
            }
            instance?.PayAllHeld();
            PayTakings(state, all: true);
            return state.CrewTakings.Count == 0;
        }

        // VehicleHelper.Delete prefix: book visible customers while their cargo still exists.
        internal static void BeforeVehicleRemoved(VehicleInstance cart)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || instance == null) return;
            var vehicle = StreetSales.Find(state, cart.id);
            if (vehicle == null) return;
            if (vehicle.IsOpen) instance.Close(vehicle, "foodtrucks:notification_closed");
            else instance.PayHeld(vehicle, null);
        }

        // Close enough to see the window: the queue view's range, outdoors.
        internal static bool PlayerNear(VehicleInstance cart)
        {
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking) return false;
            var controller = VehicleHelper.GetVehicleController(cart);
            return controller != null && Vector3.Distance(PlayerHelper.GetPosition(), controller.transform.position) <= 100f;
        }

        // For the test bridge's state: the player serves this window now, as the sales tick reads it.
        internal static bool PlayerServesNow(VehicleInstance cart) => PlayerIsAtCart(cart);

        // For crew placement only: the player occupies the cart's middle serving spot while its idle stance holds.
        internal static bool PlayerAtServingSpot(string id) => instance != null && instance.cartServing.PlayerAt(id);

        // Core decides (PlayerServing): a truck only from its hatch (or while the player is being placed there
        // after Open), a cart within reach of its window.
        private static bool PlayerIsAtCart(VehicleInstance cart)
        {
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking) return false;
            if (SaveGameManager.Current.ActiveVehicleId == cart.id) return false;
            var controller = VehicleHelper.GetVehicleController(cart);
            double? distance = controller == null ? (double?)null : Vector3.Distance(PlayerHelper.GetPosition(), controller.transform.position);
            return PlayerServing.Serves(CartDemand.KindOf(cart), TruckWindow.PlayerServes(cart.id), distance, WorkingDistance, instance?.cartServing.PlayerAt(cart.id) == true);
        }

        // One item at the cart's price. All sales wait in CrewTakings for midnight. The crew's
        // cargo update comes once per step and its log line once per game hour (FlushCrewSales). Returns the cents, or -1 if nothing was sold.
        private long SellItem(SaveState state, VehicleState vehicle, VehicleInstance cart, string itemName, bool inPerson, double? at = null)
        {
            spots.TryGetValue(cart.id, out var spot);
            var cents = CartPricing.PriceCents(vehicle, itemName, spot?.Neighbourhood);
            if (cents <= 0)
            {
                Log.Error($"Sale refused: {itemName} has no market price.");
                return -1;
            }
            // Booked on the day of the minute it was sold, which lags the live day while sales catch up (Core: GameDays.SaleDay).
            var day = GameDays.SaleDay(at, SaveGameManager.Current.Day);
            // Check both records before consuming any stock, whoever is serving.
            if (!StreetSales.CanRecord(state, vehicle.VehicleId, day, cents, 1) || !CrewTakings.CanAccrue(state, vehicle.VehicleId, day, cents))
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
            // Record before touching the cargo: emptying a stack runs the game's cargo listeners,
            // and neither the ledger nor the midnight pot may miss a sale if one of them throws. None of these calls can fail here.
            StreetSales.Record(state, vehicle.VehicleId, day, cents, 1);
            CrewTakings.Accrue(state, vehicle.VehicleId, day, cents);
            SoldCounts.Record(vehicle, day, itemName, 1);
            // The unit leaves the fleet: the commissary-order pool goes first (tax origin of later returns).
            FoodTrucks.Core.Commissary.Commissaries.LeftVehicle(state.Commissary, itemName, 1);
            // Food on board for days counts against the health grade (design: Health grades).
            // Food ages by calendar day; a stale sale marks the working day of the minute it was sold.
            var soldAt = (long)Math.Floor(at ?? HireCrew.NowExact());
            var workDay = ServiceDay(soldAt);
            var since = vehicle.StockBatches != null && vehicle.StockBatches.TryGetValue(itemName, out var batches) && batches.Count > 0 ? batches[0].Day : day;
            var book = CartDemand.Book();
            if (book != null && workDay > ReputationBook.ClosedThrough(state) && book.Sold(vehicle, itemName, HireRunner.DayOf(soldAt), workDay))
                Log.Info($"Cart {vehicle.VehicleId}: stale {itemName} sold on working day {workDay} (on board since day {since}).");
            else if (book == null || workDay <= ReputationBook.ClosedThrough(state)) ReputationBook.Consume(vehicle, itemName, 1);
            // What this unit cost at the wholesaler, for the profit shown in the day summary and Econoview.
            SalesLedger.RecordCost(state, day, vehicle.VehicleId, (long)Math.Round(Math.Max(0f, entry.pricePerUnit) * 100.0));
            sinceOpen.TryGetValue(vehicle.VehicleId, out var totals);
            sinceOpen[vehicle.VehicleId] = (totals.Sales + 1, totals.Cents + cents);
            try
            {
                // Emptying a stack already runs the game's cargo listeners; a throwing one must not stop selling.
                cart.ReduceFromCargo(entry, 1);
                if (inPerson)
                {
                    // ReduceFromCargo only signals when a stack empties; the HUD needs every change.
                    cart.OnItemsInCargoUpdated()?.Invoke();
                    GameEvent.Invoke("ba:gameevent_itemcargochanged");
                    ServingWork.RefreshStock();
                }
            }
            catch (Exception ex) { Log.Error("Cargo update listeners: " + ex); }
            if (inPerson) Log.Info($"Sale: {itemName} for {Money(cents)} on day {day}, {cart.GetAmountByItemName(itemName)} of it left.");
            return cents;
        }

        // When each food item came on board, for every food vehicle: boxes and loose stock alike.
        private static void TrackStock(SaveState state)
        {
            var tables = DemandData.Tables;
            if (tables == null) return;
            var day = SaveGameManager.Current.Day;
            foreach (var instance in SaveGameManager.Current.VehicleInstances)
            {
                if (instance == null || FoodTrucksMod.KindOf(instance.vehicleTypeName) == null) continue;
                var items = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var entry in instance.cargoInstances)
                {
                    if (entry == null) continue;
                    if (entry.amount > 0 && tables.Product(entry.itemName) != null) Add(items, entry.itemName, entry.amount);
                    if (entry.nestedCargoInstances == null) continue;
                    foreach (var nested in entry.nestedCargoInstances)
                        if (nested != null && nested.amount > 0 && tables.Product(nested.itemName) != null) Add(items, nested.itemName, nested.amount);
                }
                var vehicle = StreetSales.Find(state, instance.id);
                if (vehicle == null && items.Count == 0) continue;
                vehicle ??= StreetSales.GetOrAdd(state, instance.id);
                if (ReputationBook.TrackStock(vehicle, items, day)) FoodTrucksSession.MarkChanged();
            }
        }

        private static void Add(Dictionary<string, int> units, string item, int amount)
        {
            units.TryGetValue(item, out var have);
            units[item] = (int)Math.Min(int.MaxValue, (long)have + amount);
        }

        // Cleaning the vehicle (the on-foot menu's "Clean", OverlayButtons): only in the bay of the commissary where it
        // rents its space. A short wait while the game's clock runs, like a taxi ride, and today counts as cleaned for the
        // health grade. Closing doesn't clean; a vehicle standing in its bay at the 04:00 check counts as cleaned too.
        public static void CleanHere(string vehicleId)
        {
            var state = FoodTrucksSession.CurrentState;
            var cart = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicleId);
            var kind = FoodTrucksMod.KindOf(cart?.vehicleTypeName);
            var timeMachine = InstanceBehavior<UIs>.Instance?.timeMachine;
            if (state == null || cart == null || kind == null || timeMachine == null)
            {
                Log.Info($"Cleaning: {vehicleId} refused: {(cart == null || kind == null ? "no such food vehicle" : "no save data or time skip")}.");
                return;
            }
            if (!FoodTrucksSession.CanWrite)
            {
                Notify("foodtrucks:notification_data_readonly");
                return;
            }
            if (timeMachine.isRunning)
            {
                Log.Info($"Cleaning: {kind} {cart.id} refused: a time skip is running.");
                return;
            }
            if (Commissary.OwnBay(state, cart, out var why) == null)
            {
                Log.Info($"Cleaning: {kind} {cart.id} refused: {why}.");
                Say(state.Commissary.Spaces.Any(s => s.VehicleId == cart.id) ? "foodtrucks:notification_clean_not_in_bay" : "foodtrucks:notification_clean_no_space", kind);
                return;
            }
            var vehicle = StreetSales.GetOrAdd(state, cart.id);
            if (string.IsNullOrEmpty(vehicle.VehicleType)) vehicle.VehicleType = kind;
            var day = ServiceDayNow();
            ReputationBook.Cleaned(vehicle, day, ReputationBook.ClosedThrough(state));
            FoodTrucksSession.MarkChanged();
            var goal = TimeHelper.Now();
            goal.AddMinutes(CleaningMinutes);
            timeMachine.StartTimeMachine(goal, disableCancel: true, "foodtrucks:timemachine_cleaning");
            Log.Info($"Cleaning: {kind} {cart.id} cleaned on day {day} ({why}), {CleaningMinutes} minutes.");
            Say("foodtrucks:notification_cleaned", kind);
        }

        // Whether the on-foot menu offers "Clean": the player on foot, no time skip running, the vehicle in its own
        // commissary's bay.
        internal static bool CanOfferClean(VehicleInstance? cart)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || cart == null || FoodTrucksMod.KindOf(cart.vehicleTypeName) == null) return false;
            if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId) || InstanceBehavior<UIs>.Instance?.timeMachine?.isRunning == true) return false;
            return state.Commissary.Spaces.Any(s => s.VehicleId == cart.id) && Commissary.OwnBay(state, cart, out _) != null;
        }

        private const int CleaningMinutes = 20;

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
            Notices.Show(NotificationType.Info, key, values, 5f, notificationSound: false, trackOnSaveGame: false);
        }
    }
}
