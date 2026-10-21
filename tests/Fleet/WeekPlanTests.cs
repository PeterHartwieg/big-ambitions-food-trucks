using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

// The vehicle week plan (Peter, 16 October 2026): stops per weekday, crew shifts per weekday, the route day, the parts
// nobody covers, the auto schedule, and the day tile's copy and paste.
public class WeekPlanTests
{
    private static readonly FleetRules rules = ShippedData.Rules.Fleet;
    private static readonly HireSchedules schedules = new(rules);
    private static readonly (double X, double Z) Bay = (-2000, 0);

    private static ScheduleStop Stop(DayOfWeek day, string key, double x, int open, int close) =>
        new() { Day = day, SpotKey = key, X = x, Z = 0, OpenAt = open, CloseAt = close };

    private static VehicleState Truck(params ScheduleStop[] stops) => new()
    {
        VehicleId = "t1", VehicleType = "truck", HasPermit = true, Crew = new List<string> { "e1", "e2" },
        Schedule = new HireSchedule { Stops = WeekPlan.Ordered(stops) }
    };

    private static IReadOnlyList<RouteDay> Days(VehicleState truck) =>
        WeekPlan.RouteDays(schedules, truck.Schedule, TravelMode.Drive, home: Bay);

    private static (int, int) Shift(IEnumerable<CrewShift> shifts, string who, DayOfWeek day)
    {
        var s = shifts.Single(x => x.EmployeeId == who && x.Day == day);
        return (s.Start, s.End);
    }

    [Fact]
    public void AStopIsNumberedOnItsOwnDay()
    {
        // Playtest, 21 October 2026: a cart on its one Sunday stop read "On the way to stop 6", the stop's index in the week.
        var truck = Truck(Stop(DayOfWeek.Monday, "a", 0, 600, 700), Stop(DayOfWeek.Monday, "b", 100, 720, 800),
            Stop(DayOfWeek.Wednesday, "c", 200, 600, 700), Stop(DayOfWeek.Sunday, "d", 300, 660, 900));

        Assert.Equal(new int?[] { 1, 2, 1, 1 }, Enumerable.Range(0, 4).Select(i => WeekPlan.NumberOnDay(truck.Schedule, i)));
        Assert.Null(WeekPlan.NumberOnDay(truck.Schedule, 4));
        Assert.Null(WeekPlan.NumberOnDay(truck.Schedule, -1));
        Assert.Null(WeekPlan.NumberOnDay(null, 0));
    }

    [Fact]
    public void ARouteSavedBeforeTheWeekPlanBecomesTheSameStopsOnItsDays()
    {
        var json = "{\"schemaVersion\":2,\"licence\":{},\"vehicles\":[{\"vehicleId\":\"t1\",\"crew\":[\"e1\"],\"hireStop\":1," +
                   "\"schedule\":{\"days\":[3,1],\"stops\":[{\"spotKey\":\"b\",\"openAt\":1080,\"closeAt\":1200},{\"spotKey\":\"a\",\"openAt\":660,\"closeAt\":840}]}," +
                   "\"shifts\":[{\"day\":3,\"legs\":[{\"kind\":1,\"stopIndex\":1,\"start\":1,\"end\":2}]}]}],\"dailySales\":[]}";

        var loaded = new SaveCodec().Load(json);

        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var truck = loaded.State!.Vehicles.Single();
        Assert.Equal(new[] { "Monday a", "Monday b", "Wednesday a", "Wednesday b" },
            truck.Schedule!.Stops.Select(s => $"{s.Day} {s.SpotKey}").ToArray());
        Assert.False(truck.Schedule.ExtensionData.ContainsKey("days"));
        Assert.Empty(truck.Shifts);
        // Saved again, it has no old fields and loads the same.
        var again = new SaveCodec().Load(new SaveCodec().Serialize(loaded.State)).State!.Vehicles.Single();
        Assert.Equal(4, again.Schedule!.Stops.Count);
        // An old "every day" route.
        var every = new SaveCodec().Load("{\"schemaVersion\":2,\"vehicles\":[{\"vehicleId\":\"t1\",\"schedule\":{\"days\":[],\"stops\":[{\"spotKey\":\"a\",\"openAt\":660,\"closeAt\":840}]}}]}");
        Assert.Equal(7, every.State!.Vehicles.Single().Schedule!.Stops.Count);
    }

