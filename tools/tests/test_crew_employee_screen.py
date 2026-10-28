"""The game's employee list for vehicle crew: the production adapter methods against controlled employee/UI boundaries."""
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


def method(filename, signature):
    source = (ROOT / 'unity/FoodTrucks/Scripts' / filename).read_text()
    start = source.index(signature)
    end = source.index('{', start) + 1
    depth = 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def expression(filename, signature):
    source = (ROOT / 'unity/FoodTrucks/Scripts' / filename).read_text()
    start = source.index(signature)
    return source[start:source.index(';', start) + 1]


def run(harness, marker):
    with tempfile.TemporaryDirectory(prefix='ft-crew-screen-') as directory:
        folder = Path(directory)
        (folder / 'Program.cs').write_text(harness)
        (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>
<ProjectReference Include="{ROOT / 'Core/FoodTrucks.Core.csproj'}" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" /></ItemGroup></Project>''')
        result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q'],
                                capture_output=True, text=True)
        return result.returncode == 0 and marker in result.stdout, result.stdout + result.stderr


def adapter():
    screen = '\n'.join(method('CrewEmployeeScreen.cs', s) for s in (
        'public static void ModelPostfix(', 'public static void ShowEmployeePostfix(', 'public static bool ManageSchedulePrefix('))
    screen += '\n' + expression('CrewEmployeeScreen.cs', 'private static VehicleState? VehicleOf(')
    screen += '\n' + expression('CrewEmployeeScreen.cs', 'private static string VehicleName(')
    patches = '\n'.join(method('Patches.cs', s) for s in (
        'private static void CrewHasTask(', 'private static void CrewTaskHandled(', 'private static bool SkipCrewUnassignedTask('))
    return (HARNESS.replace('/* SCREEN */', screen).replace('/* COMPLAINTS */', patches)
            .replace('/* ISCREW */', method('HireCrew.cs', 'public static bool IsCrew(')))


class CrewEmployeeScreenTests(unittest.TestCase):
    def test_rows_filter_detail_panel_and_manage_schedule(self):
        passed, output = run(adapter().replace('/* MAIN */', SCREEN_MAIN), 'PASS: crew employee screen')
        self.assertTrue(passed, output)

    def test_complaint_suppression_apart_from_labels(self):
        passed, output = run(adapter().replace('/* MAIN */', COMPLAINT_MAIN), 'PASS: crew complaints')
        self.assertTrue(passed, output)

    def test_registered_on_the_game_members_checked_in_the_assembly(self):
        source = (ROOT / 'unity/FoodTrucks/Scripts/Patches.cs').read_text()
        for line in (
            'Patch(typeof(UI.Smartphone.Apps.MyEmployees.EmployeeModel), ".ctor", new[] { typeof(EmployeeInstance) }, typeof(CrewEmployeeScreen)',
            'Patch(myEmployees, "ShowEmployee", new[] { typeof(EmployeeInstance) }, typeof(CrewEmployeeScreen), postfix: nameof(CrewEmployeeScreen.ShowEmployeePostfix));',
            'Patch(myEmployees, "ManageSchedule", new[] { typeof(EmployeeInstance) }, typeof(CrewEmployeeScreen), prefix: nameof(CrewEmployeeScreen.ManageSchedulePrefix));',
        ):
            self.assertIn(line, source)
        self.assertIn('method == ".ctor" ? AccessTools.Constructor(type, args)', source)
        # Display only: no patch on the predicates the crew's work and pay depend on.
        for predicate in ('"IsAssignedToAnyBusiness"', '"GetCurrentTask"', '"IsAssignedToAnyWorkShift"'):
            self.assertNotIn(predicate, source)
        screen = (ROOT / 'unity/FoodTrucks/Scripts/CrewEmployeeScreen.cs').read_text()
        self.assertNotIn('assignedAddress =', screen)
        self.assertNotIn('assignedWeeklyHours', screen)


