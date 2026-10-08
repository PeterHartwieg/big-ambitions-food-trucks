#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Dialogs;
using FoodTrucks.Core.Saves;
using Helpers;
using Streets;
using IngameDebugConsole;
using Parking.UndergroundParking;
using HarmonyLib;
using UI.Notification;
using UnityEngine;
using UnityEngine.InputSystem;
using Vehicles.VehicleTypes;

namespace FoodTrucks
{
    // Debug and maintenance actions: Ctrl+F6 food truck, Ctrl+Shift+F6 licence and permits, Ctrl+F7 food cart, Ctrl+Shift+F7 vanilla hand truck, Ctrl+F11 grip log, Ctrl+F8 zone point, Ctrl+Shift+F8 tax fixture, Ctrl+F9 demand here, Ctrl+Shift+F9 hire a worker, Ctrl+F10 status, Ctrl+Shift+F10 tax statement,
    // Ctrl+Shift+F12 uninstall step. Also console commands when the game runs with -console.
    internal sealed class DebugActions : MonoBehaviour
    {
        private static readonly Action SpawnTruckAction = () => SpawnVehicle(FoodTrucksMod.TruckTypeName);
        private static readonly Action LicenceAction = CityLicensing.GrantForTesting;
        private static readonly Action SpawnCartAction = () => SpawnVehicle(FoodTrucksMod.CartTypeName);
        private static readonly Action SpawnHandTruckAction = () => SpawnVehicle("ba:vehicletype_handtruck");
        private static readonly Action GripAction = LogGrip;
        private static readonly Action ZonePointAction = MarkZonePoint;
        private static readonly Action TaxFixtureAction = TaxFixture;
        private static readonly Action TaxStatementAction = IssueTaxStatement;
        private static readonly Action StatusAction = ShowStatus;
        private static readonly Action DemandAction = ShowDemand;
        private static readonly Action HireAction = HireTestWorker;
        private static readonly Action UninstallAction = Uninstall.Request;

        private void OnEnable()
        {
            try
            {
                CommandHelper.AddCommand("foodtrucks_spawn_truck", "Food Trucks: spawn a free food truck next to you (testing)", SpawnTruckAction);
                CommandHelper.AddCommand("foodtrucks_licence", "Food Trucks: a free vendor licence and permits for every food vehicle (throwaway saves only)", LicenceAction);
                CommandHelper.AddCommand("foodtrucks_spawn_cart", "Food Trucks: spawn a free food cart next to you (testing)", SpawnCartAction);
                CommandHelper.AddCommand("foodtrucks_spawn_handtruck", "Food Trucks: spawn a vanilla hand truck to compare the grip", SpawnHandTruckAction);
                CommandHelper.AddCommand("foodtrucks_zone_point", "Food Trucks: log where you stand, for drawing park zones", ZonePointAction);
                CommandHelper.AddCommand("foodtrucks_grip", "Food Trucks: log where the hands and grip points are while pushing", GripAction);
                CommandHelper.AddCommand("foodtrucks_tax_fixture", "Food Trucks: record $200,000 of street food today, ledger only (throwaway saves only)", TaxFixtureAction);
                CommandHelper.AddCommand("foodtrucks_tax_statement", "Food Trucks: issue this year's tax statement now (throwaway saves only)", TaxStatementAction);
                CommandHelper.AddCommand("foodtrucks_hire", "Food Trucks: hire a customer service worker with skill 60, unassigned (throwaway saves only)", HireAction);
                CommandHelper.AddCommand("foodtrucks_demand", "Food Trucks: the demand model's reading for the cart next to you", DemandAction);
                CommandHelper.AddCommand("foodtrucks_status", "Food Trucks: show the mod's save data", StatusAction);
                CommandHelper.AddCommand("foodtrucks_uninstall", "Food Trucks: remove and refund all Food Trucks vehicles (run twice to confirm)", UninstallAction);
            }
            catch (Exception ex) { Log.Error("Console commands not registered: " + ex.Message); }
            Log.Info("Debug actions ready: Ctrl+F6 food truck, Ctrl+Shift+F6 licence and permits, Ctrl+F7 food cart, Ctrl+Shift+F7 vanilla hand truck, Ctrl+F11 grip log, Ctrl+F8 zone point, Ctrl+Shift+F8 tax fixture, Ctrl+F9 demand here, Ctrl+Shift+F9 hire a worker, Ctrl+F10 status, Ctrl+Shift+F10 tax statement now, Ctrl+Shift+F12 uninstall step.");
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
                CommandHelper.RemoveCommand(ZonePointAction);
                CommandHelper.RemoveCommand(TaxFixtureAction);
                CommandHelper.RemoveCommand(TaxStatementAction);
                CommandHelper.RemoveCommand(StatusAction);
                CommandHelper.RemoveCommand(DemandAction);
                CommandHelper.RemoveCommand(HireAction);
                CommandHelper.RemoveCommand(UninstallAction);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || SaveGameManager.Current == null || GameManager.ShouldBlockKeyboardShortcuts()) return;
            if (!keyboard.ctrlKey.isPressed) return;

