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
    internal sealed class VehicleTodos : MonoBehaviour
    {
        internal const int TypeBase = 9200;
        private const int TypeLast = 9299;

        private static VehicleTodos? instance;
        private static bool ready;
        private static System.Reflection.MethodInfo? addToUi, completeRoutine;
        private static long lastOverviewRefresh = long.MinValue / 2;
        private long lastMinute = long.MinValue;

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

        internal static int CountFor(string vehicleId) =>
            Ours().Count(t => !VehicleWarnings.IsFleetKind(KindOf(t)) && string.Equals(t.itemInstanceId, vehicleId, StringComparison.Ordinal));

        internal static int FleetCount() => Ours().Count(t => VehicleWarnings.IsFleetKind(KindOf(t)));

        internal static List<(string Kind, string? Item, string Text)> ListFor(string? vehicleId) =>
            Ours().Where(t => vehicleId == null
                    ? VehicleWarnings.IsFleetKind(KindOf(t))
                    : !VehicleWarnings.IsFleetKind(KindOf(t)) && string.Equals(t.itemInstanceId, vehicleId, StringComparison.Ordinal))
                .Select(t => (Name(KindOf(t)), t.itemName, TextOf(t))).ToList();

        internal static string RouteCauseText(string? vehicleId)
        {
            try
            {
                var vehicle = StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId ?? "");
                var rules = DemandData.Rules;
                if (vehicle == null || rules == null) return LocalizorManager.GetLocalization("foodtrucks:route_cause_times", null);
                var home = HireRoutes.HomeOf(vehicle);
                var cause = new HireRunner(rules.Fleet).RouteCauseOf(vehicle, HireRoutes.GameSpeed(), HireRoutes.HomePoint(home), home?.BayStops)
                            ?? new RouteCause(ScheduleProblem.BadTimes);
                return cause.Describe((key, values) => LocalizorManager.GetLocalization(key, values.ToDictionary(p => p.Key, p => p.Value)),
                    walk: Travel.ModeFor(vehicle.VehicleType) == TravelMode.Push);
            }
            catch (Exception ex)
            {
                Log.Error("Route to-do text: " + ex);
                return LocalizorManager.GetLocalization("foodtrucks:route_cause_times", null);
            }
        }

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
            if (KindOf(task) == VehicleWarningKind.RouteProblem) data["cause"] = RouteCauseText(task.itemInstanceId);
            return LocalizorManager.GetLocalization(Keys.TryGetValue(KindOf(task), out var key) ? key : "foodtrucks:todo_unknown", data);
        }

        private static IEnumerable<TodoTask> Ours() =>
            SaveGameManager.Current?.TodoTasks?.Where(IsOurs) ?? Enumerable.Empty<TodoTask>();

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
                if (ui.timeMachine != null && ui.timeMachine.isRunning) return;
                var now = HireCrew.Now();
                if (now == lastMinute) return;
                lastMinute = now;
                Reconcile(ui.tasksUI);
            }
            catch (Exception ex)
            {
                Log.Error("To-dos: " + ex);
                lastMinute = HireCrew.Now();
            }
        }

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
                    var home = HireRoutes.HomeOf(saved);
                    wanted.AddRange(VehicleWarnings.For(tables, rules, state, saved, CargoLimit.Lines(vehicle), crew, ItemExists, HireRoutes.GameSpeed(),
                        HireRoutes.HomePoint(home), home?.BayStops));
                }
                wanted.AddRange(VehicleWarnings.Fleet(rules, state.Licence, game.Day));
            }

            var existing = Ours().ToList();
            var (add, complete) = VehicleWarnings.Reconcile(wanted, existing.Select(WarningOf));
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
            var now = HireCrew.Now();
            if (add.Count > 0 || complete.Count > 0 || now - lastOverviewRefresh >= 10 || now < lastOverviewRefresh)
            {
                lastOverviewRefresh = now;
                BizManFleet.RefreshOverview();
            }
        }

        private static void Add(TasksUI tasks, VehicleWarning warning)
        {
            var task = new TodoTask
            {
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
                SaveGameManager.Current.TodoTasks.Remove(task);
            }
        }

        private static readonly Dictionary<string, bool> exists = new Dictionary<string, bool>(StringComparer.Ordinal);

        internal static bool ItemExists(string item)
        {
            if (!exists.TryGetValue(item, out var found))
                exists[item] = found = BigAmbitions.Items.ItemsGetter.GetByName(item, suppressError: true) != null;
            return found;
        }

        private static IEnumerable<VehicleInstance> FoodVehicles(GameInstance game) =>
            game.VehicleInstances.Concat(game.privateDriverVehicleInstances ?? Enumerable.Empty<VehicleInstance>())
                .Where(v => v != null && !string.IsNullOrEmpty(v.id) && FoodTrucksMod.KindOf(v.vehicleTypeName) != null)
                .GroupBy(v => v.id, StringComparer.Ordinal).Select(g => g.First());

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

        internal static void RemoveAll(string why)
        {
            var game = SaveGameManager.Current;
            if (game?.TodoTasks == null) return;
            var ours = game.TodoTasks.Where(IsOurs).ToList();
            if (ours.Count == 0) return;
            var tasks = InstanceBehavior<UIs>.Instance?.tasksUI;
            if (tasks != null) tasks.InstantlyCompleteListOfTasks(ours);
            var left = game.TodoTasks.RemoveAll(IsOurs);
            Log.Info($"To-dos: {ours.Count} removed ({why}){(left > 0 ? $", {left} of them without a row in the list" : "")}.");
        }

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
            [VehicleWarningKind.NoEquipment] = "foodtrucks:todo_no_equipment",
        };

        public static bool DescriptionPrefix(TodoTask task, bool showBusinessName, ref LanguageChangeEventDataHolder __result)
        {
            if (!IsOurs(task)) return true;
            try
            {
                var kind = KindOf(task);
                var data = new Dictionary<string, string>
                {
                    ["itemname"] = task.itemName ?? "",
                    ["day"] = (FoodTrucksSession.CurrentState?.Licence.LicenceExpiresOnDay ?? 0).ToString(),
                };
                if (kind == VehicleWarningKind.RouteProblem) data["cause"] = RouteCauseText(task.itemInstanceId);
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

        private static string Owner(TodoTask task)
        {
            if (VehicleWarnings.IsFleetKind(KindOf(task)) || task.itemInstanceId == null)
                return LocalizorManager.GetLocalization("foodtrucks:econoview_row", null);
            return StreetFoodBooks.VehicleNames().TryGetValue(task.itemInstanceId, out var name) ? name : task.itemInstanceId;
        }

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

        public static void AddToUiPostfix(TodoTask task, Transform ____tasksGroup)
        {
            if (!IsOurs(task) || ____tasksGroup == null) return;
            try
            {
                var row = ____tasksGroup.Find(task.id);
                var button = row != null ? row.GetButtonByName("DestinationButton") : null;
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
            if (KindOf(task) == VehicleWarningKind.NoCommissarySpace && vehicle != null)
            {
                var near = NearestCommissary(vehicle);
                if (near != null)
                {
                    GoToAddress(near);
                    return;
                }
            }
            if (vehicle == null)
            {
                CartSelling.Notify("foodtrucks:notification_todo_no_place");
                return;
            }
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

        private static Address? NearestCommissary(VehicleInstance vehicle)
        {
            var controller = VehicleHelper.GetVehicleController(vehicle);
            var position = controller != null ? controller.transform.position : (Vector3)vehicle.position;
            Address? best = null;
            var bestDistance = float.MaxValue;
            foreach (var site in DemandData.Commissaries?.Sites ?? Enumerable.Empty<FoodTrucks.Core.Commissary.CommissarySite>())
            {
                var address = World.BuildingAt(site.Address)?.building?.Address;
                var doors = World.EntrancesOf(site.Address);
                if (address == null || doors == null || doors.Length == 0) continue;
                var distance = doors.Min(d => Vector3.Distance(d, position));
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = address;
            }
            return best;
        }

        private static void GoToAddress(Address address)
        {
            SaveGameManager.Current.customDestination = address;
            Notices.Show(NotificationType.Success, "notification_destination_set",
                new Dictionary<string, string> { { "address", address.ToFormattedString() } }, 4f);
            GuidersManager.SetGuiderTarget(address, DirectionGuiderType.Destination);
        }

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
            foreach (var (index, task) in taken)
                if (!list.Contains(task)) list.Insert(Math.Min(index, list.Count), task);
        }
    }
}
