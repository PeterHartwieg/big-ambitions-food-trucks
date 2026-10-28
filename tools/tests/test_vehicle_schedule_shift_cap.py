"""A vehicle's Schedule tab edits against the daily shift cap: the production VehicleSchedule methods against the real
Core, with the game's drag, shift creation, employee callbacks and redraw as controlled stand-ins (issue #14)."""
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
SCRIPTS = ROOT / 'unity/FoodTrucks/Scripts'


def member(filename, signature):
    """A member's full text: a block body by brace depth, or an expression body up to its `;`."""
    source = (SCRIPTS / filename).read_text()
    start = source.index(signature)
    arrow, brace = source.find('=>', start), source.find('{', start)
    if arrow != -1 and arrow < brace:
        end, depth = arrow, 0
        while source[end] != ';' or depth:
            depth += (source[end] in '({') - (source[end] in ')}')
            end += 1
        return source[start:end + 1]
    end, depth = brace + 1, 1
    while depth:
        depth += (source[end] == '{') - (source[end] == '}')
        end += 1
    return source[start:end]


def line(filename, text):
    source = (SCRIPTS / filename).read_text()
    if text not in source:
        raise ValueError(f'{text!r} not in {filename}')
    return text


REFUSE = 'private static void Refuse('


def harness():
    source = (SCRIPTS / 'VehicleSchedule.cs').read_text()
    members = [
        line('VehicleSchedule.cs', 'private static BuildingRegistration? standIn;'),
        line('VehicleSchedule.cs', 'private static string? vehicleId;'),
        line('VehicleSchedule.cs', 'internal static bool Showing { get; private set; }'),
        member('VehicleSchedule.cs', 'private static bool Here =>'),
        member('VehicleSchedule.cs', 'private static string RowId('),
        member('VehicleSchedule.cs', 'private static string? StationOf('),
        member('VehicleSchedule.cs', 'private static DayOfWeek WeekdayOf('),
        member('VehicleSchedule.cs', 'private static List<WorkShift> Shifts('),
        member('VehicleSchedule.cs', 'private static void SyncBack('),
        member('VehicleSchedule.cs', 'public static bool EmployeeChangedPrefix('),
        member('VehicleSchedule.cs', 'public static void WeeklyHoursPostfix('),
    ]
    # Before the fix there is no refusal: SyncBack keeps whatever the tab holds (the red run).
    if REFUSE in source:
        members.append(member('VehicleSchedule.cs', REFUSE))
    return HARNESS.replace('/* VEHICLE_SCHEDULE */', '\n'.join(members))


def run(source, marker, *args):
    with tempfile.TemporaryDirectory(prefix='ft-schedule-cap-') as directory:
        folder = Path(directory)
        (folder / 'Program.cs').write_text(source)
        (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>
<ProjectReference Include="{ROOT / 'Core/FoodTrucks.Core.csproj'}" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" /></ItemGroup></Project>''')
        result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q', '--', *args],
                                capture_output=True, text=True)
        return result.returncode == 0 and marker in result.stdout, result.stdout + result.stderr


