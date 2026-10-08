#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Dialogs;
using Entities;
using FoodTrucks.Core.Catering;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Reputation;
using FoodTrucks.Core.Saves;
using Helpers;
using Localizor;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;

namespace FoodTrucks
{
    // Catering gigs (design: Catering gigs). A "Street food bookings" contact texts offers; the player takes them
    // by phone. A truck that reaches the place in time with the food starts the gig, its units are held back from
    // street sales, and when the gig ends it is paid and the units go. Core's CateringOffers and CateringGigs decide;
    // this finds the places, watches the trucks and moves the money and the stock.
    internal sealed class Catering : MonoBehaviour
    {
        public const string ContactId = "foodtrucks-bookings";
        private const string ContactDescription = "foodtrucks:bookings_description";
        private const CallDialogType DialogType = (CallDialogType)9102;
        // How near the place's nearest entrance a truck must stand to cater there.
        public const float PlaceMetres = 60f;

        private static Catering? instance;
        private static bool attached;
        private long lastMinute = -1;
        // For each started or booked gig and truck: the first game minute it was seen at the place, and the last.
        // Not saved: after a load it starts from then.
        private readonly Dictionary<(long Gig, string Vehicle), long> nearSince = new Dictionary<(long, string), long>();
        private readonly Dictionary<long, long> lastSeenNear = new Dictionary<long, long>();
        // "Not ready" told once per gig and truck.
        private readonly HashSet<(long, string)> notReadyShown = new HashSet<(long, string)>();

        private void OnEnable() => instance = this;

        private void OnDisable()
        {
            if (instance == this) instance = null;
        }

        public static void Attach()
        {
            Detach();
            CallDialogFactory.RegisterDialog(DialogType, () => new BookingsDialog());
            var contact = Contact.EnsurePermanentContact(ContactId, ContactCategoryName.General, ContactDescription);
            // Not saved by the game, so it is set again after every load.
            contact.callDialogTypeOverride = DialogType;
            GlobalEvents.onNewDay += OnNewDay;
            attached = true;
            var state = FoodTrucksSession.CurrentState;
            Log.Info($"Catering: bookings by phone; {state?.Catering.Offers.Count ?? 0} offers open, {state?.Catering.Gigs.Count ?? 0} gigs booked, booking reputation {state?.Catering.BookingReputation:F2}.");
            // A load on a day without offers yet gets that day's.
            Offer();
        }

        public static void Detach()
        {
            if (!attached) return;
            GlobalEvents.onNewDay -= OnNewDay;
            attached = false;
        }

        internal static Contact Bookings() => Contact.GetContact(ContactId, ContactCategoryName.General, ContactDescription);

        private static CateringRules? Rules => DemandData.Rules?.Catering;

        // Whether a truck is at a gig: its crew's route leaves it alone, so it doesn't drive off mid-gig.
        public static bool Busy(string vehicleId) =>
            FoodTrucksSession.CurrentState?.Catering.Gigs.Any(g => g.Status == GigStatus.Started && g.VehicleId == vehicleId) == true;

        // Units a started gig holds back from street sales, by item: its food's items, up to its units, most stock first.
        public static Dictionary<string, int> Reserved(string vehicleId, IReadOnlyDictionary<string, int> stock)
        {
            var held = new Dictionary<string, int>(StringComparer.Ordinal);
            var state = FoodTrucksSession.CurrentState;
            var tables = DemandData.Tables;
            if (state == null || tables == null) return held;
            foreach (var gig in state.Catering.Gigs.Where(g => g.Status == GigStatus.Started && g.VehicleId == vehicleId))
            {
                var left = gig.ReservedUnits;
                foreach (var item in stock.Where(p => tables.Product(p.Key)?.Category == gig.RequiredCategory)
                             .OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal))
                {
                    if (left <= 0) break;
                    held.TryGetValue(item.Key, out var already);
                    var take = Math.Min(left, item.Value - already);
                    if (take <= 0) continue;
                    held[item.Key] = already + take;
                    left -= take;
                }
            }
            return held;
        }

        // The trucks that can cater: the rules' vehicle types, with a permit.
        private static List<(VehicleInstance Vehicle, VehicleState State, string Kind)> Caterers(SaveState state, CateringRules rules) =>
            CityLicensing.FoodVehicles(state).Where(v => rules.VehicleTypes.Contains(v.Kind) && v.State.HasPermit).ToList();