    [Fact]
    public void ShiftsThatBreakTheRulesAreDroppedButTheSaveLoads()
    {
        var json = "{\"schemaVersion\":2,\"vehicles\":[{\"vehicleId\":\"t1\",\"schedule\":{\"stops\":[],\"closed\":[],\"crewShifts\":[" +
                   "{\"employeeId\":\"e1\",\"day\":1,\"start\":20,\"end\":26}]}}]}";
        var bad = new SaveCodec().Load(json);
        Assert.Equal(LoadStatus.Loaded, bad.Status);
        Assert.Empty(bad.State!.Vehicles.Single().Schedule!.CrewShifts);
        Assert.Single(bad.Notes);
        var twice = "{\"schemaVersion\":2,\"vehicles\":[{\"vehicleId\":\"t1\",\"schedule\":{\"stops\":[],\"closed\":[],\"crewShifts\":[" +
                    "{\"employeeId\":\"e1\",\"day\":1,\"start\":8,\"end\":12},{\"employeeId\":\"e1\",\"day\":1,\"start\":14,\"end\":16}]}}]}";
        // Two shifts a day for one crew member are fine (the game's schedule allows them), at the same hour they aren't.
        Assert.Equal(LoadStatus.Loaded, new SaveCodec().Load(twice).Status);
        // Shifts from before rows that overlap the same crew member's: one is dropped with a note, the save still loads.
        var overlap = new SaveCodec().Load(twice.Replace("\"start\":14", "\"start\":11"));
        Assert.Equal(LoadStatus.Loaded, overlap.Status);
        Assert.Single(overlap.State!.Vehicles.Single().Schedule!.CrewShifts);
        Assert.Single(overlap.Notes);
        // With rows named, the later colliding shift is dropped the same way.
        var named = new SaveCodec().Load(twice.Replace("\"start\":14", "\"start\":11").Replace("\"end\":12}", "\"end\":12,\"station\":\"serving1\"}")
            .Replace("\"end\":16}", "\"end\":16,\"station\":\"serving2\"}"));
        Assert.Equal(LoadStatus.Loaded, named.Status);
        Assert.Single(named.Notes);
        // Nor two shifts on one row at once: the later one goes, with a note.
        var row = "{\"schemaVersion\":2,\"vehicles\":[{\"vehicleId\":\"t1\",\"schedule\":{\"stops\":[],\"closed\":[],\"crewShifts\":[" +
                  "{\"employeeId\":\"e1\",\"day\":1,\"start\":8,\"end\":12,\"station\":\"serving2\"},{\"employeeId\":\"e2\",\"day\":1,\"start\":10,\"end\":16,\"station\":\"serving2\"}]}}]}";
        var clash = new SaveCodec().Load(row);
        Assert.Equal(LoadStatus.Loaded, clash.Status);
        Assert.Equal("e1", Assert.Single(clash.State!.Vehicles.Single().Schedule!.CrewShifts).EmployeeId);
        Assert.Contains("collides", Assert.Single(clash.Notes));
        Assert.Equal(LoadStatus.Loaded, new SaveCodec().Load(row.Replace("\"station\":\"serving2\"}]", "\"station\":\"serving1\"}]")).Status);
    }