HARNESS = r'''
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Fleet;

namespace UI.Elements {
    public class Dropdown {
        public List<string> Options = new();
        public bool Localized = true;
        public int Selected = -1;
        public void SetOptions(List<string> newOptions, bool localize = true) { Options = newOptions; Localized = localize; }
        public void ResetSelectedOption(int defaultOption = -1) => Selected = defaultOption;
    }
}

// The game's employee, with the parts of GetCurrentTask and IsAssignedToAnyBusiness the list reads (BigAmbitions.dll).
public class EmployeeInstance {
    public string id = "";
    public bool IsCandidate;
    public BuildingRegistration? building;
    public bool training, isAbsent, isReplaced;
    public int assignedWeeklyHours;
    public bool IsAssignedToAnyBusiness() => building != null;
    public bool IsTraining => training;
    public string? GetCurrentTask() {
        if (training) return "myemployees_task_training";
        if (isAbsent && !isReplaced) return "myemployees_task_absent";
        if (building == null) return null;
        return "ba:itemname_cashregister";
    }
}
public class BuildingRegistration {
    public string BusinessName = "";
    public string GetDisplayName() => BusinessName + " (address)";
}
// The game's EmployeeModel constructor, then the mod's postfix as Harmony runs it.
public class EmployeeModel {
    public readonly List<string> statuses = new();
    public string employeeName;
    public string? currentBusinessName, task;
    public int hoursPerWeek;
    public EmployeeModel(EmployeeInstance instance) {
        employeeName = instance.id;
        task = instance.GetCurrentTask();
        currentBusinessName = instance.building?.BusinessName;
        hoursPerWeek = instance.assignedWeeklyHours;
        statuses.Add(instance.IsAssignedToAnyBusiness() ? "common_assigned" : "common_unassigned");
        if (instance.IsTraining) statuses.Add("common_in_training");
        if (Game.Patched) CrewEmployeeScreen.ModelPostfix(this, instance);
    }
}
public enum TodoTaskType { EmployeeUnassigned, EmployeeIdle }

static class Game {
    public static bool Patched = true;
    public static List<EmployeeInstance> Employees = new();
    public static List<BuildingRegistration> Buildings = new();
    public static string? Error;
    public static bool NamesThrow;

    // EmployeeCellView.SetData: the task key (no task when null), the business (Unassigned when empty) and the hours.
    public static (string Task, string Business, string Hours) Cell(EmployeeModel model) =>
        (model.task ?? "myemployees_no_task", string.IsNullOrEmpty(model.currentBusinessName) ? "Unassigned" : model.currentBusinessName!, model.hoursPerWeek.ToString());
    // The status toggles of the list's filter (EmployeeFilterToggleBase) and its search text (EmployeeFilterController).
    public static List<string> Filter(string status) =>
        Employees.Select(e => new EmployeeModel(e)).Where(m => m.statuses.Contains(status)).Select(m => m.employeeName).ToList();
    public static List<string> Search(string text) =>
        Employees.Select(e => new EmployeeModel(e)).Where(m => new[] { m.employeeName, m.currentBusinessName }
            .Any(t => t != null && t.Contains(text, StringComparison.OrdinalIgnoreCase))).Select(m => m.employeeName).ToList();
    // MyEmployees.ShowEmployee's dropdown, then the postfix.
    public static UI.Elements.Dropdown Show(EmployeeInstance employee) {
        var dropdown = new UI.Elements.Dropdown();
        var options = new List<string> { "Unassigned" };
        options.AddRange(Buildings.Select(b => b.GetDisplayName()));
        dropdown.SetOptions(options, false);
        dropdown.ResetSelectedOption(employee.IsAssignedToAnyBusiness() ? Buildings.IndexOf(employee.building!) + 1 : 0);
        CrewEmployeeScreen.ShowEmployeePostfix(employee, dropdown, Buildings);
        return dropdown;
    }
    // MyEmployees.ManageSchedule: the prefix first, then the game's refusal of an employee without a building.
    public static string ManageSchedule(EmployeeInstance employee) {
        BizManVehiclePage.Opened = null;
        if (!CrewEmployeeScreen.ManageSchedulePrefix(employee)) return "vehicle page " + BizManVehiclePage.Opened;
        return employee.IsAssignedToAnyBusiness() ? "business schedule" : "myemployees_no_business_assigned_notification";
    }
}

static class FoodTrucksSession { public static SaveState? CurrentState; }
static class Log { public static void Error(Exception ex) => Game.Error = ex.Message; }
static class LocalizorManager { public static string GetLocalization(string key, object? args) => key == "foodtrucks:noun_truck" ? "food truck" : key; }
static class StreetFoodBooks {
    public static Dictionary<string, string> VehicleNames() {
        if (Game.NamesThrow) throw new InvalidOperationException("names unavailable");
        return new() { { "truck-1", "Food Truck 1" }, { "cart-1", "Food Cart 1" } };
    }
}
static class BizManVehiclePage { public static string? Opened; public static void OpenFor(string id) => Opened = id; }
static class HireCrew {
    public static EmployeeInstance? Find(string id) => Game.Employees.FirstOrDefault(e => e.id == id);
    /* ISCREW */
}
static class CrewEmployeeScreen {
    /* SCREEN */
}
static class Patches {
    /* COMPLAINTS */
    public static bool HasTaskComplaint(EmployeeInstance e) { var result = true; CrewHasTask(e, ref result); return result; }
    public static bool Handled(EmployeeInstance e) { var result = false; CrewTaskHandled(e, ref result); return result; }
    public static bool CreatesTodo(TodoTaskType type, string id) => SkipCrewUnassignedTask(type, id);
}

class Program {
    static void Check(bool value, string why) { if (!value) throw new Exception("FAIL: " + why); }
    static void Main() {
        var shop = new BuildingRegistration { BusinessName = "Corner Shop" };
        Game.Buildings.Add(shop);
        var schedule = new HireSchedule();
        foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            schedule.CrewShifts.Add(new CrewShift { EmployeeId = "crew", Day = day, Start = 10, End = 17 });
        var truck = new VehicleState { VehicleId = "truck-1", VehicleType = "truck", Crew = new() { "crew", "trainee", "sick" }, Schedule = schedule };
        var state = new SaveState { Vehicles = new() { new VehicleState { VehicleId = "cart-1", VehicleType = "cart" }, truck } };
        FoodTrucksSession.CurrentState = state;
        var crew = new EmployeeInstance { id = "crew", assignedWeeklyHours = WeekPlan.WeeklyWork(schedule, "crew").Hours };
        var trainee = new EmployeeInstance { id = "trainee", training = true, assignedWeeklyHours = 14 };
        var sick = new EmployeeInstance { id = "sick", isAbsent = true, assignedWeeklyHours = 14 };
        var worker = new EmployeeInstance { id = "worker", building = shop, assignedWeeklyHours = 40 };
        var idle = new EmployeeInstance { id = "idle" };
        Game.Employees.AddRange(new[] { crew, trainee, sick, worker, idle });
        /* MAIN */
    }
}
'''

