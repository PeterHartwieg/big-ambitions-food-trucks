#nullable enable
using System;
using System.Collections.Generic;
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
    // Debug and maintenance actions: Ctrl+F6 test truck, Ctrl+F7 food cart, Ctrl+Shift+F8 tax fixture, Ctrl+F10 status, Ctrl+Shift+F10 tax statement,
    // Ctrl+Shift+F12 uninstall step. Also console commands when the game runs with -console.
    internal sealed class DebugActions : MonoBehaviour
    {
        private static readonly Action SpawnTruckAction = () => SpawnVehicle(FoodTrucksMod.TruckTypeName);
        private static readonly Action SpawnCartAction = () => SpawnVehicle(FoodTrucksMod.CartTypeName);
        private static readonly Action TaxFixtureAction = TaxFixture;
        private static readonly Action TaxStatementAction = IssueTaxStatement;
        private static readonly Action StatusAction = ShowStatus;
        private static readonly Action UninstallAction = Uninstall.Request;

        private void OnEnable()
        {
            try
            {
                CommandHelper.AddCommand("foodtrucks_spawn_truck", "Food Trucks: spawn the test truck next to you", SpawnTruckAction);
                CommandHelper.AddCommand("foodtrucks_spawn_cart", "Food Trucks: spawn a free food cart next to you (testing)", SpawnCartAction);
                CommandHelper.AddCommand("foodtrucks_tax_fixture", "Food Trucks: record $200,000 of street food today, ledger only (throwaway saves only)", TaxFixtureAction);
                CommandHelper.AddCommand("foodtrucks_tax_statement", "Food Trucks: issue this year's tax statement now (throwaway saves only)", TaxStatementAction);
                CommandHelper.AddCommand("foodtrucks_status", "Food Trucks: show the mod's save data", StatusAction);
                CommandHelper.AddCommand("foodtrucks_uninstall", "Food Trucks: remove and refund all Food Trucks vehicles (run twice to confirm)", UninstallAction);
            }
            catch (Exception ex) { Log.Error("Console commands not registered: " + ex.Message); }
            Log.Info("Debug actions ready: Ctrl+F6 test truck, Ctrl+F7 food cart, Ctrl+Shift+F8 tax fixture, Ctrl+F10 status, Ctrl+Shift+F10 tax statement now, Ctrl+Shift+F12 uninstall step.");
        }

        private void OnDisable()
        {
            try
            {
                CommandHelper.RemoveCommand(SpawnTruckAction);
                CommandHelper.RemoveCommand(SpawnCartAction);
                CommandHelper.RemoveCommand(TaxFixtureAction);
                CommandHelper.RemoveCommand(TaxStatementAction);
                CommandHelper.RemoveCommand(StatusAction);
                CommandHelper.RemoveCommand(UninstallAction);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || SaveGameManager.Current == null || GameManager.ShouldBlockKeyboardShortcuts()) return;
            if (!keyboard.ctrlKey.isPressed) return;

            if (keyboard.f6Key.wasPressedThisFrame) Run("spawn truck", SpawnTruckAction);
            else if (keyboard.f7Key.wasPressedThisFrame) Run("spawn cart", SpawnCartAction);
            else if (keyboard.f8Key.wasPressedThisFrame && keyboard.shiftKey.isPressed) Run("tax fixture", TaxFixtureAction);
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

        // A free player-owned vehicle in front of the player: the test truck for the storage and sleep
        // checks, or a cart where pushing one from the dealer would take too long.
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
            var distance = type.spawnInPlayerObject ? 2f : 5f;
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
            foreach (var vehicle in game.VehicleInstances)
            {
                if (vehicle.vehicleTypeName != FoodTrucksMod.CartTypeName) continue;
                var record = StreetSales.Find(state, vehicle.id);
                message += $" Cart {Short(vehicle.id)}: {(record?.IsOpen == true ? "open" : "closed")}, stock {CartSelling.StockUnits(vehicle)}, " +
                           $"{record?.LifetimeSales ?? 0} sales, {CartSelling.Money(record?.LifetimeRevenueCents ?? 0)} lifetime.";
            }
            Log.Info(message);
            Notify(message);
        }

        private static string Short(string id) => id.Length > 6 ? id.Substring(0, 6) : id;

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
