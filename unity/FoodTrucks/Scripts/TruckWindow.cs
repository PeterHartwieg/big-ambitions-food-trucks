#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;
using Helpers;
using Localizor;
using Localizor.LanguageChangeEvent;
using Parking.UndergroundParking;
using PlayerActivity;
using UI;
using UI.Elements;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace FoodTrucks
{
    // The player serving from inside a truck: after "Open for business" they stand at the open hatch, in the middle of
    // the places the crew use (WindowCrew), facing the line, until they step out.
    //
    // How they are held there [code, 1.0; unverified in game]: the game's own scooter ride
    // (PlayerController.AttachToObject) makes the player kinematic, turns off their collider and NavMesh agent,
    // parents them to a transform and sets a navigation blocker, so neither WASD nor clicks move them. The
    // transform is a place object of our own at the scene's root, not a child of the truck, so a truck taken off
    // the map can never take the player with it. An activity panel of the game's own (PlayerActivityUI, as for
    // studying or working a shift) gives the buttons and keys: Step out (the cancel key), Drive, Close the
    // window. The game ends the activity itself after a time skip, when the player runs out of energy and on
    // Unstuck; each of those steps the player out. Stepping out undoes the attachment at a point on the NavMesh
    // by the rear door (by the driver's door for Drive) and warps the player's agent there.
    //
    // A save made at the hatch records the step-out point as the player's position (a postfix on
    // PlayerHelper.SaveCurrentPosition), so a load puts them outside by the truck, never inside its body.
    //
    // Back to the hatch: on foot by an open truck, its overlay (click the truck: the game's "Enter vehicle" and
    // "Manage storage" buttons) has a "Back to the hatch" button (OverlayButtons). The player walks to the rear door
    // and is placed at the hatch again as after Open, with the window and its line left as they are.
    internal sealed class TruckWindow : MonoBehaviour
    {
        private const NavigationBlocker Blocker = (NavigationBlocker)4100;
        private const float SampleMetres = 3f;
        private const float MovedMetres = 1f;
        private const int MinWaitFrames = 3;
        private const int MaxWaitFrames = 60;
        private const int FramesPerState = 2;

        private static TruckWindow? instance;

        internal const PlayerAction NoKey = PlayerAction.SnapFreePlacement;
        internal const string CloseButtonName = "FoodTrucksCloseWindow";
        internal const string DriveButtonName = "FoodTrucksDrive";
        private bool keyLabelsCleared;

        private string? vehicleId;
        private GameObject? place;
        private PlayerController? attached;
        private HatchActivity? activity;
        private Vector3 truckAt;
        private Vector3 fallbackExit;
        private int stateFrame;
        private Coroutine? entering;
        private string? enteringId;
        private readonly Dictionary<string, string> leftBecause = new Dictionary<string, string>(StringComparer.Ordinal);
        private UnityAction? returnWalk;
        private static readonly System.Reflection.FieldInfo? GoalActionField = HarmonyLib.AccessTools.Field(typeof(PlayerController), "_goalAction");

        private static bool Exhausted()
        {
            var game = SaveGameManager.Current;
            return game != null && game.gameVariables != null && !game.gameVariables.disableEnergy && game.Energy <= 0f;
        }

        public static bool PlayerAt(string id) => instance != null && instance.vehicleId == id && instance.place != null;

        public static bool PlayerServes(string id) =>
            PlayerAt(id) || (instance != null && instance.entering != null && instance.enteringId == id);

        private void OnEnable()
        {
            instance = this;
            GlobalEvents.onBindingsChanged += OnBindingsChanged;
        }

        private void OnDisable()
        {
            GlobalEvents.onBindingsChanged -= OnBindingsChanged;
            if (instance == this) instance = null;
        }

        private void OnBindingsChanged() => keyLabelsCleared = false;

        private void OnDestroy()
        {
            try { Release("session ended", move: true); }
            catch (Exception ex) { Log.Error("Truck window, session end: " + ex); }
        }

        public static void EnterFromOutside(VehicleController controller)
        {
            var id = controller.vehicleInstance?.id;
            var player = PlayerHelper.PlayerController;
            if (id == null || player == null) return;
            if (Vector3.Distance(player.transform.position, controller.transform.position) <= PlaceMetres) Enter(id);
            else BackToHatch(controller);
        }

        public static void Enter(string truckId)
        {
            if (CrewDriveProbe.Owns(truckId)) return;
            var self = instance;
            if (self == null)
            {
                Log.Info($"Truck window: {truckId} open, player left outside: no runtime.");
                return;
            }
            if (self.entering != null) self.StopCoroutine(self.entering);
            self.enteringId = truckId;
            self.entering = self.StartCoroutine(self.EnterWhenOut(truckId));
        }

        public static void StepOutNow(string reason) => instance?.StepOut(reason, drive: false);

        public static string WhyNotAtHatch(string id) =>
            instance != null && instance.leftBecause.TryGetValue(id, out var why) ? "the player left the hatch: " + why
                : "the player hasn't been at the hatch since the save was loaded (a save made there puts them outside)";

        public static bool CanGoBack(VehicleInstance? truck)
        {
            var self = instance;
            if (self == null || truck == null || self.vehicleId != null || self.entering != null || self.Returning()) return false;
            if (FoodTrucksMod.KindOf(truck.vehicleTypeName) != "truck") return false;
            if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId) || PlayerHelper.ItemInstanceInHands != null) return false;
            return StreetSales.Find(FoodTrucksSession.CurrentState, truck.id)?.IsOpen == true;
        }

        private const float PlaceMetres = 8f;

        public static void BackToHatch(VehicleController controller)
        {
            var self = instance;
            var id = controller.vehicleInstance?.id;
            if (self == null || id == null || !CanGoBack(controller.vehicleInstance))
            {
                Log.Info($"Truck window: back to the hatch of {id ?? "(no truck)"} refused: {(self == null ? "no runtime" : "not offered now")}.");
                return;
            }
            var player = PlayerHelper.PlayerController;
            if (player == null) return;
            var target = controller.GetManageStoragePosition();
            UnityAction? walk = null;
            void Arrive()
            {
                if (self == null) return;
                if (ReferenceEquals(self.returnWalk, walk)) self.returnWalk = null;
                try { self.Place(id, "back to the hatch"); }
                catch (Exception ex)
                {
                    Log.Error("Truck window, back to the hatch: " + ex);
                    self.StepOut("error while placing", drive: false);
                }
            }
            if (Vector3.Distance(player.transform.position, target) <= 1.5f)
            {
                Log.Info($"Truck window: back to the hatch of truck {id}: at the rear door already.");
                Arrive();
                return;
            }
            if (!player.ExistsRoute(target))
            {
                if (Vector3.Distance(player.transform.position, controller.transform.position) <= PlaceMetres)
                {
                    Log.Info($"Truck window: back to the hatch of truck {id}: no path to the rear door at {target}, but the player is beside the truck.");
                    Arrive();
                    return;
                }
                Log.Info($"Truck window: back to the hatch of truck {id}: no path to the rear door at {target}; refused.");
                CartSelling.Notify("foodtrucks:notification_hatch_unreachable");
                return;
            }
            Log.Info($"Truck window: back to the hatch of truck {id}: walking to {target}.");
            walk = new UnityAction(Arrive);
            self.returnWalk = walk;
            player.SetGoal(target, walk);
        }

        private bool Returning()
        {
            if (returnWalk == null) return false;
            var player = PlayerHelper.PlayerController;
            var goal = player == null ? null : GoalActionField?.GetValue(player) as UnityAction;
            var pending = GoalActionField == null ? player != null && player.hasOnGoalReachedAction : ReferenceEquals(goal, returnWalk);
            if (!pending) returnWalk = null;
            return pending;
        }

        private IEnumerator EnterWhenOut(string id)
        {
            var start = Time.frameCount;
            while (true)
            {
                yield return null;
                if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId))
                {
                    Log.Info($"Truck window: {id}: the player is back in the driver's seat; not placed at the hatch.");
                    entering = null;
                    enteringId = null;
                    yield break;
                }
                var waited = Time.frameCount - start;
                var agent = PlayerHelper.PlayerController?.Character?.navmeshAgent;
                if (waited >= MinWaitFrames && agent != null && agent.enabled && agent.isOnNavMesh) break;
                if (waited >= MaxWaitFrames) break;
            }
            entering = null;
            enteringId = null;
            try { Place(id, "open"); }
            catch (Exception ex)
            {
                Log.Error("Truck window, placing the player: " + ex);
                StepOut("error while placing", drive: false);
            }
        }

        private void Place(string id, string how)
        {
            var game = SaveGameManager.Current;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            var controller = ControllerOf(id);
            var player = PlayerHelper.PlayerController;
            var ui = InstanceBehavior<UIs>.Instance?.playerActivityUI;
            string? why = null;
            if (game == null || vehicle == null || !vehicle.IsOpen) why = "the window closed first";
            else if (!string.IsNullOrEmpty(game.ActiveVehicleId)) why = "the player is in a vehicle";
            else if (controller == null) why = "the truck is gone";
            else if (player == null || ui == null) why = "no player or activity panel";
            else if (vehicleId != null) why = "already at a hatch";
            else if (PlayerActivityUI.IsPanelOpen) why = "another activity panel is open";
            else if (PlayerHelper.ItemInstanceInHands != null) why = "the player holds something";
            else if (Exhausted()) why = "out of energy";
            else if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking) why = "indoors";
            else if (Vector3.Distance(player.transform.position, controller.transform.position) > PlaceMetres) why = "the player is not by the truck";
            if (why != null)
            {
                if (why == "out of energy") CartSelling.Notify("foodtrucks:notification_hatch_too_tired");
                else if (how == "back to the hatch") CartSelling.Notify("foodtrucks:notification_hatch_back_failed");
                Log.Info($"Truck window: {id} open ({how}), player left outside at {PlayerHelper.PlayerController?.transform.position}: {why}.");
                leftBecause[id] = "not placed (" + why + ")";
                return;
            }

            fallbackExit = player!.transform.position;
            var spot = WindowCrew.PlayerSpot(controller!.transform);
            var newPlace = new GameObject("FoodTrucks.HatchPlace." + id);
            newPlace.transform.SetPositionAndRotation(spot.Position, spot.Rotation);
            var newActivity = new HatchActivity(this, id);
            PlayerActivityUI.Show(newActivity);
            if (ui!.GetCurrentActivity != newActivity)
            {
                Destroy(newPlace);
                Log.Info($"Truck window: {id} open ({how}), player left outside at {fallbackExit}: the game refused the activity panel.");
                leftBecause[id] = "not placed (the game refused the activity panel)";
                return;
            }
            place = newPlace;
            activity = newActivity;
            keyLabelsCleared = false;
            vehicleId = id;
            attached = player;
            truckAt = controller.transform.position;
            stateFrame = Time.frameCount;
            player.ResetNavigation();
            returnWalk = null;
            player.AttachToObject(newPlace.transform, Blocker);
            CartQueueView.Recheck();
            leftBecause.Remove(id);
            Log.Info($"Truck window: player placed at the hatch of truck {id} ({how}) at {spot.Position}, facing {spot.Rotation.eulerAngles.y:F0} degrees " +
                     $"(attached like the game's scooter ride: kinematic, collider and NavMesh agent off, navigation blocked; " +
                     $"activity panel shown); steps out at {Exit(controller, driverFirst: false)}.");
        }

        private void Update()
        {
            if (vehicleId == null || activity == null) return;
            try
            {
                var state = activity.GetState();
                if ((state == PlayerActivityState.NotStarted || state == PlayerActivityState.MovingTowardsActivity) &&
                    Time.frameCount - stateFrame >= FramesPerState)
                {
                    activity.Advance();
                    stateFrame = Time.frameCount;
                }
                if (!keyLabelsCleared && activity.GetState() == PlayerActivityState.Running) keyLabelsCleared = ClearKeyLabels();
                var problem = Problem();
                if (problem != null) StepOut(problem, drive: false);
            }
            catch (Exception ex)
            {
                Log.Error("Truck window: " + ex);
                try { StepOut("error", drive: false); }
                catch (Exception stepEx) { Log.Error("Truck window, stepping out after an error: " + stepEx); }
            }
        }

        internal static bool ClearKeyLabels(int expected = 2)
        {
            var ui = InstanceBehavior<UIs>.Instance?.playerActivityUI;
            if (ui == null) return false;
            var done = 0;
            foreach (var button in ui.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                if (button.name != CloseButtonName && button.name != DriveButtonName) continue;
                var label = button.GetComponentInChildren<TextLocalizationComponent>(true);
                if (label == null) continue;
                label.Suffix = string.Empty;
                done++;
            }
            return done >= expected;
        }

        private string? Problem()
        {
            var id = vehicleId!;
            if (PlayerHelper.PlayerController != attached || attached == null) return "the player changed";
            if (place == null || attached.transform.parent != place.transform) return "the player was moved by the game";
            if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId)) return "the player is in a vehicle";
            if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking) return "indoors";
            if (Exhausted())
            {
                CartSelling.Notify("foodtrucks:notification_hatch_too_tired");
                return "out of energy";
            }
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            if (vehicle == null || !vehicle.IsOpen) return "the window closed";
            var controller = ControllerOf(id);
            if (controller == null) return "the truck is gone";
            if (Vector3.Distance(controller.transform.position, truckAt) > MovedMetres) return "the truck moved";
            var state = activity!.GetState();
            var ui = InstanceBehavior<UIs>.Instance?.playerActivityUI;
            if (state != PlayerActivityState.Finished && (ui == null || ui.GetCurrentActivity != activity)) return "the activity panel closed";
            return null;
        }

        internal void StepOut(string reason, bool drive)
        {
            if (vehicleId == null) return;
            var id = vehicleId;
            ServingWork.BeforeLeaving(id);
            var controller = ControllerOf(id);
            var exit = controller != null ? Exit(controller, drive) : fallbackExit;
            var warped = Release(reason, move: true, exitOverride: exit);
            leftBecause[id] = reason;
            CartQueueView.Recheck();
            Log.Info($"Truck window: player stepped out of truck {id} at {exit} ({reason}){(warped ? "" : "; the NavMesh warp failed")}.");
            if (drive && controller != null) StartCoroutine(DriveWhenFree(controller));
        }

        private bool Release(string reason, bool move, Vector3? exitOverride = null)
        {
            if (vehicleId == null) return false;
            var id = vehicleId;
            var player = attached;
            var thisPlace = place;
            var thisActivity = activity;
            vehicleId = null;
            attached = null;
            activity = null;
            place = null;
            var warped = false;
            if (move && player != null && thisPlace != null && player.transform.parent == thisPlace.transform)
            {
                var controller = ControllerOf(id);
                var exit = exitOverride ?? (controller != null ? Exit(controller, driverFirst: false) : fallbackExit);
                player.transform.position = exit;
                player.DeattachFromObject(Blocker);
                warped = player.Character.navmeshAgent.isActiveAndEnabled && player.Character.navmeshAgent.Warp(exit);
                var away = exit - (controller != null ? controller.transform.position : thisPlace.transform.position);
                away.y = 0f;
                if (away.sqrMagnitude > 1e-4f) player.transform.rotation = Quaternion.LookRotation(away);
            }
            else if (player != null && player == PlayerHelper.PlayerController)
            {
                if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId))
                {
                    player.UnsetNavigationBlocker(Blocker);
                    Log.Info($"Truck window: {id}: the player is in a vehicle ({reason}); only the hatch's blocker removed.");
                }
                else
                {
                    var here = Sample(player.transform.position) ?? player.transform.position;
                    player.transform.position = here;
                    player.DeattachFromObject(Blocker);
                    warped = player.Character.navmeshAgent.isActiveAndEnabled && player.Character.navmeshAgent.Warp(here);
                    Log.Info($"Truck window: {id}: the player was no longer at the hatch ({reason}); freed where they are, at {here}.");
                }
            }
            if (thisPlace != null) Destroy(thisPlace);
            thisActivity?.MarkFinished();
            return warped;
        }

        private IEnumerator DriveWhenFree(VehicleController controller)
        {
            for (var i = 0; i < 10 && PlayerActivityUI.IsPanelOpen; i++) yield return null;
            yield return null;
            if (controller == null || !string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId)) yield break;
            try
            {
                controller.DriveVehicle();
                Log.Info($"Truck window: to the driver's seat of truck {controller.vehicleInstance?.id}.");
            }
            catch (Exception ex) { Log.Error("Truck window, to the driver's seat: " + ex); }
        }

        private Vector3 Exit(VehicleController controller, bool driverFirst)
        {
            var rear = controller.vehicleLoadingPosition != null ? Sample(controller.vehicleLoadingPosition.position) : null;
            Vector3? door = null;
            try { door = Sample(controller.GetNavMeshTargetPosition()); }
            catch (Exception) {  }
            var first = driverFirst ? door ?? rear : rear ?? door;
            return first ?? Sample(controller.transform.position + controller.transform.right * 2.5f) ?? fallbackExit;
        }

        private static Vector3? Sample(Vector3 position) =>
            NavMesh.SamplePosition(position, out var hit, SampleMetres, NavMesh.AllAreas) ? hit.position : (Vector3?)null;

        private static VehicleController? ControllerOf(string id)
        {
            var cart = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == id);
            return cart == null ? null : VehicleHelper.GetVehicleController(cart);
        }

        internal static void SavePositionPostfix()
        {
            try
            {
                var self = instance;
                var game = SaveGameManager.Current;
                if (self?.vehicleId == null || game == null) return;
                var controller = ControllerOf(self.vehicleId);
                var exit = controller != null ? self.Exit(controller, driverFirst: false) : self.fallbackExit;
                game.LastPlayerPosition = exit;
                Log.Info($"Truck window: saved at the hatch of truck {self.vehicleId}; the save puts the player outside at {exit}.");
            }
            catch (Exception ex) { Log.Error("Truck window, save position: " + ex); }
        }

        internal static string? Phase()
        {
            var self = instance;
            if (self == null) return null;
            if (self.entering != null) return "starting";
            if (self.vehicleId != null && self.place != null)
                return self.activity != null && self.activity.GetState() == PlayerActivityState.Running ? "at_hatch" : "starting";
            return self.Returning() ? "walking" : null;
        }

        internal static bool PanelStepOut(string id)
        {
            if (instance == null || !PlayerAt(id)) return false;
            instance.OnStepOut();
            return true;
        }

        internal static bool PanelClose(string id)
        {
            if (instance == null || !PlayerAt(id)) return false;
            instance.OnClose(id);
            return true;
        }

        internal void OnStepOut() => StepOut("stepped out", drive: false);

        internal void OnDrive() => StepOut("to the driver's seat", drive: true);

        internal void OnClose(string id)
        {
            CartSelling.CloseByPlayer(id);
            StepOut("closed the window", drive: false);
        }

        internal void OnFinishedByGame(bool afterTimeSkip)
        {
            string reason;
            if (afterTimeSkip) reason = "a time skip ended the activity";
            else if (Exhausted()) reason = "out of energy";
            else if (attached != null && place != null && attached.transform.parent != place.transform) reason = "Unstuck or another game move took the player off the hatch";
            else reason = "the game ended the activity";
            StepOut(reason, drive: false);
        }

        internal void OnCancelledWhileStarting() => StepOut("cancelled while starting", drive: false);
    }

    internal sealed class HatchActivity : ServingActivity
    {
        private readonly TruckWindow owner;
        public HatchActivity(TruckWindow owner, string vehicleId) : base(vehicleId) { this.owner = owner; }
        public override void ChangeState(PlayerActivityState next)
        {
            var old = state;
            base.ChangeState(next);
            if (next == PlayerActivityState.NotStarted && old != PlayerActivityState.NotStarted && old != PlayerActivityState.Finished)
                owner.OnCancelledWhileStarting();
        }
        public override void Finish()
        {
            owner.OnFinishedByGame(ServingWork.IsSkipping(VehicleId));
            MarkFinished();
        }
        public override LabelInfo GetHeadlineLabel() => new LabelInfo("foodtrucks:activity_hatch_title");
        public override ButtonInfo[] GetButtons() => new[]
        {
            new ButtonInfo("FoodTrucksStepOut", "foodtrucks:activity_hatch_step_out", "gray", owner.OnStepOut, PlayerAction.Cancel),
            new ButtonInfo(TruckWindow.CloseButtonName, "foodtrucks:activity_hatch_close", "gray", () => owner.OnClose(VehicleId), TruckWindow.NoKey),
            new ButtonInfo(TruckWindow.DriveButtonName, "foodtrucks:activity_hatch_drive", "blue", owner.OnDrive, TruckWindow.NoKey)
        };
    }
}