SCREEN_MAIN = r'''
        // The report: 49 scheduled hours, "Unassigned" and "No task" without the patch.
        Game.Patched = false;
        Check(Game.Cell(new EmployeeModel(crew)) == ("myemployees_no_task", "Unassigned", "49"), "the reported row without the patch");
        Game.Patched = true;
        Check(Game.Cell(new EmployeeModel(crew)) == (CrewScreen.TaskKey, "Food Truck 1", "49"), "healthy crew: vehicle, crew job, 49 hours");
        // Training and sick keep the game's task; their business is the vehicle, as a shop's shows its building.
        Check(Game.Cell(new EmployeeModel(trainee)) == ("myemployees_task_training", "Food Truck 1", "14"), "training task kept");
        Check(Game.Cell(new EmployeeModel(sick)) == ("myemployees_task_absent", "Food Truck 1", "14"), "absence task kept");
        // Others exactly as the game has them.
        Game.Patched = false;
        var vanillaWorker = Game.Cell(new EmployeeModel(worker));
        var vanillaIdle = Game.Cell(new EmployeeModel(idle));
        var vanillaStatuses = new EmployeeModel(worker).statuses.ToList();
        Game.Patched = true;
        Check(Game.Cell(new EmployeeModel(worker)) == vanillaWorker && vanillaWorker == ("ba:itemname_cashregister", "Corner Shop", "40"), "shop employee unchanged");
        Check(new EmployeeModel(worker).statuses.SequenceEqual(vanillaStatuses), "shop employee statuses unchanged");
        Check(Game.Cell(new EmployeeModel(idle)) == vanillaIdle && vanillaIdle == ("myemployees_no_task", "Unassigned", "0"), "idle employee unchanged");
        // Filter and search agree with the table.
        Check(Game.Filter("common_assigned").SequenceEqual(new[] { "crew", "trainee", "sick", "worker" }), "assigned filter: crew and shop");
        Check(Game.Filter("common_unassigned").SequenceEqual(new[] { "idle" }), "unassigned filter: only the idle employee");
        Check(Game.Filter("common_in_training").SequenceEqual(new[] { "trainee" }), "in training filter kept");
        Check(Game.Search("Food Truck").SequenceEqual(new[] { "crew", "trainee", "sick" }), "search finds crew by vehicle");
        // Detail panel: the vehicle is the selected first option; the buildings follow as the game lists them.
        var panel = Game.Show(crew);
        Check(panel.Options.SequenceEqual(new[] { "Food Truck 1", "Corner Shop (address)" }) && panel.Selected == 0 && !panel.Localized, "crew detail panel");
        var shopPanel = Game.Show(worker);
        Check(shopPanel.Options.SequenceEqual(new[] { "Unassigned", "Corner Shop (address)" }) && shopPanel.Selected == 1, "shop detail panel unchanged");
        var idlePanel = Game.Show(idle);
        Check(idlePanel.Options[0] == "Unassigned" && idlePanel.Selected == 0, "idle detail panel unchanged");
        // Manage schedule: the vehicle's page for crew; the game's own behaviour for everyone else.
        Check(Game.ManageSchedule(crew) == "vehicle page truck-1", "crew manage schedule opens the vehicle page");
        Check(Game.ManageSchedule(worker) == "business schedule", "shop manage schedule unchanged");
        Check(Game.ManageSchedule(idle) == "myemployees_no_business_assigned_notification", "idle manage schedule unchanged");
        // Save and reload: the presentation comes back from the saved roster.
        var codec = new SaveCodec();
        FoodTrucksSession.CurrentState = codec.Load(codec.Serialize(state)).State;
        Check(Game.Cell(new EmployeeModel(crew)) == (CrewScreen.TaskKey, "Food Truck 1", "49"), "after reload");
        // No vehicle name known: the localized noun.
        FoodTrucksSession.CurrentState!.Vehicles[1].VehicleId = "truck-unnamed";
        Check(Game.Cell(new EmployeeModel(crew)).Business == "food truck", "fallback vehicle noun");
        FoodTrucksSession.CurrentState = state;
        // Put in a building: the game's row until the crew check takes them off.
        crew.building = shop;
        Check(Game.Cell(new EmployeeModel(crew)).Business == "Corner Shop" && Game.ManageSchedule(crew) == "business schedule", "building job shows as the game's");
        crew.building = null;
        // Removed from the crew: back to the game's row.
        truck.Crew.Remove("crew");
        WeekPlan.DropCrew(truck.Schedule, "crew");
        crew.assignedWeeklyHours = 0;
        Check(Game.Cell(new EmployeeModel(crew)) == ("myemployees_no_task", "Unassigned", "0"), "removed crew reverts");
        Check(new EmployeeModel(crew).statuses.SequenceEqual(new[] { "common_unassigned" }), "removed crew unassigned in the filter");
        Check(Game.Show(crew).Options[0] == "Unassigned" && Game.ManageSchedule(crew) == "myemployees_no_business_assigned_notification", "removed crew panel");
        // No session (main menu, unreadable save): the game's row.
        FoodTrucksSession.CurrentState = null;
        Check(Game.Cell(new EmployeeModel(trainee)) == ("myemployees_task_training", "Unassigned", "14"), "no session");
        FoodTrucksSession.CurrentState = state;
        // A failure in the adapter leaves the game's row and is logged.
        Game.NamesThrow = true;
        Check(Game.Cell(new EmployeeModel(trainee)) == ("myemployees_task_training", "Unassigned", "14") && Game.Error == "names unavailable", "failure logged, row left");
        Game.NamesThrow = false;
        Console.WriteLine("PASS: crew employee screen");
'''

