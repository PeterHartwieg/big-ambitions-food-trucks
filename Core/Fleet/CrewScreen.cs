using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Fleet
{
    // How the game's employee list (MyEmployees) shows a vehicle's crew member. The game reads an employee's business and
    // task from their building; a crew member has none (they stay unassigned in the game), so the row said "Unassigned"
    // and "No task" beside the hours of their shifts (a player's report, 28 October 2026). Display only: what the game
    // decides with (assigned to a business, work shifts, wages) is left as it is.
    public static class CrewScreen
    {
        // The crew's job in the task column.
        public const string TaskKey = "foodtrucks:myemployees_task_crew";
        // The game's status filter values (EmployeeStatusFilter).
        public const string Assigned = "common_assigned";
        public const string Unassigned = "common_unassigned";

        // The vehicle whose crew the employee is on; null leaves the game's row as it is: not crew, or put in a building
        // (the game shows that job, and HireCrew.Check takes them off the crew).
        public static VehicleState? VehicleOf(SaveState? state, string? employeeId, bool inBuilding)
        {
            if (state == null || string.IsNullOrEmpty(employeeId) || inBuilding) return null;
            return state.Vehicles.FirstOrDefault(v => v?.Crew != null && v.Crew.Contains(employeeId!));
        }

        // The game's own task first (training, off sick); the crew's job otherwise.
        public static string Task(string? gameTask) => gameTask ?? TaskKey;

        // The status filter: a crew member is assigned (to their vehicle). In training stays.
        public static void MarkAssigned(IList<string> statuses)
        {
            var at = statuses.IndexOf(Unassigned);
            if (at < 0) return;
            if (statuses.Contains(Assigned)) statuses.RemoveAt(at);
            else statuses[at] = Assigned;
        }
    }
}