            if (keyboard.f6Key.wasPressedThisFrame && keyboard.shiftKey.isPressed) Run("licence", LicenceAction);
            else if (keyboard.f6Key.wasPressedThisFrame) Run("spawn truck", SpawnTruckAction);
            else if (keyboard.f7Key.wasPressedThisFrame && keyboard.shiftKey.isPressed) Run("spawn hand truck", SpawnHandTruckAction);
            else if (keyboard.f7Key.wasPressedThisFrame) Run("spawn cart", SpawnCartAction);
            else if (keyboard.f11Key.wasPressedThisFrame) Run("grip", GripAction);
            else if (keyboard.f8Key.wasPressedThisFrame && keyboard.shiftKey.isPressed) Run("tax fixture", TaxFixtureAction);
            else if (keyboard.f8Key.wasPressedThisFrame) Run("zone point", ZonePointAction);
            else if (keyboard.f9Key.wasPressedThisFrame && keyboard.shiftKey.isPressed) Run("hire", HireAction);
            else if (keyboard.f9Key.wasPressedThisFrame) Run("demand here", DemandAction);
            else if (keyboard.f10Key.wasPressedThisFrame && keyboard.shiftKey.isPressed) Run("tax statement", TaxStatementAction);
            else if (keyboard.f10Key.wasPressedThisFrame) Run("status", StatusAction);
            else if (keyboard.f12Key.wasPressedThisFrame && keyboard.shiftKey.isPressed) Run("uninstall", UninstallAction);
        }

        private static void Run(string name, Action action)
        {
            try { action(); }
            catch (Exception ex)
            {
                Log.Error($"Action '{name}' failed: {ex}");
                Notify($"Food Trucks: {name} failed, see Player.log");
            }
        }

        // A hired, unassigned customer service worker, the way the game's own candidate command makes one,
        // so a test session needn't run a recruitment campaign.
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

        // A free player-owned vehicle in front of the player, where buying one at the dealer would take too long.
        private static void SpawnVehicle(string typeName)
        {
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking)
            {
                Notify("Go outside first: vehicles spawn on the street.");
                return;
            }
            var type = VehicleTypeHelper.GetVehicleType(typeName);
            if (type == null)
            {
                Notify($"Vehicle type {typeName} is not registered.");
                return;
            }
            var player = PlayerHelper.PlayerController.transform;
            var instance = new VehicleInstance(typeName) { fuel = type.maxFuel * 0.97f };
            // Both origins are vehicle centres; the car is longer.
            var distance = type.spawnInPlayerObject ? 2f : 6f;
            var controller = VehicleHelper.CreateAndSpawnVehicle(
                instance, player.position + player.forward * distance, player.rotation);
            Log.Info($"Spawned {typeName}: id {instance.id}, controller {(controller == null ? "none" : controller.GetType().Name)}.");
            Notify($"Spawned {typeName}.");
        }

        // A large ledger-only entry for today, so the next tax statement has street food income
        // above the cart's deduction and creates a bill. No money moves.
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

        // Grip measurement: while pushing any hand vehicle, logs the grip points and the hand bones relative
        // to the player root. Pushing the vanilla hand truck and then the cart gives the numbers to copy:
        // the vanilla truck's animator tilts it while pushed, so its prefab's rest pose isn't what the hands follow.
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

        // For drawing park and waterfront zones: walk a park's outline and press this at each corner.
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
            // Same period and figures as Econoview's projected tax row (EconoViewTaxes.SetupProjectedTaxRow).
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
                           $"{(record?.HasPermit == true ? "permit" : "no permit")}, stock {CartSelling.StockUnits(vehicle)}, " +
                           $"{record?.LifetimeSales ?? 0} sales, {CartSelling.Money(record?.LifetimeRevenueCents ?? 0)} lifetime.";
            }
            Log.Info(message);
            Notify(message);
        }

        private static string Short(string id) => id.Length > 6 ? id.Substring(0, 6) : id;

        // For the balance check: what the model sees at the cart you push or stand next to (within 10 m),
        // as if it opened now: the spot, whether it's legal, and the sales rate with its fitted menu.
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
                          $"Open here: {legality.Detail}{(legality.RoadBelow ? " (road layer below)" : "")}. {CartDemand.Describe(plan)}";
            Log.Info("Demand here: " + message);
            Notify(message);
        }

        private static string Money(float value) => TaxCalculationHelper.ToCurrencyFormat(TaxCalculationHelper.RoundCurrency(value));

        // The game's own year-end tax event, now. It bills the save and starts a new tax year,
        // so it is for throwaway saves only. The statement arrives as a text message from the IRS.
        private static void IssueTaxStatement()
        {
            // A second statement on top of an unpaid one would stack bills the test can't separate.
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
            Notifications.Show(NotificationType.Info, "foodtrucks:notification",
                new Dictionary<string, string> { { "message", message } }, 6f, trackOnSaveGame: false);
        }
    }
}
