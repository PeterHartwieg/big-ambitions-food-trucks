#nullable enable
using System;
using System.Collections;
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

namespace FoodTrucks
{
    // The player serving from inside a truck: after "Open here" they stand at the open hatch, in the middle of
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
    internal sealed class TruckWindow : MonoBehaviour
    {
        // A blocker value of our own, so the game's activities never unset it for us (the set holds any value).
        private const NavigationBlocker Blocker = (NavigationBlocker)4100;
        private const float SampleMetres = 3f;
        // The truck moved this far from where it stood at opening: whatever moved it, the player steps out.
        private const float MovedMetres = 1f;
        // The game warps the player to the door a frame after the seat is left (CarController.ExitVehicle).
        private const int MinWaitFrames = 3;
        private const int MaxWaitFrames = 60;
        // Frames the game's panel gets to see each state before the next (shown, moving, started).
        private const int FramesPerState = 2;

        private static TruckWindow? instance;

        // Buttons without a key (HatchActivity): an action the game routes only to free placement, never to the panel.
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
        // The truck whose hatch the player is on the way to (after Open, until placed or left outside).
        private string? enteringId;

        // Out of energy: the game sends an exhausted player to hospital, which an open activity panel stops, and
        // it fires its own out-of-energy cancel only when energy crosses the line, not when it is already below.
        private static bool Exhausted()
        {
            var game = SaveGameManager.Current;
            return game != null && game.gameVariables != null && !game.gameVariables.disableEnergy && game.Energy <= 0f;
        }

        // The player stands at this truck's hatch.
        public static bool PlayerAt(string id) => instance != null && instance.vehicleId == id && instance.place != null;

        // The player serves this truck's window: at its hatch, or on the way there in the frames after Open, so
        // the window doesn't report nobody at it before the player is placed. Only the hatch serves a truck
        // (Core PlayerServing); a player who stepped out doesn't, however close.
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

        // The game's panel rebuilds its buttons when key bindings change, which brings the key labels back; the
        // next Update clears them again, after every handler of the event has run.
        private void OnBindingsChanged() => keyLabelsCleared = false;

        private void OnDestroy()
        {
            // The session ends (another save, the main menu): let go of the player if it is still ours.
            try { Release("session ended", move: true); }
            catch (Exception ex) { Log.Error("Truck window, session end: " + ex); }
        }

