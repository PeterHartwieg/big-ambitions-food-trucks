"""A vehicle's Schedule tab day paste: the production VehicleSchedule methods against the real Core, with the game's
paste, employee callbacks and opening-hours calculation as controlled stand-ins (issue #15)."""
import os
from pathlib import Path
import re
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


PREFIX = 'public static void OpeningStatePrefix('


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
        member('VehicleSchedule.cs', 'private static List<ScheduleDay> Days('),
        member('VehicleSchedule.cs', 'private static List<WorkShift> Shifts('),
        member('VehicleSchedule.cs', 'private static void SyncBack('),
        member('VehicleSchedule.cs', 'public static void WeeklyHoursPostfix('),
        member('VehicleSchedule.cs', 'private static void OpeningHours('),
    ]
    # Before the fix there is no prefix: the native calculation then runs unpatched (the red run).
    has_prefix = PREFIX in source
    if has_prefix:
        members.append(member('VehicleSchedule.cs', PREFIX))
    return (HARNESS.replace('/* VEHICLE_SCHEDULE */', '\n'.join(members))
            .replace('/* HOME_POINT */', member('HireRoutes.cs', 'internal static (double X, double Z)? HomePoint('))
            .replace('/* PREFIX */', 'VehicleSchedule.OpeningStatePrefix' if has_prefix else 'null'))


def run(source, marker):
    with tempfile.TemporaryDirectory(prefix='ft-schedule-paste-') as directory:
        folder = Path(directory)
        (folder / 'Program.cs').write_text(source)
        (folder / 'Harness.csproj').write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup>
<OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable>
<ImplicitUsings>enable</ImplicitUsings><NuGetAudit>false</NuGetAudit></PropertyGroup><ItemGroup>
<ProjectReference Include="{ROOT / 'Core/FoodTrucks.Core.csproj'}" />
<PackageReference Include="Newtonsoft.Json" Version="13.0.2" /></ItemGroup></Project>''')
        result = subprocess.run(['dotnet', 'run', '--project', str(folder / 'Harness.csproj'), '-v:q'],
                                capture_output=True, text=True)
        return result.returncode == 0 and marker in result.stdout, result.stdout + result.stderr


class VehicleSchedulePasteTests(unittest.TestCase):
    def test_paste_keeps_route_hours_and_copies_shifts(self):
        ok, output = run(harness(), 'PASS: vehicle schedule paste')
        self.assertTrue(ok, output)

    def test_prefix_is_registered_and_guarded(self):
        patches = re.sub(r'\s+', ' ', (SCRIPTS / 'Patches.cs').read_text())
        self.assertIn('Patch(schedule, "CalculateOpeningHoursState", Type.EmptyTypes, typeof(VehicleSchedule), '
                      'prefix: nameof(VehicleSchedule.OpeningStatePrefix));', patches)
        # Registered before CheckReady, with the other Schedule patches.
        self.assertLess(patches.index('"CalculateOpeningHoursState"'), patches.index('VehicleSchedule.CheckReady();'))
        self.assertGreater(patches.index('"CalculateOpeningHoursState"'), patches.index('var schedule = typeof('))
        schedule = (SCRIPTS / 'VehicleSchedule.cs').read_text()
        guards = schedule[schedule.index('Guards ='):schedule.index('internal static void CheckReady()')]
        self.assertIn('(typeof(ScheduleHelper), "CalculateOpeningHoursState", Type.EmptyTypes, false),', guards)
        # Inert for every real business: the stand-in boundary is the prefix's first line.
        body = member('VehicleSchedule.cs', PREFIX)
        self.assertIn('if (!Here) return;', body.split('{', 1)[1].split(';', 1)[0] + ';')
        # Restores hours only: no shift, open-state or day replacement.
        for forbidden in ('workShifts', 'isOpen', 'scheduleDays =', 'SyncBack'):
            self.assertNotIn(forbidden, body)

    def test_native_name_in_the_game_assembly(self):
        # Optional: re-reads the patched method from a local copy of the game's assembly (never shipped or committed).
        folder = os.environ.get('BA_GAME_DLLS')
        ilspy = shutil.which('ilspycmd')
        if not folder or not (Path(folder) / 'BigAmbitions.dll').is_file() or not ilspy:
            self.skipTest('BA_GAME_DLLS with BigAmbitions.dll and ilspycmd needed')
        result = subprocess.run([ilspy, '--disable-updatecheck', '-t', 'UI.Smartphone.Apps.BizMan.Schedule.ScheduleHelper',
                                 str(Path(folder) / 'BigAmbitions.dll')], capture_output=True, text=True)
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertEqual(result.stdout.count('CalculateOpeningHoursState('), result.stdout.count('CalculateOpeningHoursState()'))
        self.assertIn('public static void CalculateOpeningHoursState()', result.stdout)


HARNESS = r'''
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;