class VehicleScheduleShiftCapTests(unittest.TestCase):
    def test_native_edits_over_the_cap_are_refused_and_restored(self):
        ok, output = run(harness(), 'PASS: vehicle schedule shift cap')
        self.assertTrue(ok, output)

    def test_paste_over_the_cap_is_refused_once_and_restored(self):
        # Paste and paste to all: the game replaces the destination days, then updates each crew member's weekly hours.
        ok, output = run(harness(), 'PASS: vehicle schedule paste cap', 'paste')
        self.assertTrue(ok, output)

    def test_refusal_is_wired_and_redraws_the_selected_day(self):
        sync = member('VehicleSchedule.cs', 'private static void SyncBack(')
        # Judged against the loaded rules before anything is written to the save.
        self.assertIn('WeekPlan.OverMaxHours(rules.Fleet, schedule.CrewShifts, shifts)', sync)
        self.assertLess(sync.index('OverMaxHours'), sync.index('schedule.CrewShifts = shifts;'))
        self.assertNotIn('MaxShiftHours', sync)
        redraw = member('VehicleSchedule.cs', 'private static void Redraw(')
        for name in ('"daySelectionController"', '"_selectedDayIndex"', '"OnDaySelected"', 'LoadScheduler()'):
            self.assertIn(name, redraw)
        refuse = member('VehicleSchedule.cs', REFUSE)
        self.assertIn('"foodtrucks:notification_schedule_shift_too_long", ("max", rules.MaxShiftHours.ToString())', refuse)
        locale = (ROOT / 'unity/FoodTrucks/Locales/en.json').read_text(encoding='utf-8')
        self.assertIn('"foodtrucks:notification_schedule_shift_too_long": "', locale)
        self.assertIn('{max} hours', locale)
        # The bridge setter keeps its own single-shift check (CheckShift, TooLong) and replace-per-day semantics.
        self.assertNotIn('OverMaxHours', member('VehicleSchedule.cs', 'internal static ShiftProblem SetForBridge('))

    def test_native_names_in_the_game_assembly(self):
        # Optional: re-reads the redraw's private members from a local copy of the game's assembly (never shipped).
        folder = os.environ.get('BA_GAME_DLLS')
        ilspy = shutil.which('ilspycmd')
        if not folder or not (Path(folder) / 'BigAmbitions.dll').is_file() or not ilspy:
            self.skipTest('BA_GAME_DLLS with BigAmbitions.dll and ilspycmd needed')
        dll = str(Path(folder) / 'BigAmbitions.dll')

        def decompile(name):
            result = subprocess.run([ilspy, '--disable-updatecheck', '-t', 'UI.Smartphone.Apps.BizMan.Schedule.' + name, dll],
                                    capture_output=True, text=True)
            self.assertEqual(result.returncode, 0, result.stderr)
            return result.stdout
        controller = decompile('ScheduleDaySelectionController')
        self.assertIn('private int _selectedDayIndex;', controller)
        self.assertIn('private void OnDaySelected(int dayIndex)', controller)
        schedule = decompile('BizManSchedule')
        self.assertIn('private ScheduleDaySelectionController daySelectionController;', schedule)
        self.assertIn('ScheduleHelper.CacheWorkShifts();', schedule)


