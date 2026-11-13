using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;

namespace FoodTrucks.Core.Fleet
{
    public static class CrewScreen
    {
        public const string TaskKey = "foodtrucks:myemployees_task_crew";
        public const string Assigned = "common_assigned";
        public const string Unassigned = "common_unassigned";

        public static VehicleState? VehicleOf(SaveState? state, string? employeeId, bool inBuilding)
        {
            if (state == null || string.IsNullOrEmpty(employeeId) || inBuilding) return null;
            return state.Vehicles.FirstOrDefault(v => v?.Crew != null && v.Crew.Contains(employeeId!));
        }

        public static string Task(string? gameTask) => gameTask ?? TaskKey;

        public static void MarkAssigned(IList<string> statuses)
        {
            var at = statuses.IndexOf(Unassigned);
            if (at < 0) return;
            if (statuses.Contains(Assigned)) statuses.RemoveAt(at);
            else statuses[at] = Assigned;
        }
    }
}
