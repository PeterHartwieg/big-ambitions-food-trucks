#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
using Enums;
using Priority = Enums.Priority;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Helpers;
using Localizor;
using Localizor.LanguageChangeEvent;
using Streets;
using UI;
using UI.Guiders;
using UI.Notification;
using UI.Tasks;
using UnityEngine;
using UnityEngine.UI;
using Extensions;

namespace FoodTrucks
{
    // Vehicle warnings as the game's own to-dos in the phone (plan, "Street food page and vehicle warnings"). Core decides
    // the list (VehicleWarnings); once a game minute it is reconciled with the to-dos: missing ones made with the game's
    // CreateTodoTask, cleared ones completed as the game completes its own. Each warning kind has its own TodoTaskType,
    // TypeBase + the kind's number, with the vehicle id in itemInstanceId and the food in itemName. Patches give them
    // text, destination and click, keep the game's completion check off them, and keep them out of every save file.
    internal sealed class VehicleTodos : MonoBehaviour
    {
        // Our to-do types are 9201 and up (VehicleWarningKind 1..). The game's own end at 20.
        internal const int TypeBase = 9200;
        private const int TypeLast = 9299;

        private static VehicleTodos? instance;
        private static bool ready;
        private static System.Reflection.MethodInfo? addToUi, completeRoutine;
        private static long lastOverviewRefresh = long.MinValue / 2;
        private long lastMinute = long.MinValue;

        // Called once after patching. Without the text, completion check, click and save patches our to-dos would break
        // the game's to-do list or reach a save file, so none are made then.
        internal static void CheckReady()
        {
            var tasksUi = typeof(TasksUI);
            var helper = typeof(SaveGameSerializationHelper);
            addToUi = AccessTools.Method(tasksUi, "AddTodoTaskToUI", new[] { typeof(TodoTask), typeof(bool) });
            completeRoutine = AccessTools.Method(tasksUi, "CompleteTodoTaskRoutine", new[] { typeof(TodoTask) });
            if (addToUi == null || completeRoutine == null) Log.Error("To-dos: TasksUI.AddTodoTaskToUI or CompleteTodoTaskRoutine not found.");
            ready = addToUi != null && completeRoutine != null && Has(tasksUi, "GetTodoDescription", new[] { typeof(TodoTask), typeof(bool) }) &&
                    Has(tasksUi, "CheckForCompletedTodoTasks", Type.EmptyTypes) &&
                    Has(tasksUi, "ClickTask", new[] { typeof(TodoTask) }) &&
                    Has(helper, "SerializeJsonData", new[] { typeof(string), typeof(GameInstance) }) &&
                    Has(helper, "SerializeBinaryData", new[] { typeof(string), typeof(GameInstance), typeof(bool) });
            Log.Info(ready
                ? "To-dos: vehicle warnings show as the game's to-dos."
                : "To-dos: no vehicle warnings (a patch they need isn't active).");
        }

        private static bool Has(Type type, string method, Type[] args)
        {
            var original = AccessTools.Method(type, method, args);
            var info = original == null ? null : Harmony.GetPatchInfo(original);
            return info != null && info.Prefixes.Concat(info.Finalizers).Any(p => p.PatchMethod.DeclaringType == typeof(VehicleTodos));
        }

        internal static bool IsOurs(TodoTaskType type) => (int)type > TypeBase && (int)type <= TypeLast;
        internal static bool IsOurs(TodoTask? task) => task != null && IsOurs(task.type);

        private static TodoTaskType TypeOf(VehicleWarningKind kind) => (TodoTaskType)(TypeBase + (int)kind);
        private static VehicleWarningKind KindOf(TodoTask task) => (VehicleWarningKind)((int)task.type - TypeBase);

        private static VehicleWarning WarningOf(TodoTask task)
        {
            var kind = KindOf(task);
            return new VehicleWarning(kind, VehicleWarnings.IsFleetKind(kind) ? null : task.itemInstanceId, task.itemName);
        }

        // ---- What the overview and the vehicle page show ----

