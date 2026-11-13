#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Dialogs;
using FoodTrucks.Core.Saves;
using Helpers;
using Streets;
using IngameDebugConsole;
using Localizor;
using Parking.UndergroundParking;
using HarmonyLib;
using UI.Notification;
using UnityEngine;
using UnityEngine.InputSystem;
using Vehicles.VehicleTypes;

namespace FoodTrucks
{
    internal sealed class DebugActions : MonoBehaviour
    {
        private static readonly bool Testing = Environment.GetCommandLineArgs().Any(a =>
            string.Equals(a, TestBridge.Flag, StringComparison.OrdinalIgnoreCase));

        private static readonly Action SpawnTruckAction = () => { SpawnVehicle(FoodTrucksMod.TruckTypeName); };
        private static readonly Action LicenceAction = CityLicensing.GrantForTesting;
        private static readonly Action SpawnCartAction = () => { SpawnVehicle(FoodTrucksMod.CartTypeName); };
        private static readonly Action SpawnHandTruckAction = () => { SpawnVehicle("ba:vehicletype_handtruck"); };
        private static readonly Action GripAction = LogGrip;
        private static readonly Action ZonePointAction = MarkZonePoint;
        private static readonly Action TaxFixtureAction = TaxFixture;
        private static readonly Action TaxStatementAction = IssueTaxStatement;
        private static readonly Action StatusAction = ShowStatus;
        private static readonly Action DemandAction = ShowDemand;
        private static readonly Action HireAction = () => Run("hire", HireTestWorker);
        private static readonly Action UninstallAction = Uninstall.Request;
        private static readonly Action EventAction = () => CityPitches.EventHereForTesting(PlayerHelper.GetPosition());
        private static readonly Action GigAction = () => Catering.GigHereForTesting(PlayerHelper.GetPosition());
        private static readonly Action NewItemsAction = () => Run("new items", LoadNewItems);
        private static readonly Action CommissaryWhereAction = () => Run("commissary bays", Commissary.WhereForTesting);
        private static readonly Action CommissaryDeliverAction = () => Run("commissary deliveries", Commissary.DeliverForTesting);
        private static readonly Action CommissaryNightAction = () => Run("overnight check", Commissary.NightForTesting);

        private static readonly (Key Key, bool Shift, bool Alt, string Name, string Label, bool Twice, Action Action)[] Keys =
        {
            (Key.K, false, false, "spawn truck", "food truck", false, SpawnTruckAction),
            (Key.K, false, true, "licence", "licence and permits", false, LicenceAction),
            (Key.J, false, false, "spawn cart", "food cart", false, SpawnCartAction),
            (Key.J, false, true, "spawn hand truck", "vanilla hand truck", false, SpawnHandTruckAction),
            (Key.U, false, false, "new items", "new items into the food vehicle here", false, NewItemsAction),
            (Key.U, false, true, "hire", "hire a worker", false, HireAction),
            (Key.L, false, false, "status", "status", false, StatusAction),
            (Key.L, false, true, "demand here", "demand here", false, DemandAction),
            (Key.G, false, false, "grip", "grip log", false, GripAction),
            (Key.G, false, true, "zone point", "zone point", false, ZonePointAction),
            (Key.V, false, false, "event here", "city event here", true, EventAction),
            (Key.V, false, true, "gig here", "catering gig here", true, GigAction),
            (Key.X, false, false, "tax statement", "tax statement now", true, TaxStatementAction),
            (Key.X, false, true, "tax fixture", "tax fixture", true, TaxFixtureAction),
            (Key.F3, false, false, "commissary bays", "commissary bays", false, CommissaryWhereAction),
            (Key.F3, false, true, "commissary deliveries", "commissary deliveries now", false, CommissaryDeliverAction),
            (Key.F3, true, false, "overnight check", "overnight check dry run", false, CommissaryNightAction),
            (Key.F11, true, false, "uninstall", "uninstall step", false, UninstallAction)
        };

        private const float ConfirmSeconds = 10f;
        private static string? armedName;
        private static float armedUntil = -1f;

