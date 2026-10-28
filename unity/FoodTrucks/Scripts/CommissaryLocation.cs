#nullable enable
using System;
using FoodTrucks.Core.Commissary;
using Helpers;
using Localizor;
using UI;

namespace FoodTrucks
{
    // Where a commissary is, for its phone contact, BizMan's rented space and its texts (Steam report, 26 October 2026:
    // "I rented a spot ... I have no idea where this is located"). The host building's address in the game's own words,
    // resolved each time it is shown, so contacts and spaces from older saves need no new visit. Nothing here is saved.
    internal static class CommissaryLocation
    {
        internal const string UnavailableKey = "foodtrucks:commissary_address_unavailable";

        // The site behind one of the mod's commissary contacts; null for any other contact, for an ID the current
        // data/commissaries.json no longer has, and after the uninstall step.
        internal static CommissarySite? SiteOfContact(string? contactId)
        {
            if (Commissary.Uninstalled || contactId == null || !contactId.StartsWith(Commissary.ContactPrefix, StringComparison.Ordinal)) return null;
            return DemandData.Commissaries?.Site(contactId.Substring(Commissary.ContactPrefix.Length));
        }

        // "2 21st Street" as the game formats it, or null while the building or its street name isn't loaded.
        internal static string? Address(CommissarySite site) => EventLocations.ReadableAddress(site.Address);

        internal static string AddressText(CommissarySite site) => Address(site) ?? LocalizorManager.GetLocalization(UnavailableKey, null);

        // A message key with the address in it, when there is one: "<key>_address". Older keys stay as they were, so
        // saved messages without an address still read.
        internal static string MessageKey(string key, string? address) => address == null ? key : key + "_address";

        // The contact's View on map, as the game's own does for a business contact (ContactsApp.ViewOnMapButtonOnClick):
        // the phone closes and the city map opens on the host building with its card selected. Resolved again on click.
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

        // GPS to the host building's entrance (the game's Destination guider); the loading bay is beside it.
        internal static void SetDestination(string siteId)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            if (site == null || Commissary.Uninstalled || Address(site) == null) return;
            Log.Info($"Commissary {siteId}: Set destination, {site.Address}.");
            EventLocations.SetDestination(site.Address);
        }
    }
}
