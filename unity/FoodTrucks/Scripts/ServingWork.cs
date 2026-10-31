#nullable enable
using System;
using System.Linq;
using BigAmbitions.DayNightCycle;
using BigAmbitions.InputSystem;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Helpers;
using PlayerActivity;
using UI;
using UI.Elements;
using UI.ItemPanel;
using UnityEngine;
using UnityEngine.UI;

namespace FoodTrucks
{
    // Native activity controls and cargo HUD only. Sales, stock, dates and takings remain CartSelling/Core's.
    internal static class ServingWork
    {
        private static ServingActivity? active;
        private static ServingActivity? armed;
        private static string? skipVehicle;
        private static bool skipClosed;
        private static VehicleInstance? cargo;
        private static ItemPanelUI? stockPanel;
        private static bool ending;
        private static bool catchingUp;
        private static PlayerActivityUI? Panel => InstanceBehavior<UIs>.Instance?.playerActivityUI;
        internal static ServingActivity? Current => PlayerActivityUI.IsPanelOpen ? Panel?.GetCurrentActivity as ServingActivity : null;
        internal static string? VehicleId => active?.VehicleId;
        internal static bool WasWorkSkip(string id) => skipVehicle == id;
        internal static bool IsSkipping(string id) => skipVehicle == id && CartSelling.Skipping;
        internal static bool Accelerated => InstanceBehavior<UIs>.Instance?.gameSpeed?.isFastForwarding == true || Time.timeScale > 1.01f;

        internal static void UpdateCart(CartServing stance)
        {
            if (active is CartWorkActivity cart && (!stance.PlayerAt(cart.VehicleId) || Current != cart || SaveGameManager.Current.Energy <= 0))
                cart.Finish();
            if (active == null && stance.VehicleId is { } id && !PlayerActivityUI.IsPanelOpen && !CartSelling.Skipping && SaveGameManager.Current.Energy > 0)
            {
                var activity = new CartWorkActivity(id, () => stance.Release("stopped working"));
                PlayerActivityUI.Show(activity);
                if (Panel?.GetCurrentActivity == activity) Start(activity);
            }
            if (active is CartWorkActivity starting)
            {
                starting.Advance();
                starting.ClearKeys();
            }
        }

        internal static void Start(ServingActivity activity)
        {
            if (active == activity) return;
            if (active != null) End(active);
            try
            {
                active = activity;
                // WorkActivity uses the station's consumption. Read it from the native register prefab too,
                // rather than embedding a second set of needs rates in the selling simulation.
                var register = PrefabHelper.LoadItemControllerFromPrefab("ba:itemname_cashregister") as EmployeeStationController;
                if (register == null) throw new InvalidOperationException("The native cash register work-energy settings are unavailable.");
                EnergyHelper.RemoveEnergySpender("move");
                EnergyHelper.AddEnergySpender("work", register.workEnergyConsumption);
                var vehicle = SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v.id == activity.VehicleId);
                var controller = vehicle == null ? null : VehicleHelper.GetVehicleController(vehicle);
                stockPanel = InstanceBehavior<UIs>.Instance?.playerHUD?.itemPanelUI;
                if (controller != null && stockPanel != null)
                {
                    cargo = vehicle;
                    stockPanel.SetVehicle(controller);
                    CartButton.Hide();
                    ReadOnlyButtons(stockPanel);
                    cargo!.AddCallToOnItemsInCargoUpdated(RefreshStock);
                }
            }
            catch (Exception ex)
            {
                Log.Error("Serving work: " + ex);
                activity.Finish();
                activity.MarkFinished();
            }
        }