        // Called by Open once the game's Park button has taken the player out of the driver's seat.
        public static void Enter(string truckId)
        {
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

        // The uninstall step: out of the truck before it goes.
        public static void StepOutNow(string reason) => instance?.StepOut(reason, drive: false);

        private IEnumerator EnterWhenOut(string id)
        {
            var start = Time.frameCount;
            while (true)
            {
                yield return null;
                if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId))
                {
                    // The game found no way out and put the player back in the seat.
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
            try { Place(id); }
            catch (Exception ex)
            {
                Log.Error("Truck window, placing the player: " + ex);
                StepOut("error while placing", drive: false);
            }
        }

        private void Place(string id)
        {
            var game = SaveGameManager.Current;
            var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            var controller = ControllerOf(id);
            var player = PlayerHelper.PlayerController;
            var ui = InstanceBehavior<UIs>.Instance?.playerActivityUI;
            string? why = null;
            if (game == null || vehicle == null || !vehicle.IsOpen) why = "the window closed first";
            else if (controller == null) why = "the truck is gone";
            else if (player == null || ui == null) why = "no player or activity panel";
            else if (vehicleId != null) why = "already at a hatch";
            else if (PlayerActivityUI.IsPanelOpen) why = "another activity panel is open";
            else if (PlayerHelper.ItemInstanceInHands != null) why = "the player holds something";
            else if (Exhausted()) why = "out of energy";
            else if (BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking) why = "indoors";
            else if (Vector3.Distance(player.transform.position, controller.transform.position) > 8f) why = "the player is not by the truck";
            if (why != null)
            {
                if (why == "out of energy") CartSelling.Notify("foodtrucks:notification_hatch_too_tired");
                Log.Info($"Truck window: {id} open, player left outside at {PlayerHelper.PlayerController?.transform.position}: {why}.");
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
                Log.Info($"Truck window: {id} open, player left outside at {fallbackExit}: the game refused the activity panel.");
                return;
            }
            place = newPlace;
            activity = newActivity;
            keyLabelsCleared = false;
            vehicleId = id;
            attached = player;
            truckAt = controller.transform.position;
            stateFrame = Time.frameCount;
            player.AttachToObject(newPlace.transform, Blocker);
            CartQueueView.Recheck();
            Log.Info($"Truck window: player placed at the hatch of truck {id} at {spot.Position}, facing {spot.Rotation.eulerAngles.y:F0} degrees " +
                     $"(attached like the game's scooter ride: kinematic, collider and NavMesh agent off, navigation blocked; " +
                     $"activity panel shown); steps out at {Exit(controller, driverFirst: false)}.");
        }

        private void Update()
        {
            if (vehicleId == null || activity == null) return;
            try
            {
                // The game's panel starts an activity in steps: shown (the game pauses), moving towards it (the game
                // unpauses), started (its buttons). The player is already in place, so each step lasts two frames.
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

        // The panel prints each button's key after its label; Close and Drive have none. True once both are done.
        private static bool ClearKeyLabels()
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
            return done >= 2;
        }

        // Why the player can't stay at the hatch any more, or null.
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

        // Steps the player out by the truck (the rear door; the driver's door for Drive), ends the panel's
        // activity and, for Drive, gets into the driver's seat. Safe to call again: only the first call acts.
        internal void StepOut(string reason, bool drive)
        {
            if (vehicleId == null) return;
            var id = vehicleId;
            var controller = ControllerOf(id);
            var exit = controller != null ? Exit(controller, drive) : fallbackExit;
            var warped = Release(reason, move: true, exitOverride: exit);
            CartQueueView.Recheck();
            Log.Info($"Truck window: player stepped out of truck {id} at {exit} ({reason}){(warped ? "" : "; the NavMesh warp failed")}.");
            if (drive && controller != null) StartCoroutine(DriveWhenFree(controller));
        }

        // Undoes the attachment and forgets the hatch. True when the player's agent was warped to the exit.
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
            // Only the player we attached, and only while still on our place: never a new scene's player.
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
                    // In a vehicle or on a scooter: the game holds the player its own way; only our blocker goes.
                    player.UnsetNavigationBlocker(Blocker);
                    Log.Info($"Truck window: {id}: the player is in a vehicle ({reason}); only the hatch's blocker removed.");
                }
                else
                {
                    // Something else moved the player off our place, or the attachment failed halfway: undo all of
                    // it (collider, physics, agent, parent, blocker) where they stand, on the NavMesh nearby.
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
            // The game's panel closes the frame after the activity finishes; the seat waits for that.
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

        // A point on the NavMesh just outside the truck: by the rear door, or the driver's door first for Drive.
        private Vector3 Exit(VehicleController controller, bool driverFirst)
        {
            var rear = controller.vehicleLoadingPosition != null ? Sample(controller.vehicleLoadingPosition.position) : null;
            Vector3? door = null;
            try { door = Sample(controller.GetNavMeshTargetPosition()); }
            catch (Exception) { /* a truck without door targets steps out at the rear */ }
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

        // Harmony postfix on PlayerHelper.SaveCurrentPosition: at the hatch, the save gets the step-out point.
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

        // Button handlers from the panel.
        internal void OnStepOut() => StepOut("stepped out", drive: false);

        internal void OnDrive() => StepOut("to the driver's seat", drive: true);

        internal void OnClose(string id)
        {
            // Closing runs the usual close (message, hatch down); Update then sees the window closed, but the
            // player steps out at once.
            CartSelling.CloseByPlayer(id);
            StepOut("closed the window", drive: false);
        }

        internal void OnFinishedByGame() => StepOut("the game ended the activity", drive: false);

        internal void OnCancelledWhileStarting() => StepOut("cancelled while starting", drive: false);
    }

    // The activity panel while at the hatch: a headline, a line of text and three buttons. It never blocks
    // saving (the save's position is fixed up instead) and never needs energy.
    internal sealed class HatchActivity : IPlayerActivity
    {
        private readonly TruckWindow owner;
        private readonly string vehicleId;
        private PlayerActivityState state = PlayerActivityState.NotStarted;
        private PlayerActivityState beforeFinishing = PlayerActivityState.NotStarted;

        public HatchActivity(TruckWindow owner, string vehicleId)
        {
            this.owner = owner;
            this.vehicleId = vehicleId;
        }

        public bool RequiresEnergy() => false;

        public bool BlocksAutoSave() => false;

        public PlayerActivityState GetState() => state;

        public PlayerActivityState GetStateBeforeFinishing() => beforeFinishing;

        public void ChangeState(PlayerActivityState next)
        {
            var old = state;
            state = next;
            // The game's cancel while "moving towards" the activity (the cancel key, or a click) closes the panel
            // without Finish; the player still has to step out.
            if (next == PlayerActivityState.NotStarted && old != PlayerActivityState.NotStarted && old != PlayerActivityState.Finished)
                owner.OnCancelledWhileStarting();
        }

        // Shown, then moving, then started (TruckWindow steps it on).
        internal void Advance()
        {
            if (state == PlayerActivityState.NotStarted) state = PlayerActivityState.MovingTowardsActivity;
            else if (state == PlayerActivityState.MovingTowardsActivity) state = PlayerActivityState.Started;
        }

        internal void MarkFinished()
        {
            if (state == PlayerActivityState.Finished) return;
            beforeFinishing = state;
            state = PlayerActivityState.Finished;
        }

        public void Perform(int minutes)
        {
        }

        // The game ends it: after a time skip, out of energy, Unstuck.
        public void Finish()
        {
            owner.OnFinishedByGame();
            MarkFinished();
        }

        public LabelInfo GetHeadlineLabel() => new LabelInfo("foodtrucks:activity_hatch_title");

        public LabelInfo[] GetLabels() => new[] { new LabelInfo("foodtrucks:activity_hatch_serving") };

        public ButtonInfo[] GetButtons() => new[]
        {
            new ButtonInfo("FoodTrucksStepOut", "foodtrucks:activity_hatch_step_out", "gray", owner.OnStepOut, PlayerAction.Cancel),
            // Close and Drive have no key: the panel needs some action for each, so they get one the game never
            // hands to an activity panel, and TruckWindow clears its key label once the buttons are built.
            new ButtonInfo(TruckWindow.CloseButtonName, "foodtrucks:activity_hatch_close", "gray", () => owner.OnClose(vehicleId), TruckWindow.NoKey),
            new ButtonInfo(TruckWindow.DriveButtonName, "foodtrucks:activity_hatch_drive", "blue", owner.OnDrive, TruckWindow.NoKey)
        };

        public bool HasFastForward() => false;

        public bool HasTimeMachine() => false;

        public int GetRemainingMinutesForTimeMachine() => 0;

        public bool HasProgressBar() => false;

        public float GetProgressBarPercentageValue() => 0f;

        public (string key, object arguments) GetProgressBarLabel() => (null!, null!);

        public bool HasSlider() => false;

        public int GetMinSliderValue() => 0;

        public int GetMaxSliderValue() => 0;

        public float GetCurrentSliderValue() => 0f;

        public void OnSliderValueChanged(int value)
        {
        }

        public (string key, object arguments) GetSliderInfo() => (null!, null!);
    }
}