        private static string KeyText((Key Key, bool Shift, bool Alt, string Name, string Label, bool Twice, Action Action) k) =>
            $"Ctrl+{(k.Shift ? "Shift+" : "")}{(k.Alt ? "Alt+" : "")}{k.Key}";

        private void OnEnable()
        {
            try
            {
                CommandHelper.AddCommand("foodtrucks_uninstall", "Food Trucks: remove and refund all Food Trucks vehicles (run twice to confirm)", UninstallAction);
                if (!Testing)
                {
                    Log.Info($"Debug actions off (start with {TestBridge.Flag} to enable); uninstall remains on Ctrl+Shift+F11 and foodtrucks_uninstall.");
                    return;
                }
                CommandHelper.AddCommand("foodtrucks_spawn_truck", "Food Trucks: spawn a free food truck next to you (testing)", SpawnTruckAction);
                CommandHelper.AddCommand("foodtrucks_licence", "Food Trucks: a free vendor licence and permits for every food vehicle (throwaway saves only)", LicenceAction);
                CommandHelper.AddCommand("foodtrucks_spawn_cart", "Food Trucks: spawn a free food cart next to you (testing)", SpawnCartAction);
                CommandHelper.AddCommand("foodtrucks_spawn_handtruck", "Food Trucks: spawn a vanilla hand truck to compare the grip", SpawnHandTruckAction);
                CommandHelper.AddCommand("foodtrucks_gig_here", "Food Trucks: a booked catering gig at the nearest place from in 30 minutes (throwaway saves only)", GigAction);
                CommandHelper.AddCommand("foodtrucks_event_here", "Food Trucks: a city event at the nearest event place from now for three hours, one place booked (throwaway saves only)", EventAction);
                CommandHelper.AddCommand("foodtrucks_zone_point", "Food Trucks: log where you stand, for drawing park zones", ZonePointAction);
                CommandHelper.AddCommand("foodtrucks_grip", "Food Trucks: log where the hands and grip points are while pushing", GripAction);
                CommandHelper.AddCommand("foodtrucks_tax_fixture", "Food Trucks: record $200,000 of street food today, ledger only (throwaway saves only)", TaxFixtureAction);
                CommandHelper.AddCommand("foodtrucks_tax_statement", "Food Trucks: issue this year's tax statement now (throwaway saves only)", TaxStatementAction);
                CommandHelper.AddCommand("foodtrucks_hire", "Food Trucks: hire a customer service worker with skill 60, unassigned (throwaway saves only)", HireAction);
                CommandHelper.AddCommand("foodtrucks_demand", "Food Trucks: the demand model's reading for the cart next to you", DemandAction);
                CommandHelper.AddCommand("foodtrucks_status", "Food Trucks: show the mod's save data", StatusAction);
                CommandHelper.AddCommand("foodtrucks_new_items", "Food Trucks: one box of each new food and one of each new machine into the food vehicle next to you, as if bought (throwaway saves only)", NewItemsAction);
                CommandHelper.AddCommand("foodtrucks_commissary_where", "Food Trucks: log where every commissary bay is from you and your vehicle", CommissaryWhereAction);
                CommandHelper.AddCommand("foodtrucks_commissary_deliver", "Food Trucks: deliver every paid commissary order now (throwaway saves only)", CommissaryDeliverAction);
                CommandHelper.AddCommand("foodtrucks_commissary_night", "Food Trucks: what the 04:00 overnight check would say now, without fines", CommissaryNightAction);
            }
            catch (Exception ex) { Log.Error("Console commands not registered: " + ex.Message); }
            Log.Info("Debug actions ready: " + string.Join(", ", Keys.Select(k => $"{KeyText(k)}{(k.Twice ? " twice" : "")} {k.Label}")) + ".");
        }