        // The to-dos the phone shows for this vehicle now.
        internal static int CountFor(string vehicleId) =>
            Ours().Count(t => !VehicleWarnings.IsFleetKind(KindOf(t)) && string.Equals(t.itemInstanceId, vehicleId, StringComparison.Ordinal));

        // The fleet's own to-dos (the vendor licence): 0 or 1.
        internal static int FleetCount() => Ours().Count(t => VehicleWarnings.IsFleetKind(KindOf(t)));

        // For the test bridge: each to-do of this vehicle (null: the fleet's) as its kind's name and food.
        internal static List<(string Kind, string? Item)> ListFor(string? vehicleId) =>
            Ours().Where(t => vehicleId == null
                    ? VehicleWarnings.IsFleetKind(KindOf(t))
                    : !VehicleWarnings.IsFleetKind(KindOf(t)) && string.Equals(t.itemInstanceId, vehicleId, StringComparison.Ordinal))
                .Select(t => (Name(KindOf(t)), t.itemName)).ToList();

        // For the vehicle page's Alerts box: each to-do of this vehicle as its text (without the vehicle's name) and the
        // game's priority (2 red, 1 yellow, 0 white), most urgent first.
        internal static IReadOnlyList<(string Text, int Priority)> For(string vehicleId) =>
            Ours().Where(t => !VehicleWarnings.IsFleetKind(KindOf(t)) && string.Equals(t.itemInstanceId, vehicleId, StringComparison.Ordinal))
                .Select(t => (TextOf(t), (int)t.priority))
                .OrderByDescending(t => t.Item2).ToList();

        private static string TextOf(TodoTask task)
        {
            var data = new Dictionary<string, string>
            {
                ["itemname"] = string.IsNullOrEmpty(task.itemName) ? "" : LocalizorManager.GetLocalization(task.itemName, null),
                ["day"] = (FoodTrucksSession.CurrentState?.Licence.LicenceExpiresOnDay ?? 0).ToString(),
            };
            return LocalizorManager.GetLocalization(Keys.TryGetValue(KindOf(task), out var key) ? key : "foodtrucks:todo_unknown", data);
        }

        private static IEnumerable<TodoTask> Ours() =>
            SaveGameManager.Current?.TodoTasks?.Where(IsOurs) ?? Enumerable.Empty<TodoTask>();

        // ---- Reconciling ----

        private void OnEnable() => instance = this;

        private void OnDisable()
        {
            if (instance == this) instance = null;
        }

        private void Update()
        {
            try
            {
                var ui = InstanceBehavior<UIs>.Instance;
                if (ui == null || ui.tasksUI == null || SaveGameManager.Current == null) return;
                // As the game's own check: not while the time machine runs (sleep, waiting); once it ends, the minute moved.
                if (ui.timeMachine != null && ui.timeMachine.isRunning) return;
                var now = HireCrew.Now();
                if (now == lastMinute) return;
                lastMinute = now;
                Reconcile(ui.tasksUI);
            }
            catch (Exception ex)
            {
                Log.Error("To-dos: " + ex);
                // One error a minute at most.
                lastMinute = HireCrew.Now();
            }
        }

        // Runs Core's list against the phone's to-dos now (also the test bridge's `todos` refresh).
        internal static void ReconcileNow()
        {
            var tasks = InstanceBehavior<UIs>.Instance?.tasksUI;
            if (tasks != null && SaveGameManager.Current != null) Reconcile(tasks);
        }

