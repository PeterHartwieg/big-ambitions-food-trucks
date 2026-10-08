#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Dialogs;
using Entities;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Helpers;
using Localizor;
using UI.Smartphone.Apps.Contacts;

namespace FoodTrucks
{
    // The vendor licence and the vehicle permits, sold by the City of New York over the phone
    // (Peter, 8 October 2026). The game has that contact already: it writes about parking tickets but
    // can't be called. Contact.callDialogTypeOverride, a field the game never sets, gives it a call button
    // and the mod's own dialog. Core's Licensing decides; this only asks, charges and writes.
    internal static class CityLicensing
    {
        private const string ContactId = "the_city_of_new_york";
        private const string ContactDescription = "government";
        // An unused CallDialogType value; the factory is a plain dictionary and nothing enumerates the enum.
        private const CallDialogType DialogType = (CallDialogType)9101;
        private const string FeesLabel = "foodtrucks:transaction_cityfees_label";

        private static bool attached;

        public static void Attach()
        {
            Detach();
            CallDialogFactory.RegisterDialog(DialogType, () => new CityDialog());
            var contact = Contact.EnsurePermanentContact(ContactId, ContactCategoryName.General, ContactDescription);
            // Not saved by the game, so it is set again after every load.
            contact.callDialogTypeOverride = DialogType;
            GlobalEvents.onNewDay += OnNewDay;
            attached = true;
            Log.Info("City of New York: licences and permits by phone.");
        }

        public static void Detach()
        {
            if (!attached) return;
            GlobalEvents.onNewDay -= OnNewDay;
            attached = false;
        }

        internal static Contact City() => Contact.GetContact(ContactId, ContactCategoryName.General, ContactDescription);

        internal static Licensing? NewLicensing() =>
            DemandData.Rules == null ? null : new Licensing(DemandData.Rules.Fleet);

        // The city writes when renewal opens and when the licence has run out.
        private static void OnNewDay()
        {
            try
            {
                var state = FoodTrucksSession.CurrentState;
                var licensing = NewLicensing();
                if (state == null || licensing == null || SaveGameManager.Current == null) return;
                var day = SaveGameManager.Current.Day;
                var reminder = licensing.ReminderFor(state.Licence, day);
                if (reminder == LicenceReminder.None) return;
                var key = reminder == LicenceReminder.Expired
                    ? "foodtrucks:message_city_licence_expired"
                    : "foodtrucks:message_city_licence_renewal";
                GameManager.SendTextMessage(City(), key, new Dictionary<string, string>
                {
                    { "day", state.Licence.LicenceExpiresOnDay.ToString() },
                    { "fee", CartSelling.Money(DemandData.Rules!.Fleet.LicenceFeeCents) }
                });
                Log.Info($"City of New York: {reminder} message on day {day}.");
            }
            catch (Exception ex) { Log.Error("City reminder: " + ex); }
        }

        // The player's food vehicles, in a fixed order, each with its state in the save.
        internal static List<(VehicleInstance Vehicle, VehicleState State, string Kind)> FoodVehicles(SaveState state)
        {
            var list = new List<(VehicleInstance, VehicleState, string)>();
            foreach (var vehicle in SaveGameManager.Current.VehicleInstances.Where(v => v != null).OrderBy(v => v.id, StringComparer.Ordinal))
            {
                var kind = FoodTrucksMod.KindOf(vehicle?.vehicleTypeName);
                if (vehicle == null || kind == null) continue;
                var saved = StreetSales.GetOrAdd(state, vehicle.id);
                if (string.IsNullOrEmpty(saved.VehicleType)) saved.VehicleType = kind;
                list.Add((vehicle, saved, kind));
            }
            return list;
        }

        // Pays a city fee. False (with the game's own "not enough money" notice) when the player can't.
        internal static bool Charge(long cents, string transactionKey)
        {
            var info = new TransactionInfo(transactionKey, new Dictionary<string, string>());
            info.SetTaxDeductibleName(FeesLabel);
            return GameManager.ChangeMoneySafe(-cents / 100f, info, null, null, force: false, showNotification: true);
        }

