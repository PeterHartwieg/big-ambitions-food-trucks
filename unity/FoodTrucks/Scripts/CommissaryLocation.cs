#nullable enable
using System;
using FoodTrucks.Core.Commissary;
using Helpers;
using Localizor;
using UI;

namespace FoodTrucks
{
    internal static class CommissaryLocation
    {
        internal const string UnavailableKey = "foodtrucks:commissary_address_unavailable";

        internal static CommissarySite? SiteOfContact(string? contactId)
        {
            if (Commissary.Uninstalled || contactId == null || !contactId.StartsWith(Commissary.ContactPrefix, StringComparison.Ordinal)) return null;
            return DemandData.Commissaries?.Site(contactId.Substring(Commissary.ContactPrefix.Length));
        }

        internal static string? Address(CommissarySite site) => EventLocations.ReadableAddress(site.Address);

        internal static string AddressText(CommissarySite site) => Address(site) ?? LocalizorManager.GetLocalization(UnavailableKey, null);

        internal static string MessageKey(string key, string? address) => address == null ? key : key + "_address";

        internal static void ViewOnMap(string siteId)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            if (site == null || Commissary.Uninstalled || Address(site) == null) return;
            var controller = World.BuildingAt(site.Address);
            var map = InstanceBehavior<CityManager>.Instance?.cityMap;
            if (controller == null || map == null) return;
            Log.Info($"Commissary {siteId}: View on map, {site.Address}.");
            InstanceBehavior<UIs>.Instance.fullMenu.Toggle(show: false);
            if (CityMap.IsOpen) map.FocusOnBuilding(controller);
            else
            {
                map.buildingToFocus = controller;
                map.Toggle();
            }
        }

        internal static void SetDestination(string siteId)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            if (site == null || Commissary.Uninstalled || Address(site) == null) return;
            Log.Info($"Commissary {siteId}: Set destination, {site.Address}.");
            EventLocations.SetDestination(site.Address);
        }
    }
}