        private static void Reconcile(TasksUI tasks)
        {
            var game = SaveGameManager.Current;
            var state = FoodTrucksSession.CurrentState;
            var tables = DemandData.Tables;
            var rules = DemandData.Rules?.Fleet;
            var wanted = new List<VehicleWarning>();
            if (ready && state != null && tables != null && rules != null && !Commissary.Uninstalled)
            {
                foreach (var vehicle in FoodVehicles(game))
                {
                    var kind = FoodTrucksMod.KindOf(vehicle.vehicleTypeName)!;
                    var saved = StreetSales.Find(state, vehicle.id) ?? new VehicleState { VehicleId = vehicle.id, VehicleType = kind };
                    if (string.IsNullOrEmpty(saved.VehicleType))
                        saved = new VehicleState { VehicleId = vehicle.id, VehicleType = kind, HasPermit = saved.HasPermit, Schedule = saved.Schedule, Crew = saved.Crew, Health = saved.Health, Sold = saved.Sold };
                    var crew = saved.Crew.Count(id => HireCrew.Find(id) != null);
                    wanted.AddRange(VehicleWarnings.For(tables, rules, state, saved, CargoLimit.Lines(vehicle), crew, ItemExists, HireRoutes.GameSpeed()));
                }
                wanted.AddRange(VehicleWarnings.Fleet(rules, state.Licence, game.Day));
            }

            var existing = Ours().ToList();
            var (add, complete) = VehicleWarnings.Reconcile(wanted, existing.Select(WarningOf));
            // Complete each warning once per task: duplicates come back as many times as they are extra.
            foreach (var warning in complete)
            {
                var task = existing.FirstOrDefault(t => WarningOf(t).Equals(warning));
                if (task == null) continue;
                existing.Remove(task);
                Complete(tasks, task);
                Log.Info($"To-do completed: {Describe(warning)}.");
            }
            foreach (var warning in add)
            {
                Add(tasks, warning);
                Log.Info($"To-do added: {Describe(warning)}.");
            }
            // The overview's rows follow: at once when a to-do changed, else every ten game minutes (open or closed, income).
            var now = HireCrew.Now();
            if (add.Count > 0 || complete.Count > 0 || now - lastOverviewRefresh >= 10 || now < lastOverviewRefresh)
            {
                lastOverviewRefresh = now;
                BizManFleet.RefreshOverview();
            }
        }

        // The game's CreateTodoTask and CompleteTodoTask also call TasksUI.UpdateBusinessAlerts with the task's address. Ours
        // have none, and with BizMan open before any building's page has been shown its business's address is null too, so
        // the game would schedule BizManBusiness.LoadAlerts, which then throws on the missing building and never clears its
        // schedule flag (no building's alerts would refresh again). So ours are added and completed here, by the same steps
        // without that call: into the list and its row (AddTodoTaskToUI), out of it with the game's struck-through fade
        // (CompleteTodoTaskRoutine).
        private static void Add(TasksUI tasks, VehicleWarning warning)
        {
            var task = new TodoTask
            {
                // Not the game's base64 id: a row is found by its name with Transform.Find, where '/' would be a path.
                id = "foodtrucks-" + Guid.NewGuid().ToString("N"),
                type = TypeOf(warning.Kind),
                itemName = warning.ItemName,
                itemInstanceId = warning.VehicleId,
                priority = PriorityOf(warning.Kind),
            };
            SaveGameManager.Current.TodoTasks.Add(task);
            addToUi!.Invoke(tasks, new object[] { task, true });
        }

        private static void Complete(TasksUI tasks, TodoTask task)
        {
            try
            {
                if (tasks.isActiveAndEnabled && completeRoutine!.Invoke(tasks, new object[] { task }) is System.Collections.IEnumerator routine)
                    tasks.StartCoroutine(routine);
                else tasks.InstantlyCompleteTodoTask(task);
            }
            finally
            {
                // The routine removes the task only when it has a row; one without goes here, and so does one whose row
                // the game couldn't remove (InstantlyCompleteTodoTask throws before TasksUI has started).
                SaveGameManager.Current.TodoTasks.Remove(task);
            }
        }

        private static readonly Dictionary<string, bool> exists = new Dictionary<string, bool>(StringComparer.Ordinal);

        // Whether the game has this item now: a Bakery food doesn't without the Bakery mod.
        internal static bool ItemExists(string item)
        {
            if (!exists.TryGetValue(item, out var found))
                exists[item] = found = BigAmbitions.Items.ItemsGetter.GetByName(item, suppressError: true) != null;
            return found;
        }