COMPLAINT_MAIN = r'''
        // The no-task complaint and the unassigned to-do read the roster, not the list's labels.
        Game.Patched = false;
        Check(!Patches.HasTaskComplaint(crew) && Patches.Handled(crew), "crew: no complaint, handled");
        Check(!Patches.HasTaskComplaint(trainee) && !Patches.HasTaskComplaint(sick), "crew training or sick: no complaint");
        Check(Patches.HasTaskComplaint(idle) && !Patches.Handled(idle), "idle employee: the game's complaint");
        Check(Patches.HasTaskComplaint(worker), "shop employee: the game's decision kept");
        Check(!Patches.CreatesTodo(TodoTaskType.EmployeeUnassigned, "crew"), "no unassigned to-do for crew");
        Check(Patches.CreatesTodo(TodoTaskType.EmployeeUnassigned, "idle"), "unassigned to-do for others");
        Check(Patches.CreatesTodo(TodoTaskType.EmployeeIdle, "crew"), "other to-dos untouched");
        truck.Crew.Remove("crew");
        Check(Patches.HasTaskComplaint(crew) && Patches.CreatesTodo(TodoTaskType.EmployeeUnassigned, "crew"), "removed crew: the game's complaint and to-do");
        Console.WriteLine("PASS: crew complaints");
'''


if __name__ == '__main__':
    unittest.main()
