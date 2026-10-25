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
        private static bool migrateRadius;
        private static bool migrateAddresses;
        private static string? historyState;
        // Where trucks stood and when, none of it saved: after a load it starts from then.
        // By gig and truck: the first minute the truck stood there ready for it (crew or player, licence, food,
        // portions), so a wait to confirm it is parked never stretches the deadline for a truck that got ready late.
        private readonly Dictionary<(long Gig, string Vehicle), long> readySince = new Dictionary<(long, string), long>();
        // By place and truck, so gigs back to back at one place share the truck's time there.
        private readonly Dictionary<(string Place, string Vehicle), (long Since, Vector3 At)> nearSince = new Dictionary<(string, string), (long, Vector3)>();
        // Minutes a truck must have stood still at the place before its gig starts, so driving past doesn't start it:
        // longer with the player at the wheel, since a red light or a queue holds a truck still for a moment.
        private const long StandMinutes = 2;
        private const long StandMinutesDriving = 10;
        // By started gig: the last game minute its truck was seen at the place.
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
            migrateRadius = migrateAddresses = true;
            historyState = null;
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

        // The contact, made again if the player deleted it, with its call button.
        internal static Contact Bookings()
        {
            var contact = Contact.EnsurePermanentContact(ContactId, ContactCategoryName.General, ContactDescription);
            contact.callDialogTypeOverride = DialogType;
            return contact;
        }

        // The uninstall step: offers and gigs end without a word, then the contact goes with the mod, so a save without
        // it shows no raw texts and nothing brings the contact back.
        public static void RemoveContact()
        {
            var state = FoodTrucksSession.CurrentState;
            var catering = state?.Catering;
            if (state != null && catering != null && FoodTrucksSession.CanWrite)
            {
                // Fees still owed are paid now; one the game still refuses is dropped with the rest.
                PayUnpaid(state);
                if (catering.Unpaid.Count > 0) Log.Error($"Catering: {catering.Unpaid.Count} unpaid fees dropped for the uninstall step.");
                catering.Unpaid.Clear();
                var gigs = catering.Gigs.Count;
                catering.Offers.Clear();
                catering.Gigs.Clear();
                FoodTrucksSession.MarkChanged();
                if (gigs > 0) Log.Info($"Catering: {gigs} booked gigs dropped for the uninstall step.");
            }
            var removed = SaveGameManager.Current?.Contacts.RemoveAll(c => c != null && c.id == ContactId) ?? 0;
            if (removed > 0) Log.Info("Catering: the bookings contact removed.");
        }

        private static CateringRules? Rules => DemandData.Rules?.Catering;

        // Whether a truck was at a gig at that game minute: its crew's route leaves it alone then. A gig already
        // settled still counts for the minutes it ran, since a route can catch up behind catering.
        public static bool BusyAt(string vehicleId, long minute)
        {
            var catering = FoodTrucksSession.CurrentState?.Catering;
            if (catering == null) return false;
            return catering.Gigs.Where(g => g.Status == GigStatus.Started).Concat(catering.History.Where(g => g.Status == GigStatus.Completed))
                .Any(g => g.VehicleId == vehicleId && g.ArrivedDay != null && g.ArrivedMinute != null &&
                          (g.ArrivedDay.Value - 1) * 1440L + g.ArrivedMinute.Value <= minute && minute < GigStart(g) + g.DurationMinutes);
        }

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
            if (Commissary.Uninstalled) return;
            // A contact removed some other way (an older save, another mod) is back by the next morning, not only with
            // the next offer's text, which can be days away.
            try
            {
                if (SaveGameManager.Current?.Contacts.Any(c => c != null && c.id == ContactId) == false)
                {
                    Bookings();
                    Log.Info("Catering: the bookings contact was missing from the phone; added again.");
                }
            }
            catch (Exception ex) { Log.Error("Catering, bookings contact: " + ex); }
            try { Offer(); if (FoodTrucksSession.CurrentState is SaveState state) RefreshSavedMessages(state); }
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
            var lifetimeServed = state.Vehicles.Sum(v => (long)v.LifetimeSales);
            var scanPlaces = CateringOffers.CanGenerateOffers(rules, caterers.Count, lifetimeServed);
            var places = scanPlaces ? Places(tables, rules) : new List<CateringPlace>();
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
                LifetimeServed = lifetimeServed,
                // Storage per piece: for each category, the most food items one truck holds of it, from the pieces fitted today.
                CategoryUnits = CategoryUnits(tables, caterers),
                ReferencePriceCents = prices
            };
            var offers = CateringOffers.ForDay(rules, state.Catering, context);
            FoodTrucksSession.MarkChanged();
            var placeReport = scanPlaces ? $"{places.Count} places ({places.Count(p => p.Area == "park")} at park gates)" : "places not scanned (no eligible offers)";
            Log.Info($"Catering: day {day}: {offers.Count} offers from {caterers.Count} trucks, {placeReport}, {context.LifetimeServed} items sold, booking reputation {state.Catering.BookingReputation:F2}.");
            foreach (var offer in offers)
            {
                Log.Info($"Catering offer {offer.Id}: {offer.Kind} at {offer.PlaceId} on day {offer.GigDay} {Clock(offer.StartMinute)} for {offer.DurationMinutes} min, {offer.Guests} guests, {offer.RequiredCategory}, {CartSelling.Money(offer.FeeCents)}; reply by day {offer.ReplyByDay}.");
                Notices.SendText(Bookings(), "foodtrucks:message_bookings_offer", Describe(offer));
            }
        }

        private static Dictionary<string, int> CategoryUnits(DemandTables tables, IEnumerable<(VehicleInstance Vehicle, VehicleState State, string Kind)> caterers)
        {
            var units = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var (vehicle, _, kind) in caterers)
                foreach (var pair in FoodTrucks.Core.Selling.Storage.CategoryCapacity(tables, kind, CargoLimit.Lines(vehicle)))
                    units[pair.Key] = units.TryGetValue(pair.Key, out var known) ? Math.Max(known, pair.Value) : pair.Value;
            return units;
        }

        // Every building whose own type or business points to one area: a place for that area's gigs. A building whose
        // entrance is at the edge of one of the game's parks (ParkAreas, within ParkGateMetres) is also a `park` place,
        // the park gate, when a kind wants parks (Peter, 14 October 2026).
        private const double ParkGateMetres = 25;

        private static List<CateringPlace> Places(DemandTables tables, CateringRules rules)
        {
            var wanted = new HashSet<string>(rules.Kinds.SelectMany(k => k.Areas), StringComparer.Ordinal);
            var classifier = new AreaClassifier(tables);
            var registrations = World.Registrations();
            var places = new List<CateringPlace>();
            foreach (var (controller, doors) in World.AllBuildings())
            {
                var address = controller.building.Address;
                if (address == null || doors.Length == 0 ||
                    EventLocations.ReadableAddress(controller) == null) continue;
                registrations.TryGetValue((address.streetName, address.streetNumber), out var registration);
                if (wanted.Contains("park") && ParkAreas.Near(doors[0], ParkGateMetres).Any(p => p.Type == ParkAreas.Park))
                    places.Add(new CateringPlace { PlaceId = World.AddressKey(address.streetName, address.streetNumber), Area = "park", X = doors[0].x, Z = doors[0].z });
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
            if (Commissary.Uninstalled || state == null || rules == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            var now = HireCrew.Now();
            if (now == lastMinute) return;
            lastMinute = now;
            try { Step(state, rules, now); }
            catch (Exception ex) { Log.Error("Catering: " + ex); }
        }

        // Address recovery is once per load and never waits for live buildings. Guidance follows state changes.
        // Looking up history must never create a contact, especially after the uninstall step.
        internal static void RefreshSavedMessages(SaveState state)
        {
            if (Commissary.Uninstalled) return;
            var contact = SaveGameManager.Current?.Contacts.Find(c => c != null && c.id == ContactId);
            if (contact == null) return;
            var catering = state.Catering;
            var signature = SaveGameManager.Current!.Day + ":" + contact.messagesQueue.Count + ":" +
                string.Join(",", catering.Gigs.Concat(catering.History).Select(g => g.Id + "/" + g.Status)) + ":" +
                string.Join(",", catering.Offers.Select(o => o.Id));
            if (!migrateAddresses && !migrateRadius && historyState == signature) return;
            // Materialize before touching records. Native address failures are contained by the adapter.
            var active = catering.Gigs.Where(g => g.Status == GigStatus.Booked || g.Status == GigStatus.Started).Select(Describe).ToList();
            EventLocations.RefreshCateringHistory(contact, active, catering.Gigs.Concat(catering.History).Select(Describe),
                catering.Offers.Select(Describe), SaveGameManager.Current.Day, migrateAddresses);
            migrateRadius = migrateAddresses = false;
            historyState = signature;
        }

        // Once a game minute (or once after a skip): gigs that ended or whose truck left are settled, trucks at a
        // booked place start their gig, earliest booking first, and gigs nobody came to are missed.
        private void Step(SaveState state, CateringRules rules, long now)
        {
            var tables = DemandData.Tables;
            if (tables == null) return;
            var vehicles = SaveGameManager.Current.VehicleInstances.Where(v => v != null).ToDictionary(v => v.id, v => v);

            RefreshSavedMessages(state);
            try
            {
                PayUnpaid(state);
                if (EventLocations.Ready)
                {
                    var offerCount = state.Catering.Offers.Count;
                    var cancelled = CateringGigs.CancelUnlocatable(state.Catering, p => EventLocations.ReadableAddress(p) != null);
                    if (cancelled.Count > 0 || offerCount != state.Catering.Offers.Count) FoodTrucksSession.MarkChanged();
                    foreach (var gig in cancelled)
                    {
                        lastSeenNear.Remove(gig.Id);
                        Notices.SendText(Bookings(), "foodtrucks:message_bookings_location_cancelled", Describe(gig));
                    }
                }
                SettleEnded(state, rules, tables, vehicles, now);
                // Wait for native buildings before judging destinations or overdue arrivals.
                if (!EventLocations.Ready) return;

                // Trucks standing at booked places: watched from a day before a gig's window, so one parked there before a
                // skip counts from then.
                var caterers = Caterers(state, rules);
                var watched = new HashSet<(string, string)>();
                foreach (var place in state.Catering.Gigs.Where(g => g.Status == GigStatus.Booked && now >= GigStart(g) - rules.ArrivalWindowMinutes - 1440)
                             .Select(g => g.PlaceId).Distinct())
                    foreach (var (vehicle, saved, _) in caterers)
                    {
                        if (saved.Transit != null || !Near(vehicle, place)) continue;
                        var key = (place, vehicle.id);
                        // It must stand still: moved more than a few metres since it was first seen, it starts again.
                        var position = PositionOf(vehicle);
                        if (!nearSince.TryGetValue(key, out var seen) || Vector3.Distance(seen.At, position) > 3f)
                        {
                            nearSince[key] = (now, position);
                            foreach (var gigAt in state.Catering.Gigs.Where(g => g.PlaceId == place)) readySince.Remove((gigAt.Id, vehicle.id));
                        }
                        watched.Add(key);
                        // Ready for each booking here, from when it first was; not ready, the count starts again.
                        foreach (var gigAt in state.Catering.Gigs.Where(g => g.Status == GigStatus.Booked && g.PlaceId == place))
                        {
                            if (Ready(state, rules, tables, gigAt, vehicle, saved, now)) { if (!readySince.ContainsKey((gigAt.Id, vehicle.id))) readySince[(gigAt.Id, vehicle.id)] = now; }
                            else readySince.Remove((gigAt.Id, vehicle.id));
                        }
                    }
                foreach (var key in nearSince.Keys.Where(k => !watched.Contains(k)).ToList()) nearSince.Remove(key);
                foreach (var key in readySince.Keys.Where(k => !watched.Contains((state.Catering.Gigs.FirstOrDefault(g => g.Id == k.Gig)?.PlaceId ?? "", k.Vehicle))).ToList())
                    readySince.Remove(key);

                // Bookings earliest first; a gig a skip carried past its end is settled before the truck's next booking.
                var pending = new List<long>();
                foreach (var gig in state.Catering.Gigs.Where(g => g.Status == GigStatus.Booked).OrderBy(GigStart).ThenBy(g => g.Id).ToList())
                {
                    var start = GigStart(gig);
                    if (now < start - rules.ArrivalWindowMinutes) continue;
                    foreach (var (vehicle, saved, kind) in caterers)
                    {
                        if (!nearSince.TryGetValue((gig.PlaceId, vehicle.id), out var seen)) continue;
                        var stand = SaveGameManager.Current.ActiveVehicleId == vehicle.id ? StandMinutesDriving : StandMinutes;
                        if (!readySince.TryGetValue((gig.Id, vehicle.id), out var ready))
                        {
                            // There in time but not ready: Arrive says why, once. Too late, the misses catch it.
                            var came = Math.Max(seen.Since, start - rules.ArrivalWindowMinutes);
                            if (now >= seen.Since + stand && came <= start + rules.GraceMinutes) Arrive(state, rules, tables, gig, vehicle, saved, kind, came);
                            continue;
                        }
                        // It came ready, or became free of an earlier gig, at the latest of these.
                        var arrived = Math.Max(Math.Max(Math.Max(seen.Since, ready), start - rules.ArrivalWindowMinutes), BusyUntil(vehicle.id));
                        if (arrived > now) continue;
                        // Too late for this one: another truck may still make it; nobody does, the misses catch it.
                        if (arrived > start + rules.GraceMinutes) continue;
                        // Still being confirmed as parked: the gig waits, and isn't missed meanwhile, as it was ready in time.
                        if (now < seen.Since + stand)
                        {
                            pending.Add(gig.Id);
                            continue;
                        }
                        if (!Arrive(state, rules, tables, gig, vehicle, saved, kind, arrived)) continue;
                        if (gig.Status == GigStatus.Started && GigStart(gig) + gig.DurationMinutes <= now) SettleEnded(state, rules, tables, vehicles, now);
                        break;
                    }
                }

                // A gig started for a truck that stood there through a skip may be over already: settled before the misses.
                SettleEnded(state, rules, tables, vehicles, now);

                // A gig whose truck is still being confirmed in time isn't missed yet: misses are judged up to its deadline.
                var judged = now;
                foreach (var gig in state.Catering.Gigs.Where(g => pending.Contains(g.Id)))
                    judged = Math.Min(judged, GigStart(gig) + rules.GraceMinutes);
                var day = (int)(judged / 1440) + 1;
                foreach (var missed in CateringGigs.MissOverdue(rules, state.Catering, day, (int)(judged % 1440)))
                {
                    FoodTrucksSession.MarkChanged();
                    Log.Info($"Catering gig {missed.Id} at {missed.PlaceId} failed: {missed.Failure}; booking reputation {state.Catering.BookingReputation:F2}.");
                    Notices.SendText(Bookings(), "foodtrucks:message_bookings_missed", Describe(missed));
                }
            }
            finally { RefreshSavedMessages(state); }
        }

        // The minute the truck is free of its gigs: the end of one it has started or just finished, else 0.
        private static long BusyUntil(string vehicleId)
        {
            var catering = FoodTrucksSession.CurrentState?.Catering;
            if (catering == null) return 0;
            return catering.Gigs.Where(g => g.Status == GigStatus.Started).Concat(catering.History.Where(g => g.Status == GigStatus.Completed))
                .Where(g => g.VehicleId == vehicleId && g.ArrivedDay != null)
                .Select(g => GigStart(g) + g.DurationMinutes).DefaultIfEmpty(0).Max();
        }

        private static Vector3 PositionOf(VehicleInstance vehicle)
        {
            var controller = VehicleHelper.GetVehicleController(vehicle);
            return controller != null ? controller.transform.position : (Vector3)vehicle.position;
        }

        // A truck at the place in the arrival window: the gig starts if someone can serve (its crew, or the player
        // beside it or in it), it has the licence and permit, the food on its menu and a portion per guest.
        private bool Arrive(SaveState state, CateringRules rules, DemandTables tables, CateringGig gig, VehicleInstance vehicle, VehicleState saved,
            string kind, long minute)
        {
            var key = (gig.Id, vehicle.id);
            var day = (int)(minute / 1440) + 1;
            var crew = HireCrew.Present(saved).Count;
            var player = PlayerBy(vehicle);
            var licensing = Licensing.CanOpen(state, vehicle.id, day, playerAtWindow: player, hires: crew);
            var menu = CartDemand.MenuOf(tables, vehicle);
            // Units already sold to customers walking to the window aren't there for the guests.
            var stock = Without(CartDemand.StockOf(vehicle, menu.Menu), CartSelling.HeldUnits(vehicle.id));
            var check = licensing != OpenProblem.None
                ? default
                : CateringGigs.Arrive(rules, state.Catering, gig.Id, vehicle.id, kind, day, (int)(minute % 1440),
                    menu.Menu.Items.ToList(), stock, item => tables.Product(item)?.Category);
            FoodTrucksSession.MarkChanged();
            if (licensing != OpenProblem.None || check.Status == GigCheckStatus.NotReady)
            {
                if (notReadyShown.Add(key))
                {
                    var reason = licensing == OpenProblem.NoCrew ? "nobody to serve" : licensing != OpenProblem.None ? licensing.ToString() : "food or stock";
                    Log.Info($"Catering gig {gig.Id}: {kind} {vehicle.id} at the place but not ready ({reason}).");
                    var message = licensing == OpenProblem.NoCrew ? "foodtrucks:notification_catering_no_crew"
                        : licensing != OpenProblem.None ? "foodtrucks:notification_catering_no_licence" : "foodtrucks:notification_catering_not_ready";
                    CartSelling.Say(message, kind, ("units", Units(rules, gig).ToString()), ("food", Category(gig.RequiredCategory)));
                }
                return false;
            }
            switch (check.Status)
            {
                case GigCheckStatus.Started:
                    lastSeenNear[gig.Id] = HireCrew.Now();
                    CartSelling.ServedAt(saved, kind, minute);
                    Log.Info($"Catering gig {gig.Id} started: {kind} {vehicle.id} at {gig.PlaceId} from {Clock((int)(minute % 1440))}, {gig.ReservedUnits} {gig.RequiredCategory} held back; {crew} crew, player {(player ? "there" : "away")}.");
                    CartSelling.Say("foodtrucks:notification_catering_started", kind, ("units", gig.ReservedUnits.ToString()), ("food", Category(gig.RequiredCategory)),
                        ("end", Clock((gig.StartMinute + gig.DurationMinutes) % 1440)));
                    return true;
                case GigCheckStatus.Failed:
                    Log.Info($"Catering gig {gig.Id} failed: {gig.Failure} ({kind} {vehicle.id} arrived {Clock((int)(minute % 1440))}).");
                    Notices.SendText(Bookings(), "foodtrucks:message_bookings_missed", Describe(gig));
                    return true;
                default:
                    return false;
            }
        }

        // Started gigs whose end passed or whose truck left are settled.
        private void SettleEnded(SaveState state, CateringRules rules, DemandTables tables, Dictionary<string, VehicleInstance> vehicles, long now)
        {
            foreach (var gig in state.Catering.Gigs.Where(g => g.Status == GigStatus.Started).ToList())
            {
                if (!EventLocations.Ready) continue; // No departure judgement while native locations are still loading.
                vehicles.TryGetValue(gig.VehicleId, out var truck);
                var end = GigStart(gig) + gig.DurationMinutes;
                var there = truck != null && Near(truck, gig.PlaceId);
                if (there) lastSeenNear[gig.Id] = now;
                if (there && now < end) continue;
                long at;
                if (there) at = end;
                else
                {
                    var seen = lastSeenNear.TryGetValue(gig.Id, out var last) ? last : now;
                    // Gone unseen over a skip past the end: the player can't drive during one and the route keeps the truck
                    // there for the gig's minutes, so it stayed to the end. Seen gone live: it left after it was last there.
                    at = truck != null && now - seen > 1 && now >= end ? end : Math.Min(now, seen + 1);
                }
                Settle(state, rules, tables, gig, truck, Math.Min(at, now));
            }
        }

        // Fees the game didn't credit, tried again each step; the portions left the truck already.
        private static void PayUnpaid(SaveState state)
        {
            foreach (var unpaid in state.Catering.Unpaid.ToList())
            {
                var day = SaveGameManager.Current.Day;
                var units = Math.Max(1, unpaid.Units);
                if (!StreetSales.CanRecordTotal(state, unpaid.VehicleId, day, unpaid.FeeCents, units)) continue;
                // Earned fees must pay even before native addresses exist. Keep that bank line useful.
                var place = EventLocations.Ready ? EventLocations.ReadableAddress(unpaid.PlaceId) : null;
                var info = place == null
                    ? new TransactionInfo("foodtrucks:transaction_catering_unlocated", new Dictionary<string, string>())
                    : new TransactionInfo("foodtrucks:transaction_catering", new Dictionary<string, string> { { "place", place } });
                if (!Payments.PayIn(unpaid.FeeCents, info)) continue;
                StreetSales.RecordTotal(state, unpaid.VehicleId, day, unpaid.FeeCents, units);
                state.Catering.Unpaid.Remove(unpaid);
                FoodTrucksSession.MarkChanged();
                Log.Info($"Catering gig {unpaid.GigId}: {CartSelling.Money(unpaid.FeeCents)} paid on a retry.");
            }
        }

        // Whether the truck could start the gig now: someone to serve, licence and permit, the food on its menu and a
        // portion per guest beyond those promised at the window (the checks Arrive and Core's Arrive make).
        private static bool Ready(SaveState state, CateringRules rules, DemandTables tables, CateringGig gig, VehicleInstance vehicle, VehicleState saved, long minute)
        {
            if (!rules.VehicleTypes.Contains(saved.VehicleType)) return false;
            var day = (int)(minute / 1440) + 1;
            if (Licensing.CanOpen(state, vehicle.id, day, playerAtWindow: PlayerBy(vehicle), hires: HireCrew.Present(saved).Count) != OpenProblem.None) return false;
            var menu = CartDemand.MenuOf(tables, vehicle);
            var stock = Without(CartDemand.StockOf(vehicle, menu.Menu), CartSelling.HeldUnits(vehicle.id));
            var units = stock.Where(p => menu.Menu.IsOnMenu(p.Key) && tables.Product(p.Key)?.Category == gig.RequiredCategory).Sum(p => (long)p.Value);
            return units >= Units(rules, gig);
        }

        // The player in the truck or within a few steps of it.
        private static bool PlayerBy(VehicleInstance vehicle)
        {
            if (SaveGameManager.Current.ActiveVehicleId == vehicle.id) return true;
            if (BuildingManager.IsInsideBuilding) return false;
            var controller = VehicleHelper.GetVehicleController(vehicle);
            return controller != null && Vector3.Distance(PlayerHelper.GetPosition(), controller.transform.position) <= 30f;
        }

        private static Dictionary<string, int> Without(Dictionary<string, int> stock, IReadOnlyDictionary<string, int> held)
        {
            foreach (var pair in held)
            {
                if (!stock.TryGetValue(pair.Key, out var units)) continue;
                if (units - pair.Value > 0) stock[pair.Key] = units - pair.Value;
                else stock.Remove(pair.Key);
            }
            return stock;
        }

        // The gig ends, or its truck left: paid and stocked down when it completed, a message either way.
        private void Settle(SaveState state, CateringRules rules, DemandTables tables, CateringGig gig, VehicleInstance? truck, long minute)
        {
            var vehicle = StreetSales.Find(state, gig.VehicleId);
            // The portions held back for it, as street sales saw them, before the gig is archived.
            var held = truck == null ? new Dictionary<string, int>()
                : Reserved(gig.VehicleId, Without(CartDemand.StockOf(truck, CartDemand.MenuOf(tables, truck).Menu), CartSelling.HeldUnits(gig.VehicleId)));
            var result = CateringGigs.Finish(rules, state.Catering, gig.Id, (int)(minute / 1440) + 1, (int)(minute % 1440));
            lastSeenNear.Remove(gig.Id);
            FoodTrucksSession.MarkChanged();
            if (!result.Success)
            {
                Log.Info($"Catering gig {gig.Id} failed: {gig.Failure}; booking reputation {state.Catering.BookingReputation:F2}.");
                Notices.SendText(Bookings(), "foodtrucks:message_bookings_missed", Describe(gig));
                return;
            }
            var day = SaveGameManager.Current.Day;
            // The record must take the fee before any portion leaves the truck.
            if (truck == null || vehicle == null || result.Units <= 0 || !StreetSales.CanRecordTotal(state, gig.VehicleId, day, result.FeeCents, result.Units))
            {
                Log.Error($"Catering gig {gig.Id} completed but couldn't be paid: truck {(truck == null ? "gone" : "there")}, record {(vehicle == null ? "missing" : "full")}.");
                Notices.SendText(Bookings(), "foodtrucks:message_bookings_unpaid", Describe(gig));
                return;
            }
            // Serving at a gig is a day the truck worked, for the health grade and the inspector.
            CartSelling.ServedAt(vehicle, string.IsNullOrEmpty(vehicle.VehicleType) ? "truck" : vehicle.VehicleType, minute - 1);
            var available = Without(CartDemand.StockOf(truck, CartDemand.MenuOf(tables, truck).Menu), CartSelling.HeldUnits(gig.VehicleId));
            var taken = Take(state, vehicle, truck, tables, gig.RequiredCategory, result.Units, held, available, minute);
            // Food the truck no longer had isn't paid for.
            var fee = (long)Math.Round(result.FeeCents * Math.Min(1.0, taken / (double)result.Units));
            var paid = taken > 0 && fee > 0 &&
                       Payments.PayIn(fee, new TransactionInfo("foodtrucks:transaction_catering", new Dictionary<string, string> { { "place", PlaceName(gig.PlaceId) } }));
            // Income for the tax like any street sale: the fee once, for all its portions.
            if (paid) StreetSales.RecordTotal(state, gig.VehicleId, day, fee, taken);
            else if (taken > 0 && fee > 0)
            {
                // Kept and tried again each step, so the fee isn't lost with the portions already gone.
                state.Catering.Unpaid.Add(new UnpaidFee { GigId = gig.Id, VehicleId = gig.VehicleId, PlaceId = gig.PlaceId, FeeCents = fee, Units = taken });
                Log.Error($"Catering gig {gig.Id}: the game refused the payment of {CartSelling.Money(fee)}; it will be tried again.");
            }
            Log.Info($"Catering gig {gig.Id} completed at {gig.PlaceId}: {taken} of {result.Units} {gig.RequiredCategory} served, {CartSelling.Money(paid ? fee : 0)} paid; booking reputation {state.Catering.BookingReputation:F2}.");
            Notices.SendText(Bookings(), paid ? "foodtrucks:message_bookings_paid" : "foodtrucks:message_bookings_unpaid", new Dictionary<string, string>(Describe(gig))
            {
                ["fee"] = CartSelling.Money(paid ? fee : 0)
            });
        }

        // Takes the gig's units of its food from the truck: the ones held back for it first, then any other of that
        // food. Books their wholesale cost, and stale food counts for the health grade as at the window.
        // `available`: each item's units not promised to customers walking to the window; nothing beyond them is taken.
        private static int Take(SaveState state, VehicleState vehicle, VehicleInstance truck, DemandTables tables, string category, int units,
            IReadOnlyDictionary<string, int> held, IReadOnlyDictionary<string, int> available, long minute)
        {
            var left = available.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            var taken = 0;
            var day = SaveGameManager.Current.Day;
            var menu = CartDemand.MenuOf(tables, truck).Menu;
            var book = CartDemand.Book();
            var workDay = CartSelling.ServiceDay(minute);
            var plan = held.Where(p => p.Value > 0).OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => (Item: p.Key, Units: p.Value)).ToList();
            var rest = units - plan.Sum(p => p.Units);
            if (rest > 0)
                plan.AddRange(menu.Items.Where(i => tables.Product(i)?.Category == category).OrderBy(i => i, StringComparer.Ordinal).Select(i => (Item: i, Units: rest)));
            foreach (var (item, wanted) in plan)
            {
                left.TryGetValue(item, out var free);
                var want = Math.Min(Math.Min(wanted, units - taken), free);
                left[item] = free - Math.Max(0, want);
                foreach (var entry in truck.cargoInstances
                             .Where(c => c != null && c.itemName == item && c.paid && !c.IsSealed && c.amount > 0 &&
                                         (c.nestedCargoInstances == null || c.nestedCargoInstances.Count == 0))
                             .OrderBy(c => c.amount).ToList())
                {
                    if (want <= 0) break;
                    var n = Math.Min(want, entry.amount);
                    var cost = (long)Math.Round(Math.Max(0f, entry.pricePerUnit) * 100.0);
                    for (var i = 0; i < n; i++)
                    {
                        SalesLedger.RecordCost(state, day, vehicle.VehicleId, cost);
                        if (book != null && workDay > ReputationBook.ClosedThrough(state))
                        {
                            if (book.Sold(vehicle, item, HireRunner.DayOf(minute), workDay))
                                Log.Info($"Catering: stale {item} served by {vehicle.VehicleId} on working day {workDay}.");
                        }
                        else ReputationBook.Consume(vehicle, item, 1);
                    }
                    taken += n;
                    want -= n;
                    SoldCounts.Record(vehicle, day, item, n);
                    // Served at the gig: the commissary-order pool goes first (tax origin of later returns).
                    FoodTrucks.Core.Commissary.Commissaries.LeftVehicle(state.Commissary, item, n);
                    try
                    {
                        truck.ReduceFromCargo(entry, n);
                        truck.OnItemsInCargoUpdated()?.Invoke();
                        GameEvent.Invoke("ba:gameevent_itemcargochanged");
                    }
                    catch (Exception ex) { Log.Error("Catering, taking the units: " + ex); }
                }
                if (taken >= units) break;
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
            { "bookingId", offer.Id.ToString() },
            { "locationId", offer.PlaceId },
            { "metres", PlaceMetres.ToString("F0", System.Globalization.CultureInfo.InvariantCulture) },
            { "guidance", Text(offer is CateringGig ? "foodtrucks:booking_map_hint" : "foodtrucks:offer_map_hint") },
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
            return EventLocations.Name(placeId);
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
                OnCancel = PhoneCall.HangUpAction,
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
            if (trucks == 0) return Closing("foodtrucks:dialog_bookings_no_truck", null);
            var result = CateringOffers.Accept(rules, state.Catering, offerId, day, minute, trucks);
            FoodTrucksSession.MarkChanged();
            if (!result.Success)
            {
                Log.Info($"Catering offer {offerId} not taken: {result.Failure}.");
                return Closing(result.Failure == AcceptFailure.TooManyGigs ? "foodtrucks:dialog_bookings_too_many" : "foodtrucks:dialog_bookings_gone", null);
            }
            Log.Info($"Catering offer {offerId} taken: gig at {result.Gig!.PlaceId} on day {result.Gig.GigDay}.");
            Notices.Send(Current.contact, new TextMessage("foodtrucks:message_bookings_booked", Catering.Describe(result.Gig), read: true, isNewInteraction: true));
            Catering.RefreshSavedMessages(state);
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
            Catering.RefreshSavedMessages(state);
            return Start(index);
        }

        private static DialogEntry Closing(string key, object? data)
        {
            Notices.Send(Current.contact, new TextMessage(key, data == null ? null : ToDictionary(data), read: true, isNewInteraction: true));
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
