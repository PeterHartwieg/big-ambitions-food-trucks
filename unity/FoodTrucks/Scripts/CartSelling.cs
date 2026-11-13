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
using FoodTrucks.Core.Vendor;
using Helpers;
using Localizor;
using Parking.UndergroundParking;
using UI;
using UI.Notification;
using UnityEngine;

namespace FoodTrucks
{
    internal sealed class CartSelling : MonoBehaviour
    {
        private const float WorkingDistance = 8f;

        private const float TickSeconds = 0.25f;

        private readonly Dictionary<string, double> simulatedUntil = new Dictionary<string, double>();
        private readonly HashSet<string> pausedNoticeShown = new HashSet<string>();
        private readonly Dictionary<string, (int Sales, long Cents)> sinceOpen = new Dictionary<string, (int, long)>();
        private readonly Dictionary<string, (int Customers, int Items, long Cents)> crewBatch = new Dictionary<string, (int, int, long)>();
        private readonly Dictionary<string, (long Hour, int Customers, int Items, long Cents)> crewHour = new Dictionary<string, (long, int, int, long)>();
        private readonly Dictionary<string, Dictionary<CustomerExit, int>> lostSinceOpen = new Dictionary<string, Dictionary<CustomerExit, int>>();
        private readonly Dictionary<string, Spot> spots = new Dictionary<string, Spot>();
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

        internal static Spot? SpotOf(string vehicleId) =>
            instance != null && instance.spots.TryGetValue(vehicleId, out var spot) ? spot : null;

        internal static double? SoldUntil(string vehicleId) =>
            instance != null && instance.simulatedUntil.TryGetValue(vehicleId, out var until) ? until : (double?)null;

        internal static string? PitchOf(string vehicleId) =>
            StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId)?.OpenPitch;

