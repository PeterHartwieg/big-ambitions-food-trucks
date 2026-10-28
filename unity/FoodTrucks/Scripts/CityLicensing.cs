#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Dialogs;
using Entities;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Pitches;
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
        internal const string ContactId = "the_city_of_new_york";
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
            Log.Info("City of New York: licences, permits and event places by phone.");
            // After a load: catch up on the city's announcements and end partnerships whose vehicle or venue is gone.
            var state = FoodTrucksSession.CurrentState;
            if (state != null && SaveGameManager.Current != null)
            {
                CityPitches.LogFound();
                CityPitches.NewDay(state, SaveGameManager.Current.Day, loading: true);
                Partners.Prune(state);
            }
        }

        public static void Detach()
        {
            CityPitches.Reset();
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
                var current = FoodTrucksSession.CurrentState;
                if (current != null && SaveGameManager.Current != null)
                {
                    CityPitches.NewDay(current, SaveGameManager.Current.Day, loading: false);
                    Partners.Prune(current);
                }
            }
            catch (Exception ex) { Log.Error("City events: " + ex); }
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
                Notices.SendText(City(), key, new Dictionary<string, string>
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
            // A money-change listener can throw after the game books the fee. Continue granting
            // the purchase when that happened, otherwise the player loses the money and gets nothing.
            return cents == 0 || Payments.TryPayOut(cents, info, showNotification: true);
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
            // Permits and event places are offered only to a licensed vendor.
            var missing = valid ? CityLicensing.FoodVehicles(state).Count(v => !v.State.HasPermit) : 0;
            var events = valid ? Upcoming(state).Count() : 0;
            Func<DialogEntry>? second = null;
            string? secondText = null;
            if (missing > 0 && events > 0)
            {
                second = Guard(Choose);
                secondText = "foodtrucks:dialog_city_permits_events";
            }
            else if (missing > 0)
            {
                second = Guard(() => Permit(0));
                secondText = "foodtrucks:dialog_city_permits";
            }
            else if (events > 0)
            {
                second = Guard(() => Event(0));
                secondText = "foodtrucks:dialog_city_events";
            }

            return new DialogEntry
            {
                messageData = LocalizorManager.Localize(key, new
                {
                    fee,
                    days = rules.LicenceDays.ToString(),
                    day = licence.LicenceExpiresOnDay.ToString(),
                    left = Core.Fleet.Licensing.DaysLeft(licence, day).ToString(),
                    permits = missing.ToString(),
                    events = events.ToString()
                }),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = canBuy ? Guard(BuyLicence) : null,
                ConfirmTextOverride = canBuy
                    ? LocalizorManager.Localize(valid ? "foodtrucks:dialog_city_renew" : "foodtrucks:dialog_city_buy", new { fee })
                    : default,
                OnSecondOption = second,
                SecondOptionTextOverride = secondText,
                OnCancel = PhoneCall.HangUpAction,
                onCancelMessage = new TextMessage("ba:messagetype_contacts_message_player_cancel_call")
            };
        }

        // Both permits and event places to offer: the player picks one.
        private static DialogEntry Choose() => new DialogEntry
        {
            messageData = LocalizorManager.Localize("foodtrucks:dialog_city_choose", (object?)null),
            Template = DialogEntry.TemplateType.Text,
            OnConfirm = Guard(() => Permit(0)),
            ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_city_permits", (object?)null),
            OnSecondOption = Guard(() => Event(0)),
            SecondOptionTextOverride = "foodtrucks:dialog_city_events",
            OnCancel = PhoneCall.HangUpAction,
            onCancelMessage = new TextMessage("ba:messagetype_contacts_message_player_cancel_call")
        };

        // Upcoming events, including fully booked ones; wraps around for "next".
        private static DialogEntry Event(int index)
        {
            var state = FoodTrucksSession.CurrentState;
            var tables = DemandData.Pitches;
            if (state == null || tables == null || !FoodTrucksSession.CanWrite) return Closing("foodtrucks:dialog_city_unavailable");
            if (!Core.Fleet.Licensing.IsValid(state.Licence, SaveGameManager.Current.Day)) return Closing("foodtrucks:dialog_city_event_no_licence");
            var upcoming = Upcoming(state).ToList();
            if (upcoming.Count == 0) return Start();
            index %= upcoming.Count;
            var city = upcoming[index];
            var kind = tables.Kind(city.Kind);
            var place = tables.Place(city.PlaceId);
            var fee = CartSelling.Money(city.FeeCents);
            var id = city.Id;
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize("foodtrucks:dialog_city_event", new
                {
                    @event = kind == null ? city.Kind : CityPitches.Text(kind.Name),
                    place = place == null ? CityPitches.Text("foodtrucks:event_location_unavailable") : EventLocations.Name(place.Address),
                    weekday = CityPitches.Weekday(city.Day),
                    day = city.Day.ToString(),
                    start = CityPitches.Clock(city.Start),
                    end = CityPitches.Clock(city.End),
                    left = (city.Places - city.Booked).ToString(),
                    booked = city.Booked.ToString(),
                    fee,
                    number = (index + 1).ToString(),
                    count = upcoming.Count.ToString()
                }),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = city.Booked < city.Places ? Guard(() => BookEvent(id, index)) : null,
                ConfirmTextOverride = city.Booked < city.Places ? LocalizorManager.Localize("foodtrucks:dialog_city_event_book", new { fee }) : default,
                OnSecondOption = Guard(() => EventDirections(id, index)),
                SecondOptionTextOverride = "foodtrucks:dialog_event_directions",
                OnCancel = PhoneCall.HangUpAction,
                onCancelMessage = new TextMessage("ba:messagetype_contacts_message_player_cancel_call")
            };
        }

        // Retain fully booked events in the phone so their directions remain available.
        private static IEnumerable<CityEvent> Upcoming(Core.Saves.SaveState state) => state.Events.Events
            .Where(e => HireCrew.Now() < CityEvents.EndsAt(e) && DemandData.Pitches?.Place(e.PlaceId) is { } p &&
                EventLocations.ReadableAddress(p.Address) != null)
            .OrderBy(CityEvents.StartsAt).ThenBy(e => e.Id);

        private static DialogEntry EventDirections(long id, int index)
        {
            var city = FoodTrucksSession.CurrentState?.Events.Events.FirstOrDefault(e => e.Id == id);
            var place = city == null ? null : DemandData.Pitches?.Place(city.PlaceId);
            if (place == null || EventLocations.ReadableAddress(place.Address) == null) return Event(index);
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize("foodtrucks:dialog_event_location", new { place = EventLocations.Name(place.Address), metres = DemandData.Pitches!.EventRadiusMetres.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) }),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => { EventLocations.SetDestination(place.Address); return Event(index); }),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_event_set_destination", (object?)null),
                OnSecondOption = Guard(() => FoodTrucksSession.CurrentState is { } current && Upcoming(current).Skip(1).Any() ? Event(index + 1) : Start()),
                SecondOptionTextOverride = FoodTrucksSession.CurrentState is { } snapshot && Upcoming(snapshot).Skip(1).Any()
                    ? "foodtrucks:dialog_city_event_next" : "foodtrucks:dialog_city_back",
                OnCancel = PhoneCall.HangUpAction,
                onCancelMessage = new TextMessage("ba:messagetype_contacts_message_player_cancel_call")
            };
        }

        private static DialogEntry? BookEvent(long eventId, int index)
        {
            var state = FoodTrucksSession.CurrentState;
            var tables = DemandData.Pitches;
            if (state == null || tables == null || !FoodTrucksSession.CanWrite) return Closing("foodtrucks:dialog_city_unavailable");
            if (!Core.Fleet.Licensing.IsValid(state.Licence, SaveGameManager.Current.Day)) return Closing("foodtrucks:dialog_city_event_no_licence");
            var city = state.Events.Events.FirstOrDefault(e => e.Id == eventId);
            var now = HireCrew.Now();
            if (city == null || city.Booked >= city.Places || now >= CityEvents.EndsAt(city)) return Event(index);
            var location = tables.Place(city.PlaceId);
            if (location == null || EventLocations.ReadableAddress(location.Address) == null) return Event(index);
            // Paid first: a refused payment books nothing.
            if (!CityLicensing.Charge(city.FeeCents, "foodtrucks:transaction_event")) return null;
            SalesLedger.RecordEventPlaces(state, SaveGameManager.Current.Day, city.FeeCents);
            var result = CityEvents.Book(state.Events, eventId, now);
            FoodTrucksSession.MarkChanged();
            var kind = tables.Kind(city.Kind);
            Log.Info($"City of New York: place {city.Booked} of {city.Places} booked at event {city.Id} ({city.Kind} at {city.PlaceId}, day {city.Day}) for {CartSelling.Money(city.FeeCents)}: {result}.");
            Notices.Send(Current.contact, new TextMessage("foodtrucks:message_city_event_booked", new Dictionary<string, string>
            {
                { "bookingId", city.Id.ToString() }, { "locationId", location.Address },
                { "end", CityPitches.Clock(city.End) }, { "fee", CartSelling.Money(city.FeeCents) },
                { "event", kind == null ? city.Kind : CityPitches.Text(kind.Name) },
                { "place", EventLocations.Name(location.Address) },
                { "weekday", CityPitches.Weekday(city.Day) },
                { "day", city.Day.ToString() },
                { "start", CityPitches.Clock(city.Start) },
                { "booked", city.Booked.ToString() }
            }, read: true, isNewInteraction: true));
            return Event(index);
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
            SalesLedger.RecordLicence(state, day, rules.LicenceFeeCents);
            licensing.Buy(state.Licence, day, course);
            FoodTrucksSession.MarkChanged();
            Log.Info($"City of New York: licence bought on day {day} for {CartSelling.Money(rules.LicenceFeeCents)}, valid to day {state.Licence.LicenceExpiresOnDay}.");
            Notices.Send(Current.contact, new TextMessage("foodtrucks:message_city_licence_issued",
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
                OnCancel = PhoneCall.HangUpAction,
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
            SalesLedger.RecordLine(state, day, vehicleId, LedgerLine.Permit, fee);
            Core.Fleet.Licensing.BuyPermit(state, vehicleId, day);
            FoodTrucksSession.MarkChanged();
            var kind = StreetSales.Find(state, vehicleId)?.VehicleType ?? "cart";
            Log.Info($"City of New York: permit for {kind} {vehicleId} bought on day {day} for {CartSelling.Money(fee)}.");
            Notices.Send(Current.contact, new TextMessage("foodtrucks:message_city_permit_issued",
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
            Notices.Send(Current.contact, new TextMessage(key, null, read: true, isNewInteraction: true));
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize(key, (object?)null),
                Template = DialogEntry.TemplateType.Text,
                OnVisible = Current.FinishDialog
            };
        }
    }
}
