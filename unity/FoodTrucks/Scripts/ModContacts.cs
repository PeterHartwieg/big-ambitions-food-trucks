#nullable enable
using System;
using Entities;
using Helpers;
using UI;
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

        internal static void OpenConversation(Contact contact)
        {
            SaveGameManager.Current.PlayerDefaults.contactsLastName = contact.id;
            SaveGameManager.Current.PlayerDefaults.contactsLastCategoryName = contact.category;
            InstanceBehavior<UIs>.Instance.fullMenu.ShowApp(AppName.Contacts);
        }

        internal static void OpenAndCall(Contact contact)
        {
            OpenConversation(contact);
            var app = InstanceBehavior<UIs>.Instance.fullMenu.contactsApp;
            if (app != null && app.isActiveAndEnabled) app.StartCoroutine(CallNextFrame(app, contact));
        }

        private static System.Collections.IEnumerator CallNextFrame(ContactsApp app, Contact contact)
        {
            yield return null;
            try
            {
                var button = HarmonyLib.Traverse.Create(app).Field("callButton").GetValue<UnityEngine.UI.Button>();
                if (button != null && button.isActiveAndEnabled && button.interactable) app.StartCall();
                else Log.Info($"Contacts: no call button usable for {contact.id}; the conversation is open.");
            }
            catch (Exception ex)
            {
                Log.Error("Contacts: starting the call: " + ex);
            }
        }

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

        private static UnityEngine.Events.UnityAction? viewOnMap;

        private static bool headerFailed;

        internal static void HeaderPostfix(Contact contact, UnityEngine.UI.Button ___viewOnMapButton, UnityEngine.GameObject ___addressGameObject,
            Localizor.LanguageChangeEvent.TextLocalizationComponent ___addressLabel)
        {
            try
            {
                if (viewOnMap != null && ___viewOnMapButton != null) ___viewOnMapButton.onClick.RemoveListener(viewOnMap);
                viewOnMap = null;
                var site = CommissaryLocation.SiteOfContact(contact?.id);
                if (site == null || ___viewOnMapButton == null || ___addressGameObject == null || ___addressLabel == null) return;
                var address = CommissaryLocation.Address(site);
                ___addressLabel.SetValue(address ?? CommissaryLocation.AddressText(site), clearKey: true);
                ___addressGameObject.SetActive(true);
                ___viewOnMapButton.onClick.RemoveAllListeners();
                if (address == null)
                {
                    ___viewOnMapButton.gameObject.SetActive(false);
                    return;
                }
                var siteId = site.Id;
                viewOnMap = () =>
                {
                    try { CommissaryLocation.ViewOnMap(siteId); }
                    catch (Exception ex) { Log.Error("Contacts: commissary View on map: " + ex); }
                };
                ___viewOnMapButton.onClick.AddListener(viewOnMap);
                ___viewOnMapButton.gameObject.SetActive(true);
            }
            catch (Exception ex)
            {
                if (!headerFailed) Log.Error("Contacts: commissary address: " + ex);
                headerFailed = true;
            }
        }

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