        // Debug key for throwaway saves: a licence and a permit for every food vehicle, free and without the course.
        public static void GrantForTesting()
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return;
            var day = SaveGameManager.Current.Day;
            state.Licence.HasVendorLicence = true;
            state.Licence.LicenceExpiresOnDay = day - 1 + rules.Fleet.LicenceDays;
            state.Licence.FirstLicensedDay ??= day;
            var vehicles = FoodVehicles(state);
            foreach (var (_, saved, _) in vehicles) saved.HasPermit = true;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Debug: licence to day {state.Licence.LicenceExpiresOnDay} and permits for {vehicles.Count} vehicles granted.");
            CartSelling.Notify("foodtrucks:notification_debug_licence", ("day", state.Licence.LicenceExpiresOnDay.ToString()), ("count", vehicles.Count.ToString()));
        }
    }

    // The call: the licence first, then a permit for each vehicle that has none, one at a time.
    internal sealed class CityDialog : Dialog
    {
        // The game calls the constructor and every button without a guard, so nothing may throw out of them.
        public CityDialog()
        {
            try { DialogController.current.ShowEntry(Start()); }
            catch (Exception ex)
            {
                Log.Error("City of New York call: " + ex);
                try { DialogController.current?.CancelDialog(); }
                catch (Exception inner) { Log.Error("City of New York call, hanging up: " + inner); }
            }
        }

        // A button's step, with any failure logged and the call ended with the city's apology.
        private static Func<DialogEntry> Guard(Func<DialogEntry?> step) => () =>
        {
            try { return step()!; }
            catch (Exception ex)
            {
                Log.Error("City of New York call: " + ex);
                try { return Closing("foodtrucks:dialog_city_unavailable"); }
                catch (Exception inner)
                {
                    Log.Error("City of New York call, closing: " + inner);
                    return null!;
                }
            }
        };

        private static DialogController Current => DialogController.current;

        private static DialogEntry Start()
        {
            var state = FoodTrucksSession.CurrentState;
            var licensing = CityLicensing.NewLicensing();
            if (state == null || licensing == null || !FoodTrucksSession.CanWrite) return Closing("foodtrucks:dialog_city_unavailable");
            var rules = DemandData.Rules!.Fleet;
            var day = SaveGameManager.Current.Day;
            var fee = CartSelling.Money(rules.LicenceFeeCents);
            var licence = state.Licence;
            var valid = Core.Fleet.Licensing.IsValid(licence, day);
            var canBuy = licensing.CanBuy(licence, day, passedFoodSafety: true) == LicenceProblem.None;
            string key;
            if (!licence.HasVendorLicence) key = "foodtrucks:dialog_city_start_none";
            else if (!valid) key = "foodtrucks:dialog_city_start_expired";
            else if (canBuy) key = "foodtrucks:dialog_city_start_renewable";
            else key = "foodtrucks:dialog_city_start_valid";
            // Permits are offered only to a licensed vendor.
            var missing = valid ? CityLicensing.FoodVehicles(state).Count(v => !v.State.HasPermit) : 0;

            return new DialogEntry
            {
                messageData = LocalizorManager.Localize(key, new
                {
                    fee,
                    days = rules.LicenceDays.ToString(),
                    day = licence.LicenceExpiresOnDay.ToString(),
                    left = Core.Fleet.Licensing.DaysLeft(licence, day).ToString(),
                    permits = missing.ToString()
                }),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = canBuy ? Guard(BuyLicence) : null,
                ConfirmTextOverride = canBuy
                    ? LocalizorManager.Localize(valid ? "foodtrucks:dialog_city_renew" : "foodtrucks:dialog_city_buy", new { fee })
                    : default,
                OnSecondOption = missing > 0 ? Guard(() => Permit(0)) : null,
                SecondOptionTextOverride = missing > 0 ? "foodtrucks:dialog_city_permits" : null,
                OnCancel = Current.CancelDialog,
                onCancelMessage = new TextMessage("ba:messagetype_contacts_message_player_cancel_call")
            };
        }

        private static DialogEntry? BuyLicence()
        {
            var state = FoodTrucksSession.CurrentState;
            var licensing = CityLicensing.NewLicensing();
            if (state == null || licensing == null || !FoodTrucksSession.CanWrite) return Closing("foodtrucks:dialog_city_unavailable");
            var day = SaveGameManager.Current.Day;
            var course = EducationHelper.HasCompletedDiploma(DiplomaName.FoodSafetyCourse);
            var problem = licensing.CanBuy(state.Licence, day, course);
            if (problem == LicenceProblem.NoCourse)
            {
                Log.Info("City of New York: licence refused, no Food Safety course.");
                return Closing("foodtrucks:dialog_city_no_course");
            }
            if (problem != LicenceProblem.None) return Start();
            var rules = DemandData.Rules!.Fleet;
            if (!CityLicensing.Charge(rules.LicenceFeeCents, "foodtrucks:transaction_licence")) return null;
            licensing.Buy(state.Licence, day, course);
            FoodTrucksSession.MarkChanged();
            Log.Info($"City of New York: licence bought on day {day} for {CartSelling.Money(rules.LicenceFeeCents)}, valid to day {state.Licence.LicenceExpiresOnDay}.");
            Current.contact.SendMessage(new TextMessage("foodtrucks:message_city_licence_issued",
                new Dictionary<string, string> { { "day", state.Licence.LicenceExpiresOnDay.ToString() } }, read: true, isNewInteraction: true));
            return Start();
        }

        // The vehicles without a permit, from the index-th on; wraps around for "next".
        private static DialogEntry Permit(int index)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || DemandData.Rules == null) return Closing("foodtrucks:dialog_city_unavailable");
            var day = SaveGameManager.Current.Day;
            if (!Core.Fleet.Licensing.IsValid(state.Licence, day)) return Closing("foodtrucks:dialog_city_permit_no_licence");
            var missing = CityLicensing.FoodVehicles(state).Where(v => !v.State.HasPermit).ToList();
            if (missing.Count == 0) return Start();
            index %= missing.Count;
            var (vehicle, _, kind) = missing[index];
            var fee = CartSelling.Money(DemandData.Rules.Fleet.PermitFeeCents);
            var id = vehicle.id;
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize("foodtrucks:dialog_city_permit", new
                {
                    vehicle = LocalizorManager.GetLocalization("foodtrucks:noun_" + kind, null),
                    place = Place(vehicle),
                    number = (index + 1).ToString(),
                    count = missing.Count.ToString(),
                    fee
                }),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => BuyPermit(id, index)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_city_permit_buy", new { fee }),
                OnSecondOption = missing.Count > 1 ? Guard(() => Permit(index + 1)) : Guard(Start),
                SecondOptionTextOverride = missing.Count > 1 ? "foodtrucks:dialog_city_permit_next" : "foodtrucks:dialog_city_back",
                OnCancel = Current.CancelDialog,
                onCancelMessage = new TextMessage("ba:messagetype_contacts_message_player_cancel_call")
            };
        }

        private static DialogEntry? BuyPermit(string vehicleId, int index)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite || DemandData.Rules == null) return Closing("foodtrucks:dialog_city_unavailable");
            var day = SaveGameManager.Current.Day;
            var problem = Core.Fleet.Licensing.CanBuyPermit(state, vehicleId, day);
            if (problem == PermitProblem.NoLicence) return Closing("foodtrucks:dialog_city_permit_no_licence");
            if (problem != PermitProblem.None) return Permit(index);
            var fee = DemandData.Rules.Fleet.PermitFeeCents;
            if (!CityLicensing.Charge(fee, "foodtrucks:transaction_permit")) return null;
            Core.Fleet.Licensing.BuyPermit(state, vehicleId, day);
            FoodTrucksSession.MarkChanged();
            var kind = StreetSales.Find(state, vehicleId)?.VehicleType ?? "cart";
            Log.Info($"City of New York: permit for {kind} {vehicleId} bought on day {day} for {CartSelling.Money(fee)}.");
            Current.contact.SendMessage(new TextMessage("foodtrucks:message_city_permit_issued",
                new Dictionary<string, string> { { "vehicle", LocalizorManager.GetLocalization("foodtrucks:noun_" + kind, null) } }, read: true, isNewInteraction: true));
            return Permit(index);
        }

        // Where the vehicle stands, by neighbourhood, so the player can tell two carts apart.
        private static string Place(VehicleInstance vehicle)
        {
            var controller = VehicleHelper.GetVehicleController(vehicle);
            var tables = DemandData.Tables;
            var neighbourhood = controller != null && tables != null
                ? World.ReadSpot(tables, controller.transform.position).Neighbourhood
                : vehicle.parkingNeighbourhood;
            return string.IsNullOrEmpty(neighbourhood)
                ? LocalizorManager.GetLocalization("foodtrucks:dialog_city_somewhere", null)
                : LocalizorManager.GetLocalization(neighbourhood, null);
        }

        // A last line from the city, then the call ends.
        private static DialogEntry Closing(string key)
        {
            Current.contact.SendMessage(new TextMessage(key, null, read: true, isNewInteraction: true));
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize(key, (object?)null),
                Template = DialogEntry.TemplateType.Text,
                OnVisible = Current.FinishDialog
            };
        }
    }
}
