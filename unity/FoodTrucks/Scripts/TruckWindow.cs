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
    //
    // Back to the hatch: on foot by an open truck, its overlay (click the truck: the game's "Enter vehicle" and
    // "Manage storage" buttons) has a "Back to the hatch" button (HatchButton). The player walks to the rear door
    // and is placed at the hatch again as after Open, with the window and its line left as they are.
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
        // Why the player last left each truck's hatch this session, for the "nobody at the window" log line.
        private readonly Dictionary<string, string> leftBecause = new Dictionary<string, string>(StringComparer.Ordinal);
        // The walk to a rear door for Back to the hatch: the game's goal callback we set, while it is still the
        // player's goal. Another click replaces or clears the game's goal, which ends ours.
        private UnityAction? returnWalk;
        private static readonly System.Reflection.FieldInfo? GoalActionField = HarmonyLib.AccessTools.Field(typeof(PlayerController), "_goalAction");

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

        // Why nobody is at this truck's hatch, for the log: the last step-out's reason this session, else the load.
        public static string WhyNotAtHatch(string id) =>
            instance != null && instance.leftBecause.TryGetValue(id, out var why) ? "the player left the hatch: " + why
                : "the player hasn't been at the hatch since the save was loaded (a save made there puts them outside)";

        // "Back to the hatch" is offered for this vehicle: an open truck, the player on foot, empty-handed, not at a
        // hatch or on the way to one.
        public static bool CanGoBack(VehicleInstance? truck)
        {
            var self = instance;
            if (self == null || truck == null || self.vehicleId != null || self.entering != null || self.Returning()) return false;
            if (FoodTrucksMod.KindOf(truck.vehicleTypeName) != "truck") return false;
            if (!string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId) || PlayerHelper.ItemInstanceInHands != null) return false;
            return StreetSales.Find(FoodTrucksSession.CurrentState, truck.id)?.IsOpen == true;
        }

        // The overlay's "Back to the hatch": walk to the rear door (the game's own storage spot), then be placed at the
        // hatch as after Open. The window stays open and its line waits; nothing is closed or reopened.
        // How far from the truck's middle the player may be placed at its hatch.
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
            // An unreachable rear door is fine within Place's own reach of the truck (PlaceMetres; review round 4: 0.9.8
            // took the game's 2.5 m from the truck's middle, so a player at the rear corner of a truck backed against a
            // wall was refused although Place would accept them), and otherwise refused with a notice, since the game
            // would run the goal at once wherever the player stands.
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

        // A Back to the hatch walk is under way: our callback is still the player's goal.
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
            // Any walk still pending (an earlier Back to the hatch, or another interaction's goal) is dropped, so its
            // callback can't run after the player steps out again (review round 2).
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
            leftBecause[id] = reason;
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

        // The panel's Step out and Close window for the test bridge, as if clicked: false unless the player is at this
        // truck's hatch.
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

        // The game ends the activity after a time skip (it calls Perform first, in the same frame), when the player
        // runs out of energy, and on Unstuck (which moves the player off our place first) or other game events.
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

    // The activity panel while at the hatch: a headline, a line of text and three buttons. It never blocks
    // saving (the save's position is fixed up instead) and never needs energy.
    internal sealed class HatchActivity : IPlayerActivity
    {
        private readonly TruckWindow owner;
        private readonly string vehicleId;
        private PlayerActivityState state = PlayerActivityState.NotStarted;
        private PlayerActivityState beforeFinishing = PlayerActivityState.NotStarted;
        // The frame the game last called Perform, and the minutes it passed: the game calls it every running minute
        // with 1, and at a time skip's end, just before Finish, with the whole skip.
        private int performedFrame = -1;
        private int performedMinutes;

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
            performedFrame = Time.frameCount;
            performedMinutes = minutes;
        }

        // The game ends it: after a time skip, out of energy, Unstuck.
        public void Finish()
        {
            owner.OnFinishedByGame(performedFrame == Time.frameCount && performedMinutes > 1);
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