        // The player's food vehicles, those left with the game's private driver included.
        private static IEnumerable<VehicleInstance> FoodVehicles(GameInstance game) =>
            game.VehicleInstances.Concat(game.privateDriverVehicleInstances ?? Enumerable.Empty<VehicleInstance>())
                .Where(v => v != null && !string.IsNullOrEmpty(v.id) && FoodTrucksMod.KindOf(v.vehicleTypeName) != null)
                .GroupBy(v => v.id, StringComparer.Ordinal).Select(g => g.First());

        // The game's colours: yellow (1) for low stock and a fading grade, red (2) for what stops sales or costs money,
        // white (0) for a licence still running, as the game's tax reminder.
        private static Priority PriorityOf(VehicleWarningKind kind) => kind switch
        {
            VehicleWarningKind.LowStock => (Priority)1,
            VehicleWarningKind.HealthGradeC => (Priority)1,
            VehicleWarningKind.LicenceRunningOut => (Priority)0,
            _ => (Priority)2
        };

        private static string Describe(VehicleWarning warning)
        {
            if (warning.VehicleId == null) return $"{Name(warning.Kind)} (fleet)";
            var vehicle = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v.id == warning.VehicleId);
            var kind = FoodTrucksMod.KindOf(vehicle?.vehicleTypeName) ?? "vehicle";
            return $"{Name(warning.Kind)} for {kind} {warning.VehicleId}{(warning.ItemName == null ? "" : ", " + warning.ItemName)}";
        }

        // "low_stock", "no_commissary_space": the kind as the log and the test bridge name it.
        internal static string Name(VehicleWarningKind kind)
        {
            var text = kind.ToString();
            var name = new System.Text.StringBuilder();
            for (var i = 0; i < text.Length; i++)
            {
                if (i > 0 && char.IsUpper(text[i]) && !char.IsUpper(text[i - 1])) name.Append('_');
                name.Append(char.ToLowerInvariant(text[i]));
            }
            return name.ToString();
        }

        // Every one of ours out of the phone at once, without the completed look: the uninstall step, or a session ending.
        internal static void RemoveAll(string why)
        {
            var game = SaveGameManager.Current;
            if (game?.TodoTasks == null) return;
            var ours = game.TodoTasks.Where(IsOurs).ToList();
            if (ours.Count == 0) return;
            var tasks = InstanceBehavior<UIs>.Instance?.tasksUI;
            if (tasks != null) tasks.InstantlyCompleteListOfTasks(ours);
            // InstantlyCompleteListOfTasks drops only tasks with a row in the list; the rest go here.
            var left = game.TodoTasks.RemoveAll(IsOurs);
            Log.Info($"To-dos: {ours.Count} removed ({why}){(left > 0 ? $", {left} of them without a row in the list" : "")}.");
        }

        // ---- Patches (TasksUI) ----

        private static readonly Dictionary<VehicleWarningKind, string> Keys = new Dictionary<VehicleWarningKind, string>
        {
            [VehicleWarningKind.LowStock] = "foodtrucks:todo_low_stock",
            [VehicleWarningKind.EmptyStock] = "foodtrucks:todo_empty_stock",
            [VehicleWarningKind.MissingCrew] = "foodtrucks:todo_missing_crew",
            [VehicleWarningKind.RouteProblem] = "foodtrucks:todo_route_problem",
            [VehicleWarningKind.NoPermit] = "foodtrucks:todo_no_permit",
            [VehicleWarningKind.NoCommissarySpace] = "foodtrucks:todo_no_commissary_space",
            [VehicleWarningKind.HealthGradeC] = "foodtrucks:todo_health_grade_c",
            [VehicleWarningKind.LicenceRunningOut] = "foodtrucks:todo_licence_running_out",
            [VehicleWarningKind.LicenceExpired] = "foodtrucks:todo_licence_expired",
        };

