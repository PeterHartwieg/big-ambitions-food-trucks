using System;
using System.Collections.Generic;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class CrewScreenTests
{
    private const string Training = "myemployees_task_training";
    private const string Absent = "myemployees_task_absent";
    private const string InTraining = "common_in_training";

    // Seven 7-hour shifts: 49 hours a week, as in the player's report.
    private static (SaveState State, VehicleState Vehicle) FortyNineHours()
    {
        var schedule = new HireSchedule();
        foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            schedule.CrewShifts.Add(new CrewShift { EmployeeId = "crew", Day = day, Start = 10, End = 17 });
        var vehicle = new VehicleState { VehicleId = "truck-1", VehicleType = "truck", Crew = new List<string> { "crew" }, Schedule = schedule };
        var state = new SaveState { Vehicles = new List<VehicleState> { new VehicleState { VehicleId = "cart-1", VehicleType = "cart" }, vehicle } };
        return (state, vehicle);
    }

    [Fact]
    public void HealthyCrewWithScheduledHoursShowsVehicleAndCrewJob()
    {
        var (state, vehicle) = FortyNineHours();
        Assert.Equal(49, WeekPlan.WeeklyWork(vehicle.Schedule, "crew").Hours);
        Assert.Same(vehicle, CrewScreen.VehicleOf(state, "crew", inBuilding: false));
        Assert.Equal(CrewScreen.TaskKey, CrewScreen.Task(null));
        var statuses = new List<string> { CrewScreen.Unassigned };
        CrewScreen.MarkAssigned(statuses);
        Assert.Equal(new[] { CrewScreen.Assigned }, statuses);
    }

    [Theory]
    [InlineData(Training)]
    [InlineData(Absent)]
    public void TrainingAndAbsenceKeepTheGameTask(string gameTask)
    {
        Assert.Equal(gameTask, CrewScreen.Task(gameTask));
    }

    [Fact]
    public void FilterKeepsInTrainingAndNeverDuplicatesAssigned()
    {
        var training = new List<string> { CrewScreen.Unassigned, InTraining };
        CrewScreen.MarkAssigned(training);
        Assert.Equal(new[] { CrewScreen.Assigned, InTraining }, training);
        var both = new List<string> { CrewScreen.Assigned, CrewScreen.Unassigned };
        CrewScreen.MarkAssigned(both);
        Assert.Equal(new[] { CrewScreen.Assigned }, both);
        var assigned = new List<string> { CrewScreen.Assigned };
        CrewScreen.MarkAssigned(assigned);
        Assert.Equal(new[] { CrewScreen.Assigned }, assigned);
    }

    [Fact]
    public void OtherEmployeesAndBuildingJobsStayAsTheGameShowsThem()
    {
        var (state, _) = FortyNineHours();
        Assert.Null(CrewScreen.VehicleOf(state, "shop-worker", inBuilding: true));
        Assert.Null(CrewScreen.VehicleOf(state, "idle", inBuilding: false));
        // Put in a building: the game's job shows until HireCrew.Check takes them off the crew.
        Assert.Null(CrewScreen.VehicleOf(state, "crew", inBuilding: true));
        Assert.Null(CrewScreen.VehicleOf(null, "crew", inBuilding: false));
        Assert.Null(CrewScreen.VehicleOf(state, null, inBuilding: false));
        Assert.Null(CrewScreen.VehicleOf(state, "", inBuilding: false));
    }

    [Fact]
    public void RemovedCrewRevertsToTheGameRow()
    {
        var (state, vehicle) = FortyNineHours();
        vehicle.Crew.Remove("crew");
        WeekPlan.DropCrew(vehicle.Schedule, "crew");
        Assert.Null(CrewScreen.VehicleOf(state, "crew", inBuilding: false));
        Assert.Equal(0, WeekPlan.WeeklyWork(vehicle.Schedule, "crew").Hours);
    }

    [Fact]
    public void SaveAndReloadKeepsThePresentation()
    {
        var (state, _) = FortyNineHours();
        var codec = new SaveCodec();
        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var vehicle = CrewScreen.VehicleOf(loaded.State, "crew", inBuilding: false);
        Assert.NotNull(vehicle);
        Assert.Equal("truck-1", vehicle!.VehicleId);
        Assert.Equal(49, WeekPlan.WeeklyWork(vehicle.Schedule, "crew").Hours);
    }
}