        private static void OnNewDay()
        {
            try { Offer(); }
            catch (Exception ex) { Log.Error("Catering offers: " + ex); }
        }

        // Today's offers, once a day; the city's buildings are the places.
        private static void Offer()
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            var tables = DemandData.Tables;
            if (state == null || rules == null || tables == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            var day = SaveGameManager.Current.Day;
            var minute = TimeHelper.CurrentHour * 60 + (int)TimeHelper.CurrentMinute;
            if (state.Catering.RandomState == 0) state.Catering.RandomState = SeededRandom.SeedFor("catering", day);
            foreach (var expired in CateringOffers.ExpireOffers(state.Catering, day, Math.Min(1439, minute)))
                Log.Info($"Catering: offer {expired.Id} expired unanswered.");
            if (day <= state.Catering.LastOfferDay) return;
            var caterers = Caterers(state, rules);
            var places = Places(tables, rules);
            var prices = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var (vehicle, _, _) in caterers)
                foreach (var item in CartDemand.MenuOf(tables, vehicle).Menu.Items)
                {
                    var category = tables.Product(item)?.Category;
                    var cents = CartPricing.DefaultCents(item);
                    if (category == null || cents <= 0) continue;
                    prices[category] = prices.TryGetValue(category, out var known) ? Math.Max(known, cents) : cents;
                }
            var context = new OfferContext
            {
                Day = day,
                Places = places,
                EligibleVehicles = caterers.Count,
                LifetimeServed = state.Vehicles.Sum(v => (long)v.LifetimeSales),
                MaxUnits = caterers.Count == 0 ? (int?)null : caterers.Max(v => tables.Vehicle(v.Kind)?.StockUnits ?? 0),
                ReferencePriceCents = prices
            };
            var offers = CateringOffers.ForDay(rules, state.Catering, context);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Catering: day {day}: {offers.Count} offers from {caterers.Count} trucks, {places.Count} places, {context.LifetimeServed} items sold, booking reputation {state.Catering.BookingReputation:F2}.");
            foreach (var offer in offers)
            {
                Log.Info($"Catering offer {offer.Id}: {offer.Kind} at {offer.PlaceId} on day {offer.GigDay} {Clock(offer.StartMinute)} for {offer.DurationMinutes} min, {offer.Guests} guests, {offer.RequiredCategory}, {CartSelling.Money(offer.FeeCents)}; reply by day {offer.ReplyByDay}.");
                GameManager.SendTextMessage(Bookings(), "foodtrucks:message_bookings_offer", Describe(offer));
            }
        }

        // Every building whose own type or business points to one area: a place for that area's gigs.
        private static List<CateringPlace> Places(DemandTables tables, CateringRules rules)
        {
            var wanted = new HashSet<string>(rules.Kinds.SelectMany(k => k.Areas), StringComparer.Ordinal);
            var classifier = new AreaClassifier(tables);
            var registrations = World.Registrations();
            var places = new List<CateringPlace>();
            foreach (var (controller, doors) in World.AllBuildings())
            {
                var address = controller.building.Address;
                if (address == null || doors.Length == 0) continue;
                registrations.TryGetValue((address.streetName, address.streetNumber), out var registration);
                var rule = classifier.RuleFor(controller.building.BuildingType, registration?.businessTypeName);
                if (rule == null || rule.Count == 0) continue;
                var area = rule.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal).First().Key;
                if (!wanted.Contains(area)) continue;
                places.Add(new CateringPlace { PlaceId = World.AddressKey(address.streetName, address.streetNumber), Area = area, X = doors[0].x, Z = doors[0].z });
            }
            return places;
        }

        private void Update()
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            var now = HireCrew.Now();
            if (now == lastMinute) return;
            lastMinute = now;
            try { Step(state, rules, now); }
            catch (Exception ex) { Log.Error("Catering: " + ex); }
        }

        // Once a game minute (or once after a skip): gigs that ended or whose truck left are settled, trucks at a
        // booked place start their gig, and gigs nobody came to are missed.
        private void Step(SaveState state, CateringRules rules, long now)
        {
            var tables = DemandData.Tables;
            if (tables == null) return;
            var vehicles = SaveGameManager.Current.VehicleInstances.Where(v => v != null).ToDictionary(v => v.id, v => v);

            foreach (var gig in state.Catering.Gigs.Where(g => g.Status == GigStatus.Started).ToList())
            {
                vehicles.TryGetValue(gig.VehicleId, out var truck);
                var end = GigStart(gig) + gig.DurationMinutes;
                var there = truck != null && Near(truck, gig.PlaceId);
                if (there) lastSeenNear[gig.Id] = now;
                if (there && now < end) continue;
                // Left: at the last minute it was seen there (a skip can hide when). Stayed: at the end.
                var at = there ? end : Math.Min(now, lastSeenNear.TryGetValue(gig.Id, out var seen) ? seen + 1 : now);
                Settle(state, rules, tables, gig, truck, Math.Min(at, now));
            }

            foreach (var gig in state.Catering.Gigs.Where(g => g.Status == GigStatus.Booked).ToList())
            {
                var start = GigStart(gig);
                if (now < start - rules.ArrivalWindowMinutes) continue;
                foreach (var (vehicle, saved, kind) in Caterers(state, rules))
                {
                    var key = (gig.Id, vehicle.id);
                    if (SaveGameManager.Current.ActiveVehicleId == vehicle.id || saved.Transit != null || !Near(vehicle, gig.PlaceId))
                    {
                        nearSince.Remove(key);
                        continue;
                    }
                    if (!nearSince.TryGetValue(key, out var since)) nearSince[key] = since = now;
                    // Parked there through a skip: it arrived when it first stood there, or when the window opened.
                    var arrived = Math.Max(since, start - rules.ArrivalWindowMinutes);
                    if (Arrive(state, rules, tables, gig, vehicle, saved, kind, arrived)) break;
                }
            }

            var day = (int)(now / 1440) + 1;
            foreach (var missed in CateringGigs.MissOverdue(rules, state.Catering, day, (int)(now % 1440)))
            {
                FoodTrucksSession.MarkChanged();
                Log.Info($"Catering gig {missed.Id} at {missed.PlaceId} failed: {missed.Failure}; booking reputation {state.Catering.BookingReputation:F2}.");
                GameManager.SendTextMessage(Bookings(), "foodtrucks:message_bookings_missed", Describe(missed));
            }
        }

        // A truck at the place in the arrival window: the gig starts if it can sell the food and has enough of it.
        private bool Arrive(SaveState state, CateringRules rules, DemandTables tables, CateringGig gig, VehicleInstance vehicle, VehicleState saved,
            string kind, long minute)
        {
            var key = (gig.Id, vehicle.id);
            var day = (int)(minute / 1440) + 1;
            // Catering is selling food: it needs the licence and the truck's permit, as the window does.
            var licensing = Licensing.CanOpen(state, vehicle.id, day, playerAtWindow: false, hires: 1);
            var menu = CartDemand.MenuOf(tables, vehicle);
            var stock = CartDemand.StockOf(vehicle, menu.Menu);
            var check = licensing != OpenProblem.None
                ? default
                : CateringGigs.Arrive(rules, state.Catering, gig.Id, vehicle.id, kind, day, (int)(minute % 1440),
                    menu.Menu.Items.ToList(), stock, item => tables.Product(item)?.Category);
            FoodTrucksSession.MarkChanged();
            if (licensing != OpenProblem.None || check.Status == GigCheckStatus.NotReady)
            {
                if (notReadyShown.Add(key))
                {
                    Log.Info($"Catering gig {gig.Id}: {kind} {vehicle.id} at the place but not ready ({(licensing != OpenProblem.None ? licensing.ToString() : "food or stock")}).");
                    CartSelling.Say(licensing != OpenProblem.None ? "foodtrucks:notification_catering_no_licence" : "foodtrucks:notification_catering_not_ready", kind,
                        ("units", Units(rules, gig).ToString()), ("food", Category(gig.RequiredCategory)));
                }
                return false;
            }
            switch (check.Status)
            {
                case GigCheckStatus.Started:
                    lastSeenNear[gig.Id] = HireCrew.Now();
                    Log.Info($"Catering gig {gig.Id} started: {kind} {vehicle.id} at {gig.PlaceId} from {Clock((int)(minute % 1440))}, {gig.ReservedUnits} {gig.RequiredCategory} held back.");
                    CartSelling.Say("foodtrucks:notification_catering_started", kind, ("units", gig.ReservedUnits.ToString()), ("food", Category(gig.RequiredCategory)),
                        ("end", Clock((gig.StartMinute + gig.DurationMinutes) % 1440)));
                    return true;
                case GigCheckStatus.Failed:
                    Log.Info($"Catering gig {gig.Id} failed: {gig.Failure} ({kind} {vehicle.id} arrived {Clock((int)(minute % 1440))}).");
                    GameManager.SendTextMessage(Bookings(), "foodtrucks:message_bookings_missed", Describe(gig));
                    return true;
                default:
                    return false;
            }
        }

        // The gig ends, or its truck left: paid and stocked down when it completed, a message either way.
        private void Settle(SaveState state, CateringRules rules, DemandTables tables, CateringGig gig, VehicleInstance? truck, long minute)
        {
            var result = CateringGigs.Finish(rules, state.Catering, gig.Id, (int)(minute / 1440) + 1, (int)(minute % 1440));
            lastSeenNear.Remove(gig.Id);
            FoodTrucksSession.MarkChanged();
            if (!result.Success)
            {
                Log.Info($"Catering gig {gig.Id} failed: {gig.Failure}; booking reputation {state.Catering.BookingReputation:F2}.");
                GameManager.SendTextMessage(Bookings(), "foodtrucks:message_bookings_missed", Describe(gig));
                return;
            }
            var vehicle = StreetSales.Find(state, gig.VehicleId);
            var taken = truck == null || vehicle == null ? 0 : Take(state, vehicle, truck, tables, gig.RequiredCategory, result.Units);
            // Food the truck no longer had isn't paid for.
            var fee = result.Units <= 0 ? 0 : (long)Math.Round(result.FeeCents * Math.Min(1.0, taken / (double)result.Units));
            var day = SaveGameManager.Current.Day;
            var paid = fee > 0 && StreetSales.CanRecord(state, gig.VehicleId, day, fee, Math.Max(1, taken)) &&
                       Payments.PayIn(fee, new TransactionInfo("foodtrucks:transaction_catering", new Dictionary<string, string> { { "place", PlaceName(gig.PlaceId) } }));
            // Income for the tax like any street sale.
            if (paid) StreetSales.Record(state, gig.VehicleId, day, fee, Math.Max(1, taken));
            Log.Info($"Catering gig {gig.Id} completed at {gig.PlaceId}: {taken} of {result.Units} {gig.RequiredCategory} served, {CartSelling.Money(paid ? fee : 0)} paid; booking reputation {state.Catering.BookingReputation:F2}.");
            GameManager.SendTextMessage(Bookings(), "foodtrucks:message_bookings_paid", new Dictionary<string, string>
            {
                { "place", PlaceName(gig.PlaceId) }, { "guests", gig.Guests.ToString() }, { "fee", CartSelling.Money(paid ? fee : 0) }
            });
        }

        // Takes the gig's units of its food from the truck, most stock first, and books what they cost.
        private static int Take(SaveState state, VehicleState vehicle, VehicleInstance truck, DemandTables tables, string category, int units)
        {
            var taken = 0;
            var day = SaveGameManager.Current.Day;
            var menu = CartDemand.MenuOf(tables, truck).Menu;
            foreach (var entry in truck.cargoInstances
                         .Where(c => c != null && c.paid && !c.IsSealed && c.amount > 0 && menu.IsOnMenu(c.itemName) &&
                                     (c.nestedCargoInstances == null || c.nestedCargoInstances.Count == 0) && tables.Product(c.itemName)?.Category == category)
                         .OrderByDescending(c => c.amount).ToList())
            {
                if (taken >= units) break;
                var n = Math.Min(units - taken, entry.amount);
                var item = entry.itemName;
                var cost = (long)Math.Round(Math.Max(0f, entry.pricePerUnit) * 100.0);
                for (var i = 0; i < n; i++) SalesLedger.RecordCost(state, day, cost);
                ReputationBook.Consume(vehicle, item, n);
                taken += n;
                try
                {
                    truck.ReduceFromCargo(entry, n);
                    truck.OnItemsInCargoUpdated()?.Invoke();
                    GameEvent.Invoke("ba:gameevent_itemcargochanged");
                }
                catch (Exception ex) { Log.Error("Catering, taking the units: " + ex); }
            }
            return taken;
        }

        // Within reach of the place: a truck parked by the building's nearest entrance.
        private static bool Near(VehicleInstance vehicle, string placeId)
        {
            var controller = VehicleHelper.GetVehicleController(vehicle);
            var position = controller != null ? controller.transform.position : (Vector3)vehicle.position;
            if (!string.IsNullOrEmpty(vehicle.streetName)) return false;
            return World.DistanceTo(placeId, position.x, position.z) <= PlaceMetres;
        }

        private static long GigStart(CateringOffer gig) => (gig.GigDay - 1) * 1440L + gig.StartMinute;

        private static int Units(CateringRules rules, CateringOffer gig) => (int)Math.Ceiling(gig.Guests * rules.UnitsPerGuest);

        internal static Dictionary<string, string> Describe(CateringOffer offer) => new Dictionary<string, string>
        {
            { "kind", Text("foodtrucks:catering_kind_" + offer.Kind) },
            { "guests", offer.Guests.ToString() },
            { "place", PlaceName(offer.PlaceId) },
            { "weekday", CityPitches.Weekday(offer.GigDay) },
            { "day", offer.GigDay.ToString() },
            { "start", Clock(offer.StartMinute) },
            { "end", Clock((offer.StartMinute + offer.DurationMinutes) % 1440) },
            { "food", Category(offer.RequiredCategory) },
            { "fee", CartSelling.Money(offer.FeeCents) },
            { "reply", offer.ReplyByDay.ToString() }
        };

        // "12 Fifth Street": the place's number and street as the game names it.
        internal static string PlaceName(string placeId)
        {
            var hash = placeId.LastIndexOf('#');
            return hash < 0 ? placeId : $"{placeId.Substring(hash + 1)} {Text(placeId.Substring(0, hash))}";
        }

        internal static string Category(string category) => Text("foodtrucks:category_" + category);

        private static string Clock(int minute) => CityPitches.Clock(minute);

        private static string Text(string key) => LocalizorManager.GetLocalization(key, null);

        // The places of gigs booked and not over, for the map.
        public static HashSet<string> BookedPlaces()
        {
            var state = FoodTrucksSession.CurrentState;
            return state == null ? new HashSet<string>(StringComparer.Ordinal)
                : new HashSet<string>(state.Catering.Gigs.Where(g => g.Status == GigStatus.Booked || g.Status == GigStatus.Started).Select(g => g.PlaceId), StringComparer.Ordinal);
        }

        // Debug key for throwaway saves: a gig at the nearest office, starting in 30 minutes, booked, for the
        // food of the nearest truck's menu, 40 guests.
        public static void GigHereForTesting(Vector3 position)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            var tables = DemandData.Tables;
            if (state == null || rules == null || tables == null || !FoodTrucksSession.CanWrite) return;
            var truck = Caterers(state, rules).Select(v => v.Vehicle).FirstOrDefault();
            var category = truck == null ? null : CartDemand.MenuOf(tables, truck).Menu.Items.Select(i => tables.Product(i)?.Category).FirstOrDefault(c => c != null);
            var place = Places(tables, rules).OrderBy(p => (p.X - position.x) * (p.X - position.x) + (p.Z - position.z) * (p.Z - position.z)).FirstOrDefault();
            if (category == null || place == null)
            {
                CartSelling.Notify("foodtrucks:notification", ("message", "Debug: needs a truck with a permit and equipment."));
                return;
            }
            var now = HireCrew.Now() + 30;
            var gig = new CateringGig
            {
                Id = state.Catering.NextOfferId++, Kind = rules.Kinds[0].Id, PostedDay = (int)(now / 1440) + 1, ReplyByDay = (int)(now / 1440) + 1,
                GigDay = (int)(now / 1440) + 1, StartMinute = (int)(now % 1440), DurationMinutes = 60, PlaceId = place.PlaceId, Guests = 40,
                RequiredCategory = category, FeeCents = 40 * Math.Max(100, CartPricing.DefaultCents(tables.ProductNames.First(p => tables.Product(p)?.Category == category))),
                Status = GigStatus.Booked
            };
            state.Catering.Gigs.Add(gig);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Debug: catering gig {gig.Id} booked at {place.PlaceId} from {Clock(gig.StartMinute)} for 60 min, 40 guests, {category}.");
            CartSelling.Notify("foodtrucks:notification", ("message", $"Debug: catering gig at {PlaceName(place.PlaceId)} from {Clock(gig.StartMinute)}, 40 {Category(category)}."));
        }
    }

    // The bookings call: each open offer in turn, to take or turn down.
    internal sealed class BookingsDialog : Dialog
    {
        public BookingsDialog()
        {
            try { DialogController.current.ShowEntry(Start(0)); }
            catch (Exception ex)
            {
                Log.Error("Bookings call: " + ex);
                try { DialogController.current?.CancelDialog(); }
                catch (Exception inner) { Log.Error("Bookings call, hanging up: " + inner); }
            }
        }

        private static DialogController Current => DialogController.current;

        private static Func<DialogEntry> Guard(Func<DialogEntry?> step) => () =>
        {
            try { return step()!; }
            catch (Exception ex)
            {
                Log.Error("Bookings call: " + ex);
                try { return Closing("foodtrucks:dialog_bookings_unavailable", null); }
                catch (Exception inner)
                {
                    Log.Error("Bookings call, closing: " + inner);
                    return null!;
                }
            }
        };

        private static DialogEntry Start(int index)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules?.Catering;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return Closing("foodtrucks:dialog_bookings_unavailable", null);
            var day = SaveGameManager.Current.Day;
            var minute = TimeHelper.CurrentHour * 60 + (int)TimeHelper.CurrentMinute;
            CateringOffers.ExpireOffers(state.Catering, day, minute);
            var offers = state.Catering.Offers.OrderBy(o => o.GigDay).ThenBy(o => o.StartMinute).ThenBy(o => o.Id).ToList();
            if (offers.Count == 0)
            {
                var booked = state.Catering.Gigs.Count(g => g.Status == GigStatus.Booked || g.Status == GigStatus.Started);
                return Closing("foodtrucks:dialog_bookings_none", new { booked = booked.ToString() });
            }
            index %= offers.Count;
            var offer = offers[index];
            var id = offer.Id;
            var text = Catering.Describe(offer);
            text["number"] = (index + 1).ToString();
            text["count"] = offers.Count.ToString();
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize("foodtrucks:dialog_bookings_offer", text),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => Accept(id, index)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_bookings_accept", (object?)null),
                OnSecondOption = Guard(() => Decline(id, index)),
                SecondOptionTextOverride = "foodtrucks:dialog_bookings_decline",
                OnCancel = Current.CancelDialog,
                onCancelMessage = new TextMessage("ba:messagetype_contacts_message_player_cancel_call")
            };
        }

        private static DialogEntry Accept(long offerId, int index)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = DemandData.Rules?.Catering;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return Closing("foodtrucks:dialog_bookings_unavailable", null);
            var day = SaveGameManager.Current.Day;
            var minute = TimeHelper.CurrentHour * 60 + (int)TimeHelper.CurrentMinute;
            var trucks = CityLicensing.FoodVehicles(state).Count(v => rules.VehicleTypes.Contains(v.Kind) && v.State.HasPermit);
            var result = CateringOffers.Accept(rules, state.Catering, offerId, day, minute, trucks);
            FoodTrucksSession.MarkChanged();
            if (!result.Success)
            {
                Log.Info($"Catering offer {offerId} not taken: {result.Failure}.");
                return Closing(result.Failure == AcceptFailure.TooManyGigs ? "foodtrucks:dialog_bookings_too_many" : "foodtrucks:dialog_bookings_gone", null);
            }
            Log.Info($"Catering offer {offerId} taken: gig at {result.Gig!.PlaceId} on day {result.Gig.GigDay}.");
            Current.contact.SendMessage(new TextMessage("foodtrucks:message_bookings_booked", Catering.Describe(result.Gig), read: true, isNewInteraction: true));
            return Start(index);
        }

        private static DialogEntry Decline(long offerId, int index)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || !FoodTrucksSession.CanWrite) return Closing("foodtrucks:dialog_bookings_unavailable", null);
            if (CateringOffers.Decline(state.Catering, offerId))
            {
                FoodTrucksSession.MarkChanged();
                Log.Info($"Catering offer {offerId} turned down.");
            }
            return Start(index);
        }

        private static DialogEntry Closing(string key, object? data)
        {
            Current.contact.SendMessage(new TextMessage(key, data == null ? null : ToDictionary(data), read: true, isNewInteraction: true));
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize(key, data),
                Template = DialogEntry.TemplateType.Text,
                OnVisible = Current.FinishDialog
            };
        }

        private static Dictionary<string, string> ToDictionary(object data) =>
            data.GetType().GetProperties().ToDictionary(p => p.Name, p => p.GetValue(data)?.ToString() ?? "");
    }
}