        private void OnDisable()
        {
            try
            {
                CommandHelper.RemoveCommand(SpawnTruckAction);
                CommandHelper.RemoveCommand(LicenceAction);
                CommandHelper.RemoveCommand(SpawnCartAction);
                CommandHelper.RemoveCommand(SpawnHandTruckAction);
                CommandHelper.RemoveCommand(GripAction);
                CommandHelper.RemoveCommand(EventAction);
                CommandHelper.RemoveCommand(GigAction);
                CommandHelper.RemoveCommand(ZonePointAction);
                CommandHelper.RemoveCommand(TaxFixtureAction);
                CommandHelper.RemoveCommand(TaxStatementAction);
                CommandHelper.RemoveCommand(StatusAction);
                CommandHelper.RemoveCommand(DemandAction);
                CommandHelper.RemoveCommand(HireAction);
                CommandHelper.RemoveCommand(UninstallAction);
                CommandHelper.RemoveCommand(NewItemsAction);
                CommandHelper.RemoveCommand(CommissaryWhereAction);
                CommandHelper.RemoveCommand(CommissaryDeliverAction);
                CommandHelper.RemoveCommand(CommissaryNightAction);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || SaveGameManager.Current == null || GameManager.ShouldBlockKeyboardShortcuts()) return;
            if (!keyboard.ctrlKey.isPressed) return;
            var shift = keyboard.shiftKey.isPressed;
            var alt = keyboard.altKey.isPressed;
            foreach (var k in Keys)
            {
                if (!Testing && k.Name != "uninstall") continue;
                if (k.Shift != shift || k.Alt != alt || !keyboard[k.Key].wasPressedThisFrame) continue;
                if (k.Twice && (armedName != k.Name || Time.unscaledTime > armedUntil))
                {
                    armedName = k.Name;
                    armedUntil = Time.unscaledTime + ConfirmSeconds;
                    Log.Info($"Debug: {KeyText(k)} {k.Label} asked once; it runs on a second press within {ConfirmSeconds:F0} seconds.");
                    Notify($"Food Trucks debug: {k.Label}, changes this save. Press {KeyText(k)} again within {ConfirmSeconds:F0} seconds.");
                    return;
                }
                armedName = null;
                armedUntil = -1f;
                Run(k.Name, k.Action);
                return;
            }
        }

        internal static bool RunByName(string name)
        {
            if (!Testing) return false;
            foreach (var k in Keys)
            {
                if (k.Name != name) continue;
                Log.Info($"Debug: {k.Label} run by the test bridge.");
                Run(k.Name, k.Action);
                return true;
            }
            return false;
        }

        internal static IEnumerable<string> Names => Keys.Select(k => k.Name);

        private static void Run(string name, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                Log.Error($"Action '{name}' failed: {ex}");
                Notify($"Food Trucks: {name} failed, see Player.log");
            }
        }

        private static void HireTestWorker()
        {
            var candidate = RecruitmentHelper.GenerateCandidate(HireCrew.CustomerServiceSkill, 60f, null);
            if (candidate == null)
            {
                Notify("No candidate generated.");
                return;
            }
            EmployeeHelper.HireCandidate(candidate);
            Log.Info($"Debug: hired {HireCrew.Name(candidate)} ({candidate.id}), customer service {HireCrew.Skill(candidate):F0}, ${candidate.hourlyWage:F2} an hour.");
            Notify($"Hired {HireCrew.Name(candidate)}, customer service {HireCrew.Skill(candidate):F0}.");
        }

        private static void LoadNewItems()
        {
            var vehicle = FoodVehicleHere();
            if (vehicle == null)
            {
                Notify("Stand next to a food cart or truck, push the cart or drive the truck.");
                return;
            }
            var lines = new List<string>();
            foreach (var name in ModItems.Machines.Concat(ModItems.Foods))
            {
                if (!ModItems.IsRegistered(name))
                {
                    lines.Add($"{name} not registered");
                    continue;
                }
                var item = BigAmbitions.Items.ItemsGetter.GetByName(name);
                var price = Array.IndexOf(ModItems.Machines, name) >= 0 ? item.DefaultMarketPrice : item.wholesalePrice;
                var before = vehicle.GetAmountByItemName(name);
                vehicle.TryToAddToCargo(new BigAmbitions.Items.CargoInstance(name, item.boxSize, price));
                var added = vehicle.GetAmountByItemName(name) - before;
                lines.Add($"{name.Substring(ModItems.Prefix.Length)} {added}/{item.boxSize}");
            }
            FoodTrucksSession.MarkChanged();
            var message = $"New items into {vehicle.vehicleTypeName} {vehicle.id}: {string.Join(", ", lines)}.";
            Log.Info("Debug: " + message);
            Notify(message);
        }

