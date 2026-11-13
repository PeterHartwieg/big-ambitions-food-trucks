#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Buildings;
using City.CityMap;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Saves;
using HarmonyLib;
using Helpers;
using Localizor.LanguageChangeEvent;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    internal static class MapFilters
    {
        private const string LineKey = "foodtrucks:mapfilter_line";
        private const string VehiclesKey = "foodtrucks:mapfilter_vehicles";
        private const string PitchesKey = "foodtrucks:mapfilter_pitches";
        private const string CateringKey = "foodtrucks:mapfilter_catering";
        private const string CommissariesKey = "foodtrucks:mapfilter_commissaries";
        private static readonly Color PitchColour = new Color(0.95f, 0.70f, 0.15f);
        private static readonly Color CateringColour = new Color(0.25f, 0.55f, 0.95f);
        internal static readonly Color CommissaryColour = new Color(0.60f, 0.35f, 0.85f);
        private static string KeyOf(string filter) => "foodtrucks:mapfilter_" + filter;

        private static readonly Dictionary<string, string> IconBusiness = new Dictionary<string, string>
        {
            { StreetFoodMap.Offices, "ba:businesstype_lawfirm" },
            { StreetFoodMap.Factories, "ba:businesstype_factory" },
            { StreetFoodMap.Nightlife, "ba:businesstype_nightclub" },
            { StreetFoodMap.Shows, "ba:businesstype_theater" },
        };

        private static readonly Color OpenColour = new Color(0.20f, 0.65f, 0.30f);

        private static MethodInfo? createLine;
        private static MethodInfo? createFilter;
        private static MethodInfo? isSelected;
        private static AreaClassifier? classifier;
        private static bool loggedFailure;
        private static List<string> selected = new List<string>();
        private static bool hideClosed;
        private static Dictionary<string, Color> pitchAddresses = new Dictionary<string, Color>(StringComparer.Ordinal);
        private static AccessTools.FieldRef<CityMapFilters, bool>? hideClosedField;

        private static readonly Dictionary<string, PointOfInterest> markers = new Dictionary<string, PointOfInterest>();
        private static readonly HashSet<string> greened = new HashSet<string>();

        public static void InitializePostfix(CityMapFilters __instance)
        {
            try
            {
                createLine ??= AccessTools.Method(typeof(CityMapFilters), "CreateLine", new[] { typeof(string) });
                createFilter ??= AccessTools.Method(typeof(CityMapFilters), "CreateFilter");
                if (createLine == null || createFilter == null)
                {
                    Log.Error("Map filters: CreateLine or CreateFilter not found; no Street food line.");
                    return;
                }
                var line = createLine.Invoke(__instance, new object[] { LineKey });
                foreach (var filter in StreetFoodMap.BuildingFilters)
                    AddFilter(__instance, line, KeyOf(filter), BusinessTypeHelper.GetData(IconBusiness[filter])?.icon);
                AddFilter(__instance, line, PitchesKey, BusinessTypeHelper.GetData("ba:businesstype_fastfoodrestaurant")?.icon);
                AddFilter(__instance, line, CateringKey, BusinessTypeHelper.GetData("ba:businesstype_eventplanningagency")?.icon);
                AddFilter(__instance, line, CommissariesKey, BusinessTypeHelper.GetData("ba:businesstype_wholesalestore")?.icon);
                AddFilter(__instance, line, VehiclesKey, InstanceBehavior<GlobalReferences>.Instance?.vehiclePOIIcon);
                Log.Info($"Map filters: Street food line with {StreetFoodMap.BuildingFilters.Count + 4} filters.");
            }
            catch (Exception ex) { Log.Error("Map filters: " + ex); }
        }

        private static void AddFilter(CityMapFilters filters, object line, string key, Sprite? icon)
        {
            createFilter!.Invoke(filters, new object?[] { key, icon, new CityMapFilterData(), line, default(LanguageChangeEventDataHolder), null, null });
        }

        private static bool Selected(CityMapFilters filters, string key)
        {
            isSelected ??= AccessTools.Method(typeof(CityMapFilters), "IsFilterSelected", new[] { typeof(string) });
            return isSelected != null && (bool)isSelected.Invoke(filters, new object[] { key });
        }

        public static void BuildingPostfix(CityMapFilters __instance, CityBuildingController controller)
        {
            try
            {
                var tables = DemandData.Tables;
                if (tables == null || controller == null || controller.building == null || controller.buildingRegistration == null) return;
                if (selected.Count == 0 && pitchAddresses.Count == 0) return;
                var at = controller.building.Address;
                if (at != null && pitchAddresses.TryGetValue(World.AddressKey(at.streetName, at.streetNumber), out var marked))
                {
                    if (controller.poi == null) controller.CreatePOI();
                    else if (controller.poi.Permanent) controller.poi.SetHidden(false);
                    controller.SetHighlight(true, (Color32)marked);
                    var iconType = marked == CateringColour ? "ba:businesstype_eventplanningagency"
                        : marked == CommissaryColour ? "ba:businesstype_wholesalestore" : "ba:businesstype_fastfoodrestaurant";
                    controller.poi.SetIcon(BusinessTypeHelper.GetData(iconType)?.icon, marked);
                    return;
                }
                if (selected.Count == 0) return;
                classifier ??= new AreaClassifier(tables);
                var business = controller.buildingRegistration.businessTypeName;
                var match = selected.FirstOrDefault(f => StreetFoodMap.Highlights(classifier, f, controller.building.BuildingType, business));
                if (match == null) return;
                if (hideClosed && !controller.buildingRegistration.AvailableForRent &&
                    controller.building.BuildingType != "ba:buildingtype_residential" &&
                    !BusinessHelper.IsBusinessOpen(controller.buildingRegistration)) return;

                Color colour;
                Sprite? icon;
                if (!string.IsNullOrEmpty(business) && business != "ba:businesstype_empty")
                {
                    var data = BusinessTypeHelper.GetData(controller.buildingRegistration);
                    colour = data.cityMapFilterColor;
                    icon = data.icon;
                }
                else
                {
                    var data = BuildingTypeHelper.GetData(controller.building);
                    colour = data.mapFilterColor;
                    icon = data.poiIcon;
                }
                if (controller.poi == null) controller.CreatePOI();
                else if (controller.poi.Permanent) controller.poi.SetHidden(false);
                controller.SetHighlight(true, (Color32)colour);
                controller.poi.SetIcon(icon, colour);
            }
            catch (Exception ex)
            {
                if (loggedFailure) return;
                loggedFailure = true;
                Log.Error("Map filters, building pass: " + ex);
            }
        }

        public static void ApplyPostfix(CityMapFilters __instance)
        {
            try
            {
                if (!CityMap.IsOpen) return;
                selected = StreetFoodMap.BuildingFilters.Where(f => Selected(__instance, KeyOf(f))).ToList();
                hideClosedField ??= AccessTools.FieldRefAccess<CityMapFilters, bool>("_hideClosed");
                hideClosed = hideClosedField(__instance);
                pitchAddresses = new Dictionary<string, Color>(StringComparer.Ordinal);
                if (Selected(__instance, PitchesKey)) foreach (var address in PitchAddresses()) pitchAddresses[address] = PitchColour;
                if (Selected(__instance, CateringKey)) foreach (var address in Catering.BookedPlaces()) pitchAddresses[address] = CateringColour;
                if (Selected(__instance, CommissariesKey) && DemandData.Commissaries != null)
                    foreach (var site in DemandData.Commissaries.Sites) pitchAddresses[site.Address] = CommissaryColour;
                var show = Selected(__instance, VehiclesKey);
                var state = FoodTrucksSession.CurrentState;
                var game = SaveGameManager.Current;
                var map = InstanceBehavior<CityManager>.Instance?.cityMap;
                if (game == null || map == null) return;
                var seen = new HashSet<string>();
                foreach (var vehicle in game.VehicleInstances)
                {
                    var kind = FoodTrucksMod.KindOf(vehicle?.vehicleTypeName);
                    if (vehicle == null || kind != "cart") continue;
                    var controller = VehicleHelper.GetVehicleController(vehicle);
                    if (controller == null) continue;
                    seen.Add(vehicle.id);
                    var open = StreetSales.Find(state, vehicle.id)?.IsOpen == true;
                    var references = InstanceBehavior<GlobalReferences>.Instance;
                    var background = open && show ? OpenColour : references.vehiclePOIBackgroundColor;
                    if (controller.poi != null && !markers.ContainsKey(vehicle.id))
                    {
                        if (open && show)
                        {
                            controller.poi.SetBackground(OpenColour);
                            greened.Add(vehicle.id);
                        }
                        else if (greened.Remove(vehicle.id))
                        {
                            controller.poi.SetBackground(vehicle.parkingState == ParkingState.Illegal
                                ? references.vehicleIllegalParkingPOIBackgroundColor : references.vehiclePOIBackgroundColor);
                        }
                        continue;
                    }
                    if (!markers.TryGetValue(vehicle.id, out var marker) || marker == null)
                    {
                        if (!show) continue;
                        marker = map.AddPoi(controller.transform, references.vehiclePOIIcon, references.vehiclePOIBackgroundColor);
                        markers[vehicle.id] = marker;
                    }
                    marker.target = controller.transform;
                    marker.SetBackground(background);
                    marker.SetText(Text(open ? "foodtrucks:mapfilter_open" : "foodtrucks:noun_" + kind));
                    marker.SetHidden(!show);
                }
                foreach (var id in markers.Keys.Where(id => !seen.Contains(id)).ToList()) Remove(id);
            }
            catch (Exception ex) { Log.Error("Map filters, vehicles: " + ex); }
        }

        internal static HashSet<string> PitchAddresses()
        {
            var set = new HashSet<string>(StringComparer.Ordinal);
            var pitches = DemandData.Pitches;
            var state = FoodTrucksSession.CurrentState;
            if (pitches == null) return set;
            foreach (var spot in pitches.Spots) set.Add(spot.Address);
            if (state != null && SaveGameManager.Current != null)
                foreach (var city in state.Events.Events.Where(e => e.Day >= SaveGameManager.Current.Day))
                    if (pitches.Place(city.PlaceId) is { } place) set.Add(place.Address);
            return set;
        }

        private static string Text(string key) => Localizor.LocalizorManager.GetLocalization(key, null);

        private static void Remove(string id)
        {
            if (markers.TryGetValue(id, out var marker) && marker != null) Object.Destroy(marker.gameObject);
            markers.Remove(id);
        }

        public static void Clear()
        {
            foreach (var id in markers.Keys.ToList()) Remove(id);
            classifier = null;
            selected = new List<string>();
            pitchAddresses = new Dictionary<string, Color>(StringComparer.Ordinal);
            greened.Clear();
        }
    }
}