        internal static void End(ServingActivity activity)
        {
            if (active != activity || ending) return;
            var oldCargo = cargo;
            var panel = stockPanel;
            // Teardown may run after Unity has destroyed the HUD. Release ownership before any callbacks
            // or game calls, so even a failed cleanup cannot block this or the next session's work.
            active = null;
            cargo = null;
            stockPanel = null;
            ending = true;
            try
            {
                // Book every already simulated order before walking/driving/closing can change service or stock.
                try { CartSelling.PayAllHeldNow(); }
                catch (Exception ex) { Log.Error("Serving work, held orders: " + ex); }
                try { oldCargo?.RemoveCallFromOnItemsInCargoUpdated(RefreshStock); }
                catch (Exception ex) { Log.Error("Serving work, cargo listener: " + ex); }
                try
                {
                    // Toggle(false) clears GameManager.selectedVehicle without a null check. While the city unloads or
                    // the game quits, the manager is gone or no longer returned, and the scene removes the panel anyway
                    // (NullReferenceException in Toggle after a bridge quit while serving, issue #19).
                    var game = GameManager.isCitySceneBeingUnloaded ? null : InstanceBehavior<GameManager>.Instance;
                    if (panel != null && game != null && string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId)) panel.Toggle(false);
                }
                catch (Exception ex) { Log.Error("Serving work, stock panel: " + ex); }
                try { EnergyHelper.RemoveEnergySpender("work"); }
                catch (Exception ex) { Log.Error("Serving work, energy: " + ex); }
                try
                {
                    var ui = InstanceBehavior<UIs>.Instance;
                    var speed = ui != null ? ui.gameSpeed : null;
                    if (speed != null && speed.isFastForwarding)
                    {
                        speed.isFastForwarding = false;
                        speed.Reset();
                    }
                }
                catch (Exception ex) { Log.Error("Serving work, speed: " + ex); }
                // Keep skipVehicle until the end event has resumed the sales clocks, irrespective of event order.
                try
                {
                    var ui = InstanceBehavior<UIs>.Instance;
                    var machine = ui != null ? ui.timeMachine : null;
                    if (machine != null && machine.isRunning && skipVehicle == activity.VehicleId) machine.CancelTimeMachine();
                }
                catch (Exception ex) { Log.Error("Serving work, time machine: " + ex); }
            }
            finally { ending = false; }
        }

        internal static void Reset()
        {
            try
            {
                var activity = active;
                if (activity != null)
                {
                    activity.MarkFinished();
                    End(activity); // Also clean up an activity that was already marked Finished.
                }
            }
            finally
            {
                active = null;
                armed = null;
                skipVehicle = null;
                skipClosed = false;
                cargo = null;
                stockPanel = null;
                ending = false;
                catchingUp = false;
            }
        }

        internal static void Arm(ServingActivity activity) => armed = activity;
        internal static void SkipStarted()
        {
            skipVehicle = armed != null && armed == Current ? armed.VehicleId : null;
            skipClosed = false;
            armed = null;
        }
        internal static void SkipEnded() { skipVehicle = null; skipClosed = false; }
        internal static void WindowClosed(string id) { if (skipVehicle == id) skipClosed = true; }

        internal static void RefreshStock() => stockPanel?.SetupCargoSlots();

        internal static void BeforeLeaving(string id)
        {
            if (catchingUp || active?.VehicleId != id) return;
            catchingUp = true;
            try
            {
                // Keep the old server in place until the last elapsed minute has sold. Routes decide their
                // closing boundaries first, so a late frame cannot sell past the end of a crew stop.
                var now = HireCrew.NowExact();
                HireRoutes.CatchUpTo((long)Math.Floor(now));
                CartSelling.CatchUpTo(now);
            }
            finally { catchingUp = false; }
        }

        internal static void DrivingPrefix()
        {
            var activity = active;
            if (activity == null) return;
            try { activity.Finish(); }
            catch (Exception ex) { Log.Error("Serving work, entering vehicle: " + ex); }
            finally
            {
                activity.MarkFinished();
                End(activity);
            }
        }

        // Runs BEFORE SetVehicle: its nested button/row updates must already belong to the game when
        // entering a vehicle, including entry without a navigation event or an intervening LateUpdate.
        internal static void OtherPanel() => stockPanel = null;
        internal static void VehiclePanel(VehicleController vehicle)
        {
            if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId))
            {
                OtherPanel(); // Never hide or clear the vehicle the game has just selected.
                DrivingPrefix();
            }
            else if (vehicle == null || vehicle.vehicleInstance != cargo) OtherPanel();
        }

        // SetVehicle does not select/enter the vehicle. These buttons are driving/loading controls, not serving.
        internal static void ReadOnlyButtons(ItemPanelUI __instance)
        {
            if (stockPanel != __instance || active == null || !string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId)) return;
            foreach (var button in new[] { __instance.parkButton, __instance.autoParkButton, __instance.sleepButton,
                __instance.sellButton, __instance.grabButton, __instance.discardButton, __instance.leaveButton, __instance.placeButton, __instance.useMopButton })
                if (button != null) button.gameObject.SetActive(false);
        }

        internal static void ReadOnlyCargo(ItemPanelUI __instance)
        {
            if (stockPanel != __instance || active == null || !string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId)) return;
            foreach (var button in __instance.cargoContainer.GetComponentsInChildren<Button>(true))
            {
                // Normal driving/pushing creates its rows from this template afterwards. Keep it interactive.
                if (button.transform.IsChildOf(__instance.cargoSlotTemplate)) continue;
                button.interactable = false;
            }
        }

        // The game gates its toggle and shortcut on being indoors. Lift only that gate for our activity;
        // the toggle's original listener still sets GameSpeed(TimeSpeed.FastForward) and resets it afterwards.
        internal static void SpeedOptionsPostfix(PlayerActivityUI __instance, Toggle ___fastForwardToggle)
        {
            if (__instance.GetCurrentActivity is ServingActivity activity)
                ___fastForwardToggle.transform.parent.gameObject.SetActive(activity.HasFastForward());
        }

        internal static bool ActionPrefix(PlayerActivityUI __instance, PlayerAction playerAction, Toggle ___fastForwardToggle)
        {
            if (!(__instance.GetCurrentActivity is ServingActivity activity) || (int)playerAction != 2 || !activity.HasFastForward()) return true;
            InputActionHelper.Reset(playerAction);
            ___fastForwardToggle.isOn = !___fastForwardToggle.isOn;
            return false;
        }

        internal static void TickPrefix(ref float deltaTimeWithMultiplier)
        {
            if (skipVehicle == null || !CartSelling.Skipping) return;
            deltaTimeWithMultiplier = skipClosed || active == null ? 0 : (float)ServingTime.Advance(HireCrew.NowExact(), deltaTimeWithMultiplier);
        }

        internal static void TickPostfix()
        {
            if (skipVehicle == null || !CartSelling.Skipping) return;
            CartSelling.CatchUpTo(HireCrew.NowExact());
            if (skipClosed || StreetSales.Find(FoodTrucksSession.CurrentState, skipVehicle)?.IsOpen != true || active == null)
                InstanceBehavior<UIs>.Instance?.timeMachine?.CancelTimeMachine();
        }

        // Bridge calls the actual toggle/skip button, including the panel's duration and end-of-skip bookkeeping.
        internal static void Control(string id, string op, int? minutes, bool enabled)
        {
            var activity = Current;
            if (activity == null || activity.VehicleId != id || activity.GetState() != PlayerActivityState.Running)
                throw new InvalidOperationException("Stand at this vehicle's serving window and wait for its work panel first.");
            if (CartSelling.Skipping) throw new InvalidOperationException("A time skip is running; wait for it to end.");
            if (op == "fast_forward")
            {
                var toggle = AccessTools.Field(typeof(PlayerActivityUI), "fastForwardToggle").GetValue(Panel) as Toggle;
                if (toggle == null) throw new InvalidOperationException("The work fast-forward toggle is unavailable.");
                toggle.isOn = enabled;
            }
            else
            {
                if (!minutes.HasValue || minutes < ServingTime.MinimumMinutes || minutes > ServingTime.MaximumMinutes)
                    throw new InvalidOperationException($"minutes must be {ServingTime.MinimumMinutes} to {ServingTime.MaximumMinutes}.");
                Panel!.ChangeSliderValue(minutes.Value - activity.GetCurrentSliderValue(), exactMinutes: true);
                Panel.ForceTimeMachine();
            }
        }
    }

    internal abstract class ServingActivity : IPlayerActivity
    {
        internal string VehicleId { get; }
        protected PlayerActivityState state = PlayerActivityState.NotStarted;
        private PlayerActivityState beforeFinishing;
        private int minutes = ServingTime.DefaultMinutes;
        private int stateFrame = Time.frameCount;
        protected ServingActivity(string id) { VehicleId = id; }
        public bool RequiresEnergy() => true;
        public bool BlocksAutoSave() => false;
        public PlayerActivityState GetState() => state;
        public PlayerActivityState GetStateBeforeFinishing() => beforeFinishing;
        public virtual void ChangeState(PlayerActivityState next)
        {
            state = next;
            if (next == PlayerActivityState.Running) ServingWork.Start(this);
        }
        internal void Advance()
        {
            if (Time.frameCount - stateFrame < 2) return;
            stateFrame = Time.frameCount;
            if (state == PlayerActivityState.NotStarted) state = PlayerActivityState.MovingTowardsActivity;
            else if (state == PlayerActivityState.MovingTowardsActivity) state = PlayerActivityState.Started;
        }
        internal void MarkFinished()
        {
            if (state == PlayerActivityState.Finished) return;
            beforeFinishing = state;
            state = PlayerActivityState.Finished;
            ServingWork.End(this);
        }
        // The game calls Perform with the entire skip at its end as well as with running minutes.
        // Catch up to an absolute clock, never resell that duration or drain needs a second time.
        public void Perform(int elapsed) => CartSelling.CatchUpTo(HireCrew.NowExact());
        public abstract void Finish();
        public abstract LabelInfo GetHeadlineLabel();
        public LabelInfo[] GetLabels() => new[] { new LabelInfo("foodtrucks:activity_hatch_serving") };
        public abstract ButtonInfo[] GetButtons();
        public bool HasFastForward() => state == PlayerActivityState.Running;
        public bool HasTimeMachine() => state == PlayerActivityState.Running;
        public int GetRemainingMinutesForTimeMachine() => minutes;
        public Timestamp GetTimeMachineGoal()
        {
            ServingWork.Arm(this);
            return TimeHelper.Now().AddMinutes(minutes);
        }
        public bool HasProgressBar() => false;
        public float GetProgressBarPercentageValue() => 0;
        public (string key, object arguments) GetProgressBarLabel() => (null!, null!);
        public bool HasSlider() => state == PlayerActivityState.Running;
        public int GetMinSliderValue() => ServingTime.MinimumMinutes;
        public int GetMaxSliderValue() => ServingTime.MaximumMinutes;
        public float GetCurrentSliderValue() => minutes;
        public void OnSliderValueChanged(int value) => minutes = ServingTime.Selection(value);
        public (string key, object arguments) GetSliderInfo() => ("foodtrucks:activity_work_duration", new { hours = minutes / 60, minutes = minutes % 60 });
    }

    internal sealed class CartWorkActivity : ServingActivity
    {
        private readonly Action release;
        private bool keysCleared;
        internal CartWorkActivity(string id, Action release) : base(id) { this.release = release; }
        internal void ClearKeys()
        {
            if (!keysCleared && state == PlayerActivityState.Running) keysCleared = TruckWindow.ClearKeyLabels(1);
        }
        public override void Finish() { ServingWork.BeforeLeaving(VehicleId); release(); MarkFinished(); }
        public override void ChangeState(PlayerActivityState next)
        {
            if (next == PlayerActivityState.NotStarted && state != PlayerActivityState.NotStarted) Finish();
            else base.ChangeState(next);
        }
        public override LabelInfo GetHeadlineLabel() => new LabelInfo("foodtrucks:activity_cart_title");
        public override ButtonInfo[] GetButtons() => new[]
        {
            new ButtonInfo("FoodTrucksStopWork", "workui_stop", "gray", Finish, PlayerAction.Cancel),
            new ButtonInfo(TruckWindow.CloseButtonName, "foodtrucks:activity_hatch_close", "gray", () => CartSelling.CloseByPlayer(VehicleId), TruckWindow.NoKey)
        };
    }
}