        private static string StorageSummary(VehicleInstance vehicle)
        {
            var view = StoragePanel.Of(vehicle);
            return view == null ? "no storage data" : StoragePanel.Summary(vehicle, view);
        }

        private static VehicleInstance? FoodVehicleHere()
        {
            if (SaveGameManager.Current.ActiveVehicleId != null)
            {
                var current = VehicleHelper.GetCurrentVehicle();
                if (FoodTrucksMod.KindOf(current?.vehicleTypeName) != null) return current;
            }
            var player = PlayerHelper.GetPosition();
            return VehicleHelper.AllPlayerVehicles
                .Where(v => v != null && FoodTrucksMod.KindOf(v.vehicleInstance?.vehicleTypeName) != null)
                .OrderBy(v => Vector3.Distance(v.transform.position, player))
                .FirstOrDefault(v => Vector3.Distance(v.transform.position, player) <= 10f)?.vehicleInstance;
        }

        internal static VehicleInstance? SpawnVehicle(string typeName, bool announce = true)
        {
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking)
            {
                Notify("Go outside first: vehicles spawn on the street.");
                return null;
            }
            var type = VehicleTypeHelper.GetVehicleType(typeName);
            if (type == null)
            {
                Notify($"Vehicle type {typeName} is not registered.");
                return null;
            }
            var player = PlayerHelper.PlayerController.transform;
            var instance = new VehicleInstance(typeName) { fuel = type.maxFuel * 0.97f };
            var distance = type.spawnInPlayerObject ? 2f : 6f;
            var free = FreeSpawnPlace(player, distance, type.spawnInPlayerObject, out var blocked);
            if (free == null)
            {
                Log.Info($"Not spawned {typeName}: no room in front of or beside the player at x {player.position.x:F1}, z {player.position.z:F1} (in front: {blocked}).");
                Notify("No room to spawn here: move to an open spot.");
                return null;
            }
            var at = free.Value;
            var controller = VehicleHelper.CreateAndSpawnVehicle(instance, at, player.rotation);
            Log.Info($"Spawned {typeName}: id {instance.id}, controller {(controller == null ? "none" : controller.GetType().Name)}, at x {at.x:F1}, z {at.z:F1}.");
            if (announce) Notify($"Spawned {LocalizorManager.GetLocalization(typeName, null)}.");
            return instance;
        }

        private static Vector3? FreeSpawnPlace(Transform player, float distance, bool handVehicle, out string why)
        {
            var half = handVehicle ? new Vector3(0.6f, 0.8f, 1.0f) : new Vector3(1.3f, 1.4f, 3.2f);
            var step = handVehicle ? 1.6f : 3.2f;
            var start = player.position + player.forward * distance;
            var open = LayerHelper.groundLayerMask | (1 << LayerHelper.RoadsLayerIndex) | LayerHelper.parkingAreaLayerMask;
            bool IsPlayer(Collider c) => c.gameObject.layer == LayerHelper.PlayerLayerIndex || c.transform.IsChildOf(player);
            bool InBox(Collider c) => c != null && ((1 << c.gameObject.layer) & (int)open) == 0 && !IsPlayer(c);
            bool Wall(Collider c) => InBox(c) && ((1 << c.gameObject.layer) & (int)LayerHelper.humanLayerMask) == 0 &&
                                     c.gameObject.layer != LayerHelper.ParkedVehiclesLayerIndex && c.gameObject.layer != LayerHelper.AiVehiclesLayerIndex &&
                                     c.GetComponentInParent<ThirdPersonCharacter>() == null && c.GetComponentInParent<VehicleController>() == null &&
                                     Mathf.Max(c.bounds.size.x, c.bounds.size.z) >= 0.8f;
            why = "";
            foreach (var (side, ahead) in new[] { (0, 0), (1, 0), (-1, 0), (2, 0), (-2, 0), (0, 1), (1, 1), (-1, 1), (0, 2) })
            {
                var at = start + player.right * (side * step) + player.forward * (ahead * half.z * 2.4f);
                var inBox = Physics.OverlapBox(at + Vector3.up * (half.y + 0.3f), half, player.rotation, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    .FirstOrDefault(InBox);
                var eye = player.position + Vector3.up * 1.0f;
                var target = at + Vector3.up * 1.0f;
                var wall = Physics.RaycastAll(eye, target - eye, Vector3.Distance(eye, target), Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    .Where(h => Wall(h.collider)).OrderBy(h => h.distance).Select(h => h.collider).FirstOrDefault();
                if (inBox == null && wall == null) return at;
                if (why.Length == 0)
                    why = inBox != null ? $"{BayProps.Describe(inBox)} stands there" : $"{BayProps.Describe(wall!)} is in the way from where you stand";
            }
            return null;
        }

        private const long TaxFixtureCents = 200_000_00;

        private static void TaxFixture()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite)
            {
                Notify("Food Trucks save data is not loaded or is read-only.");
                return;
            }
            var day = SaveGameManager.Current.Day;
            if (!SalesLedger.CanRecord(state, day, TaxFixtureCents))
            {
                Notify("The street food record is full.");
                return;
            }
            SalesLedger.Record(state, day, TaxFixtureCents);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Tax fixture: ${TaxFixtureCents / 100} of street food recorded for day {day} (ledger only).");
            Notify($"Tax fixture: $200,000 of street food recorded for today. No money moved.");
        }

        private static void LogGrip()
        {
            var controller = PlayerHelper.PlayerController;
            var vehicle = VehicleHelper.GetCurrentVehicleBase();
            if (controller == null || vehicle == null)
            {
                Notify("Push a hand truck or the food cart first.");
                return;
            }
            var root = controller.transform;
            string At(Transform? t)
            {
                if (t == null) return "none";
                var position = root.InverseTransformPoint(t.position);
                var rotation = (Quaternion.Inverse(root.rotation) * t.rotation).eulerAngles;
                return $"position {position:F3}, rotation {rotation:F1}";
            }
            var character = controller.Character;
            var truck = vehicle as HandTruck;
            var moving = character != null && character.CurrentVelocity > 0.1f ? "moving" : "standing";
            var message = $"Grip ({vehicle.vehicleInstance?.vehicleTypeName}, {moving}): vehicle {At(vehicle.transform)}; " +
                          $"left point {At(truck?.LHandIKAttachmentPoint)}; right point {At(truck?.RHandIKAttachmentPoint)}; " +
                          $"left hand {At(character?.leftHand)}; right hand {At(character?.rightHand)}; " +
                          $"model {At(PlayerHelper.GetAnimator()?.transform)}.";
            Log.Info(message);
            Notify("Grip logged. Press it once standing and once while walking.");
        }

        private static int zonePoints;

        private static void MarkZonePoint()
        {
            var position = PlayerHelper.GetPosition();
            var tables = DemandData.Tables;
            var neighbourhood = tables == null ? null : World.ReadSpot(tables, position).Neighbourhood;
            zonePoints++;
            Log.Info($"Zone point {zonePoints}: {World.Short(neighbourhood)}, x {position.x:F1}, z {position.z:F1}.");
            Notify($"Point {zonePoints} marked.");
        }

        private static void ShowStatus()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null)
            {
                Notify($"Food Trucks save data not loaded ({FoodTrucksSession.LastLoadStatus}).");
                return;
            }
            var game = SaveGameManager.Current;
            var year = game.gameVariables.daysPerYear;
            var start = game.Day / year * year + 1;
            var lastFinished = game.Day - 1;
            var finishedDays = Math.Max(0, lastFinished - start + 1);
            var streetFood = SalesLedger.SumCents(state, start, game.Day) / 100f;
            var message = $"Food Trucks {FoodTrucksMod.Version}: data {FoodTrucksSession.LastLoadStatus}, save count {state.SaveCount}, " +
                          $"street food this tax year {Money(streetFood)}.";
            if (finishedDays > 0)
            {
                var sales = TaxCalculationHelper.GetBusinessSales(start, lastFinished);
                var projected = TaxCalculationHelper.CalculateProjectedPeriod(start, lastFinished, finishedDays, year, game.gameVariables.taxPercentage);
                var rows = TaxCalculationHelper.GetBusinessIncome(start, lastFinished);
                var row = rows.Find(r => r.Item1 == "Street food");
                message += $" Taxable sales days {start}-{lastFinished}: {Money(sales)}; statement row {Money(row.Item2)}; " +
                           $"projected tax {TaxCalculationHelper.ToCurrencyFormat(projected.TotalTax)}.";
            }
            message += state.Licence.HasVendorLicence ? $" Licence to day {state.Licence.LicenceExpiresOnDay}." : " No licence.";
            foreach (var vehicle in game.VehicleInstances)
            {
                var kind = FoodTrucksMod.KindOf(vehicle.vehicleTypeName);
                if (kind == null) continue;
                var record = StreetSales.Find(state, vehicle.id);
                message += $" {(kind == "truck" ? "Truck" : "Cart")} {Short(vehicle.id)}: {(record?.IsOpen == true ? "open" : "closed")}, " +
                           $"{(record?.HasPermit == true ? "permit" : "no permit")}, stock {CartSelling.StockUnits(vehicle)} ({StorageSummary(vehicle)}), " +
                           $"{record?.LifetimeSales ?? 0} sales, {CartSelling.Money(record?.LifetimeRevenueCents ?? 0)} lifetime.";
            }
            Log.Info(message);
            Notify(message);
        }

        private static string Short(string id) => id.Length > 6 ? id.Substring(0, 6) : id;

        private static void ShowDemand()
        {
            var tables = DemandData.Tables;
            if (tables == null)
            {
                Notify($"Demand data not loaded: {DemandData.Error}");
                return;
            }
            var game = SaveGameManager.Current;
            VehicleController? controller = null;
            if (game.ActiveVehicleId != null && FoodTrucksMod.KindOf(VehicleHelper.GetCurrentVehicle()?.vehicleTypeName) != null)
                controller = VehicleHelper.GetCurrentVehicleBase();
            if (controller == null)
            {
                var player = PlayerHelper.GetPosition();
                controller = VehicleHelper.AllPlayerVehicles
                    .Where(v => v != null && FoodTrucksMod.KindOf(v.vehicleInstance?.vehicleTypeName) != null)
                    .OrderBy(v => Vector3.Distance(v.transform.position, player))
                    .FirstOrDefault(v => Vector3.Distance(v.transform.position, player) <= 10f);
            }
            if (controller == null)
            {
                Notify("Stand next to a food cart or truck, push the cart or drive the truck.");
                return;
            }
            var position = controller.transform.position;
            var spot = World.ReadSpot(tables, position);
            var legality = Legality.Check(position);
            var plan = CartDemand.Plan(tables, controller.vehicleInstance, spot);
            var message = $"Day {game.Day} ({World.DayOfWeekOf(game.Day)}) {TimeHelper.CurrentHour:D2}:{(int)TimeHelper.CurrentMinute:D2}, " +
                          $"rain {(BigAmbitions.DayNightCycle.RainHelper.isRaining ? "yes" : "no")}. Spot: {World.Describe(spot)}. " +
                          $"Open for business: {legality.Detail}{(legality.RoadBelow ? " (road layer below)" : "")}. {CartDemand.Describe(plan)}";
            Log.Info("Demand here: " + message);
            Notify(message);
        }

        private static string Money(float value) => TaxCalculationHelper.ToCurrencyFormat(TaxCalculationHelper.RoundCurrency(value));

        private static void IssueTaxStatement()
        {
            if (TaxHelper.HasAnyTaxesToPay())
            {
                Notify("A tax bill is still unpaid; no new statement issued.");
                return;
            }
            var method = AccessTools.Method(typeof(TaxHelper), "ExecutePlayerTaxesEvent");
            if (method == null)
            {
                Notify("TaxHelper.ExecutePlayerTaxesEvent not found in this game version.");
                return;
            }
            method.Invoke(null, null);
            Log.Info("Tax statement issued on request.");
            Notify("Tax statement sent: open the IRS message on your phone.");
        }

        private static void Notify(string message)
        {
            Notices.Show(NotificationType.Info, "foodtrucks:notification",
                new Dictionary<string, string> { { "message", message } }, 6f, trackOnSaveGame: false);
        }
    }
}