        private readonly Dictionary<string, List<HeldSale>> held = new Dictionary<string, List<HeldSale>>();
        private readonly HashSet<string> heldFailed = new HashSet<string>();
        private SalesSimulator? simulator;
        private float nextTick;
        private readonly CartServing cartServing = new CartServing();

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
            public double Minute { get; }
            public bool InPerson { get; }
        }
        private bool skipping;

        private static CartSelling? instance;

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
            foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen && v.Service != null && !crewed.Contains(v.VehicleId) && !ServingWork.WasWorkSkip(v.VehicleId)))
            {
                if (SalesSimulator.Close(vehicle.Service!).Left.Count > 0) FoodTrucksSession.MarkChanged();
            }
        }

        private static bool Crewed(VehicleState vehicle) => HireCrew.OnShift(vehicle, HireCrew.Now()).Count > 0;

        private static bool RunByRoute(VehicleState vehicle) => vehicle.HireStop != null;

        public static void OpenHere(VehicleController controller)
        {
            if (CrewDriveProbe.Owns(controller)) return;
            try { instance?.Open(controller); }
            catch (Exception ex)
            {
                Log.Error("Open failed: " + ex);
                Notify("foodtrucks:notification_open_failed");
            }
        }

        internal static bool CanOfferOpen(VehicleInstance? cart)
        {
            if (CrewDriveProbe.Owns(cart?.id)) return false;
            if (cart == null || FoodTrucksMod.KindOf(cart.vehicleTypeName) == null) return false;
            if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId)) return false;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, cart.id);
            return vehicle == null || (!vehicle.IsOpen && vehicle.Transit == null);
        }

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
            var eventProblem = CityPitches.EventProblem(state, cart.id, position, HireCrew.Now());
            if (eventProblem != null)
            {
                Say(eventProblem, kind);
                return;
            }

            HireRoutes.PlayerTook(vehicle);
            var (plan, pitch) = StartSelling(vehicle, cart, controller, position, kind, hireStop: null);
            var serving = HireCrew.Serving(vehicle, HireCrew.Now());
            Log.Info($"Open: {kind} {cart.id} opened by the player with {plan.StockUnits} items; serving: {(serving.Count == 0 ? "the player" : HireCrew.Names(serving))}.");
            if (pitch != null) Say(pitch.Value.Key, kind, pitch.Value.Data);
            cartServing.Release("another window opened");
            if (kind == "truck") TruckWindow.EnterFromOutside(controller);
            else cartServing.Open(controller);
        }

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
                var parking = cart.parkingState;
                if (parking != ParkingState.Legal)
                {
                    Log.Info($"Pitch: truck {cart.id} at {position}: saved parking state {parking}.");
                    return parking == ParkingState.NotAvailable ? "foodtrucks:legality_truck_moving" : "foodtrucks:legality_truck_parking";
                }
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

        private (CartPlan Plan, (string Key, (string Key, string Value)[] Data)? Pitch) StartSelling(VehicleState vehicle, VehicleInstance cart,
            VehicleController? controller, Vector3 position, string kind, int? hireStop, double? openedAt = null)
        {
            var tables = DemandData.Tables!;
            var rules = DemandData.Rules!;
            vehicle.IsOpen = true;
            vehicle.HireStop = hireStop;
            vehicle.SaleProgress = 0;
            vehicle.Service = SalesSimulator.Open(Seed(cart.id));
            simulatedUntil[cart.id] = SalesClock.Start(openedAt ?? HireCrew.NowExact());
            pausedNoticeShown.Remove(cart.id);
            sinceOpen[cart.id] = (0, 0);
            lostSinceOpen.Remove(cart.id);
            var state = FoodTrucksSession.CurrentState!;
            var spot = World.ReadSpot(tables, position, partnerVenue: Partners.VenueOf(cart.id));
            spots[cart.id] = spot;
            var pitch = CityPitches.Opened(state, vehicle, position, (long)Math.Floor(openedAt ?? HireCrew.NowExact()));
            if (state.Vendor != null)
            {
                VendorBook.Opened(state, cart.id, (long)Math.Floor(openedAt ?? HireCrew.NowExact()), byCrew: hireStop != null, VendorNotes);
                LogVendorNotes();
            }
            TruckHatch.Set(controller, true);
            Inspect(vehicle, kind, (long)Math.Floor(openedAt ?? HireCrew.NowExact()));
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

        internal static int ServiceDay(long absoluteMinute) =>
            ReputationBook.ServiceDay(absoluteMinute, DemandData.Tables?.NightEndsMinute ?? 5 * 60);

        internal static int ServiceDayNow() => ServiceDay(HireCrew.Now());

        private void LateUpdate()
        {
            cartServing.LateUpdate();
            try { ServingWork.UpdateCart(cartServing); }
            catch (Exception ex) { Log.Error("Serving work, cart update: " + ex); }
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
            var dropped = book.CloseDaysBefore(state, today - 1);
            var after = state.Vehicles.Sum(v => v.Days?.Count ?? 0);
            if (dropped > 0) Log.Error($"Reputation: {dropped} visits came after their day was counted; dropped.");
            if (before != after || lastCounted != state.Regulars.LastUpdatedDay)
            {
                FoodTrucksSession.MarkChanged();
                Log.Info($"Reputation through day {today - 2}: {before - after} vehicle days counted; regulars at {state.Regulars.Entries.Count} pitches and weekdays; " +
                         string.Join(", ", state.Vehicles.Where(v => v.Health != null).Select(v => $"{v.VehicleId} {v.Health!.Grade} ({book.Points(v)} points)")) + ".");
            }
            foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen && (v.Health == null || (v.Health.LastDrawDay ?? 0) < today)))
                Inspect(vehicle, string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType, HireCrew.Now(), opening: false);
        }

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
                var hatch = HireRoutes.Hatch(vehicle.VehicleId, controller.transform);
                if (!hatch.Opens) return hatch.RefusalKey;
            }
            var eventProblem = CityPitches.EventProblem(state, cart.id, position, minute);
            if (eventProblem != null) return eventProblem;
            self.StartSelling(vehicle, cart, controller, position, kind, stop, minute);
            return null;
        }

        internal static void CloseByCrew(VehicleState vehicle, long minute)
        {
            if (instance == null) return;
            AdvanceTo(vehicle, minute);
            if (!vehicle.IsOpen) return;
            instance.Close(vehicle, "foodtrucks:notification_hire_closed", minute);
        }

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

        internal static List<EmployeeInstance> ServingNow(VehicleState vehicle, string kind)
        {
            var now = HireCrew.Now();
            if (!vehicle.IsOpen) return HireCrew.OnShift(vehicle, now);
            var cart = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId);
            var atWindow = cart != null && (!Skipping || ServingWork.IsSkipping(cart.id)) && PlayerIsAtCart(cart);
            return HireCrew.Team(vehicle, now, atWindow ? ServiceRules.EffectiveSkill(PlayerSkill(), 100) : (double?)null, kind).Crew;
        }

        internal static void AdvanceTo(VehicleState vehicle, double minute)
        {
            var self = instance;
            var state = FoodTrucksSession.CurrentState;
            if (self == null || state == null || !FoodTrucksSession.CanWrite || !vehicle.IsOpen) return;
            self.Advance(state, vehicle, minute, int.MaxValue);
        }

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
            public List<EmployeeInstance> Crew { get; }
        }

        private static Window Service(RuleTables rules, string kind, VehicleState vehicle, bool playerAtWindow, long? minute = null)
        {
            var (crew, skills) = HireCrew.Team(vehicle, minute ?? HireCrew.Now(), playerAtWindow ? ServiceRules.EffectiveSkill(PlayerSkill(), 100) : (double?)null, kind);
            var team = rules.Service.Team(skills);
            return new Window(skills.Count, team.Skill, team.Speed, team.Mood, rules.Service.QueueLimit(kind, rules.Queue.MaxQueue), crew);
        }

        private static double PlayerSkill()
        {
            var skills = SaveGameManager.Current?.charactersData?.FirstOrDefault()?.skills;
            var skill = skills?.Find(s => s != null && s.name == CustomerServiceSkill);
            return skill != null ? skill.value : 50;
        }

        private const string CustomerServiceSkill = "ba:skill_customerservice";

        private static long Seed(string vehicleId) => SeededRandom.SeedFor(vehicleId, (long)Math.Floor(HireCrew.NowExact()));

        private void OnEnterVehicle(VehicleController controller)
        {
            cartServing.Release("entered a vehicle");
            try
            {
                var cart = controller?.vehicleInstance;
                if (cart == null || FoodTrucksMod.KindOf(cart.vehicleTypeName) == null) return;
                PrefabFixes.ApplyTo(controller);
                TruckHatch.Set(controller, false);
                var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, cart.id);
                if (vehicle != null) HireRoutes.PlayerTook(vehicle);
                if (vehicle != null && vehicle.IsOpen && FoodTrucksSession.CanWrite) Close(vehicle, "foodtrucks:notification_closed");
            }
            catch (Exception ex) { Log.Error(ex); }
        }

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

        private void Close(VehicleState vehicle, string messageKey, double? at = null)
        {
            ServingWork.WindowClosed(vehicle.VehicleId);
            cartServing.Closed(vehicle.VehicleId);
            var state = FoodTrucksSession.CurrentState;
            var closedAt = (long)Math.Floor(at ?? HireCrew.NowExact());
            var crewWindow = vehicle.HireStop != null || HireCrew.OnShift(vehicle, closedAt).Count > 0;
            vehicle.OpenPitch = null;
            if (state != null)
            {
                PremiumSpots.Release(state, vehicle, closedAt);
                CityEvents.Release(state.Events, vehicle.VehicleId, closedAt);
            }
            if (vehicle.HireStop != null && messageKey == "foodtrucks:notification_closed_error")
                HireRunner.GiveUp(vehicle, vehicle.HireStop.Value, (long)Math.Floor(at ?? HireCrew.NowExact()));
            PayHeld(vehicle, null);
            LogCrewHour(vehicle.VehicleId, SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId));
            heldFailed.Remove(vehicle.VehicleId);
            if (state?.Vendor != null) VendorBook.Closed(state, vehicle.VehicleId);
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

        public static void CloseAllQuietly()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return;
            instance?.PayAllHeld();
            foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen))
            {
                if (state.Vendor != null) VendorBook.Closed(state, vehicle.VehicleId);
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
            if (Time.unscaledTime < nextTick && !Skipping && !ServingWork.Accelerated) return;
            nextTick = Time.unscaledTime + TickSeconds;
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            try
            {
                TrackStock(state);
                foreach (var vehicle in state.Vehicles.Where(v => v.IsOpen && !RunByRoute(v)).ToList())
                    Advance(state, vehicle, HireCrew.NowExact());
                PayTakings(state, all: false);
            }
            catch (Exception ex)
            {
                Log.Error("Sales tick: " + ex);
                try { PayAllHeld(); }
                catch (Exception payEx) { Log.Error("Paying held sales: " + payEx); }
                enabled = false;
                Notify("foodtrucks:notification_tick_failed");
                return;
            }
            if (state.Vendor == null && VendorRuntime.Current?.Active != true) return;
            try { VendorRuntime.Current?.Tick(); }
            catch (Exception ex) { Log.Error("Vendor API tick: " + ex); }
        }

        private void Tick(SaveState state, VehicleState vehicle, double until)
        {
            var cart = SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v.id == vehicle.VehicleId);
            if (cart == null)
            {
                Close(vehicle, "foodtrucks:notification_closed_missing");
                return;
            }

            var now = until;
            if (!simulatedUntil.TryGetValue(vehicle.VehicleId, out var from))
            {
                simulatedUntil[vehicle.VehicleId] = SalesClock.Start(now);
                return;
            }
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
            if (!Licensing.IsValid(state.Licence, (int)((long)Math.Floor(from) / 1440) + 1))
            {
                Close(vehicle, state.Licence.HasVendorLicence ? "foodtrucks:notification_closed_licence" : "foodtrucks:notification_closed_no_licence", from);
                return;
            }
            TruckHatch.Set(VehicleHelper.GetVehicleController(cart), true);
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
                if (vehicle.OpenPitch == null && spots.TryGetValue(vehicle.VehicleId, out var openSpot))
                    vehicle.OpenPitch = World.PitchKey(state, openSpot.X, openSpot.Z);
                var pitch = vehicle.OpenPitch;
                if (pitch != null)
                    ReputationBook.AddOpenMinutes(vehicle, (long)Math.Floor(from), (long)Math.Floor(from) + elapsed, pitch, tables.NightEndsMinute, ReputationBook.ClosedThrough(state));
            }
            var spot = SpotFor(tables, cart, from);
            if (spot == null) return;
            if (!CityPitches.Tick(state, vehicle, spot, (long)Math.Floor(from)))
            {
                Close(vehicle, "foodtrucks:notification_event_closed", from);
                return;
            }
            var plan = CartDemand.Plan(tables, cart, spot, Reserved(vehicle.VehicleId), from);
            if (!plan.Usable)
            {
                Close(vehicle, "foodtrucks:notification_closed_no_equipment", from);
                return;
            }
            if (plan.StockUnits == 0)
            {
                Close(vehicle, "foodtrucks:notification_sold_out", from);
                return;
            }
            if (elapsed > SalesTick.MaxCatchUpMinutes) return;
            if (window.Servers > 0) Partners.Worked(state, vehicle, spot, (long)Math.Floor(from), elapsed);
            HireCrew.Served(state, vehicle, window.Crew, (long)Math.Floor(from));

            if (state.Vendor != null)
            {
                var own = VendorBook.OwnCustomers(state, vehicle, (long)Math.Floor(from), VendorNotes);
                LogVendorNotes();
                if (!own)
                {
                    FoodTrucksSession.MarkChanged();
                    return;
                }
            }

            vehicle.Service ??= SalesSimulator.Open(Seed(cart.id));
            simulator ??= new SalesSimulator(tables, rules.Queue);
            var startMinute = vehicle.Service.Minute;
            var result = simulator.Advance(vehicle.Service, Input(tables, plan, window.Servers, window), elapsed);
            var inPerson = atWindow;
            CountLost(vehicle.VehicleId, result);
            var failed = false;
            var showing = !ServingWork.Accelerated && !inSkip && CartQueueView.Showing(vehicle.VehicleId);
            foreach (var customer in result.Sales.GroupBy(s => s.CustomerId))
            {
                var items = customer.Select(s => s.Item).ToList();
                var soldAt = from + (customer.Max(s => s.Minute) - startMinute);
                if (showing)
                {
                    if (!held.TryGetValue(vehicle.VehicleId, out var list)) held[vehicle.VehicleId] = list = new List<HeldSale>();
                    list.Add(new HeldSale(customer.Key, items, soldAt, inPerson));
                }
                else if (!ServeCustomer(state, vehicle, cart, plan.Menu.Menu, items, inPerson, soldAt))
                {
                    failed = true;
                    break;
                }
            }
            FlushCrewSales(vehicle, cart, from);
            CartQueueView.Record(vehicle.VehicleId, result, window.Mood);
            FoodTrucksSession.MarkChanged();
            if (failed) Close(vehicle, "foodtrucks:notification_closed_error", now);
            else if (result.SoldOut) Close(vehicle, "foodtrucks:notification_sold_out", from + elapsed);
        }

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

        internal static Dictionary<string, int> HeldUnits(string vehicleId) =>
            instance?.Reserved(vehicleId) ?? new Dictionary<string, int>();

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
                AddOnsPerCustomer = plan.AddOnItem == null ? 0 : plan.AddOnsPerFoodBuyer * mainShare,
                Servers = servers,
                Raining = RainHelper.isRaining,
                ServiceSpeed = window.Speed,
                QueueLimit = window.QueueLimit
            };
        }

        private Spot? SpotFor(DemandTables tables, VehicleInstance cart, double at)
        {
            var absolute = (long)Math.Floor(at);
            var day = (int)(absolute / 1440) + 1;
            var hour = (int)(absolute % 1440 / 60);
            if (spots.TryGetValue(cart.id, out var spot) && spot.Day == day && spot.Hour == hour && !spot.Provisional) return spot;
            var controller = VehicleHelper.GetVehicleController(cart);
            if (controller == null && spot == null) return null;
            spot = World.ReadSpot(tables, controller != null ? controller.transform.position : new Vector3(spot!.X, spot.Y, spot.Z), at, Partners.VenueOf(cart.id));
            var again = spots.TryGetValue(cart.id, out var before) && before.Day == spot.Day && before.Hour == spot.Hour;
            spots[cart.id] = spot;
            var noun = FoodTrucksMod.KindOf(cart.vehicleTypeName) == "truck" ? "Truck" : "Cart";
            if (!again || !spot.Provisional) Log.Info($"{noun} {cart.id} spot on day {spot.Day}, {spot.Hour:D2}:00{(spot.Provisional ? " (venues still being simulated)" : "")}: {World.Describe(spot)}.");
            return spot;
        }

        private bool ServeCustomer(SaveState state, VehicleState vehicle, VehicleInstance cart, Menu menu, List<string> items, bool inPerson, double? at = null)
        {
            var labels = new List<string>();
            long cents = 0;
            foreach (var item in items)
            {
                var sold = SellItem(state, vehicle, cart, item, inPerson, at);
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

        private static void PayTakings(SaveState state, bool all, string? vehicleId = null)
        {
            var today = SaveGameManager.Current.Day;
            var self = instance;
            Func<string, double>? soldThrough = null;
            if (self != null && self.enabled) soldThrough = self.SoldThroughOf;
            var owned = new HashSet<string>(SaveGameManager.Current.VehicleInstances
                .Concat(SaveGameManager.Current.privateDriverVehicleInstances ?? Enumerable.Empty<VehicleInstance>()).Select(v => v.id));
            var due = all ? CrewTakings.All(state) : CrewTakings.Due(state, today, soldThrough, owned.Contains);
            if (due.Count == 0) return;
            var names = StreetFoodBooks.VehicleNames();
            foreach (var taking in due)
            {
                if (vehicleId != null && taking.VehicleId != vehicleId) continue;
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
                if (!Payments.PayIn(taking.Cents, info, taking.Day)) continue;
                CrewTakings.Settled(state, taking);
                FoodTrucksSession.MarkChanged();
                Log.Info($"Street food sales paid in: {name} ({taking.VehicleId}), day {taking.Day}: {taking.Sales} items, {Money(taking.Cents)}.");
            }
        }

        private double SoldThroughOf(string vehicleId)
        {
            var through = double.PositiveInfinity;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId);
            if (vehicle != null && vehicle.IsOpen && simulatedUntil.TryGetValue(vehicleId, out var until)) through = Math.Min(through, until);
            if (HireRoutes.RoutedUntil(vehicleId) is { } routed) through = Math.Min(through, routed);
            if (held.TryGetValue(vehicleId, out var list) && list.Count > 0) through = Math.Min(through, list.Min(h => h.Minute));
            return through;
        }

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

        internal static void SettleClosingDay()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            CatchUpDay(HireSchedules.Absolute(TimeHelper.CurrentDay, 0));
            PayTakings(state, all: false);
        }

        internal static bool PayAllTakingsNow()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null)
            {
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

        internal static bool PayVehicleTakingsNow(VehicleInstance cart)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return false;
            BeforeVehicleRemoved(cart);
            PayTakings(state, all: true, vehicleId: cart.id);
            return !state.CrewTakings.Any(t => t.VehicleId == cart.id);
        }

        internal static void BeforeVehicleRemoved(VehicleInstance cart)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || instance == null) return;
            var vehicle = StreetSales.Find(state, cart.id);
            if (vehicle == null) return;
            if (vehicle.IsOpen) instance.Close(vehicle, "foodtrucks:notification_closed");
            else instance.PayHeld(vehicle, null);
        }

        internal static bool PlayerNear(VehicleInstance cart)
        {
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking) return false;
            var controller = VehicleHelper.GetVehicleController(cart);
            return controller != null && Vector3.Distance(PlayerHelper.GetPosition(), controller.transform.position) <= 100f;
        }

        internal static bool PlayerServesNow(VehicleInstance cart) => PlayerIsAtCart(cart);

        internal static bool PlayerAtServingSpot(string id) => instance != null && instance.cartServing.PlayerAt(id);

        private static bool PlayerIsAtCart(VehicleInstance cart)
        {
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking) return false;
            if (SaveGameManager.Current.ActiveVehicleId == cart.id) return false;
            var controller = VehicleHelper.GetVehicleController(cart);
            double? distance = controller == null ? (double?)null : Vector3.Distance(PlayerHelper.GetPosition(), controller.transform.position);
            return PlayerServing.Serves(CartDemand.KindOf(cart), TruckWindow.PlayerServes(cart.id), distance, WorkingDistance, instance?.cartServing.PlayerAt(cart.id) == true);
        }

        private long SellItem(SaveState state, VehicleState vehicle, VehicleInstance cart, string itemName, bool inPerson, double? at = null)
        {
            spots.TryGetValue(cart.id, out var spot);
            var cents = CartPricing.PriceCents(vehicle, itemName, spot?.Neighbourhood);
            if (cents <= 0)
            {
                Log.Error($"Sale refused: {itemName} has no market price.");
                return -1;
            }
            var day = GameDays.SaleDay(at, SaveGameManager.Current.Day);
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
            StreetSales.Record(state, vehicle.VehicleId, day, cents, 1);
            CrewTakings.Accrue(state, vehicle.VehicleId, day, cents);
            SoldCounts.Record(vehicle, day, itemName, 1);
            FoodTrucks.Core.Commissary.Commissaries.LeftVehicle(state.Commissary, itemName, 1);
            var soldAt = (long)Math.Floor(at ?? HireCrew.NowExact());
            var workDay = ServiceDay(soldAt);
            var since = vehicle.StockBatches != null && vehicle.StockBatches.TryGetValue(itemName, out var batches) && batches.Count > 0 ? batches[0].Day : day;
            var book = CartDemand.Book();
            if (book != null && workDay > ReputationBook.ClosedThrough(state) && book.Sold(vehicle, itemName, HireRunner.DayOf(soldAt), workDay))
                Log.Info($"Cart {vehicle.VehicleId}: stale {itemName} sold on working day {workDay} (on board since day {since}).");
            else if (book == null || workDay <= ReputationBook.ClosedThrough(state)) ReputationBook.Consume(vehicle, itemName, 1);
            SalesLedger.RecordCost(state, day, vehicle.VehicleId, (long)Math.Round(Math.Max(0f, entry.pricePerUnit) * 100.0));
            sinceOpen.TryGetValue(vehicle.VehicleId, out var totals);
            sinceOpen[vehicle.VehicleId] = (totals.Sales + 1, totals.Cents + cents);
            try
            {
                cart.ReduceFromCargo(entry, 1);
                if (inPerson)
                {
                    cart.OnItemsInCargoUpdated()?.Invoke();
                    GameEvent.Invoke("ba:gameevent_itemcargochanged");
                    ServingWork.RefreshStock();
                }
            }
            catch (Exception ex) { Log.Error("Cargo update listeners: " + ex); }
            if (inPerson) Log.Info($"Sale: {itemName} for {Money(cents)} on day {day}, {cart.GetAmountByItemName(itemName)} of it left.");
            return cents;
        }

        private static readonly List<string> VendorNotes = new List<string>();

        private static void LogVendorNotes()
        {
            foreach (var line in VendorNotes) Log.Info(line);
            VendorNotes.Clear();
        }

        internal static bool Running => instance != null && instance.enabled;

        internal static VendorFacts VendorFactsOf(VehicleInstance cart, VehicleState? vehicle, DemandTables tables, RuleTables rules)
        {
            var kind = CartDemand.KindOf(cart);
            var menu = CartDemand.MenuOf(tables, cart);
            var items = menu.Problem == MenuProblem.None ? menu.Menu.Items.OrderBy(i => i, StringComparer.Ordinal).ToList() : new List<string>();
            var stock = CartDemand.StockOf(cart, menu.Menu);
            var held = instance?.Reserved(cart.id);
            var gig = Catering.Reserved(cart.id, stock);
            var spot = SpotOf(cart.id);
            var prices = new long[items.Count];
            var units = new int[items.Count];
            for (var i = 0; i < items.Count; i++)
            {
                prices[i] = Math.Max(0, CartPricing.PriceCents(vehicle, items[i], spot?.Neighbourhood));
                stock.TryGetValue(items[i], out var have);
                var taken = 0;
                if (held != null && held.TryGetValue(items[i], out var h)) taken += h;
                if (gig.TryGetValue(items[i], out var g)) taken += g;
                units[i] = Math.Max(0, have - taken);
            }
            var meals = 0;
            if (vehicle != null && vehicle.IsOpen)
            {
                var window = Service(rules, kind, vehicle, !Skipping && PlayerIsAtCart(cart));
                var onSale = items.Where((_, i) => units[i] > 0).ToList();
                meals = VendorGeometry.MealsPerHour(ServiceRate.OrderMinutes(tables, onSale, null), window.Speed, window.Servers);
            }
            var controller = VehicleHelper.GetVehicleController(cart);
            Vector3 at;
            float heading = 0f;
            if (controller != null)
            {
                at = controller.transform.position;
                heading = controller.transform.eulerAngles.y;
            }
            else if (vehicle?.HireStop is int stop && vehicle.Schedule?.Stops is { } stops && stop < stops.Count)
            {
                at = new Vector3((float)stops[stop].X, (float)stops[stop].Y, (float)stops[stop].Z);
                heading = (float)stops[stop].Heading;
            }
            else at = (Vector3)cart.position;
            var side = kind == "truck" ? 1 : CartQueueView.QueueSide(cart.id) ?? 1;
            var place = VendorGeometry.Window(kind, side, at.x, at.z, heading);
            return new VendorFacts
            {
                VehicleId = cart.id, Kind = kind, X = (float)place.X, Y = at.y, Z = (float)place.Z,
                QueueX = (float)place.QueueX, QueueY = at.y, QueueZ = (float)place.QueueZ, QueueHeading = (float)place.QueueHeading,
                MealsPerHour = meals, Items = items, PriceCents = prices, Units = units
            };
        }

        internal static long VendorSellOne(string vehicleId, string item, long minute)
        {
            var self = instance;
            var state = FoodTrucksSession.CurrentState;
            var vehicle = StreetSales.Find(state, vehicleId);
            var cart = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == vehicleId);
            if (self == null || !self.enabled || state == null || !FoodTrucksSession.CanWrite || vehicle == null || cart == null) return -1;
            var cents = self.SellItem(state, vehicle, cart, item, inPerson: false, at: minute);
            if (cents < 0) return -1;
            self.crewBatch.TryGetValue(vehicleId, out var batch);
            self.crewBatch[vehicleId] = (batch.Customers + 1, batch.Items + 1, batch.Cents + cents);
            self.FlushCrewSales(vehicle, cart, minute);
            FoodTrucksSession.MarkChanged();
            return cents;
        }

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

        internal static bool CanOfferClean(VehicleInstance? cart)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || cart == null || FoodTrucksMod.KindOf(cart.vehicleTypeName) == null) return false;
            if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId) || InstanceBehavior<UIs>.Instance?.timeMachine?.isRunning == true) return false;
            return state.Commissary.Spaces.Any(s => s.VehicleId == cart.id) && Commissary.OwnBay(state, cart, out _) != null;
        }

        private const int CleaningMinutes = 20;

        internal static int StockUnits(VehicleInstance cart)
        {
            var tables = DemandData.Tables;
            return tables == null ? 0 : CargoRules.FoodUnits(tables, CargoLimit.Lines(cart));
        }

        internal static string Money(long cents) =>
            TaxCalculationHelper.ToCurrencyFormat(TaxCalculationHelper.RoundCurrency(cents / 100f));

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
