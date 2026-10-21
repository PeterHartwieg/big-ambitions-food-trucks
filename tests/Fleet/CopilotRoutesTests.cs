using System;
using System.Collections.Generic;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Tests.Fleet
{
    public class CopilotRoutesTests
    {
        private static ScheduleStop Stop(DayOfWeek day, double x, int open, int close) =>
            new ScheduleStop { Day = day, X = x, Z = 1, SpotKey = "spot", OpenAt = open, CloseAt = close };

        [Fact]
        public void ReviewDoesNotChangeStateAndApplyKeepsOtherDaysAndCrew()
        {
            var rules = new FleetRules();
            var vehicle = new VehicleState { VehicleId = "cart", VehicleType = "cart", Crew = new List<string> { "crew" },
                Schedule = new HireSchedule { Stops = new List<ScheduleStop> { Stop(DayOfWeek.Tuesday, 2, 600, 800) },
                    Closed = new List<DayOfWeek> { DayOfWeek.Tuesday },
                    CrewShifts = new List<CrewShift> { new CrewShift { Day = DayOfWeek.Monday, EmployeeId = "crew", Start = 9, End = 16 } } } };
            var revision = CopilotRoutes.Revision(vehicle);
            Assert.Equal(ScheduleProblem.None, CopilotRoutes.Preview(vehicle, DayOfWeek.Monday,
                new[] { Stop(DayOfWeek.Monday, 1, 600, 900) }, rules, 1, out var candidate));
            Assert.Equal(revision, CopilotRoutes.Revision(vehicle));
            Assert.Equal(ScheduleProblem.None, CopilotRoutes.Apply(vehicle, DayOfWeek.Monday, candidate, rules));
            Assert.Equal(2, vehicle.Schedule!.Stops.Count);
            Assert.Single(vehicle.Schedule.CrewShifts);
            Assert.Contains(DayOfWeek.Tuesday, vehicle.Schedule.Closed);
            Assert.NotEqual(revision, CopilotRoutes.Revision(vehicle));
        }

        [Fact]
        public void TravelGapAndManualReverseOrderAreRefusedWithoutFitting()
        {
            var vehicle = new VehicleState { VehicleId = "truck", VehicleType = "truck" };
            var stops = new[] { Stop(DayOfWeek.Monday, 0, 600, 700), Stop(DayOfWeek.Monday, 1000, 710, 800) };
            Assert.Equal(ScheduleProblem.Unreachable, CopilotRoutes.Preview(vehicle, DayOfWeek.Monday, stops, new FleetRules(), 1, out _));
            Assert.Equal(710, stops[1].OpenAt);
            Array.Reverse(stops);
            Assert.Equal(ScheduleProblem.Overlap, CopilotRoutes.Preview(vehicle, DayOfWeek.Monday, stops, new FleetRules(), 1, out _));
            Assert.Null(vehicle.Schedule);
        }

        [Fact]
        public void LateMondayCannotOverlapTuesdayAndClearingKeepsStaffHours()
        {
            var vehicle = new VehicleState { VehicleId = "v", Schedule = new HireSchedule {
                Stops = new List<ScheduleStop> { Stop(DayOfWeek.Tuesday, 0, 60, 120) },
                CrewShifts = new List<CrewShift> { new CrewShift { Day = DayOfWeek.Monday, EmployeeId = "c", Start = 9, End = 16 } } } };
            Assert.Equal(ScheduleProblem.Overlap, CopilotRoutes.Preview(vehicle, DayOfWeek.Monday,
                new[] { Stop(DayOfWeek.Monday, 0, 1300, 1600) }, new FleetRules(), 1, out _));
            Assert.Equal(ScheduleProblem.None, CopilotRoutes.Preview(vehicle, DayOfWeek.Tuesday,
                Array.Empty<ScheduleStop>(), new FleetRules(), 1, out var blank));
            CopilotRoutes.Apply(vehicle, DayOfWeek.Tuesday, blank, new FleetRules());
            Assert.Empty(vehicle.Schedule!.Stops);
            Assert.Single(vehicle.Schedule.CrewShifts);
        }
    }
}