// ---- The game's schedule data and the paste, as ScheduleDayButton / ScheduleDaySelectionController do it ----
enum DayOfWeekOrdered { Monday = 1, Tuesday, Wednesday, Thursday, Friday, Saturday, Sunday }
enum WorkShiftType { Default }
class OpeningHourSlot {
    public int startingHour, endingHour;
    public OpeningHourSlot(int start, int end) { startingHour = start; endingHour = end; }
}
class WorkShift { public int startingHour, endingHour; public string employeeId = ""; public string itemInstanceId = ""; public WorkShiftType type; }
class ScheduleDay {
    public DayOfWeekOrdered day; public bool isOpen;
    public List<OpeningHourSlot> openingHourSlots = new(); public List<WorkShift> workShifts = new();
}
class BuildingRegistration { public List<ScheduleDay> scheduleDays = new(); }
class BizManBusiness { public BuildingRegistration? buildingRegistration; }
class EmployeeInstance {
    public string id = "";
    // The game clears an unassigned employee's hours; Harmony then runs the mod's postfix.
    public void UpdateWeeklyHoursAndDays(List<ScheduleDay> days) { Native.WeeklyCalls++; VehicleSchedule.WeeklyHoursPostfix(this); }
}
static class ScheduleHelper {
    public static BizManBusiness? Business;
    public static List<EmployeeInstance> Employees = new();
    public static List<ScheduleDay> ScheduleDays => Business!.buildingRegistration!.scheduleDays;
    public static ScheduleDay CurrentScheduleDay = new();
    public static bool[] OpeningHoursState = new bool[24];
    public static ScheduleDay GetScheduleDay(int index) => ScheduleDays[index - 1];
    // Harmony prefix (void: the original always runs), then the game's calculation.
    static readonly Action? Prefix = /* PREFIX */;
    public static void CalculateOpeningHoursState() {
        Prefix?.Invoke();
        Native.Slots = ScheduleDays.Select(d => Program.Slots(d.openingHourSlots)).ToList();
        OpeningHoursState = new bool[24];
        if (!CurrentScheduleDay.isOpen) return;
        foreach (var slot in CurrentScheduleDay.openingHourSlots)
            for (var i = slot.startingHour; i < slot.endingHour; i++) OpeningHoursState[i] = true;
    }
}
static class Native {
    public static ScheduleDay? Copied;
    public static int WeeklyCalls;
    // Every day's slots as the calculation consumed them (after any prefix).
    public static List<string> Slots = new();
    public static void Copy(int index) => Copied = ScheduleHelper.GetScheduleDay(index);
    public static void Paste(int index, bool partOfPasteToAll) {
        if (Copied == null || !ScheduleHelper.GetScheduleDay(index).isOpen) return;
        var day = ScheduleHelper.GetScheduleDay(index);
        day.isOpen = Copied.isOpen;
        day.openingHourSlots = Copied.openingHourSlots.Select(s => new OpeningHourSlot(s.startingHour, s.endingHour)).ToList();
        day.workShifts = Copied.workShifts.Select(s => new WorkShift { startingHour = s.startingHour, endingHour = s.endingHour,
            employeeId = s.employeeId, itemInstanceId = s.itemInstanceId, type = s.type }).ToList();
        if (!partOfPasteToAll) {
            foreach (var employee in ScheduleHelper.Employees) employee.UpdateWeeklyHoursAndDays(ScheduleHelper.ScheduleDays);
            Select(index);
        }
    }
    // The confirmed paste to all: every other day, then the employees once and the source selected.
    public static void PasteToAll(int index) {
        for (var i = 1; i <= 7; i++) if (i != index) Paste(i, true);
        foreach (var employee in ScheduleHelper.Employees) employee.UpdateWeeklyHoursAndDays(ScheduleHelper.ScheduleDays);
        Select(index);
    }
    // BizManSchedule.OnDaySelected -> ScheduleHeaderController.UpdateState -> CalculateOpeningHoursState.
    public static void Select(int index) {
        ScheduleHelper.CurrentScheduleDay = ScheduleHelper.GetScheduleDay(index);
        ScheduleHelper.CalculateOpeningHoursState();
    }
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
static class HireCrew {
    public static List<EmployeeInstance> Employees = new();
    public static int WeeklyWork;
    public static EmployeeInstance? Find(string id) => Employees.FirstOrDefault(e => e.id == id);
    public static void SetWeeklyWork(VehicleState vehicle) => WeeklyWork++;
    public static long Now() => 0;
}
static class HireRoutes {
    public static (double X, double Z) Bay = (-1000, 0);
    internal static (ScheduleStop Place, string SiteId, IReadOnlyCollection<int> BayStops)? HomeOf(VehicleState vehicle) =>
        (new ScheduleStop { SpotKey = HireSchedules.HomeSpotKey, X = Bay.X, Z = Bay.Z }, "site", new List<int>());
    internal static double GameSpeed() => 1.0;
    /* HOME_POINT */
}

static class VehicleSchedule {
    // What VehicleSchedule.Open sets for the stand-in (the rest of Open needs the game's UI).
    internal static void Begin(VehicleState vehicle, BuildingRegistration registration) {
        registration.scheduleDays = Days(vehicle, vehicle.VehicleId);
        standIn = registration; vehicleId = vehicle.VehicleId; Showing = true;
    }
    internal static void End() { Showing = false; standIn = null; vehicleId = null; }
    internal static List<ScheduleDay> DaysFor(VehicleState vehicle) => Days(vehicle, vehicle.VehicleId);
/* VEHICLE_SCHEDULE */
}

class Program {
    static void Check(bool value, string why) { if (!value) throw new Exception("FAIL: " + why); }
    public static string Slots(IEnumerable<OpeningHourSlot> slots) => string.Join(" ", slots.Select(s => $"{s.startingHour}-{s.endingHour}"));
    static string Shifts(VehicleState v, DayOfWeek day) => string.Join(" ", v.Schedule!.CrewShifts.Where(s => s.Day == day)
        .OrderBy(s => s.EmployeeId).ThenBy(s => s.Start).Select(s => $"{s.EmployeeId}@{s.Start}-{s.End}/{s.Station}"));
    static string Route(VehicleState v) => Newtonsoft.Json.JsonConvert.SerializeObject(new { v.Schedule!.Stops, v.Schedule.Closed });
    // A cart is pushed, so its route is a tenth of the truck's.
    static double Scale = 1;
    static ScheduleStop Stop(DayOfWeek day, double x, int open, int close) => new() { Day = day, SpotKey = "spot" + day, X = x * Scale, OpenAt = open, CloseAt = close };
    static readonly DayOfWeek[] Week = { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
    static int Index(DayOfWeek day) => Array.IndexOf(Week, day) + 1;

    static VehicleState Vehicle(string kind, bool crew, bool identical) {
        var truck = kind == "truck";
        Scale = truck ? 1 : 0.1;
        HireRoutes.Bay = (-1000 * Scale, 0);
        var v = new VehicleState { VehicleId = kind + "1", VehicleType = kind, HasPermit = true,
            Crew = crew ? (truck ? new List<string> { "e1", "e2" } : new List<string> { "e1" }) : new List<string>() };
        v.Schedule = new HireSchedule {
            Stops = WeekPlan.Ordered(new[] {
                Stop(DayOfWeek.Monday, 0, 9 * 60, 12 * 60),
                Stop(DayOfWeek.Tuesday, 300, 14 * 60, 18 * 60),
                Stop(DayOfWeek.Wednesday, -300, 18 * 60, 21 * 60),
                Stop(DayOfWeek.Thursday, 0, 10 * 60, 14 * 60),
                // Past midnight: Saturday opens 00:00 with no stop of its own.
                Stop(DayOfWeek.Friday, 0, 21 * 60, 25 * 60),
            }),
            Closed = new List<DayOfWeek> { DayOfWeek.Thursday },
        };
        if (crew) {
            void Add(string e, DayOfWeek d, int a, int b, string row) => v.Schedule.CrewShifts.Add(new CrewShift { EmployeeId = e, Day = d, Start = a, End = b, Station = row });
            Add("e1", DayOfWeek.Monday, 9, 12, "serving1");
            if (truck) Add("e2", DayOfWeek.Monday, 8, 13, "serving2");
            if (identical) Add("e1", DayOfWeek.Tuesday, 9, 12, "serving1");
            else Add("e1", DayOfWeek.Tuesday, 14, 18, "serving1");
            Add("e1", DayOfWeek.Wednesday, 18, 21, "serving1");
            Add("e1", DayOfWeek.Thursday, 10, 14, "serving1");
            Add("e1", DayOfWeek.Friday, 21, 24, "serving1");
            Add("e1", DayOfWeek.Saturday, 0, 2, "serving1");
            WeekPlan.SortShifts(v.Schedule);
        }
        return v;
    }

    // The route's opening hours as Core projects them, before any paste.
    static List<string> Expected(VehicleState v) {
        var home = HireRoutes.HomeOf(v);
        var hours = WeekPlan.RouteHours(WeekPlan.RouteDays(new HireSchedules(DemandData.Rules!.Fleet), v.Schedule, Travel.ModeFor(v.VehicleType),
            1.0, HireRoutes.HomePoint(home), home?.BayStops));
        return Week.Select(d => string.Join(" ", WeekPlan.OpeningSlots(hours, d).Select(s => $"{s.Start}-{s.End}"))).ToList();
    }

    static (VehicleState Vehicle, BuildingRegistration StandIn, List<string> Expected) Setup(string kind, bool crew = true, bool identical = false) {
        var state = new SaveState();
        FoodTrucksSession.CurrentState = state;
        var v = Vehicle(kind, crew, identical);
        state.Vehicles.Add(v);
        HireCrew.Employees = new[] { "e1", "e2" }.Select(id => new EmployeeInstance { id = id }).ToList();
        var registration = new BuildingRegistration();
        VehicleSchedule.Begin(v, registration);
        ScheduleHelper.Business = new BizManBusiness { buildingRegistration = registration };
        // FetchEmployees: the vehicle's crew.
        ScheduleHelper.Employees = v.Crew.Select(HireCrew.Find).Where(e => e != null).Select(e => e!).ToList();
        Native.Copied = null; Native.WeeklyCalls = 0; HireCrew.WeeklyWork = 0; FoodTrucksSession.Changes = 0;
        Log.Infos.Clear(); Log.Errors.Clear();
        var expected = Expected(v);
        // The fixture means something: distinct bands, travel around the stops, the midnight carryover, a closed day.
        Check(expected[0] != expected[1] && expected[1] != expected[2], kind + ": distinct route bands " + string.Join("|", expected));
        Check(expected[0] != "9-12" && expected[1] != "14-18", kind + ": bands include the drive from the bay " + string.Join("|", expected));
        Check(expected[5].StartsWith("0-"), kind + ": Saturday carries Friday past midnight " + string.Join("|", expected));
        Check(expected[3] == "" && expected[6] == "", kind + ": no hours on the closed Thursday or the empty Sunday " + string.Join("|", expected));
        // The stand-in starts on the route's hours.
        Check(registration.scheduleDays.Select(d => Slots(d.openingHourSlots)).SequenceEqual(expected), kind + ": stand-in starts on the route");
        return (v, registration, expected);
    }

    static void RouteHoursEverywhere(BuildingRegistration standIn, List<string> expected, string why) {
        Check(Native.Slots.SequenceEqual(expected), why + ": calculation read the route's hours, got " + string.Join("|", Native.Slots) + " want " + string.Join("|", expected));
        Check(standIn.scheduleDays.Select(d => Slots(d.openingHourSlots)).SequenceEqual(expected), why + ": stand-in days on the route's hours");
    }

    static void StateFor(BuildingRegistration standIn, List<string> expected, DayOfWeek day, string why) {
        Native.Select(Index(day));
        var want = new bool[24];
        if (standIn.scheduleDays[Index(day) - 1].isOpen)
            foreach (var slot in expected[Index(day) - 1].Split(' ', StringSplitOptions.RemoveEmptyEntries))
                for (var h = int.Parse(slot.Split('-')[0]); h < int.Parse(slot.Split('-')[1]); h++) want[h] = true;
        Check(ScheduleHelper.OpeningHoursState.SequenceEqual(want), $"{why}: {day} opening state follows its route");
        // An empty row's add button sits on the first open hour (GenerateAddEmployeeVisibilityLookup).
        Check(Array.IndexOf(ScheduleHelper.OpeningHoursState, true) == Array.IndexOf(want, true), $"{why}: {day} add button on its own first hour");
    }

    static void Main() {
        DemandData.Rules = new Rules();
        foreach (var kind in new[] { "cart", "truck" }) {
            var truck = kind == "truck";
            var mon = truck ? "e1@9-12/serving1 e2@8-13/serving2" : "e1@9-12/serving1";

            // 1. Single paste Monday -> Tuesday: Tuesday keeps its band, takes Monday's shifts.
            var (v, standIn, expected) = Setup(kind);
            var route = Route(v);
            Native.Select(1); Native.Copy(1); Native.Paste(2, false);
            RouteHoursEverywhere(standIn, expected, kind + " single paste");
            StateFor(standIn, expected, DayOfWeek.Tuesday, kind + " single paste");
            Check(Shifts(v, DayOfWeek.Tuesday) == mon, kind + " single paste: Tuesday has Monday's shifts in Core, got " + Shifts(v, DayOfWeek.Tuesday));
            Check(Shifts(v, DayOfWeek.Monday) == mon && Shifts(v, DayOfWeek.Wednesday) == "e1@18-21/serving1", kind + " single paste: other days unchanged");
            Check(Route(v) == route, kind + " single paste: stops and closed days unchanged");
            Check(standIn.scheduleDays[1].workShifts.Count == (truck ? 2 : 1) && standIn.scheduleDays[1].isOpen, kind + " single paste: stand-in day keeps pasted shifts, open");
            Check(HireCrew.WeeklyWork > 0 && Native.WeeklyCalls == v.Crew.Count, kind + " single paste: crew callbacks ran");
            // Save and reload: the shifts and route stay, the hours come from the route again.
            var reloaded = new SaveCodec().Load(new SaveCodec().Serialize(FoodTrucksSession.CurrentState!)).State!;
            var again = StreetSales.Find(reloaded, v.VehicleId)!;
            Check(Shifts(again, DayOfWeek.Tuesday) == mon && Route(again) == route, kind + " reload keeps shifts and route");
            Check(VehicleSchedule.DaysFor(again).Select(d => Slots(d.openingHourSlots)).SequenceEqual(expected), kind + " reload: fresh days agree with the repaired ones");

            // 2. Single paste onto the midnight carryover day.
            (v, standIn, expected) = Setup(kind);
            Native.Select(1); Native.Copy(1); Native.Paste(6, false);
            RouteHoursEverywhere(standIn, expected, kind + " paste onto Saturday");
            StateFor(standIn, expected, DayOfWeek.Saturday, kind + " paste onto Saturday");
            Check(Shifts(v, DayOfWeek.Saturday) == mon, kind + " paste onto Saturday: shifts copied");

            // 3. Paste to all from Monday: every open day keeps its band; the closed Thursday is untouched.
            (v, standIn, expected) = Setup(kind);
            route = Route(v);
            var thursday = standIn.scheduleDays[3];
            var thursdayShifts = thursday.workShifts;
            Native.Select(1); Native.Copy(1); Native.PasteToAll(1);
            RouteHoursEverywhere(standIn, expected, kind + " paste to all");
            foreach (var day in Week) StateFor(standIn, expected, day, kind + " paste to all");
            foreach (var day in new[] { DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday })
                Check(Shifts(v, day) == mon, $"{kind} paste to all: {day} has Monday's shifts, got {Shifts(v, day)}");
            Check(!thursday.isOpen && thursday.workShifts == thursdayShifts && Slots(thursday.openingHourSlots) == "", kind + " paste to all: closed Thursday untouched");
            Check(Shifts(v, DayOfWeek.Thursday) == "e1@10-14/serving1", kind + " paste to all: closed Thursday's shift kept in Core");
            Check(Route(v) == route, kind + " paste to all: stops and closed days unchanged");
            Check(Native.WeeklyCalls == v.Crew.Count, kind + " paste to all: crew callbacks once");

            // 4. Empty source (Sunday, open, no shifts, no hours): shifts cleared, hours and add buttons stay the route's.
            foreach (var all in new[] { false, true }) {
                (v, standIn, expected) = Setup(kind);
                Native.Select(7); Native.Copy(7);
                if (all) Native.PasteToAll(7); else Native.Paste(2, false);
                var why = $"{kind} empty source{(all ? " to all" : "")}";
                RouteHoursEverywhere(standIn, expected, why);
                StateFor(standIn, expected, DayOfWeek.Tuesday, why);
                StateFor(standIn, expected, DayOfWeek.Monday, why);
                Check(Shifts(v, DayOfWeek.Tuesday) == "", why + ": Tuesday's shifts cleared in Core");
                Check(standIn.scheduleDays[1].workShifts.Count == 0, why + ": cleared shifts stay cleared on the stand-in");
                Check(all ? Shifts(v, DayOfWeek.Monday) == "" && Shifts(v, DayOfWeek.Thursday) == "e1@10-14/serving1"
                          : Shifts(v, DayOfWeek.Monday) == mon, why + ": other days as the paste left them");
            }

            // 5. Zero roster: no employee callback at all; the hours still follow the route.
            foreach (var all in new[] { false, true }) {
                (v, standIn, expected) = Setup(kind, crew: false);
                Native.Select(1); Native.Copy(1);
                if (all) Native.PasteToAll(1); else Native.Paste(2, false);
                var why = $"{kind} zero roster{(all ? " to all" : "")}";
                Check(Native.WeeklyCalls == 0 && HireCrew.WeeklyWork == 0, why + ": no callbacks");
                RouteHoursEverywhere(standIn, expected, why);
                StateFor(standIn, expected, DayOfWeek.Tuesday, why);
                Check(v.Schedule!.CrewShifts.Count == 0, why + ": no shifts");
            }

            // 6. Identical shifts: SyncBack finds no change and returns early; the hours still follow the route.
            (v, standIn, expected) = Setup(kind, identical: true);
            Native.Select(1); Native.Copy(1);
            if (truck) {
                // Keep it identical on a truck too: copy a one-shift day.
                standIn.scheduleDays[0].workShifts.RemoveAll(s => s.employeeId == "e2");
                v.Schedule!.CrewShifts.RemoveAll(s => s.EmployeeId == "e2");
            }
            var before = Newtonsoft.Json.JsonConvert.SerializeObject(v.Schedule);
            Native.Paste(2, false);
            RouteHoursEverywhere(standIn, expected, kind + " identical shifts");
            StateFor(standIn, expected, DayOfWeek.Tuesday, kind + " identical shifts");
            Check(Newtonsoft.Json.JsonConvert.SerializeObject(v.Schedule) == before && FoodTrucksSession.Changes == 0, kind + " identical shifts: early return, nothing written");
            Check(HireCrew.WeeklyWork > 0 && !Log.Infos.Any(m => m.Contains("now has")), kind + " identical shifts: weekly work set, no shift change logged");

            // 7. A real business: the copied hours stay copied, the prefix does nothing, and the vehicle isn't touched.
            foreach (var showing in new[] { false, true }) {
                (v, standIn, expected) = Setup(kind);
                if (!showing) VehicleSchedule.End();
                var vehicleBefore = Newtonsoft.Json.JsonConvert.SerializeObject(v.Schedule);
                var shop = new BuildingRegistration { scheduleDays = Week.Select((d, i) => new ScheduleDay {
                    day = (DayOfWeekOrdered)(i + 1), isOpen = d != DayOfWeek.Thursday,
                    openingHourSlots = new List<OpeningHourSlot> { new(8 + i, 12 + i) },
                    workShifts = new List<WorkShift> { new() { startingHour = 8 + i, endingHour = 12 + i, employeeId = "s" + i, itemInstanceId = "till" } },
                }).ToList() };
                ScheduleHelper.Business = new BizManBusiness { buildingRegistration = shop };
                ScheduleHelper.Employees = new List<EmployeeInstance> { new() { id = "s0" }, HireCrew.Employees[0] };
                var standInBefore = standIn.scheduleDays.Select(d => Slots(d.openingHourSlots)).ToList();
                var why = $"{kind} real business{(showing ? " beside the stand-in" : "")}";
                Native.Select(1); Native.Copy(1); Native.Paste(2, false);
                Check(Slots(shop.scheduleDays[1].openingHourSlots) == "8-12" && shop.scheduleDays[1].workShifts.Single().employeeId == "s0", why + ": single paste copies hours and shifts");
                Check(ScheduleHelper.OpeningHoursState.Select((o, h) => o == (h >= 8 && h < 12)).All(x => x), why + ": state from the copied hours");
                Native.PasteToAll(1);
                Check(shop.scheduleDays.Where(d => d.isOpen).All(d => Slots(d.openingHourSlots) == "8-12"), why + ": paste to all copies hours");
                Check(Slots(shop.scheduleDays[3].openingHourSlots) == "11-15", why + ": closed day untouched");
                Check(standIn.scheduleDays.Select(d => Slots(d.openingHourSlots)).SequenceEqual(standInBefore), why + ": stand-in days untouched");
                Check(Newtonsoft.Json.JsonConvert.SerializeObject(v.Schedule) == vehicleBefore, why + ": vehicle's schedule untouched");
            }
            VehicleSchedule.End();
            Check(Log.Errors.Count == 0, kind + ": no errors: " + string.Join("; ", Log.Errors));
        }
        Console.WriteLine("PASS: vehicle schedule paste");
    }
}
'''


if __name__ == '__main__':
    unittest.main()
