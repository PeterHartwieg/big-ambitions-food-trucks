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

        public static void ShowEmployeePostfix(EmployeeInstance employeeInstance, UI.Elements.Dropdown ___assignedAddressDropdown,
            List<BuildingRegistration> ____businessRegistrations)
        {
            try
            {
                if (employeeInstance == null || employeeInstance.IsCandidate || ___assignedAddressDropdown == null) return;
                var vehicle = VehicleOf(employeeInstance);
                if (vehicle == null) return;
                var options = new List<string> { VehicleName(vehicle) };
                options.AddRange(BuildingNames(____businessRegistrations));
                ___assignedAddressDropdown.SetOptions(options, localize: false);
                ___assignedAddressDropdown.ResetSelectedOption(0);
            }
            catch (Exception ex) { Log.Error(ex); }
        }

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