    [Fact]
    public void ShiftsFromBeforeRowsGetFreeRowsAndTheSaveLoads()
    {
        // A truck's two crew both 09:00-16:00 and a third 10:00-12:00, saved by the round 2 build (no rows).
        var json = "{\"schemaVersion\":2,\"vehicles\":[{\"vehicleId\":\"t1\",\"crew\":[\"e1\",\"e2\",\"e3\",\"e4\"],\"schedule\":{\"stops\":[],\"closed\":[],\"crewShifts\":[" +
                   "{\"employeeId\":\"e1\",\"day\":1,\"start\":9,\"end\":16},{\"employeeId\":\"e2\",\"day\":1,\"start\":9,\"end\":16}," +
                   "{\"employeeId\":\"e3\",\"day\":1,\"start\":10,\"end\":12},{\"employeeId\":\"e4\",\"day\":1,\"start\":11,\"end\":13}]}}]}";

        var loaded = new SaveCodec().Load(json);

        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var shifts = loaded.State!.Vehicles.Single().Schedule!.CrewShifts;
        // Two serving rows: the third and fourth fit no row and go, with notes.
        Assert.Equal(new[] { "e1 serving1", "e2 serving2" }, shifts.OrderBy(s => s.EmployeeId).Select(s => $"{s.EmployeeId} {s.Station}"));
        Assert.Equal(2, loaded.Notes.Count);
        // A shift on the old driving row moves to a free serving row.
        var driving = new SaveCodec().Load(json.Replace("\"start\":10,\"end\":12}", "\"start\":17,\"end\":19,\"station\":\"driving\"}"));
        Assert.Contains(driving.State!.Vehicles.Single().Schedule!.CrewShifts, s => s.EmployeeId == "e3" && s.Station == "serving1");
    }

    [Fact]
    public void GapsAreThePartsOfTheRouteDayWithNobodyOnShift()
    {
        // Wednesday: restock 9:10, drive 9:40, open 11:00-14:00, back in the bay 15:20.
        var truck = Truck(Stop(DayOfWeek.Wednesday, "office", 0, 660, 840));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Wednesday, 9, 13);
        var day = Days(truck)[WeekPlan.Position(DayOfWeek.Wednesday)];

        var gap = Assert.Single(WeekPlan.Gaps(day, truck.Schedule, truck.Crew));