        // TasksUI.GetTodoDescription: our text ("Food truck 2: Hotdog is low on stock"). The game's switch has no case for
        // our types and would give an empty label.
        public static bool DescriptionPrefix(TodoTask task, bool showBusinessName, ref LanguageChangeEventDataHolder __result)
        {
            if (!IsOurs(task)) return true;
            try
            {
                var kind = KindOf(task);
                var data = new Dictionary<string, string>
                {
                    // The key itself, as the game's alert_low_stock passes it: the label translates it when it shows.
                    ["itemname"] = task.itemName ?? "",
                    ["day"] = (FoodTrucksSession.CurrentState?.Licence.LicenceExpiresOnDay ?? 0).ToString(),
                };
                __result = LanguageChangeEventDataHolder.Create(Keys.TryGetValue(kind, out var key) ? key : "foodtrucks:todo_unknown", data);
                if (showBusinessName) __result.Prefix = Owner(task) + ": ";
                return false;
            }
            catch (Exception ex)
            {
                Log.Error("To-do text: " + ex);
                return true;
            }
        }

        // "Food truck 2", or "Street food" for the fleet's own.
        private static string Owner(TodoTask task)
        {
            if (VehicleWarnings.IsFleetKind(KindOf(task)) || task.itemInstanceId == null)
                return LocalizorManager.GetLocalization("foodtrucks:econoview_row", null);
            return StreetFoodBooks.VehicleNames().TryGetValue(task.itemInstanceId, out var name) ? name : task.itemInstanceId;
        }

        // TasksUI.CheckForCompletedTodoTasks: ours are out of the list while it runs. Its switch has no case for our types and
        // throws on them (ArgumentOutOfRangeException), which would stop the check for every to-do after ours.
        public static void CompletionCheckPrefix(out List<(int Index, TodoTask Task)>? __state)
        {
            __state = null;
            try { __state = Take(SaveGameManager.Current?.TodoTasks); }
            catch (Exception ex) { Log.Error("To-do completion check: " + ex); }
        }

        public static Exception? CompletionCheckFinalizer(Exception? __exception, List<(int Index, TodoTask Task)>? __state)
        {
            try { PutBack(SaveGameManager.Current?.TodoTasks, __state); }
            catch (Exception ex) { Log.Error("To-do completion check: " + ex); }
            return __exception;
        }

        // TasksUI.ClickTask: a vehicle's to-do opens its page in BizMan, the licence's the City's conversation. The game's
        // switch would throw on our types.
        public static bool ClickPrefix(TodoTask task)
        {
            if (!IsOurs(task)) return true;
            try
            {
                if (VehicleWarnings.IsFleetKind(KindOf(task)) || string.IsNullOrEmpty(task.itemInstanceId))
                {
                    var menu = InstanceBehavior<UIs>.Instance.fullMenu;
                    menu.ShowApp(AppName.Contacts);
                    menu.contactsApp.OpenAppWithContact(CityLicensing.City());
                }
                else BizManVehiclePage.OpenFor(task.itemInstanceId);
                Log.Info($"To-do clicked: {Describe(WarningOf(task))}.");
            }
            catch (Exception ex) { Log.Error("To-do click: " + ex); }
            return false;
        }

        // TasksUI.AddTodoTaskToUI: the row's destination button. The game's GetDestination knows only addresses and has none
        // for our types; a vehicle on the street has no address, so the button is set up here: the vehicle's place, as the
        // fleet's Set destination; the City's address for the licence.
        public static void AddToUiPostfix(TodoTask task, Transform ____tasksGroup)
        {
            if (!IsOurs(task) || ____tasksGroup == null) return;
            try
            {
                var row = ____tasksGroup.Find(task.id);
                var button = row != null ? row.GetButtonByName("DestinationButton") : null;
                // Active already: set up when the row was made (the game returns early for a row it has).
                if (button == null || button.gameObject.activeSelf) return;
                button.onClick = new Button.ButtonClickedEvent();
                button.onClick.AddListener(() =>
                {
                    try { SetDestination(task); }
                    catch (Exception ex) { Log.Error("To-do destination: " + ex); }
                });
                button.gameObject.SetActive(true);
            }
            catch (Exception ex) { Log.Error("To-do row: " + ex); }
        }