HARNESS = r'''
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;

// ---- The game's schedule data, and its drag and add as ScheduleHelper.EditWorkShift / AddWorkShift do them ----
enum DayOfWeekOrdered { Monday = 1, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday }
enum WorkShiftType { Default }
class OpeningHourSlot { }
class WorkShift { public int startingHour, endingHour; public string employeeId = ""; public string itemInstanceId = ""; public WorkShiftType type; }
class ScheduleDay {
    public DayOfWeekOrdered day; public bool isOpen = true;
    public List<OpeningHourSlot> openingHourSlots = new(); public List<WorkShift> workShifts = new();
}
class BuildingRegistration { public List<ScheduleDay> scheduleDays = new(); }
class BizManBusiness { public BuildingRegistration? buildingRegistration; }
class EmployeeInstance {
    public string id = "";
    public void UpdateWeeklyHoursAndDays(List<ScheduleDay> days) => VehicleSchedule.WeeklyHoursPostfix(this);
}
static class ScheduleHelper {
    public static BizManBusiness? Business;
    public static List<ScheduleDay> ScheduleDays => Business!.buildingRegistration!.scheduleDays;
    public static ScheduleDay GetScheduleDay(int index) => ScheduleDays[index - 1];
}
static class Native {
    public static int Redraws, NativeUpdates;
    // The drag's handle: it keeps the WorkShift it moved and draws it after the edit (OnSlideEnd's _updateCell).
    public static WorkShift? Dragged;
    // Harmony: the mod's prefix, then the game's update only if it lets it run.
    static void Changed() { if (VehicleSchedule.EmployeeChangedPrefix()) NativeUpdates++; }
    public static void Edit(int dayIndex, string employeeId, int originalStart, int start, int end) {
        var shift = ScheduleHelper.GetScheduleDay(dayIndex).workShifts.Find(s => s.employeeId == employeeId && s.startingHour == originalStart)!;
        shift.startingHour = start; shift.endingHour = end;
        Dragged = shift;
        Changed();
    }
    public static void Add(int dayIndex, string row, string employeeId, int start, int end) {
        ScheduleHelper.GetScheduleDay(dayIndex).workShifts.Add(new WorkShift { startingHour = start, endingHour = end, employeeId = employeeId, itemInstanceId = row });
        Changed();
    }
    // Paste (one day or to all): the destination days get copies of the source day's shifts, then the game's
    // UpdateWeeklyHoursAndDays runs for each crew member, with the mod's postfix after each.
    public static void Paste(int source, params int[] targets) {
        var from = ScheduleHelper.GetScheduleDay(source).workShifts;
        foreach (var target in targets)
            ScheduleHelper.GetScheduleDay(target).workShifts = from.Select(s => new WorkShift { startingHour = s.startingHour,
                endingHour = s.endingHour, employeeId = s.employeeId, itemInstanceId = s.itemInstanceId, type = s.type }).ToList();
        WeeklyHours();
    }
    public static void WeeklyHours() { foreach (var e in HireCrew.Employees) e.UpdateWeeklyHoursAndDays(ScheduleHelper.ScheduleDays); }
}

// ---- Mod boundaries ----
class Rules { public FleetRules Fleet = new FleetRules(); }
static class DemandData { public static Rules? Rules; }
static class FoodTrucksSession {
    public static SaveState? CurrentState;
    public static bool CanWrite => true;
    public static int Changes;
    public static void MarkChanged() => Changes++;
}
static class Log {
    public static List<string> Infos = new(), Errors = new();
    public static void Info(string message) => Infos.Add(message);
    public static void Error(string message) => Errors.Add(message);
}
static class CartSelling {
    public static List<string> Notices = new();
    internal static void Notify(string key, params (string Key, string Value)[] data) =>
        Notices.Add(key + string.Concat(data.Select(d => $" {d.Key}={d.Value}")));
}
static class HireCrew {
    public static List<EmployeeInstance> Employees = new();
    public static int WeeklyWork;
    public static EmployeeInstance? Find(string id) => Employees.FirstOrDefault(e => e.id == id);
    public static void SetWeeklyWork(VehicleState vehicle) => WeeklyWork++;
    public static long Now() => 0;
}
static class HireRoutes {
    public static List<Action> Queue = new();
    public static void Later(Action action) => Queue.Add(action);
    // The next frame.
    public static void Frame() { var due = Queue.ToList(); Queue.Clear(); foreach (var a in due) a(); }
}

static class VehicleSchedule {
    // What VehicleSchedule.Open sets for the stand-in (the rest of Open needs the game's UI; opening hours aren't used).
    internal static void Begin(VehicleState vehicle, BuildingRegistration registration) {
        registration.scheduleDays = Enumerable.Range(1, 7).Select(i => new ScheduleDay { day = (DayOfWeekOrdered)i,
            workShifts = Shifts(vehicle, vehicle.VehicleId, WeekdayOf((DayOfWeekOrdered)i)) }).ToList();
        standIn = registration; vehicleId = vehicle.VehicleId; Showing = true;
    }
    internal static void End() { Showing = false; standIn = null; vehicleId = null; }
    // The game's redraw of the selected day (Redraw's native calls).
    static void Redraw() => Native.Redraws++;
/* VEHICLE_SCHEDULE */
}

class Program {
    static void Check(bool value, string why) { if (!value) throw new Exception("FAIL: " + why); }
    static readonly DayOfWeek[] Week = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
    static int Index(DayOfWeek day) => Array.IndexOf(Week, day) + 1;
    static string Core(VehicleState v) => string.Join(" ", v.Schedule!.CrewShifts.OrderBy(s => Index(s.Day)).ThenBy(s => s.EmployeeId).ThenBy(s => s.Start)
        .Select(s => $"{s.EmployeeId}@{s.Day}:{s.Start}-{s.End}/{s.Station}"));
    // The stand-in's days as the tab would draw them.
    static string Tab(BuildingRegistration r) => string.Join(" ", r.scheduleDays.SelectMany(d => d.workShifts.Select(s => (d.day, s)))
        .OrderBy(p => (int)p.day).ThenBy(p => p.s.employeeId).ThenBy(p => p.s.startingHour)
        .Select(p => $"{p.s.employeeId}@{p.day}:{p.s.startingHour}-{p.s.endingHour}/{p.s.itemInstanceId.Substring(p.s.itemInstanceId.LastIndexOf(':') + 1)}"));
    static string Saved() => new SaveCodec().Serialize(FoodTrucksSession.CurrentState!);

    static (VehicleState, BuildingRegistration) Setup(string kind, int cap, params (string Who, DayOfWeek Day, int Start, int End, string Row)[] shifts) {
        DemandData.Rules = new Rules { Fleet = new FleetRules(maxShiftHours: cap) };
        var state = new SaveState();
        FoodTrucksSession.CurrentState = state;
        var v = new VehicleState { VehicleId = kind + "1", VehicleType = kind, HasPermit = true,
            Crew = kind == "truck" ? new List<string> { "e1", "e2" } : new List<string> { "e1" } };
        v.Schedule = new HireSchedule();
        foreach (var s in shifts) v.Schedule.CrewShifts.Add(new CrewShift { EmployeeId = s.Who, Day = s.Day, Start = s.Start, End = s.End, Station = s.Row });
        WeekPlan.SortShifts(v.Schedule);
        state.Vehicles.Add(v);
        HireCrew.Employees = v.Crew.Select(id => new EmployeeInstance { id = id }).ToList();
        var registration = new BuildingRegistration();
        VehicleSchedule.Begin(v, registration);
        ScheduleHelper.Business = new BizManBusiness { buildingRegistration = registration };
        Native.Redraws = 0; Native.NativeUpdates = 0; Native.Dragged = null;
        HireRoutes.Queue.Clear(); CartSelling.Notices.Clear(); Log.Infos.Clear(); Log.Errors.Clear();
        FoodTrucksSession.Changes = 0;
        Check(Tab(registration) == Core(v), kind + ": stand-in starts on Core's shifts");
        return (v, registration);
    }

    static void Refused(VehicleState v, BuildingRegistration standIn, string core, string save, int cap, string why) {
        Check(Core(v) == core, why + ": Core keeps the accepted shifts, got " + Core(v));
        Check(Saved() == save, why + ": the save is unchanged");
        Check(Tab(standIn) == core, why + ": the stand-in's days are the accepted shifts again, got " + Tab(standIn));
        Check(FoodTrucksSession.Changes == 0, why + ": nothing marked changed");
        Check(CartSelling.Notices.Count == 1 && CartSelling.Notices[0] == $"foodtrucks:notification_schedule_shift_too_long max={cap}",
            why + ": one notice naming the cap, got " + string.Join("|", CartSelling.Notices));
        Check(Native.NativeUpdates == 0, why + ": the game's own employee update stays skipped");
        Check(Native.Redraws == 0, why + ": no redraw inside the game's callback");
        HireRoutes.Frame();
        Check(Native.Redraws == 1, why + ": the tab is drawn again on the next frame");
        // A later callback (another employee update, the page closing) reads the restored days: nothing more happens.
        CartSelling.Notices.Clear();
        VehicleSchedule.EmployeeChangedPrefix();
        HireRoutes.Frame();
        Check(Core(v) == core && CartSelling.Notices.Count == 0 && Native.Redraws == 1 && FoodTrucksSession.Changes == 0,
            why + ": the refused change can't come back on a later sync");
        // Save and reload: only the accepted shifts.
        var again = StreetSales.Find(new SaveCodec().Load(Saved()).State!, v.VehicleId)!;
        Check(Core(again) == core, why + ": reload has the accepted shifts");
    }

    // A paste copies a whole day, so it can only go over the cap from a day saved over it before the cap was checked.
    static void Paste() {
        var M = DayOfWeek.Monday; var T = DayOfWeek.Tuesday;
        foreach (var kind in new[] { "cart", "truck" }) {
            // Tuesday 0-8 plus 10-16 (14 hours) onto Wednesday, and onto every other day (paste to all).
            foreach (var targets in new[] { new[] { 3 }, new[] { 1, 3, 4, 5, 6, 7 } }) {
                var why = $"{kind} paste of the over-limit Tuesday onto {targets.Length} days";
                var (v, standIn) = Setup(kind, 12, ("e1", T, 0, 8, "serving1"), ("e1", T, 10, 16, "serving1"), ("e1", M, 0, 6, "serving1"));
                var core = Core(v); var save = Saved();
                Native.Paste(Index(T), targets);
                Refused(v, standIn, core, save, 12, why);
                // The weekly hours updates again (another paste callback): nothing more happens.
                CartSelling.Notices.Clear();
                Native.WeeklyHours();
                HireRoutes.Frame();
                Check(Core(v) == core && Tab(standIn) == core && CartSelling.Notices.Count == 0 && Native.Redraws == 1 && FoodTrucksSession.Changes == 0,
                    why + ": later weekly hours updates change nothing");
            }

            // A valid day (0-6 plus 8-14, exactly 12) onto Wednesday and Friday: accepted and saved.
            {
                var (v, standIn) = Setup(kind, 12, ("e1", M, 0, 6, "serving1"), ("e1", M, 8, 14, "serving1"));
                Native.Paste(Index(M), 3, 5);
                Check(Core(v) == "e1@Monday:0-6/serving1 e1@Monday:8-14/serving1 e1@Wednesday:0-6/serving1 e1@Wednesday:8-14/serving1 " +
                    "e1@Friday:0-6/serving1 e1@Friday:8-14/serving1", kind + " valid paste: accepted, got " + Core(v));
                Check(Tab(standIn) == Core(v) && CartSelling.Notices.Count == 0 && FoodTrucksSession.Changes == 1, kind + " valid paste: no notice, marked changed once");
                HireRoutes.Frame();
                Check(Native.Redraws == 0, kind + " valid paste: no redraw");
                var again = StreetSales.Find(new SaveCodec().Load(Saved()).State!, v.VehicleId)!;
                Check(Core(again) == Core(v), kind + " valid paste: survives save and reload");
            }
        }
    }

    static void Main(string[] args) {
        if (args.Contains("paste")) {
            Paste();
            Console.WriteLine("PASS: vehicle schedule paste cap");
            return;
        }
        var M = DayOfWeek.Monday; var T = DayOfWeek.Tuesday; var S = DayOfWeek.Sunday;
        foreach (var kind in new[] { "cart", "truck" }) {
            var truck = kind == "truck";

            // 1. One bar stretched past the cap: 0-12 to 0-13, and to 0-24.
            foreach (var end in new[] { 13, 24 }) {
                var (v, standIn) = Setup(kind, 12, ("e1", M, 0, 12, "serving1"));
                var core = Core(v); var save = Saved();
                Native.Edit(Index(M), "e1", 0, 0, end);
                Refused(v, standIn, core, save, 12, $"{kind} stretch to 0-{end}");
                Check(!standIn.scheduleDays[0].workShifts.Contains(Native.Dragged!), kind + ": the dragged shift object is replaced, not kept");
            }

            // 2. Two shifts adding up past the cap: 0-8 plus 10-16 (on the second row too, on a truck).
            foreach (var row in truck ? new[] { "serving1", "serving2" } : new[] { "serving1" }) {
                var (v, standIn) = Setup(kind, 12, ("e1", M, 0, 8, "serving1"));
                var core = Core(v); var save = Saved();
                Native.Add(Index(M), $"ft:{v.VehicleId}:{row}", "e1", 10, 16);
                Refused(v, standIn, core, save, 12, $"{kind} two shifts on {row}");
            }

            // 3. Exactly at the cap with two shifts: accepted and saved.
            {
                var (v, standIn) = Setup(kind, 12, ("e1", M, 0, 6, "serving1"));
                Native.Add(Index(M), $"ft:{v.VehicleId}:serving1", "e1", 8, 14);
                Check(Core(v) == "e1@Monday:0-6/serving1 e1@Monday:8-14/serving1", kind + " at the cap: both shifts kept, got " + Core(v));
                Check(CartSelling.Notices.Count == 0 && FoodTrucksSession.Changes == 1, kind + " at the cap: no notice, marked changed");
                HireRoutes.Frame();
                Check(Native.Redraws == 0, kind + " at the cap: no redraw");
                var again = StreetSales.Find(new SaveCodec().Load(Saved()).State!, v.VehicleId)!;
                Check(Core(again) == Core(v), kind + " at the cap: survives save and reload");
            }

            // 4. A custom cap of 8: 0-8 is fine, 0-9 is refused with the notice naming 8.
            {
                var (v, standIn) = Setup(kind, 8, ("e1", T, 2, 6, "serving1"));
                Native.Edit(Index(T), "e1", 2, 0, 8);
                Check(Core(v) == "e1@Tuesday:0-8/serving1" && CartSelling.Notices.Count == 0, kind + " cap 8: 0-8 accepted, got " + Core(v));
                var core = Core(v); var save = Saved(); FoodTrucksSession.Changes = 0;
                Native.Edit(Index(T), "e1", 0, 0, 9);
                Refused(v, standIn, core, save, 8, kind + " cap 8: 0-9");
            }

            // 5. A midnight split: Sunday 14-24 with Monday 0-4 is fine; Monday 6-15 makes Monday 13 hours.
            {
                var (v, standIn) = Setup(kind, 12, ("e1", S, 14, 24, "serving1"), ("e1", M, 0, 4, "serving1"));
                var core = Core(v); var save = Saved();
                Native.Add(Index(M), $"ft:{v.VehicleId}:serving1", "e1", 6, 15);
                Refused(v, standIn, core, save, 12, kind + " midnight split");
                FoodTrucksSession.Changes = 0;
                Native.Add(Index(M), $"ft:{v.VehicleId}:serving1", "e1", 6, 14);
                Check(Core(v) == "e1@Monday:0-4/serving1 e1@Monday:6-14/serving1 e1@Sunday:14-24/serving1", kind + " midnight split: 12 on Monday accepted, got " + Core(v));
            }

            // 6. A save with an over-limit day from before the cap was checked: an unrelated valid edit is accepted, the old
            //    day stays as saved, no notice; changing the old day's hours is judged.
            {
                var (v, standIn) = Setup(kind, 12, ("e1", T, 0, 8, "serving1"), ("e1", T, 10, 16, "serving1"), ("e1", M, 0, 6, "serving1"));
                Native.Edit(Index(M), "e1", 0, 0, 8);
                Check(Core(v) == "e1@Monday:0-8/serving1 e1@Tuesday:0-8/serving1 e1@Tuesday:10-16/serving1", kind + " legacy: unrelated edit accepted, got " + Core(v));
                Check(Tab(standIn) == Core(v), kind + " legacy: the tab still shows the old day");
                Check(CartSelling.Notices.Count == 0 && FoodTrucksSession.Changes == 1, kind + " legacy: no notice for the old day");
                HireRoutes.Frame();
                Check(Native.Redraws == 0, kind + " legacy: no redraw");
                if (truck) {
                    // Another crew member, the same Tuesday.
                    Native.Add(Index(T), $"ft:{v.VehicleId}:serving2", "e2", 0, 12);
                    Check(Core(v).Contains("e2@Tuesday:0-12/serving2") && CartSelling.Notices.Count == 0, kind + " legacy: second crew member's day accepted");
                }
                var core = Core(v); var save = Saved(); FoodTrucksSession.Changes = 0;
                Native.Edit(Index(T), "e1", 10, 11, 16);
                Refused(v, standIn, core, save, 12, kind + " legacy: still over after the edit");
                Native.Edit(Index(T), "e1", 10, 12, 16);
                Check(Core(v).Contains("e1@Tuesday:0-8/serving1 e1@Tuesday:12-16/serving1"), kind + " legacy: brought within the cap, accepted, got " + Core(v));
            }

            // 7. The tab closed before the next frame: no redraw of a page that isn't the vehicle's.
            {
                var (v, standIn) = Setup(kind, 12, ("e1", M, 0, 12, "serving1"));
                Native.Edit(Index(M), "e1", 0, 0, 13);
                VehicleSchedule.End();
                HireRoutes.Frame();
                Check(Native.Redraws == 0 && Core(v) == "e1@Monday:0-12/serving1", kind + " closed: no redraw, Core kept");
            }

            // 8. Two crew members each at 12 hours on their own rows (a truck): per crew member, not per vehicle.
            if (truck) {
                var (v, standIn) = Setup(kind, 12);
                Native.Add(Index(M), $"ft:{v.VehicleId}:serving1", "e1", 0, 12);
                Native.Add(Index(M), $"ft:{v.VehicleId}:serving2", "e2", 0, 12);
                Check(Core(v) == "e1@Monday:0-12/serving1 e2@Monday:0-12/serving2" && CartSelling.Notices.Count == 0, kind + " two crew at 12: accepted, got " + Core(v));
            }
        }
        Console.WriteLine("PASS: vehicle schedule shift cap");
    }
}
'''


if __name__ == '__main__':
    unittest.main()
