#nullable enable
using System;
using Entities;
using UI.Smartphone.Apps.Contacts;

namespace FoodTrucks
{
    // The mod's own phone contacts (Street Food Bookings and each commissary) can't be deleted from the contacts list, like
    // the game's own service contacts (Uncle Fred, NY AutoTow). Each card had the game's red X, which removes a contact for
    // good after one confirmation; nothing made Street Food Bookings again until its next text, so its calls were gone for
    // days (0.9.7 playtest: gone between 15:25 and 17:44, with no log line from the mod and nothing in the game that
    // removes a contact on its own). The uninstall step still removes them. The City of New York isn't one of them: it is
    // the game's contact (ParkingSimulator texts parking tickets from it), deletable as in the game, and CityLicensing
    // adds it again on each load.
    internal static class ModContacts
    {
        private static bool failed;

        internal static bool IsOurs(string? id) =>
            id != null && (id == Catering.ContactId || id.StartsWith(Commissary.ContactPrefix, StringComparison.Ordinal));

        // Postfix on ContactScrollerController.LoadContact: the card just added has no delete button.
        internal static void LoadContactPostfix(ContactScrollerController __instance, Contact contact)
        {
            try
            {
                if (!IsOurs(contact?.id) || Commissary.Uninstalled) return;
                var data = __instance.data;
                if (data == null || data.Count == 0) return;
                var model = data[data.Count - 1];
                if (model != null && ReferenceEquals(model.contact, contact)) model.isPermanent = true;
            }
            catch (Exception ex)
            {
                if (!failed) Log.Error("Contacts: keeping the mod's contacts: " + ex);
                failed = true;
            }
        }

        // Postfix on ContactsHelper.GetPermanentIds: "Delete all" keeps them too.
        internal static void PermanentIdsPostfix(ref System.Collections.Generic.List<string> __result)
        {
            try
            {
                if (__result == null || Commissary.Uninstalled) return;
                var contacts = SaveGameManager.Current?.Contacts;
                if (contacts == null) return;
                foreach (var contact in contacts)
                    if (contact != null && IsOurs(contact.id) && !__result.Contains(contact.id)) __result.Add(contact.id);
            }
            catch (Exception ex)
            {
                if (!failed) Log.Error("Contacts: keeping the mod's contacts: " + ex);
                failed = true;
            }
        }
    }
}
