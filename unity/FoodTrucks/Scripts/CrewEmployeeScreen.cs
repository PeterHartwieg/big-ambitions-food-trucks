#nullable enable
using System;
using System.Collections.Generic;
using Entities;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Localizor;
using UI.Smartphone.Apps.MyEmployees;

namespace FoodTrucks
{
    // The game's employee list (MyEmployees) for a vehicle's crew: their row, the status filter and the detail panel name
    // the vehicle and the crew's job instead of "Unassigned" and "No task" (CrewScreen), and Manage schedule opens the
    // vehicle's page. Display only: the employee stays unassigned in the game (HireCrew), so nothing that decides work,
    // pay or schedules changes. [code, unverified in game]
    internal static class CrewEmployeeScreen
    {
        // EmployeeModel's constructor, which every row of the list, its filter and its search are made from.
        public static void ModelPostfix(EmployeeModel __instance, EmployeeInstance instance)
        {
            try
            {
                var vehicle = VehicleOf(instance);
                if (vehicle == null) return;
                __instance.currentBusinessName = VehicleName(vehicle);
                __instance.task = CrewScreen.Task(__instance.task);
                CrewScreen.MarkAssigned(__instance.statuses);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        // MyEmployees.ShowEmployee: the detail panel's business dropdown starts with "Unassigned", selected for anyone
        // without a building. For crew that first option names the vehicle; picking a building still moves them there, as
        // the game does, and off the crew (HireCrew.Check).
        public static void ShowEmployeePostfix(EmployeeInstance employeeInstance, UI.Elements.Dropdown ___assignedAddressDropdown,
            List<BuildingRegistration> ____businessRegistrations)
        {
            try
            {
                if (employeeInstance == null || employeeInstance.IsCandidate || ___assignedAddressDropdown == null) return;
                var vehicle = VehicleOf(employeeInstance);
                if (vehicle == null) return;
                // The same options as the game's after the first, so its pick handler finds the same building.
                var options = new List<string> { VehicleName(vehicle) };
                options.AddRange(BuildingNames(____businessRegistrations));
                ___assignedAddressDropdown.SetOptions(options, localize: false);
                ___assignedAddressDropdown.ResetSelectedOption(0);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

        // MyEmployees.ManageSchedule: the game refuses an employee without a building. A crew member's shifts are on their
        // vehicle's page, whose Schedule button opens the game's Schedule tab for the vehicle.
        public static bool ManageSchedulePrefix(EmployeeInstance employeeInstance)
        {
            try
            {
                var vehicle = VehicleOf(employeeInstance);
                if (vehicle == null) return true;
                BizManVehiclePage.OpenFor(vehicle.VehicleId);
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return true;
            }
        }

        // One label per building, in the game's order: its pick handler maps option i to building i - 1. A building whose name
        // fails is shown blank (logged once) rather than dropping the vehicle option or shifting the buildings after it.
        private static List<string> BuildingNames(List<BuildingRegistration>? registrations)
        {
            var names = new List<string>();
            var logged = false;
            foreach (var registration in registrations ?? new List<BuildingRegistration>())
            {
                try { names.Add(registration?.GetDisplayName() ?? ""); }
                catch (Exception ex)
                {
                    names.Add("");
                    if (!logged) Log.Error(ex);
                    logged = true;
                }
            }
            return names;
        }

        private static VehicleState? VehicleOf(EmployeeInstance? employee) =>
            employee == null ? null : CrewScreen.VehicleOf(FoodTrucksSession.CurrentState, employee.id, employee.IsAssignedToAnyBusiness());

        private static string VehicleName(VehicleState vehicle) =>
            StreetFoodBooks.VehicleNames().TryGetValue(vehicle.VehicleId, out var name) && !string.IsNullOrEmpty(name) ? name
                : LocalizorManager.GetLocalization("foodtrucks:noun_" + (string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType), null);
    }
}
