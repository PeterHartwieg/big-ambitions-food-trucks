#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.DayNightCycle;
using BigAmbitions.Items;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Saves;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
    // What the cart's spot looks like to the demand model: nearby buildings and businesses, the
    // neighbourhood, and the zones on top. Computed when a cart opens and at the start of each day,
    // never every minute (implementation plan, Game adapters).
    internal sealed class Spot
    {
        public Spot(AreaMix areas, string? neighbourhood, int buildings, int day, Vector3 position = default, int hour = -1,
            IReadOnlyList<(NearbyPlace Place, VenueNow? Venue)>? places = null)
        {
            Areas = areas;
            Neighbourhood = neighbourhood;
            Buildings = buildings;
            Day = day;
            X = position.x;
            Y = position.y;
            Z = position.z;
            Hour = hour;
            Places = places ?? Array.Empty<(NearbyPlace, VenueNow?)>();
        }

        public float Y { get; }
        // The game hour its venues were read for: a new hour reads them again.
        public int Hour { get; }
        // The buildings within reach, and for live venues what the game said of them this hour.
        public IReadOnlyList<(NearbyPlace Place, VenueNow? Venue)> Places { get; }

        // Each area's live crowd at that minute of this hour (LiveCrowds, AreaClassifier.LiveFactors).
        public IReadOnlyDictionary<string, double> LiveFactors(DemandTables tables, LiveCrowds? live, int minuteOfDay, DayOfWeek day)
        {
            if (live == null || !Places.Any(p => p.Venue != null)) return new Dictionary<string, double>();
            var weighted = Places.Select(p => p.Venue == null ? p.Place
                : new NearbyPlace(p.Place.BuildingType, p.Place.BusinessType, p.Place.DistanceMetres, live.Weight(At(p.Venue, minuteOfDay), minuteOfDay, day)));
            return new AreaClassifier(tables).LiveFactors(weighted);
        }

        // The venue at that minute: one that closed at the top of this hour has been closed since then.
        private VenueNow At(VenueNow venue, int minuteOfDay) => venue.ClosedMinutesAgo == null ? venue
            : new VenueNow { Kind = venue.Kind, Open = false, Workers = venue.Workers, ClosedMinutesAgo = Math.Max(0, minuteOfDay - Hour * 60) };

        public string Live(DemandTables tables, LiveCrowds? live, int minuteOfDay, DayOfWeek day)
        {
            var factors = LiveFactors(tables, live, minuteOfDay, day);
            return factors.Count == 0 ? "" : string.Join(", ", factors.Select(f => $"{f.Key} x{f.Value:F2}"));
        }

        // Where it was read, on the ground.
        public float X { get; }
        public float Z { get; }

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

        // `at`: the absolute game minute whose hour the venues are read for; null is now.
        public static Spot ReadSpot(DemandTables tables, Vector3 position, double? at = null)
        {
            var absolute = (long)Math.Floor(at ?? HireCrew.NowExact());
            var readDay = (int)(absolute / 1440) + 1;
            var readHour = (int)(absolute % 1440 / 60);
            var radius = tables.Classifier.RadiusMetres;
            // Business types by address, read directly: BuildingHelper.GetBuildingRegistration creates
            // a registration when none exists, which a read must not do.
            var businesses = new Dictionary<(string, int), BuildingRegistration>();
            foreach (var registration in SaveGameManager.Current.BuildingRegistrations)
            {
                if (registration == null || string.IsNullOrEmpty(registration.businessTypeName)) continue;
                var address = registration.Address;
                if (address == null) continue;
                businesses[(address.streetName, address.streetNumber)] = registration;
            }
            var live = DemandData.Rules?.LiveCrowds;
            var read = new List<(NearbyPlace, VenueNow?)>();

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
                businesses.TryGetValue((address.streetName, address.streetNumber), out var registration);
                var business = registration?.businessTypeName;
                var place = new NearbyPlace(building.BuildingType, business, distance);
                places.Add(place);
                read.Add((place, live != null && registration != null && live.KindOf(business) is var kind && kind != VenueKind.Other
                    ? VenueNow(registration, kind, readDay, readHour) : null));
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
            return new Spot(areas, neighbourhood, places.Count, readDay, position, readHour, read);
        }

        // A venue as the game has it this hour: open by its own schedule (AI businesses have one too), when it
        // closed if that was within the last hour, and for offices and factories the staff at work.
        private static VenueNow VenueNow(BuildingRegistration registration, VenueKind kind, int day, int hour)
        {
            var open = OpenAt(registration, day, hour);
            int? closedAgo = null;
            if (!open)
            {
                var previousDay = hour == 0 ? day - 1 : day;
                var previousHour = hour == 0 ? 23 : hour - 1;
                // Closed at the top of this hour; the minutes since are counted when the weight is asked for.
                if (previousDay >= 1 && OpenAt(registration, previousDay, previousHour)) closedAgo = 0;
            }
            int? workers = null;
            if (kind == VenueKind.Office || kind == VenueKind.Factory)
            {
                if (registration.RentedByPlayer)
                {
                    var today = registration.scheduleDays?.Find(d => d != null && d.day == TimeHelper.GetDayOfWeek(day));
                    workers = today?.workShifts?.Count(s => s != null && !string.IsNullOrEmpty(s.employeeId) && s.startingHour <= hour && s.endingHour > hour) ?? 0;
                }
                else if (registration.aiEmployees != null) workers = open ? registration.aiEmployees.Count : 0;
            }
            return new VenueNow { Kind = kind, Open = open, ClosedMinutesAgo = closedAgo, Workers = workers };
        }

        private static bool OpenAt(BuildingRegistration registration, int day, int hour)
        {
            if (registration.temporarilyClosed || registration.scheduleDays == null) return false;
            var schedule = registration.scheduleDays.Find(d => d != null && d.day == TimeHelper.GetDayOfWeek(day));
            return schedule != null && schedule.IsOpenAtHour(hour);
        }

        // Day 1 is a Monday (seen in game: "Monday (Day 1)", Milestone 1 session).
        public static DayOfWeek DayOfWeekOf(int gameDay) =>
            (DayOfWeek)(((int)DayOfWeek.Monday + Math.Max(0, gameDay - 1)) % 7);

        // How near a pitch already known (a regulars entry or a route stop) counts as that pitch, so parking a
        // few metres off on another day still finds its regulars.
        private const double SamePitchMetres = 30;

        // The key regulars and routes know a pitch by: a known pitch nearby (regulars, route stops, today's
        // visits not counted yet), or a new one on a 10 m grid.
        public static string PitchKey(SaveState? state, float x, float z)
        {
            string? best = null;
            var bestDistance = double.MaxValue;
            if (state != null)
            {
                var known = state.Regulars.Entries.Select(e => e.SpotKey)
                    .Concat(state.Vehicles.SelectMany(v => v.Schedule?.Stops ?? new List<ScheduleStop>()).Select(s => s.SpotKey))
                    .Concat(state.Vehicles.SelectMany(v => v.Days ?? new List<DayRecord>()).SelectMany(d => d.Visits).Select(v => v.SpotKey));
                foreach (var key in known.Distinct())
                {
                    if (!TryParsePitch(key, out var kx, out var kz)) continue;
                    var distance = Math.Sqrt((kx - x) * (kx - x) + (kz - z) * (kz - z));
                    if (distance <= SamePitchMetres && distance < bestDistance)
                    {
                        best = key;
                        bestDistance = distance;
                    }
                }
            }
            return best ?? $"{Math.Round(x / 10.0) * 10:F0}:{Math.Round(z / 10.0) * 10:F0}";
        }

        private static bool TryParsePitch(string key, out double x, out double z)
        {
            x = z = 0;
            var parts = key.Split(':');
            return parts.Length == 2 &&
                   double.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) &&
                   double.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out z);
        }

        // `at`: the absolute game minute ((day - 1) × 1440 + minute) to ask about; null is now.
        public static DemandQuery Query(DemandTables tables, Spot spot, IReadOnlyCollection<string> menu,
            double reputation = 1.0, IReadOnlyDictionary<string, double>? categoryShares = null, double? at = null)
        {
            var absolute = (long)Math.Floor(at ?? HireCrew.NowExact());
            var gameDay = (int)(absolute / 1440) + 1;
            var minuteOfDay = (int)(absolute - (gameDay - 1) * 1440L);
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
                MinuteOfDay = minuteOfDay,
                Day = DayOfWeekOf(gameDay),
                Month = tables.MonthOf(gameDay),
                Raining = RainHelper.isRaining,
                MarketDemand = market,
                Promotion = promotion > 0 ? promotion : 1.0,
                Reputation = reputation,
                CategoryShares = categoryShares,
                // The venues as read this hour, at the asked minute: a step in an earlier hour still sees this
                // hour's open or closed venues, at most a frame of game time behind during a skip.
                AreaLive = spot.LiveFactors(tables, DemandData.Rules?.LiveCrowds, minuteOfDay, DayOfWeekOf(gameDay)),
            };
        }

        // Plain text for the status and the demand readout.
        public static string Describe(Spot spot) =>
            $"{Short(spot.Neighbourhood)}, {spot.Buildings} buildings within reach, areas {spot.Areas}" +
            (spot.Places.Any(p => p.Venue != null) ? $"; {spot.Places.Count(p => p.Venue != null)} live venues, now {Live(spot)}" : "");

        private static string Live(Spot spot)
        {
            var tables = DemandData.Tables;
            if (tables == null) return "";
            var text = spot.Live(tables, DemandData.Rules?.LiveCrowds, TimeHelper.CurrentHour * 60 + (int)TimeHelper.CurrentMinute, DayOfWeekOf(SaveGameManager.Current.Day));
            return text.Length == 0 ? "as usual" : text;
        }

        public static string Short(string? id)
        {
            if (string.IsNullOrEmpty(id)) return "unknown";
            var i = id!.LastIndexOf('_');
            return i >= 0 ? id.Substring(i + 1) : id;
        }
    }
}
