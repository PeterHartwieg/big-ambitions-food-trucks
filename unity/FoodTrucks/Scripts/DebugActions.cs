#nullable enable
using System;
using System.Collections.Generic;
using Dialogs;
using FoodTrucks.Core.Saves;
using Helpers;
using Streets;
using IngameDebugConsole;
using UI.Notification;
using UnityEngine;
using UnityEngine.InputSystem;
using Vehicles.VehicleTypes;

namespace FoodTrucks
{
    // Milestone 0 spike actions. Ctrl+F6 to Ctrl+F10, or console commands when the game runs with -console.
    internal sealed class DebugActions : MonoBehaviour
    {
        private const float TestSaleAmount = 10f;
        // Above the game's $150,000 income tax threshold, so Econoview shows a projected tax.
        private const long TaxFixtureCents = 200_000_00;
        private static readonly Action SpawnTruckAction = () => SpawnVehicle(FoodTrucksMod.TruckTypeName);
        private static readonly Action SpawnCartAction = () => SpawnVehicle(FoodTrucksMod.CartTypeName);
        private static readonly Action SaleAction = TestSale;
        private static readonly Action TaxFixtureAction = TaxFixture;
        private static readonly Action NpcAction = ToggleNpc;
        private static readonly Action StatusAction = ShowStatus;

        private static StandingNpc? npc;

        private void OnEnable()
        {
            try
            {
                CommandHelper.AddCommand("foodtrucks_spawn_truck", "Food Trucks: spawn the test truck next to you", SpawnTruckAction);
                CommandHelper.AddCommand("foodtrucks_spawn_cart", "Food Trucks: spawn the test cart next to you", SpawnCartAction);
                CommandHelper.AddCommand("foodtrucks_sale", "Food Trucks: record a $10 test sale", SaleAction);
                CommandHelper.AddCommand("foodtrucks_tax_fixture", "Food Trucks: record $200,000 of street food yesterday, ledger only", TaxFixtureAction);
                CommandHelper.AddCommand("foodtrucks_npc", "Food Trucks: spawn or remove the queue test character", NpcAction);
                CommandHelper.AddCommand("foodtrucks_status", "Food Trucks: show the mod's save data", StatusAction);
            }
            catch (Exception ex) { Log.Error("Console commands not registered: " + ex.Message); }
            Log.Info("Debug actions ready: Ctrl+F6 truck, Ctrl+F7 cart, Ctrl+F8 test sale, Ctrl+Shift+F8 tax fixture, Ctrl+F9 test customer, Ctrl+F10 status.");
        }

        private void OnDisable()
        {
            try
            {
                CommandHelper.RemoveCommand(SpawnTruckAction);
                CommandHelper.RemoveCommand(SpawnCartAction);
                CommandHelper.RemoveCommand(SaleAction);
                CommandHelper.RemoveCommand(TaxFixtureAction);
                CommandHelper.RemoveCommand(NpcAction);
                CommandHelper.RemoveCommand(StatusAction);
            }
            catch (Exception ex) { Log.Error(ex); }
            if (npc != null) Destroy(npc.gameObject);
            npc = null;
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || SaveGameManager.Current == null || GameManager.ShouldBlockKeyboardShortcuts()) return;
            if (!keyboard.ctrlKey.isPressed) return;

            if (keyboard.f6Key.wasPressedThisFrame) Run("spawn truck", SpawnTruckAction);
            else if (keyboard.f7Key.wasPressedThisFrame) Run("spawn cart", SpawnCartAction);
            else if (keyboard.f8Key.wasPressedThisFrame && keyboard.shiftKey.isPressed) Run("tax fixture", TaxFixtureAction);
            else if (keyboard.f8Key.wasPressedThisFrame) Run("test sale", SaleAction);
            else if (keyboard.f9Key.wasPressedThisFrame) Run("test customer", NpcAction);
            else if (keyboard.f10Key.wasPressedThisFrame) Run("status", StatusAction);
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

