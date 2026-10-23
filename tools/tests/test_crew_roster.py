"""Exercise the production roster admission and UI with controlled employee/UI boundaries."""
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


class CrewRosterTests(unittest.TestCase):
    def test_add_replace_remove_and_keep_offering_employees_beyond_serving_capacity(self):
        with tempfile.TemporaryDirectory(prefix='ft-crew-roster-') as directory:
            folder = Path(directory)
            source = HARNESS.replace('/* ADD */', method('HireCrew.cs', 'public static bool Add('))
            source = source.replace('/* CREW */', method('BizManWeekPlan.cs', 'private static void Crew('))
            source = source.replace('/* CHOOSE */', method('BizManVehiclePage.cs', 'internal static void ChooseCrew('))
            (folder / 'Program.cs').write_text(source)
            (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>
<ProjectReference Include="{ROOT / 'Core/FoodTrucks.Core.csproj'}" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" /></ItemGroup></Project>''')
            result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q'],
                                    capture_output=True, text=True, timeout=120)
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS: crew roster', result.stdout)


HARNESS = r'''
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Fleet;

class Program {
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static void Main() {
        foreach (var kind in new[] { "cart", "truck" }) {
            var state = new SaveState();
            FoodTrucksSession.CurrentState = state;
            var vehicle = new VehicleState { VehicleId = kind, VehicleType = kind };
            var other = new VehicleState { VehicleId = "other", VehicleType = kind };
            state.Vehicles.AddRange(new[] { vehicle, other });
            HireCrew.Employees = Enumerable.Range(1, 102).Select(i => new EmployeeInstance { id = "e" + i }).ToList();
            Check(HireCrew.Add(state, other, HireCrew.Find("e102")!), "assign other vehicle");
            for (var i = 1; i <= 100; i++) {
                BizManWeekPlan.Draw(state, vehicle);
                Check(BizManVehiclePage.Rows.Count == i, "one row per employee plus add row");
                var add = BizManVehiclePage.Rows.Last();
                Check(add.Label == "foodtrucks:bizman_page_crew_add", "add row remains visible");
                Check(!add.Options.Contains("e102"), "other vehicle's employee not offered");
                add.Pick(add.Options.IndexOf("e" + i));
                Check(vehicle.Crew.Count == i && vehicle.Crew.Last() == "e" + i, "add without replacing crew");
            }
            Check(!HireCrew.Add(state, vehicle, HireCrew.Find("e1")!), "duplicate still refused");
            Check(!HireCrew.Add(state, vehicle, HireCrew.Find("e102")!), "other vehicle assignment refused");
            BizManWeekPlan.Draw(state, vehicle);
            var replace = BizManVehiclePage.Rows[49];
            replace.Pick(replace.Options.IndexOf("e101"));
            Check(vehicle.Crew.Count == 100 && vehicle.Crew[49] == "e101" && !vehicle.Crew.Contains("e50"), "replace beyond serving limit");
            BizManWeekPlan.Draw(state, vehicle);
            BizManVehiclePage.Rows[49].Pick(0);
            Check(vehicle.Crew.Count == 99 && !vehicle.Crew.Contains("e101"), "remove beyond serving limit");
            BizManWeekPlan.Draw(state, vehicle);
            Check(BizManVehiclePage.Rows.Last().Options.Contains("e101"), "removed employee available again");
            var stale = BizManVehiclePage.Rows[49];
            var displayed = vehicle.Crew[49];
            HireCrew.Remove(vehicle, vehicle.Crew[0], "resigned while page open");
            var afterResignation = vehicle.Crew.ToArray();
            stale.Pick(0);
            Check(vehicle.Crew.SequenceEqual(afterResignation) && vehicle.Crew.Contains(displayed), "stale row cannot remove a different employee");
            BizManWeekPlan.Draw(state, vehicle);
            var refreshed = BizManVehiclePage.Rows[vehicle.Crew.IndexOf(displayed)];
            refreshed.Pick(0);
            Check(!vehicle.Crew.Contains(displayed), "refreshed row removes the intended employee");
        }
        Console.WriteLine("PASS: crew roster");
    }
}
class EmployeeInstance { public string id = ""; public double satisfaction = 100, hourlyWage = 10; }
class Transform { }
static class FoodTrucksSession {
    public static SaveState? CurrentState;
    public static bool CanWrite => true;
    public static void MarkChanged() { }
}
static class Log { public static void Info(string message) { } }
static class CartSelling {
    public static void Say(string key, string kind, params (string, string)[] args) { }
    public static void Notify(string key, params (string, string)[] args) { }
}
static class HireCrew {
    public static List<EmployeeInstance> Employees = new();
    public static EmployeeInstance? Find(string id) => Employees.FirstOrDefault(e => e.id == id);
    public static string Name(EmployeeInstance e) => e.id;
    public static double Skill(EmployeeInstance e) => 50;
    public static int Limit(string kind) => kind == "truck" ? 2 : 1;
    public static void SetWeeklyWork(VehicleState v) { }
    public static void ClearUnassignedTask(EmployeeInstance e) { }
    public static List<EmployeeInstance> Available(SaveState state) => Employees
        .Where(e => !state.Vehicles.Any(v => v.Crew.Contains(e.id))).ToList();
    public static void Remove(VehicleState v, string id, string reason) { v.Crew.Remove(id); WeekPlan.DropCrew(v.Schedule, id); }
    /* ADD */
}
static class BizManParts {
    public class Box { public Box(Transform t, string title) { } public void Section(string text) { } }
}
static class BizManVehiclePage {
    public record Row(string Label, List<string> Options, Action<int> Pick);
    public static List<Row> Rows = new();
    public static string CrewOption(EmployeeInstance e) => e.id;
    public static void AddRow(BizManParts.Box b, string label, List<string> options, int selected, bool enabled, Action<int> pick)
        => Rows.Add(new Row(label, options, pick));
    static string KindOf(string id) => id;
    static void RefreshLater() { }
    /* CHOOSE */
}
static class BizManWeekPlan {
    static string Text(string key) => key;
    static string Format(string key, params (string, string)[] args) => key;
    public static void Draw(SaveState state, VehicleState vehicle) {
        BizManVehiclePage.Rows.Clear();
        Crew(new Transform(), state, vehicle, vehicle.VehicleId, vehicle.VehicleType, true);
    }
    /* CREW */
}
'''