        Assert.Equal((13 * 60, 15 * 60 + 20), (gap.Start, gap.End));
        Assert.Equal((LegKind.Open, 0), (gap.Kind, gap.StopIndex));
        Assert.Equal(ScheduleProblem.Uncovered, new HireRunner(rules).RouteProblem(truck, home: Bay));
        WeekPlan.SetShift(rules, truck, "e2", DayOfWeek.Wednesday, 12, 16);
        Assert.Empty(WeekPlan.Gaps(day, truck.Schedule, truck.Crew));
        Assert.Equal(ScheduleProblem.None, new HireRunner(rules).RouteProblem(truck, home: Bay));
        // Shifts of someone no longer on the crew don't count.
        Assert.NotEmpty(WeekPlan.Gaps(day, truck.Schedule, new[] { "e1" }));
    }

    [Fact]
    public void ALateRouteNeedsTheNextDaysShiftAfterMidnight()
    {
        // Thursday 18:00 to 01:00 at a stop next to the bay (1 km: 40 minutes each way).
        var truck = Truck(Stop(DayOfWeek.Thursday, "bars", -1000, 18 * 60, 25 * 60));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Thursday, 16, 24);
        var day = Days(truck)[WeekPlan.Position(DayOfWeek.Thursday)];
        Assert.Equal((16 * 60 + 50, 25 * 60 + 40), (day.Start, day.End));

        Assert.Equal((24 * 60, 25 * 60 + 40), (WeekPlan.Gaps(day, truck.Schedule, truck.Crew).Single().Start, WeekPlan.Gaps(day, truck.Schedule, truck.Crew).Single().End));
        WeekPlan.SetShift(rules, truck, "e2", DayOfWeek.Friday, 0, 2);
        Assert.Empty(WeekPlan.Gaps(day, truck.Schedule, truck.Crew));
    }

    [Fact]
    public void AutoScheduleCoversTheRouteDayThenAddsASecondAtTheBusiestStop()
    {
        var truck = Truck(Stop(DayOfWeek.Wednesday, "office", 0, 660, 840));
        var crew = new[] { new CrewWish("e1"), new CrewWish("e2") };

        var shifts = WeekPlan.AutoSchedule(rules, Days(truck), crew, staff: 2);

        // 9:10 to 15:20 needs 09:00 to 16:00; the second person for the stop's 11:00 to 14:00.
        Assert.Equal((9, 16), Shift(shifts, "e1", DayOfWeek.Wednesday));
        Assert.Equal((11, 14), Shift(shifts, "e2", DayOfWeek.Wednesday));
        Assert.Equal(2, shifts.Count);
        // A cart (staff 1) gets no second person.
        Assert.Single(WeekPlan.AutoSchedule(rules, Days(truck), crew, staff: 1));
        // The result covers the day.
        truck.Schedule!.CrewShifts = shifts;
        Assert.False(WeekPlan.AnyGap(Days(truck), truck.Schedule, truck.Crew));
    }

    [Fact]
    public void ADayLongerThanAShiftIsSplitWithAnHourTogether()
    {
        // Restock from 05:50, back in the bay at 23:40: 05:00 to 24:00, 19 hours.
        var truck = Truck(Stop(DayOfWeek.Monday, "early", -1000, 7 * 60, 14 * 60), Stop(DayOfWeek.Monday, "late", -1000, 15 * 60, 23 * 60));
        var crew = new[] { new CrewWish("e1"), new CrewWish("e2") };

        var shifts = WeekPlan.AutoSchedule(rules, Days(truck), crew, staff: 2);

        Assert.Equal((5, 15), Shift(shifts, "e1", DayOfWeek.Monday));
        Assert.Equal((14, 24), Shift(shifts, "e2", DayOfWeek.Monday));
        Assert.All(shifts, s => Assert.True(s.End - s.Start <= rules.MaxShiftHours));
    }

    [Fact]
    public void AutoScheduleFavoursWhoeverIsFurthestBelowTheirHoursAndKeepsTheSecondWithinThem()
    {
        var truck = Truck(Stop(DayOfWeek.Monday, "a", 0, 660, 840), Stop(DayOfWeek.Tuesday, "a", 0, 660, 840));
        // e2 wants at least 10 hours, e1 at most 8.
        var crew = new[] { new CrewWish("e1", 0, 8), new CrewWish("e2", 10, 40) };

        var shifts = WeekPlan.AutoSchedule(rules, Days(truck), crew, staff: 2);

        Assert.Equal((9, 16), Shift(shifts, "e2", DayOfWeek.Monday));
        // Tuesday: e2 (7 of 10) is still further below than e1 (0 of 0).
        Assert.Equal((9, 16), Shift(shifts, "e2", DayOfWeek.Tuesday));
        // e1 joins at the stop on Monday (3 hours), and again Tuesday (6 of at most 8).
        Assert.Equal(6, shifts.Where(s => s.EmployeeId == "e1").Sum(s => s.End - s.Start));
        var tight = new[] { new CrewWish("e1", 0, 4), new CrewWish("e2", 10, 40) };
        Assert.Equal(3, WeekPlan.AutoSchedule(rules, Days(truck), tight, staff: 2).Where(s => s.EmployeeId == "e1").Sum(s => s.End - s.Start));
    }

    [Fact]
    public void AutoScheduleForOneDayKeepsTheOthers()
    {
        var truck = Truck(Stop(DayOfWeek.Monday, "a", 0, 660, 840), Stop(DayOfWeek.Tuesday, "a", 0, 660, 840));
        var kept = new[] { new CrewShift { EmployeeId = "e1", Day = DayOfWeek.Monday, Start = 8, End = 12 } };

        var shifts = WeekPlan.AutoSchedule(rules, Days(truck), new[] { new CrewWish("e1") }, 1, kept, new[] { DayOfWeek.Tuesday });

        Assert.Equal((8, 12), Shift(shifts, "e1", DayOfWeek.Monday));
        Assert.Equal((9, 16), Shift(shifts, "e1", DayOfWeek.Tuesday));
    }

    [Fact]
    public void ALateRouteIsAutoScheduledToMidnightAndOnFromMidnight()
    {
        var truck = Truck(Stop(DayOfWeek.Thursday, "bars", -1000, 18 * 60, 25 * 60));

        var shifts = WeekPlan.AutoSchedule(rules, Days(truck), new[] { new CrewWish("e1"), new CrewWish("e2") }, staff: 1);

        Assert.Equal((16, 24), Shift(shifts, "e1", DayOfWeek.Thursday));
        Assert.Equal((0, 2), Shift(shifts, "e2", DayOfWeek.Friday));
    }

    [Fact]
    public void TheOpeningHoursAreTheRouteDaysWholeHours()
    {
        // Thursday 16:50 to Friday 01:40 (a late stop, 1 km from the bay).
        var truck = Truck(Stop(DayOfWeek.Thursday, "bars", -1000, 18 * 60, 25 * 60));
        var hours = WeekPlan.RouteHours(Days(truck));

        Assert.Equal(new[] { (16, 24) }, WeekPlan.OpeningSlots(hours, DayOfWeek.Thursday));
        Assert.Equal(new[] { (0, 2) }, WeekPlan.OpeningSlots(hours, DayOfWeek.Friday));
        Assert.Empty(WeekPlan.OpeningSlots(hours, DayOfWeek.Monday));
    }

    [Fact]
    public void ChunksAreAtMostTheCapWithAnHourOfOverlap()
    {
        Assert.Equal(new[] { (0, 9), (8, 17), (16, 24) }, WeekPlan.Chunks(0, 24, 12));
        Assert.Equal(new[] { (6, 18) }, WeekPlan.Chunks(6, 18, 12));
        Assert.Equal(new[] { (6, 7), (7, 8) }, WeekPlan.Chunks(6, 8, 1));
    }

    [Fact]
    public void ADaysRouteIsCopiedAndPastedWithoutItsShiftsOrClosedMark()
    {
        var truck = Truck(Stop(DayOfWeek.Monday, "a", 0, 660, 840), Stop(DayOfWeek.Tuesday, "b", 0, 700, 900));
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 9, 16);
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Tuesday, 10, 12);
        truck.Schedule!.Closed.Add(DayOfWeek.Wednesday);
        var copy = RouteEditor.CopyDay(truck.Schedule, DayOfWeek.Monday);

        Assert.Equal(ScheduleProblem.None, RouteEditor.PasteRoute(schedules, truck, copy, DayOfWeek.Tuesday));
        Assert.Equal(new[] { "Monday a", "Tuesday a" }, truck.Schedule!.Stops.Select(s => $"{s.Day} {s.SpotKey}").ToArray());
        Assert.Equal((10, 12), Shift(truck.Schedule.CrewShifts, "e1", DayOfWeek.Tuesday));

        Assert.Equal(ScheduleProblem.None, RouteEditor.PasteRouteToAllDays(schedules, truck, copy));
        Assert.Equal(7, truck.Schedule.Stops.Count);
        Assert.Equal(2, truck.Schedule.CrewShifts.Count);
        Assert.Equal(new[] { DayOfWeek.Wednesday }, truck.Schedule.Closed);

        RouteEditor.ClearDay(truck, DayOfWeek.Sunday);
        Assert.Equal(6, truck.Schedule.Stops.Count);
        Assert.DoesNotContain(truck.Schedule.CrewShifts, s => s.Day == DayOfWeek.Sunday);
    }

    [Fact]
    public void APasteThatWouldRunANightIntoTheNextMorningIsRefused()
    {
        var truck = Truck(Stop(DayOfWeek.Monday, "night", 0, 22 * 60, 26 * 60), Stop(DayOfWeek.Wednesday, "early", 0, 60, 180));
        var before = truck.Schedule!.Stops.ToList();

        Assert.Equal(ScheduleProblem.Overlap, RouteEditor.PasteRoute(schedules, truck, RouteEditor.CopyDay(truck.Schedule, DayOfWeek.Monday), DayOfWeek.Tuesday));

        Assert.Equal(before, truck.Schedule.Stops);
    }

    [Fact]
    public void TheOpenWindowFollowsItsStopWhenAnotherDayIsPasted()
    {
        var truck = Truck(Stop(DayOfWeek.Monday, "a", 0, 660, 840), Stop(DayOfWeek.Friday, "f", 0, 660, 840));
        truck.HireStop = 1;
        // Pasting Monday onto Tuesday puts a stop before Friday's.
        RouteEditor.PasteRoute(schedules, truck, RouteEditor.CopyDay(truck.Schedule, DayOfWeek.Monday), DayOfWeek.Tuesday);

        Assert.Equal("f", truck.Schedule!.Stops[truck.HireStop!.Value].SpotKey);
    }

    [Fact]
    public void StopsKeepMondayFirstOrderWhenAdded()
    {
        var truck = Truck(Stop(DayOfWeek.Sunday, "sun", 0, 660, 840));
        RouteEditor.AddStop(schedules, truck, Stop(DayOfWeek.Monday, "mon", 0, 900, 960));
        RouteEditor.AddStop(schedules, truck, Stop(DayOfWeek.Monday, "mon early", 0, 600, 660));

        Assert.Equal(new[] { "mon early", "mon", "sun" }, truck.Schedule!.Stops.Select(s => s.SpotKey).ToArray());
        Assert.Equal(new[] { 0, 1 }, WeekPlan.StopsOf(truck.Schedule, DayOfWeek.Monday));
    }

    [Fact]
    public void TheCrewRestocksInTheMorningRestockAndThenLeavesWithoutASecondOne()
    {
        // Day 3 (Wednesday): restock 9:10 to 9:40 in the bay, then the drive.
        var truck = WeekTestData.AllDay(Truck(Stop(DayOfWeek.Wednesday, "office", 0, 660, 840)));
        var runner = new HireRunner(rules);
        runner.EnsurePlan(truck, HireSchedules.Absolute(3, 360), Bay.X, Bay.Z, home: Bay);
        var inBay = new VehicleWhere(Bay.X, Bay.Z, leaveAlone: false, canRestock: true, home: Bay);

        Assert.Equal(HireActionKind.None, runner.Step(truck, HireSchedules.Absolute(3, 9 * 60), inBay).Kind);
        Assert.Equal(HireActionKind.Restock, runner.Step(truck, HireSchedules.Absolute(3, 9 * 60 + 10), inBay).Kind);
        HireRunner.Restocked(truck, HireSchedules.Absolute(3, 9 * 60 + 10));
        Assert.Equal(HireActionKind.None, runner.Step(truck, HireSchedules.Absolute(3, 9 * 60 + 20), inBay).Kind);
        Assert.Equal(HireActionKind.Depart, runner.Step(truck, HireSchedules.Absolute(3, 9 * 60 + 40), inBay).Kind);
    }

    [Fact]
    public void AutoScheduleKeepsEachRowFreeOfOverlaps()
    {
        // A long day split with an hour together, on a cart (one serving row): the second shift starts as the first ends.
        var truck = Truck(Stop(DayOfWeek.Monday, "early", -1000, 7 * 60, 14 * 60), Stop(DayOfWeek.Monday, "late", -1000, 15 * 60, 23 * 60));

        var shifts = WeekPlan.AutoSchedule(rules, Days(truck), new[] { new CrewWish("e1"), new CrewWish("e2") }, staff: 1);

        Assert.True(WeekPlan.CheckShifts(shifts));
        Assert.Equal(new[] { "serving1", "serving1" }, shifts.OrderBy(s => s.Start).Select(s => s.Station));
        Assert.Equal(shifts.Min(s => s.End), shifts.Max(s => s.Start));
    }

    [Fact]
    public void WeekCountCountsWhoIsOnShiftEachHour()
    {
        var truck = Truck();
        WeekPlan.SetShift(rules, truck, "e1", DayOfWeek.Monday, 9, 12);
        WeekPlan.SetShift(rules, truck, "e2", DayOfWeek.Monday, 11, 13);

        var count = WeekPlan.WeekCount(truck.Schedule, truck.Crew);

        Assert.Equal(new[] { 0, 1, 1, 2, 1, 0 }, count.Skip(8).Take(6).ToArray());
        Assert.True(new CrewWish("e1", 3, 5).Fulfilled(3));
        Assert.False(new CrewWish("e1", 4, 5).Fulfilled(3));
    }
}