        // Spikes 1 and 2: a player-owned vehicle of the mod's type, two metres in front of the player.
        private static void SpawnVehicle(string typeName)
        {
            var type = VehicleTypeHelper.GetVehicleType(typeName);
            if (type == null)
            {
                Notify($"Vehicle type {typeName} is not registered.");
                return;
            }
            var player = PlayerHelper.PlayerController.transform;
            var instance = new VehicleInstance(typeName) { fuel = type.maxFuel * 0.97f };
            // A car's origin is its centre, so it needs more room than the cart, whose origin is the pusher's spot.
            var distance = type.spawnInPlayerObject ? 1.5f : 5f;
            var controller = VehicleHelper.CreateAndSpawnVehicle(
                instance, player.position + player.forward * distance, player.rotation);
            Log.Info($"Spawned {typeName}: id {instance.id}, controller {(controller == null ? "none" : controller.GetType().Name)}.");
            Notify($"Spawned {typeName}.");
        }

        // Spikes 4 and 6: money with the mod's transaction label, recorded for income tax.
        private static void TestSale()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite)
            {
                Notify("Food Trucks save data is not loaded or is read-only.");
                return;
            }
            var info = new TransactionInfo("foodtrucks:transaction_sale",
                new Dictionary<string, string> { { "itemName", "test sale" } });
            if (!GameManager.ChangeMoneySafe(TestSaleAmount, info))
            {
                Notify("Test sale refused by the game.");
                return;
            }
            var day = SaveGameManager.Current.Day;
            SalesLedger.Record(state, day, (long)Math.Round(TestSaleAmount * 100));
            FoodTrucksSession.MarkChanged();
            Log.Info($"Test sale: ${TestSaleAmount} on day {day}; street food revenue that day ${SalesLedger.SumCents(state, day, day) / 100f}.");
            Notify($"Test sale ${TestSaleAmount}. Today's street food: ${SalesLedger.SumCents(state, day, day) / 100f}.");
        }

        // Spike 4: a large ledger-only entry for yesterday, so the tax threshold is crossed. No money moves.
        private static void TaxFixture()
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite)
            {
                Notify("Food Trucks save data is not loaded or is read-only.");
                return;
            }
            var day = SaveGameManager.Current.Day - 1;
            if (day < 1)
            {
                Notify("Sleep until day 2 first: the tax projection only counts finished days.");
                return;
            }
            SalesLedger.Record(state, day, TaxFixtureCents);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Tax fixture: ${TaxFixtureCents / 100} of street food recorded for day {day} (ledger only).");
            ShowStatus();
        }

        // Spike 5: one character walks from eight metres away to a spot in front of the player and stands there.
        private static void ToggleNpc()
        {
            if (npc != null)
            {
                Destroy(npc.gameObject);
                npc = null;
                Notify("Test customer removed.");
                return;
            }
            var player = PlayerHelper.PlayerController.transform;
            var spawn = player.position + player.right * 8f;
            var stand = player.position + player.forward * 2f;
            npc = StandingNpc.Create(spawn, stand, Quaternion.LookRotation(-player.forward));
            Notify(npc == null ? "Test customer could not find a path." : "Test customer walking.");
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
                          $"street food this tax year ${streetFood:N0}.";
            if (finishedDays > 0)
            {
                var sales = TaxCalculationHelper.GetBusinessSales(start, lastFinished);
                var projected = TaxCalculationHelper.CalculateProjectedPeriod(start, lastFinished, finishedDays, year, game.gameVariables.taxPercentage);
                var rows = TaxCalculationHelper.GetBusinessIncome(start, lastFinished);
                var row = rows.Find(r => r.Item1 == "Street food");
                message += $" Taxable sales days {start}-{lastFinished}: ${sales:N0}; statement row ${row.Item2:N0}; projected tax ${projected.TotalTax:N0}.";
            }
            Log.Info(message);
            Notify(message);
        }

        // For choosing the dealer in Milestone 1: every vehicle dealer's contact id and address.
        public static void LogDealerRoster()
        {
            try
            {
                foreach (var building in BuildingHelper.allBuildings)
                {
                    var service = building.SpecialService;
                    if (service == null || service.dialogType != CallDialogType.VehicleStoreDialog) continue;
                    Log.Info($"Vehicle dealer: contact '{service.businessName}', address {building.Address.ToFormattedString()}.");
                }
            }
            catch (Exception ex) { Log.Error("Dealer roster: " + ex.Message); }
        }

        private static void Notify(string message)
        {
            Notifications.Show(NotificationType.Info, "foodtrucks:notification",
                new Dictionary<string, string> { { "message", message } }, 6f, trackOnSaveGame: false);
        }
    }
}
