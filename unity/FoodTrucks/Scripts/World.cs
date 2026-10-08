#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.DayNightCycle;
using BigAmbitions.Items;
using FoodTrucks.Core.Demand;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
    // What the cart's spot looks like to the demand model: nearby buildings and businesses, the
    // neighbourhood, and the zones on top. Computed when a cart opens and at the start of each day,
    // never every minute (implementation plan, Game adapters).
    internal sealed class Spot
    {
        public Spot(AreaMix areas, string? neighbourhood, int buildings, int day)
        {
            Areas = areas;
            Neighbourhood = neighbourhood;
            Buildings = buildings;
            Day = day;
        }

        public AreaMix Areas { get; }
        public string? Neighbourhood { get; }
        public int Buildings { get; }
        // The game day it was read on; a new day reads it again.
        public int Day { get; }
    }

    internal static class World
    {
        // The building type the classifier knows subway stations by (demand.json, classifier.buildingTypes).
        public const string SubwayEntrance = "subwayentrance";

        // Every city building's entrance doors, cached per city load. All city scenes load together, so
        // the controllers exist even far from the player. Not cached while the city isn't there yet.
        private static List<(CityBuildingController Controller, Vector3[] Doors)>? buildings;

        public static void Reset() => buildings = null;

        private static List<(CityBuildingController Controller, Vector3[] Doors)> Buildings()
        {
            if (buildings != null) return buildings;
            var list = new List<(CityBuildingController, Vector3[])>();
            var controllers = InstanceBehavior<CityManager>.Instance?.cityBuildingControllers;
            if (controllers == null || controllers.Length == 0) return list;
            foreach (var controller in controllers)
            {
                if (controller == null || controller.building == null) continue;
                var doors = (controller.entranceDoors ?? Array.Empty<Buildings.Outdoors.BuildingEntranceDoor>())
                    .Where(d => d != null && d.doorTransform != null)
                    .Select(d => d.doorTransform.position).ToArray();
                if (doors.Length == 0) doors = new[] { controller.transform.position };
                list.Add((controller, doors));
            }
            buildings = list;
            Log.Info($"World: {list.Count} buildings, {list.Sum(b => b.Item2.Length)} entrances cached.");
            return buildings;
        }

        // Every entrance of every building, for the legality rule.
        public static IEnumerable<Vector3> DoorPositions() => Buildings().SelectMany(b => b.Doors);

        public static Spot ReadSpot(DemandTables tables, Vector3 position)
        {
            var radius = tables.Classifier.RadiusMetres;
            // Business types by address, read directly: BuildingHelper.GetBuildingRegistration creates
            // a registration when none exists, which a read must not do.
            var businesses = new Dictionary<(string, int), string>();
            foreach (var registration in SaveGameManager.Current.BuildingRegistrations)
            {
                if (registration == null || string.IsNullOrEmpty(registration.businessTypeName)) continue;
                var address = registration.Address;
                if (address == null) continue;
                businesses[(address.streetName, address.streetNumber)] = registration.businessTypeName;
            }

            var places = new List<NearbyPlace>();
            string? neighbourhood = null;
            var nearest = float.MaxValue;
            // One vote per building, at its nearest entrance.
            foreach (var (controller, entrances) in Buildings())
            {
                var distance = entrances.Min(door => Vector3.Distance(position, door));
                var building = controller.building;
                if (distance < nearest)
                {
                    nearest = distance;
                    neighbourhood = building.Neighbourhood;
                }
                if (distance > radius) continue;
                var address = building.Address;
                businesses.TryGetValue((address.streetName, address.streetNumber), out var business);
                places.Add(new NearbyPlace(building.BuildingType, business, distance));
            }
            // Subway stations bring their own crowd (the `transit` area in demand.json).
            var stations = InstanceBehavior<CityManager>.Instance?.subwayStations;
            if (stations != null)
            {
                foreach (var station in stations)
                {
                    if (station == null) continue;
                    var distance = Vector3.Distance(position, station.transform.position);
                    if (distance <= radius) places.Add(new NearbyPlace(SubwayEntrance, null, distance));
                }
            }
            var areas = new AreaClassifier(tables).Classify(places, position.x, position.z);
            return new Spot(areas, neighbourhood, places.Count, SaveGameManager.Current.Day);
        }

        // Day 1 is a Monday (seen in game: "Monday (Day 1)", Milestone 1 session).
        public static DayOfWeek DayOfWeekOf(int gameDay) =>
            (DayOfWeek)(((int)DayOfWeek.Monday + Math.Max(0, gameDay - 1)) % 7);

        public static DemandQuery Query(DemandTables tables, Spot spot, IReadOnlyCollection<string> menu)
        {
            var game = SaveGameManager.Current;
            var market = new Dictionary<string, double>();
            if (!string.IsNullOrEmpty(spot.Neighbourhood))
            {
                // Only for products the game tracks; for the rest it reports 0, which its own callers read as
                // 100 (CustomerEntriesCalculatorRetail). Left out, Core treats them as unsaturated.
                foreach (var item in menu)
                {
                    if (ItemsGetter.GetByName(item, suppressError: true)?.isADemandedProduct != true) continue;
                    market[item] = ProductMarketHelper.GetNeighborhoodDemand(item, spot.Neighbourhood).demand;
                }
            }
            // The game's default for this multiplier is 0.5; difficulty moves it (GameVariables).
            var promotion = game.gameVariables.baseCustomerPromotionMultiplier / 0.5f;
            return new DemandQuery
            {
                Areas = spot.Areas,
                Neighbourhood = spot.Neighbourhood,
                Menu = menu,
                MinuteOfDay = TimeHelper.CurrentHour * 60 + (int)TimeHelper.CurrentMinute,
                Day = DayOfWeekOf(game.Day),
                Month = tables.MonthOf(game.Day),
                Raining = RainHelper.isRaining,
                MarketDemand = market,
                Promotion = promotion > 0 ? promotion : 1.0,
            };
        }

        // Plain text for the status and the demand readout.
        public static string Describe(Spot spot) =>
            $"{Short(spot.Neighbourhood)}, {spot.Buildings} buildings within reach, areas {spot.Areas}";

        public static string Short(string? id)
        {
            if (string.IsNullOrEmpty(id)) return "unknown";
            var i = id!.LastIndexOf('_');
            return i >= 0 ? id.Substring(i + 1) : id;
        }
    }
}