        private static void SetDestination(TodoTask task)
        {
            var game = SaveGameManager.Current;
            if (VehicleWarnings.IsFleetKind(KindOf(task)) || string.IsNullOrEmpty(task.itemInstanceId))
            {
                var address = CityLicensing.City()?.Address;
                if (address == null) return;
                GoToAddress(address);
                return;
            }
            var vehicle = game.VehicleInstances.FirstOrDefault(v => v.id == task.itemInstanceId);
            if (vehicle == null)
            {
                // With the private driver: nowhere to go to.
                CartSelling.Notify("foodtrucks:notification_todo_no_place");
                return;
            }
            // On its way between stops (HireRoutes.Depart): off the map, its saved place is where it left from.
            if (StreetSales.Find(FoodTrucksSession.CurrentState, vehicle.id)?.Transit != null)
            {
                CartSelling.Notify("foodtrucks:notification_todo_on_the_way");
                return;
            }
            if (!string.IsNullOrEmpty(vehicle.streetName))
            {
                GoToAddress(vehicle.Address);
                return;
            }
            var controller = VehicleHelper.GetVehicleController(vehicle);
            var position = controller != null ? controller.transform.position : (Vector3)vehicle.position;
            var name = Owner(task);
            var references = InstanceBehavior<GlobalReferences>.Instance;
            // A point can't be kept as the save's destination, which is an address; the old one would come back.
            game.customDestination = null;
            GuidersManager.SetGuiderTarget(position, name, references.vehiclePOIIcon, references.vehiclePOIBackgroundColor, DirectionGuiderType.Destination);
            Notices.Show(NotificationType.Success, "notification_destination_set", new Dictionary<string, string> { { "address", name } }, 4f);
        }

        // As the game's own destination button does.
        private static void GoToAddress(Address address)
        {
            SaveGameManager.Current.customDestination = address;
            Notices.Show(NotificationType.Success, "notification_destination_set",
                new Dictionary<string, string> { { "address", address.ToFormattedString() } }, 4f);
            GuidersManager.SetGuiderTarget(address, DirectionGuiderType.Destination);
        }

        // ---- Patches (saving and loading) ----

        // SaveGameSerializationHelper.SerializeJsonData and SerializeBinaryData: ours are out of the game while it is written,
        // so a save never holds them (a save opened without the mod has none), and back in right after.
        public static void SerializePrefix(GameInstance instance, out List<(int Index, TodoTask Task)>? __state)
        {
            __state = null;
            try
            {
                __state = Take(instance?.TodoTasks);
                if (__state != null && __state.Count > 0) Log.Info($"To-dos: {__state.Count} kept out of the save file.");
            }
            catch (Exception ex) { Log.Error("To-dos before saving: " + ex); }
        }

        public static Exception? SerializeFinalizer(Exception? __exception, GameInstance instance, List<(int Index, TodoTask Task)>? __state)
        {
            try { PutBack(instance?.TodoTasks, __state); }
            catch (Exception ex) { Log.Error("To-dos after saving: " + ex); }
            return __exception;
        }

        // SaveGameManager.InitializeLoadedSave: any of ours in a loaded save (one written by a build without the strip, or
        // edited by hand) goes before the game builds its to-do list; the minute's reconcile makes them again if due.
        public static void LoadedPostfix()
        {
            try
            {
                var removed = SaveGameManager.Current?.TodoTasks?.RemoveAll(IsOurs) ?? 0;
                if (removed > 0) Log.Info($"To-dos: {removed} of ours found in the loaded save and dropped.");
            }
            catch (Exception ex) { Log.Error("To-dos on load: " + ex); }
        }

        private static List<(int Index, TodoTask Task)>? Take(List<TodoTask>? list)
        {
            if (list == null) return null;
            var taken = new List<(int, TodoTask)>();
            for (var i = 0; i < list.Count; i++)
                if (IsOurs(list[i])) taken.Add((i, list[i]));
            if (taken.Count == 0) return null;
            list.RemoveAll(IsOurs);
            return taken;
        }

        private static void PutBack(List<TodoTask>? list, List<(int Index, TodoTask Task)>? taken)
        {
            if (list == null || taken == null) return;
            // Ascending original places, so each goes back where it was when nothing else moved.
            foreach (var (index, task) in taken)
                if (!list.Contains(task)) list.Insert(Math.Min(index, list.Count), task);
        }
    }
}
