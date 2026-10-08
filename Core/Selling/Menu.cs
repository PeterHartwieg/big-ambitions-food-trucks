using System;
using System.Collections.Generic;
using FoodTrucks.Core.Demand;

namespace FoodTrucks.Core.Selling
{
    public enum MenuProblem
    {
        None,
        UnknownVehicle,
        UnknownEquipment,
        TooMuchEquipment
    }

    public sealed class MenuBuild
    {
        internal MenuBuild(Menu menu, MenuProblem problem, string? detail)
        {
            Menu = menu;
            Problem = problem;
            Detail = detail;
        }

        // Empty when there is a problem.
        public Menu Menu { get; }
        public MenuProblem Problem { get; }
        // The vehicle type or equipment ID behind the problem.
        public string? Detail { get; }
    }

    // The items a vehicle sells. From Milestone 2 the menu follows the fitted equipment.
    public sealed class Menu
    {
        public static readonly Menu Empty = new Menu(Array.Empty<string>());

        private readonly List<string> items;

        public Menu(IEnumerable<string> items)
        {
            if (items == null) throw new ArgumentNullException(nameof(items));
            this.items = new List<string>();
            foreach (var item in items)
            {
                if (string.IsNullOrEmpty(item)) throw new ArgumentException("Menu items must not be empty.", nameof(items));
                if (!this.items.Contains(item)) this.items.Add(item);
            }
        }

        public IReadOnlyList<string> Items => items;

        public bool IsOnMenu(string? itemName)
        {
            if (string.IsNullOrEmpty(itemName)) return false;
            foreach (var item in items)
            {
                if (string.Equals(item, itemName, StringComparison.Ordinal)) return true;
            }
            return false;
        }

        // The item the next customer buys: the menu item with the most units in stock,
        // ties broken by menu order. Null when nothing on the menu is in stock.
        public string? PickNext(IReadOnlyDictionary<string, int> unitsByItem)
        {
            if (unitsByItem == null) throw new ArgumentNullException(nameof(unitsByItem));
            string? best = null;
            var bestUnits = 0;
            foreach (var item in items)
            {
                if (unitsByItem.TryGetValue(item, out var units) && units > bestUnits)
                {
                    best = item;
                    bestUnits = units;
                }
            }
            return best;
        }

        public static MenuBuild FromCargo(DemandTables tables, string? vehicleType, IEnumerable<CargoLine> cargo) =>
            FromEquipment(tables, vehicleType, CargoRules.FittedEquipment(tables, cargo));

        // The products the fitted equipment unlocks, in fitting order. Equipment is inventory: each
        // piece takes one of the vehicle's slots, and a second piece of the same kind adds no items.
        public static MenuBuild FromEquipment(DemandTables tables, string? vehicleType, IEnumerable<string?> fittedEquipment)
        {
            if (tables == null) throw new ArgumentNullException(nameof(tables));
            if (fittedEquipment == null) throw new ArgumentNullException(nameof(fittedEquipment));
            var vehicle = tables.Vehicle(vehicleType);
            if (vehicle == null) return new MenuBuild(Empty, MenuProblem.UnknownVehicle, vehicleType);

            var products = new List<string>();
            var pieces = 0;
            foreach (var id in fittedEquipment)
            {
                var equipment = tables.Equipment(id);
                if (equipment == null) return new MenuBuild(Empty, MenuProblem.UnknownEquipment, id);
                if (++pieces > vehicle.EquipmentSlots) return new MenuBuild(Empty, MenuProblem.TooMuchEquipment, id);
                products.AddRange(equipment.Products);
            }
            return new MenuBuild(new Menu(products), MenuProblem.None, null);
        }
    }
}
