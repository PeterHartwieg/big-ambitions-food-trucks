#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BigAmbitions.Items;
using Dialogs;
using Entities;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Helpers;
using Localizor;
using UI;
using UI.InteriorDesigner;
using UI.ItemPanel;
using UI.Smartphone.Apps.Contacts;
using UnityEngine;

namespace FoodTrucks
{
    // Commissaries (design: Commissary and new foods). Garages from data/commissaries.json where the player rents a
    // space per vehicle in person (the Commissary button at the bay), orders stock by phone for delivery at 08:00,
    // restocks at the bay, and pays rent daily; at 04:00 the City fines a food vehicle that isn't in its bay.
    // Core's Commissaries decides; this finds the bays, moves the money and the cargo, and sends the texts.
    internal sealed class Commissary : MonoBehaviour
    {
        // Each commissary's phone contact is this plus its ID; en.json names it.
        public const string ContactPrefix = "foodtrucks-commissary-";
        private const string ContactDescription = "foodtrucks:commissary_description";
        private const CallDialogType DialogType = (CallDialogType)9103;
        // Rent and orders are tax-deductible under this label; overnight fines are not.
        private const string ExpenseLabel = "foodtrucks:transaction_commissary_label";
        // The player standing this much beyond the bay radius has visited the commissary.
        private const float VisitExtraMetres = 15f;
        private const float CheckSeconds = 0.5f;

        private static bool attached;
        // Set by the uninstall step for the rest of this session: nothing runs, nothing unlocks, no contact comes back.
        private static bool uninstalled;
        internal static bool Uninstalled => uninstalled;
        private long lastMinute = -1;
        private float nextCheck;
        // Vehicles told once per load that their space is at a commissary data/commissaries.json no longer has.
        private readonly HashSet<string> unknownSiteShown = new HashSet<string>();

        private static CommissaryRules? Rules => DemandData.Rules?.Commissary;
        private static CommissaryTables? Sites => DemandData.Commissaries;

        public static void Attach()
        {
            Detach();
            uninstalled = false;
            var state = FoodTrucksSession.CurrentState;
            var sites = Sites;
            if (sites == null || Rules == null)
            {
                Log.Info("Commissary: not active (commissaries.json or rules not loaded).");
                return;
            }
            CallDialogFactory.RegisterDialog(DialogType, () => new CommissaryDialog());
            attached = true;
            var found = sites.Sites.Count(s => Bays(s) != null);
            Log.Info($"Commissary: {found} of {sites.Sites.Count} garages found in the city" +
                     (found < sites.Sites.Count ? " (missing: " + string.Join(", ", sites.Sites.Where(s => Bays(s) == null).Select(s => s.Address)) + ")" : "") + ".");
            foreach (var site in sites.Sites)
            {
                var bay = Bays(site)?.FirstOrDefault();
                if (bay != null) Log.Info($"Commissary {site.Id} ({site.Address}): bay at x {bay.Value.X:F1}, z {bay.Value.Z:F1}{(site.Bay != null ? " (set in data)" : " (nearest entrance)")}.");
                LogBusinessAt(site);
            }
            if (state == null) return;
            var commissary = state.Commissary;
            // The game doesn't save a contact's call button, so it is set again after every load.
            foreach (var id in commissary.Unlocked)
                if (sites.Site(id) != null) ContactFor(id);
            Log.Info($"Commissary state: unlocked {Join(commissary.Unlocked)}; spaces {Join(commissary.Spaces.Select(s => $"{s.Kind} {World.Short(s.VehicleId)} at {s.CommissaryId}"))}; " +
                     $"{commissary.Orders.Count} orders on the way, {commissary.Stock.Values.Sum(l => l.Sum(x => (long)x.Units))} units in stock, " +
                     $"{commissary.Charges.Count} charges and {commissary.Fines.Count} fines unpaid; night checked through day {commissary.NightCheckedThroughDay?.ToString() ?? "(not started)"}.");
        }

        public static void Detach() => attached = false;

        // A new game gives random AI shops to retail and office units and a rival's factory to a few random warehouses
        // (CityGenerator.PopulateBuildings, SetupRivalFactories); homes never get one. The garages are picked to avoid
        // that, but a rival factory can still land in one, so the log says when a garage's building has a business.
        private static void LogBusinessAt(CommissarySite site)
        {
            try
            {
                var (street, number) = CommissaryTables.SplitAddress(site.Address);
                var registration = SaveGameManager.Current?.BuildingRegistrations.FirstOrDefault(r => r != null && r.StreetName == street && r.StreetNumber == number);
                var business = registration?.businessTypeName;
                if (string.IsNullOrEmpty(business) || business == "ba:businesstype_empty") return;
                Log.Info($"Commissary {site.Id} ({site.Address}): the building has a business in this game ({business}" +
                         $"{(string.IsNullOrEmpty(registration!.BusinessName) ? "" : ", " + registration.BusinessName)}{(registration.RentedByPlayer ? ", yours" : "")}); the bay works the same.");
            }
            catch (Exception ex) { Log.Error("Commissary, building check: " + ex.Message); }
        }

        private static string Join(IEnumerable<string> values)
        {
            var list = values.ToList();
            return list.Count == 0 ? "none" : string.Join(", ", list);
        }

        // The bay's points: the one set in data, else the building's entrances. Null while the city has no such building.
        internal static IEnumerable<(double X, double Z)>? Bays(CommissarySite site)
        {
            if (site.Bay != null) return new[] { site.Bay.Value };
            return World.EntrancesOf(site.Address)?.Select(d => ((double)d.x, (double)d.z));
        }

        private static bool CityReady(CommissaryTables sites) => sites.Sites.Any(s => Bays(s) != null);

        // The commissary whose bay a position is in.
        internal static CommissarySite? SiteAt(Vector3 position, float extraMetres = 0f)
        {
            var sites = Sites;
            var rules = Rules;
            if (sites == null || rules == null) return null;
            return sites.At(Bays, position.x, position.z, rules.BayRadiusMetres + extraMetres);
        }

        private static Vector3 PositionOf(VehicleInstance vehicle)
        {
            var controller = VehicleHelper.GetVehicleController(vehicle);
            return controller != null ? controller.transform.position : (Vector3)vehicle.position;
        }

        // Parked in the open (not in a building or garage) and within the commissary's bay.
        private static bool InBay(VehicleInstance vehicle, CommissarySite site)
        {
            var rules = Rules;
            if (rules == null || !string.IsNullOrEmpty(vehicle.streetName)) return false;
            var position = PositionOf(vehicle);
            return CommissaryTables.Distance(Bays(site), position.x, position.z) <= rules.BayRadiusMetres;
        }

        internal static string Name(CommissarySite site) => LocalizorManager.GetLocalization(site.Name, null);

        private static string NameOf(string? id)
        {
            var site = Sites?.Site(id);
            return site == null ? id ?? "" : Name(site);
        }

        // The commissary's contact, with its call button. Created (as a new contact, with the game's notice) on the first visit.
        internal static Contact ContactFor(string siteId)
        {
            var contact = Contact.EnsurePermanentContact(ContactPrefix + siteId, ContactCategoryName.General, ContactDescription);
            contact.callDialogTypeOverride = DialogType;
            return contact;
        }

        // The first visit: the commissary's contact appears, as the game's own contacts do, and it writes.
        internal static bool Unlock(SaveState state, CommissarySite site, string how)
        {
            if (uninstalled) return false;
            if (!FoodTrucksSession.CanWrite || !Commissaries.Unlock(state.Commissary, site.Id)) return false;
            FoodTrucksSession.MarkChanged();
            var contact = Contact.GetContact(ContactPrefix + site.Id, ContactCategoryName.General, ContactDescription, null, hasWelcomeMessages: false, skipNewNotification: false);
            contact.callDialogTypeOverride = DialogType;
            try { Contact.ShowAddedContactNotifications(); }
            catch (Exception ex) { Log.Error("Commissary, new contact notice: " + ex); }
            var rules = Rules!;
            GameManager.SendTextMessage(contact, "foodtrucks:message_commissary_welcome", new Dictionary<string, string>
            {
                { "commissary", Name(site) },
                { "cart", CartSelling.Money(rules.RentPerMonthCents.TryGetValue("cart", out var cart) ? cart : 0) },
                { "truck", CartSelling.Money(rules.RentPerMonthCents.TryGetValue("truck", out var truck) ? truck : 0) }
            });
            Log.Info($"Commissary {site.Id}: unlocked on day {SaveGameManager.Current.Day} ({how}); its contact added.");
            return true;
        }

        // The order form's call can end with the phone closed, which tells no one: its item list is closed then. Checked
        // in Update and again in LateUpdate, so a phone closed anywhere in a frame (input, UI events, other scripts'
        // Update) is seen before the next frame's clicks reach the list.
        private void LateUpdate() => CommissaryOrderForm.Watch();

        private void Update()
        {
            CommissaryOrderForm.Watch();
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            var sites = Sites;
            if (!attached || uninstalled || state == null || rules == null || sites == null || !FoodTrucksSession.CanWrite || SaveGameManager.Current == null) return;
            if (!CityReady(sites)) return;
            if (Time.unscaledTime >= nextCheck)
            {
                nextCheck = Time.unscaledTime + CheckSeconds;
                try { Look(state); }
                catch (Exception ex) { Log.Error("Commissary, visit check: " + ex); }
            }
            var now = HireCrew.Now();
            if (now == lastMinute) return;
            lastMinute = now;
            try { Step(state, rules, sites, now); }
            catch (Exception ex) { Log.Error("Commissary: " + ex); }
        }

        // Twice a second: the player at a bay unlocks it; the panel's Commissary button shows while the vehicle is in a bay.
        private static void Look(SaveState state)
        {
            if (!BuildingManager.IsInsideBuilding)
            {
                var visit = SiteAt(PlayerHelper.GetPosition(), VisitExtraMetres);
                if (visit != null) Unlock(state, visit, "visited");
            }
            var vehicle = VehicleHelper.GetCurrentVehicle();
            var atBay = vehicle != null && FoodTrucksMod.KindOf(vehicle.vehicleTypeName) != null && SiteAt(PositionOf(vehicle)) != null;
            CartButton.ShowCommissary(atBay);
        }

        // Once a game minute (or once after a skip): unpaid charges tried again, spaces of vehicles gone given up,
        // rent charged, orders delivered, and the 04:00 check.
        private void Step(SaveState state, CommissaryRules rules, CommissaryTables sites, long now)
        {
            var day = (int)(now / 1440) + 1;
            var minute = (int)(now % 1440);
            PayPending(state, rules, showNotification: false);
            PruneSpaces(state, rules);
            ChargeRent(state, rules);
            Deliver(state, rules, day, minute);
            NightCheck(state, rules, sites, now, day, minute);
        }

        // Debts the game refused earlier (not enough money), tried again: rent and order payments, then fines.
        private static void PayPending(SaveState state, CommissaryRules rules, bool showNotification)
        {
            var commissary = state.Commissary;
            var today = SaveGameManager.Current.Day;
            var settledOrder = false;
            foreach (var charge in commissary.Charges.ToList())
            {
                if (charge.OrderId != null)
                {
                    if (!Payments.TryPayOut(charge.Cents, OrderInfo(charge.CommissaryId), showNotification)) continue;
                    commissary.Charges.Remove(charge);
                    SalesLedger.RecordDeliveryFees(state, today, charge.FeeCents);
                    settledOrder = true;
                    Log.Info($"Commissary: order {charge.OrderId} at {charge.CommissaryId} paid on a retry, {CartSelling.Money(charge.Cents)} (fee {CartSelling.Money(charge.FeeCents)}).");
                }
                else
                {
                    if (!Payments.TryPayOut(charge.Cents, RentInfo(charge.CommissaryId), showNotification)) continue;
                    commissary.Charges.Remove(charge);
                    SalesLedger.RecordCommissaryRent(state, today, charge.VehicleId, charge.Cents);
                    Log.Info($"Commissary rent: {World.Short(charge.VehicleId)} at {charge.CommissaryId} for day {charge.Day}, {CartSelling.Money(charge.Cents)} paid on a retry.");
                }
                FoodTrucksSession.MarkChanged();
            }
            foreach (var fine in commissary.Fines.ToList())
            {
                if (!Payments.TryPayOut(fine.Cents, FineInfo(), showNotification)) continue;
                commissary.Fines.Remove(fine);
                SalesLedger.RecordOvernightFine(state, today, fine.VehicleId, fine.Cents);
                FoodTrucksSession.MarkChanged();
                Log.Info($"Overnight fine: {World.Short(fine.VehicleId)} for the night of day {fine.Day}, {CartSelling.Money(fine.Cents)} paid on a retry.");
            }
            // Core schedules a delivery once it sees the debt gone.
            if (settledOrder) Deliver(state, rules, (int)(HireCrew.Now() / 1440) + 1, (int)(HireCrew.Now() % 1440));
        }

        // The player's food vehicles, by ID: on the map and those with the private driver.
        private static Dictionary<string, VehicleInstance> Fleet()
        {
            var game = SaveGameManager.Current;
            var fleet = new Dictionary<string, VehicleInstance>(StringComparer.Ordinal);
            foreach (var vehicle in game.VehicleInstances.Concat(game.privateDriverVehicleInstances ?? Enumerable.Empty<VehicleInstance>()))
                if (vehicle != null && !string.IsNullOrEmpty(vehicle.id) && FoodTrucksMod.KindOf(vehicle.vehicleTypeName) != null && !fleet.ContainsKey(vehicle.id))
                    fleet[vehicle.id] = vehicle;
            return fleet;
        }

        // A space whose vehicle the player no longer has (sold, removed) is given up after its rent is settled.
        private static void PruneSpaces(SaveState state, CommissaryRules rules)
        {
            var commissary = state.Commissary;
            if (commissary.Spaces.Count == 0) return;
            var fleet = Fleet();
            var gone = commissary.Spaces.Where(s => !fleet.ContainsKey(s.VehicleId)).Select(s => s.VehicleId).ToList();
            if (gone.Count == 0) return;
            ChargeRent(state, rules);
            foreach (var id in gone)
            {
                Commissaries.Release(commissary, id);
                Log.Info($"Commissary: the space of {World.Short(id)} given up, the vehicle is gone.");
            }
            FoodTrucksSession.MarkChanged();
        }

        // Rent for every day not charged yet, today's included; a refused payment waits as a charge.
        private static void ChargeRent(SaveState state, CommissaryRules rules)
        {
            var today = SaveGameManager.Current.Day;
            if (today < 1) return;
            var charges = Commissaries.RentDue(state.Commissary, rules, today);
            if (charges.Count == 0) return;
            FoodTrucksSession.MarkChanged();
            foreach (var charge in charges)
            {
                if (charge.Cents <= 0) continue;
                if (Payments.TryPayOut(charge.Cents, RentInfo(charge.CommissaryId), showNotification: false))
                {
                    SalesLedger.RecordCommissaryRent(state, today, charge.VehicleId, charge.Cents);
                    Log.Info($"Commissary rent: {World.Short(charge.VehicleId)} at {charge.CommissaryId} for day {charge.Day}, {CartSelling.Money(charge.Cents)} paid.");
                    continue;
                }
                state.Commissary.Charges.Add(charge);
                Log.Info($"Commissary rent: {World.Short(charge.VehicleId)} at {charge.CommissaryId} for day {charge.Day}, {CartSelling.Money(charge.Cents)} unpaid (not enough money); tried again.");
                if (state.Commissary.Unlocked.Contains(charge.CommissaryId))
                    GameManager.SendTextMessage(ContactFor(charge.CommissaryId), "foodtrucks:message_commissary_rent_unpaid", new Dictionary<string, string>
                    {
                        { "rent", CartSelling.Money(charge.Cents) },
                        { "day", charge.Day.ToString() },
                        { "vehicle", VehicleName(state, charge.VehicleId) }
                    });
            }
        }

        internal static void Deliver(SaveState state, CommissaryRules rules, int day, int minute)
        {
            var delivered = Commissaries.Deliver(state.Commissary, rules, day, minute);
            if (delivered.Count == 0) return;
            FoodTrucksSession.MarkChanged();
            foreach (var order in delivered)
            {
                Log.Info($"Commissary delivery: order {order.Id}, {order.Units} {order.ItemName} at {order.CommissaryId} (ordered day {order.OrderedDay}, due day {order.DeliverDay}).");
                if (state.Commissary.Unlocked.Contains(order.CommissaryId))
                    GameManager.SendTextMessage(ContactFor(order.CommissaryId), "foodtrucks:message_commissary_delivered", new Dictionary<string, string>
                    {
                        { "units", order.Units.ToString() },
                        { "item", LocalizorManager.GetLocalization(order.ItemName, null) },
                        { "commissary", NameOf(order.CommissaryId) }
                    });
            }
        }

        // The 04:00 check (design: Overnight rule). Core keeps the day it checked through, so a load or a skip catches up.
        private void NightCheck(SaveState state, CommissaryRules rules, CommissaryTables sites, long now, int day, int minute)
        {
            var commissary = state.Commissary;
            var fleet = Fleet();
            if (commissary.NightCheckedThroughDay == null)
            {
                // The first check this save sees starts tracking from now: a night that has passed before the rule
                // existed isn't fined (Core would check this morning's 04:00 after 04:00).
                Commissaries.NightCheck(commissary, rules, day, minute, Enumerable.Empty<CommissaryVehicle>());
                FoodTrucksSession.MarkChanged();
                Log.Info($"Overnight check: starts on day {day} at {CityPitches.Clock(minute)}, checked through day {commissary.NightCheckedThroughDay}; {fleet.Count} food vehicles.");
                if (fleet.Count > 0)
                    GameManager.SendTextMessage(CityLicensing.City(), "foodtrucks:message_city_overnight_rule", new Dictionary<string, string>
                    {
                        { "fine", CartSelling.Money(rules.OvernightFineCents) },
                        { "time", CityPitches.Clock(rules.NightCheckMinute) }
                    });
                return;
            }
            // Nothing to check until 04:00 has passed since the last check.
            var latest = minute >= rules.NightCheckMinute ? day : day - 1;
            if (latest <= commissary.NightCheckedThroughDay.Value) return;
            var flags = Flags(state, sites, fleet, now);
            var fines = Commissaries.NightCheck(commissary, rules, day, minute, flags);
            FoodTrucksSession.MarkChanged();
            Log.Info($"Overnight check through day {commissary.NightCheckedThroughDay}: " +
                     (flags.Count == 0 ? "no food vehicles" : string.Join("; ", flags.Select(Describe))) + $"; {fines.Count} fines.");
            foreach (var fine in fines)
            {
                var paid = fine.Cents <= 0 || Payments.TryPayOut(fine.Cents, FineInfo(), showNotification: false);
                if (paid) SalesLedger.RecordOvernightFine(state, SaveGameManager.Current.Day, fine.VehicleId, fine.Cents);
                else commissary.Fines.Add(fine);
                Log.Info($"Overnight fine: {World.Short(fine.VehicleId)} out on the night of day {fine.Day}, {CartSelling.Money(fine.Cents)} {(paid ? "paid" : "unpaid (not enough money); tried again")}.");
                GameManager.SendTextMessage(CityLicensing.City(), paid ? "foodtrucks:message_city_overnight_fine" : "foodtrucks:message_city_overnight_fine_unpaid", new Dictionary<string, string>
                {
                    { "vehicle", VehicleName(state, fine.VehicleId) },
                    { "day", fine.Day.ToString() },
                    { "fine", CartSelling.Money(fine.Cents) },
                    { "time", CityPitches.Clock(rules.NightCheckMinute) }
                });
            }
        }

        private static string Describe(CommissaryVehicle v) =>
            $"{World.Short(v.VehicleId)} {(v.AtOwnCommissaryBay ? "in its bay" : "out")}{(v.PlayerInside ? ", player inside" : "")}{(v.OnLateStop ? ", late stop" : "")}{(v.InTransit ? ", in transit" : "")}{(v.AtPlayerProperty ? ", at your own building" : "")}";

        // The addresses ("street#number") of buildings the player rents (a business, warehouse or home: the game's
        // RentedByPlayer) or owns (its real estate list).
        private static HashSet<string> PlayerProperty()
        {
            var game = SaveGameManager.Current;
            var set = new HashSet<string>(StringComparer.Ordinal);
            foreach (var registration in game.BuildingRegistrations)
            {
                var address = registration?.Address;
                if (registration != null && registration.RentedByPlayer && address != null && !string.IsNullOrEmpty(address.streetName))
                    set.Add(World.AddressKey(address.streetName, address.streetNumber));
            }
            foreach (var estate in game.realEstate ?? new List<RealEstate>())
                if (estate?.address != null && !string.IsNullOrEmpty(estate.address.streetName))
                    set.Add(World.AddressKey(estate.address.streetName, estate.address.streetNumber));
            return set;
        }

        // Peter (9 October 2026): a vehicle at the player's own building isn't fined. The game records a vehicle inside a
        // building (a warehouse's vehicle slot, a garage, any interior) with that building's address, so that counts;
        // the game has no parking lot of a building, so outside it counts within the bay radius of one of its entrances.
        private static bool AtProperty(VehicleInstance vehicle, HashSet<string> property)
        {
            if (property.Count == 0) return false;
            if (!string.IsNullOrEmpty(vehicle.streetName)) return property.Contains(World.AddressKey(vehicle.streetName, vehicle.streetNumber));
            var rules = Rules;
            if (rules == null) return false;
            var position = PositionOf(vehicle);
            return property.Any(address => World.DistanceTo(address, position.x, position.z) <= rules.BayRadiusMetres);
        }

        // Each food vehicle's standing for the night check.
        private List<CommissaryVehicle> Flags(SaveState state, CommissaryTables sites, Dictionary<string, VehicleInstance> fleet, long now)
        {
            var game = SaveGameManager.Current;
            var withDriver = new HashSet<string>((game.privateDriverVehicleInstances ?? new List<VehicleInstance>()).Where(v => v != null).Select(v => v.id), StringComparer.Ordinal);
            var property = PlayerProperty();
            var list = new List<CommissaryVehicle>();
            foreach (var vehicle in fleet.Values.OrderBy(v => v.id, StringComparer.Ordinal))
            {
                var saved = StreetSales.Find(state, vehicle.id);
                var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.id);
                var site = space == null ? null : sites.Site(space.CommissaryId);
                if (space != null && site == null && unknownSiteShown.Add(vehicle.id))
                    Log.Error($"Commissary: {World.Short(vehicle.id)} has its space at '{space.CommissaryId}', which commissaries.json doesn't have; it counts as out overnight.");
                list.Add(new CommissaryVehicle
                {
                    VehicleId = vehicle.id,
                    AtOwnCommissaryBay = site != null && !withDriver.Contains(vehicle.id) && InBay(vehicle, site),
                    PlayerInside = game.ActiveVehicleId == vehicle.id || TruckWindow.PlayerAt(vehicle.id),
                    // Only a window the crew has open at a stop right now: a route that sold out and closed early is out like any other.
                    OnLateStop = saved != null && saved.IsOpen && saved.HireStop != null,
                    InTransit = withDriver.Contains(vehicle.id) || saved?.Transit != null,
                    AtPlayerProperty = !withDriver.Contains(vehicle.id) && AtProperty(vehicle, property)
                });
            }
            return list;
        }

        // "Food cart", "Food truck 2": as Econoview names them.
        private static string VehicleName(SaveState state, string vehicleId)
        {
            if (StreetFoodBooks.VehicleNames().TryGetValue(vehicleId, out var name)) return name;
            var kind = StreetSales.Find(state, vehicleId)?.VehicleType;
            return LocalizorManager.GetLocalization(kind == "truck" ? FoodTrucksMod.TruckTypeName : FoodTrucksMod.CartTypeName, null);
        }

        private static TransactionInfo RentInfo(string siteId)
        {
            var info = new TransactionInfo("foodtrucks:transaction_commissary_rent", new Dictionary<string, string> { { "commissary", NameOf(siteId) } });
            info.SetTaxDeductibleName(ExpenseLabel);
            return info;
        }

        internal static TransactionInfo OrderInfo(string siteId)
        {
            var info = new TransactionInfo("foodtrucks:transaction_commissary_order", new Dictionary<string, string> { { "commissary", NameOf(siteId) } });
            info.SetTaxDeductibleName(ExpenseLabel);
            return info;
        }

        // A fine is no business expense: no tax label.
        private static TransactionInfo FineInfo() => new TransactionInfo("foodtrucks:transaction_overnight_fine", new Dictionary<string, string>());

        // ---- The Commissary button on the vehicle's panel ----

        public static void ShowPanel(ItemPanelUI panel)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            var cart = VehicleHelper.GetCurrentVehicle();
            var kind = FoodTrucksMod.KindOf(cart?.vehicleTypeName);
            if (state == null || rules == null || cart == null || kind == null || uninstalled) return;
            if (!FoodTrucksSession.CanWrite)
            {
                CartSelling.Notify("foodtrucks:notification_data_readonly");
                return;
            }
            var site = SiteAt(PositionOf(cart));
            if (site == null)
            {
                CartSelling.Say("foodtrucks:notification_commissary_not_here", kind);
                return;
            }
            if (DropdownSelector.onShow == null)
            {
                Log.Error("Commissary: the game's list picker isn't available in this scene.");
                CartSelling.Notify("foodtrucks:notification_prices_unavailable");
                return;
            }
            Unlock(state, site, "vehicle at the bay");
            var id = cart.id;
            var siteId = site.Id;
            var options = new List<(string Label, Action Act)>();
            var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == id);
            var rent = CartSelling.Money(rules.RentPerMonthCents.TryGetValue(kind, out var monthly) ? monthly : 0);
            if (space == null)
                options.Add((Format("foodtrucks:commissary_option_rent", rent, Name(site)), () => Rent(id, kind, siteId)));
            else if (space.CommissaryId != siteId)
                options.Add((Format("foodtrucks:commissary_option_move", NameOf(space.CommissaryId), Name(site), rent), () => Rent(id, kind, siteId)));
            else
                options.Add((Format("foodtrucks:commissary_option_release", Name(site)), () => Release(id, kind, siteId)));
            var units = state.Commissary.Stock.TryGetValue(siteId, out var lines) ? lines.Sum(l => (long)l.Units) : 0;
            options.Add(units > 0
                ? (Format("foodtrucks:commissary_option_restock", units.ToString()), () => Restock(id, kind, siteId))
                : (Format("foodtrucks:commissary_option_nostock", Name(site)), () => CartSelling.Say("foodtrucks:notification_commissary_nostock", kind, ("commissary", Name(site)))));
            if (state.Commissary.Spaces.Any(s => s.CommissaryId == siteId))
                options.Add((Format("foodtrucks:commissary_option_order", Name(site)), () => CallToOrder(siteId)));
            var labels = options.Select(o => o.Label).ToList();
            DropdownSelector.Show(labels, chosen =>
            {
                try { options.FirstOrDefault(o => o.Label == chosen).Act?.Invoke(); }
                catch (Exception ex) { Log.Error("Commissary button: " + ex); }
            }, "foodtrucks:commissary_header", "foodtrucks:commissary_body");
        }

        // Renting a space here, or moving the vehicle's space here; its first day's rent is charged at once.
        private static void Rent(string vehicleId, string kind, string siteId)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return;
            var day = SaveGameManager.Current.Day;
            var before = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicleId)?.CommissaryId;
            if (!rules.RentPerMonthCents.TryGetValue(kind, out var monthly))
            {
                Log.Error($"Commissary: no rent for {kind} in rules.json.");
                return;
            }
            // Days not charged yet go to the old place first.
            ChargeRent(state, rules);
            Commissaries.Rent(state.Commissary, rules, vehicleId, kind, siteId, day);
            FoodTrucksSession.MarkChanged();
            ChargeRent(state, rules);
            if (before == null)
            {
                Log.Info($"Commissary {siteId}: {kind} {World.Short(vehicleId)} rented a space on day {day}, {CartSelling.Money(monthly)} a month ({CartSelling.Money(rules.DailyRentCents(kind))} a day).");
                CartSelling.Say("foodtrucks:notification_commissary_rented", kind, ("commissary", NameOf(siteId)), ("rent", CartSelling.Money(monthly)));
            }
            else
            {
                Log.Info($"Commissary {siteId}: {kind} {World.Short(vehicleId)} moved its space here from {before} on day {day}.");
                CartSelling.Say("foodtrucks:notification_commissary_moved", kind, ("commissary", NameOf(siteId)), ("from", NameOf(before)));
            }
        }

        private static void Release(string vehicleId, string kind, string siteId)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return;
            // Core wants the rent settled before a space goes.
            ChargeRent(state, rules);
            if (!Commissaries.Release(state.Commissary, vehicleId)) return;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Commissary {siteId}: {kind} {World.Short(vehicleId)} gave up its space on day {SaveGameManager.Current.Day}.");
            CartSelling.Say("foodtrucks:notification_commissary_released", kind, ("commissary", NameOf(siteId)));
        }

        // Loads the commissary's stock into the vehicle (storage per piece; plan, "Storage per piece"). Equipment first,
        // oldest first, as far as the slots take it (CargoLimit), so a piece fitted here already holds food. Then Core's
        // Storage.Restock plans the food: each fitted piece is filled from its foods, a shared piece split evenly by
        // weight and topped up from the other food. Foods with no fitted piece stay here, and the message says so.
        // Whatever doesn't go in goes back to the commissary's stock in its place.
        private static void Restock(string vehicleId, string kind, string siteId)
        {
            var state = FoodTrucksSession.CurrentState;
            var tables = DemandData.Tables;
            if (state == null || tables == null || !FoodTrucksSession.CanWrite) return;
            var vehicle = SaveGameManager.Current.VehicleInstances.FirstOrDefault(v => v != null && v.id == vehicleId);
            if (vehicle == null || !state.Commissary.Stock.TryGetValue(siteId, out var stock) || stock.Count == 0) return;

            var loaded = new Dictionary<string, int>(StringComparer.Ordinal);
            var back = 0L;
            var pieces = InStock(state, siteId).Where(p => tables.EquipmentForGameItem(p.Key) != null)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            if (pieces.Count > 0) back += Load(state, vehicle, siteId, pieces, loaded);

            var foods = InStock(state, siteId).Where(p => tables.EquipmentForGameItem(p.Key) == null)
                .ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
            var plan = Storage.Restock(tables, kind, CargoLimit.Lines(vehicle), foods);
            if (plan.Load.Count > 0) back += Load(state, vehicle, siteId, plan.Load, loaded);
            FoodTrucksSession.MarkChanged();

            try { GameEvent.Invoke("ba:gameevent_itemcargochanged"); }
            catch (Exception ex) { Log.Error("Commissary restock, cargo event: " + ex); }
            var left = state.Commissary.Stock.TryGetValue(siteId, out var rest) ? rest.Sum(x => (long)x.Units) : 0;
            var view = Storage.Of(tables, kind, CargoLimit.Lines(vehicle));
            Log.Info($"Commissary restock: {kind} {World.Short(vehicleId)} at {siteId} loaded {(loaded.Count == 0 ? "nothing" : string.Join(", ", loaded.Select(p => $"{p.Value} {p.Key}")))}; " +
                     $"no piece for {(plan.NotHeld.Count == 0 ? "none" : string.Join(", ", plan.NotHeld))}; {back} units refused by the cargo and kept; " +
                     $"{left} units left in stock; storage {(view == null ? "?" : StoragePanel.Summary(vehicle, view))}.");
            var text = string.Join(", ", loaded.Select(p => $"{p.Value} {LocalizorManager.GetLocalization(p.Key, null)}"));
            var notHeld = string.Join(", ", plan.NotHeld.Select(i => LocalizorManager.GetLocalization(i, null)));
            if (loaded.Count == 0 && plan.NotHeld.Count == 0)
                CartSelling.Say("foodtrucks:notification_commissary_restock_full", kind, ("left", left.ToString()));
            else if (loaded.Count == 0)
                CartSelling.Say("foodtrucks:notification_commissary_restock_notheld", kind, ("notheld", notHeld), ("left", left.ToString()));
            else if (plan.NotHeld.Count == 0)
                CartSelling.Say("foodtrucks:notification_commissary_restocked", kind, ("loaded", text), ("left", left.ToString()));
            else
                CartSelling.Say("foodtrucks:notification_commissary_restocked_notheld", kind, ("loaded", text), ("notheld", notHeld), ("left", left.ToString()));
        }

        // What this commissary holds of each item.
        private static Dictionary<string, int> InStock(SaveState state, string siteId)
        {
            var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
            if (!state.Commissary.Stock.TryGetValue(siteId, out var stock)) return wanted;
            foreach (var line in stock) wanted[line.ItemName] = (int)Math.Min(int.MaxValue, (wanted.TryGetValue(line.ItemName, out var w) ? w : 0) + (long)line.Units);
            return wanted;
        }

        // Takes the wanted units out of stock, oldest first, loads them through TryToAddToCargo (so CargoLimit still has the
        // last word) and returns what didn't go in to its batch. Returns the units returned.
        private static long Load(SaveState state, VehicleInstance vehicle, string siteId, IReadOnlyDictionary<string, int> wanted,
            Dictionary<string, int> loaded)
        {
            var taken = Commissaries.Restock(state.Commissary, siteId, wanted);
            var back = new List<CommissaryStockLine>();
            foreach (var line in taken)
            {
                var moved = 0;
                if (ItemsGetter.GetByName(line.ItemName, suppressError: true) != null)
                {
                    var before = vehicle.GetAmountByItemName(line.ItemName);
                    try { vehicle.TryToAddToCargo(new CargoInstance(line.ItemName, line.Units, line.UnitPriceCents / 100f, paid: true)); }
                    catch (Exception ex) { Log.Error($"Commissary restock: loading {line.ItemName} threw: {ex}"); }
                    moved = Math.Max(0, Math.Min(line.Units, vehicle.GetAmountByItemName(line.ItemName) - before));
                }
                else Log.Error($"Commissary restock: {line.ItemName} isn't a game item (its mod removed?); it stays in stock.");
                if (moved > 0) loaded[line.ItemName] = (loaded.TryGetValue(line.ItemName, out var l) ? l : 0) + moved;
                if (moved < line.Units)
                    back.Add(new CommissaryStockLine
                    {
                        ItemName = line.ItemName, Units = line.Units - moved, UnitPriceCents = line.UnitPriceCents, BatchId = line.BatchId,
                        ExtensionData = line.ExtensionData, OrderMetadata = line.OrderMetadata
                    });
            }
            if (back.Count > 0) Commissaries.ReturnToStock(state.Commissary, siteId, back);
            return back.Sum(b => (long)b.Units);
        }

        private static string Format(string key, params string[] values) => string.Format(LocalizorManager.GetLocalization(key, null), values);

        // ---- Ordering (the commissary's phone contact) ----

        // What a commissary sells: economy.json's foods and the game items its equipment maps to, where the game has the
        // item (a mod's items only while it is installed) and a price. Foods first, then machines, each by name.
        internal static List<(string Item, int Box, long UnitCents)> Catalogue(bool machines)
        {
            var tables = DemandData.Tables;
            var list = new List<(string, int, long)>();
            if (tables == null) return list;
            var names = machines
                ? tables.EquipmentIds.Select(tables.Equipment).Where(e => e != null).SelectMany(e => e!.GameItems)
                : tables.ProductNames;
            foreach (var name in names.Distinct(StringComparer.Ordinal))
            {
                var item = ItemsGetter.GetByName(name, suppressError: true);
                if (item == null) continue;
                var price = item.GetWholesalePrice();
                // Furniture has no wholesale price: its shop price, with the difficulty's market multiplier as GetWholesalePrice applies to foods.
                if (price <= 0f) price = item.DefaultMarketPrice * (SaveGameManager.Current?.gameVariables.marketPriceMultiplier ?? 1f);
                var cents = (long)Math.Round(price * 100.0);
                if (cents <= 0) continue;
                // Equipment is sold by the piece, whatever box the game packs it in; food by the game's whole box.
                list.Add((name, machines ? 1 : Math.Max(1, item.boxSize), cents));
            }
            return list.OrderBy(i => LocalizorManager.GetLocalization(i.Item1, null), StringComparer.CurrentCulture).ToList();
        }

        // What the commissary's order form lists: the catalogue with what this commissary holds of each item.
        internal static List<CommissaryOrderForm.Line> FormLines(SaveState state, string siteId)
        {
            var stock = state.Commissary.Stock.TryGetValue(siteId, out var lines) ? lines : new List<CommissaryStockLine>();
            return Catalogue(false).Concat(Catalogue(true))
                .Select(c => new CommissaryOrderForm.Line(c.Item, c.Box, c.UnitCents, stock.Where(l => l.ItemName == c.Item).Sum(l => (long)l.Units)))
                .ToList();
        }

        // The bay's "Order stock": the phone opens on the commissary's conversation and calls it, so ordering happens
        // in one place, the conversation's order form.
        private static void CallToOrder(string siteId)
        {
            HireRoutes.Later(() =>
            {
                try
                {
                    Log.Info($"Commissary {siteId}: the bay's Order stock opens its conversation.");
                    ContactsApp.OpenAndStartCall(ContactFor(siteId));
                }
                catch (Exception ex)
                {
                    Log.Error("Commissary order, opening the phone: " + ex);
                    CartSelling.Notify("foodtrucks:notification_commissary_call", ("commissary", NameOf(siteId)));
                }
            });
        }

        // A quote placed as Core's orders, each paid now; a refused payment books the order anyway, held until paid
        // (Core's pending charge). Returns each order with whether it was paid.
        internal static List<(CommissaryOrder Order, bool Paid)> PlaceQuote(string siteId, CommissaryQuote quote)
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            var placed = new List<(CommissaryOrder, bool)>();
            if (state == null || rules == null || uninstalled || !FoodTrucksSession.CanWrite) return placed;
            var now = HireCrew.Now();
            var day = (int)(now / 1440) + 1;
            var minute = (int)(now % 1440);
            var orders = CommissaryOrdering.Place(state.Commissary, rules, siteId, quote, (day, minute));
            FoodTrucksSession.MarkChanged();
            var today = SaveGameManager.Current.Day;
            var unpaid = false;
            foreach (var order in orders)
            {
                // Core has booked every order already: one whose payment throws is held as unpaid like a refused one,
                // so it isn't delivered free and the orders after it are still paid.
                bool paid;
                try { paid = Payments.TryPayOut(order.TotalCents, OrderInfo(siteId), showNotification: true); }
                catch (Exception ex)
                {
                    Log.Error($"Commissary order {order.Id}: payment failed, held until paid: {ex}");
                    paid = false;
                }
                if (paid)
                {
                    try { SalesLedger.RecordDeliveryFees(state, today, order.FeeCents); }
                    catch (Exception ex) { Log.Error($"Commissary order {order.Id}: paid, but its delivery fee wasn't recorded for Econoview: {ex}"); }
                }
                else
                {
                    state.Commissary.Charges.Add(new CommissaryCharge
                    {
                        CommissaryId = siteId, Day = today, Cents = order.TotalCents, OrderId = order.Id, FeeCents = order.FeeCents
                    });
                    unpaid = true;
                }
                var boxes = quote.Lines.FirstOrDefault(l => l.ItemName == order.ItemName)?.Boxes ?? 0;
                Log.Info($"Commissary order {order.Id}: {boxes} boxes, {order.Units} {order.ItemName} at {siteId} for {CartSelling.Money(order.GoodsCents)} " +
                         $"plus {CartSelling.Money(order.FeeCents)} delivery, {(paid ? "paid" : "unpaid (not enough money), held until paid")}; " +
                         $"due day {order.DeliverDay} at {CityPitches.Clock(rules.DeliveryMinute)}.");
                placed.Add((order, paid));
            }
            // Core marks the unpaid orders as waiting for their payment.
            if (unpaid) Deliver(state, rules, day, minute);
            return placed;
        }

        // ---- The uninstall step ----

        // Stock and orders are paid back (goods and the fees of orders not delivered), debts the game takes now are paid,
        // the rest dropped, and the contacts go with the mod.
        public static void RemoveForUninstall()
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state != null && rules != null && FoodTrucksSession.CanWrite)
            {
                var commissary = state.Commissary;
                var spaces = commissary.Spaces.Count;
                // Rent up to today belongs to the vehicles' time there.
                try { ChargeRent(state, rules); }
                catch (Exception ex) { Log.Error("Uninstall: commissary rent: " + ex); }
                var refund = Commissaries.Refund(commissary);
                var paidBack = false;
                if (refund.Total > 0)
                {
                    // The game's tax tracking ignores money coming in, so the deduction is taken back by hand below.
                    var info = new TransactionInfo("foodtrucks:transaction_commissary_refund", new Dictionary<string, string>());
                    paidBack = Payments.PayIn(refund.Total, info);
                    if (paidBack)
                    {
                        SalesLedger.RecordDeliveryFees(state, SaveGameManager.Current.Day, -refund.FeeCents);
                        var reversed = ReverseDeduction(refund.Total);
                        Log.Info($"Uninstall: {CartSelling.Money(reversed)} of this tax year's commissary deduction taken back for the refund.");
                    }
                    else Log.Error($"Uninstall: the commissary refund of {CartSelling.Money(refund.Total)} was not paid.");
                }
                // Rent and fines still owed: paid now if the game takes them, else dropped.
                PayPending(state, rules, showNotification: false);
                if (commissary.Charges.Count > 0 || commissary.Fines.Count > 0)
                    Log.Error($"Uninstall: {commissary.Charges.Count} commissary charges and {commissary.Fines.Count} fines unpaid, dropped.");
                commissary.Charges.Clear();
                commissary.Fines.Clear();
                commissary.Unlocked.Clear();
                FoodTrucksSession.MarkChanged();
                Log.Info($"Uninstall: commissary refund {CartSelling.Money(paidBack ? refund.Total : 0)} (goods {CartSelling.Money(refund.GoodsCents)}, delivery fees {CartSelling.Money(refund.FeeCents)}); {spaces} spaces given up.");
                if (paidBack) CartSelling.Notify("foodtrucks:notification_uninstall_commissary", ("refund", CartSelling.Money(refund.Total)));
            }
            uninstalled = true;
            CartButton.ShowCommissary(false);
            CommissaryOrderForm.Forget(Sites?.Sites.Select(s => ContactPrefix + s.Id) ?? Enumerable.Empty<string>());
            var removed = SaveGameManager.Current?.Contacts.RemoveAll(c => c != null && c.id != null && c.id.StartsWith(ContactPrefix, StringComparison.Ordinal)) ?? 0;
            if (removed > 0) Log.Info($"Uninstall: {removed} commissary contacts removed.");
        }

        // Takes a refunded amount off this tax year's deductions under the commissary label (TaxHelper keeps them per label
        // and values in currentTaxPeriodDeductibleExpenses; it only ever adds). Never below 0; returns the cents taken.
        // Rent shares the label, so an order paid in an earlier tax year can take this year's rent deduction down.
        internal static long ReverseDeduction(long cents)
        {
            var expenses = SaveGameManager.Current?.currentTaxPeriodDeductibleExpenses;
            if (expenses == null || cents <= 0) return 0;
            var left = cents / 100.0;
            foreach (var expense in expenses.Where(e => e != null && e.key == ExpenseLabel && e.amount > 0f))
            {
                var take = Math.Min(left, expense.amount);
                expense.amount = (float)Math.Max(0.0, expense.amount - take);
                left -= take;
                if (left <= 0) break;
            }
            return (long)Math.Round(cents - Math.Max(0.0, left) * 100.0);
        }

        // ---- Debug ----

        // Where each commissary's bay is from the player and the vehicle they are in, and the nearest one.
        public static void WhereForTesting()
        {
            var sites = Sites;
            var rules = Rules;
            if (sites == null || rules == null)
            {
                CartSelling.Notify("foodtrucks:notification", ("message", "Commissaries not loaded; see Player.log."));
                return;
            }
            var player = PlayerHelper.GetPosition();
            var cart = VehicleHelper.GetCurrentVehicle();
            var vehicle = cart != null && FoodTrucksMod.KindOf(cart.vehicleTypeName) != null ? PositionOf(cart) : (Vector3?)null;
            var from = vehicle ?? player;
            var nearest = sites.Sites.OrderBy(s => CommissaryTables.Distance(Bays(s), from.x, from.z)).First();
            foreach (var site in sites.Sites)
            {
                var bay = Bays(site)?.FirstOrDefault();
                Log.Info($"Debug: commissary {site.Id} ({site.Address}) bay {(bay == null ? "not found" : $"x {bay.Value.X:F1}, z {bay.Value.Z:F1}")}, " +
                         $"{CommissaryTables.Distance(Bays(site), player.x, player.z):F0} m from the player" +
                         (vehicle != null ? $", {CommissaryTables.Distance(Bays(site), vehicle.Value.x, vehicle.Value.z):F0} m from the vehicle" : "") + ".");
            }
            var distance = CommissaryTables.Distance(Bays(nearest), from.x, from.z);
            Log.Info($"Debug: at x {from.x:F1}, z {from.z:F1}; nearest commissary {nearest.Id}, {distance:F0} m (bay radius {rules.BayRadiusMetres} m).");
            CartSelling.Notify("foodtrucks:notification", ("message", $"Nearest commissary: {Name(nearest)}, {distance:F0} m ({(distance <= rules.BayRadiusMetres ? "in the bay" : "outside the bay")})."));
        }

        // Orders paid for arrive now, as if it were their 08:00 (throwaway saves).
        public static void DeliverForTesting()
        {
            var state = FoodTrucksSession.CurrentState;
            var rules = Rules;
            if (state == null || rules == null || !FoodTrucksSession.CanWrite) return;
            var now = HireCrew.Now();
            var day = (int)(now / 1440) + 1;
            var minute = (int)(now % 1440);
            var due = minute >= rules.DeliveryMinute ? day : day - 1;
            var count = 0;
            foreach (var order in state.Commissary.Orders.Where(o => !o.AwaitingPayment && o.DeliverDay > due))
            {
                order.DeliverDay = due;
                count++;
            }
            // Delivered (and so removed) in the same step, before any save could see the moved days.
            Deliver(state, rules, day, minute);
            Log.Info($"Debug: {count} commissary orders brought forward and delivered.");
            CartSelling.Notify("foodtrucks:notification", ("message", $"Debug: {count} commissary orders delivered now."));
        }

        // What the 04:00 check would say now, without fining anyone.
        public static void NightForTesting()
        {
            var state = FoodTrucksSession.CurrentState;
            var sites = Sites;
            if (state == null || sites == null || instance == null) return;
            var flags = instance.Flags(state, sites, Fleet(), HireCrew.Now());
            var fined = flags.Count(v => !(v.PlayerInside || v.OnLateStop || v.InTransit || v.AtPlayerProperty || (v.AtOwnCommissaryBay && state.Commissary.Spaces.Any(s => s.VehicleId == v.VehicleId))));
            var message = $"Overnight check now (dry run): {(flags.Count == 0 ? "no food vehicles" : string.Join("; ", flags.Select(Describe)))}; {fined} would be fined.";
            Log.Info("Debug: " + message);
            CartSelling.Notify("foodtrucks:notification", ("message", message));
        }

        private static Commissary? instance;
        private void OnEnable() => instance = this;

        private void OnDisable()
        {
            if (instance == this) instance = null;
        }
    }

    // A commissary's call, all in its conversation: what it holds for you, then "Order stock" opens the order form
    // (CommissaryOrderForm), the commissary reads the order back with its total, and "Confirm order" places it.
    internal sealed class CommissaryDialog : Dialog
    {
        public CommissaryDialog()
        {
            try { DialogController.current.ShowEntry(Start()); }
            catch (Exception ex)
            {
                Log.Error("Commissary call: " + ex);
                try { DialogController.current?.CancelDialog(); }
                catch (Exception inner) { Log.Error("Commissary call, hanging up: " + inner); }
            }
        }

        private static DialogController Current => DialogController.current;

        private static Func<DialogEntry> Guard(Func<DialogEntry?> step) => () =>
        {
            try { return step()!; }
            catch (Exception ex)
            {
                Log.Error("Commissary call: " + ex);
                try { return Closing("foodtrucks:dialog_commissary_unavailable", null); }
                catch (Exception inner)
                {
                    Log.Error("Commissary call, closing: " + inner);
                    return null!;
                }
            }
        };

        // Hanging up, from any step.
        private static void HangUp()
        {
            CommissaryOrderForm.End();
            Current?.CancelDialog();
        }

        private static TextMessage HangUpMessage() => new TextMessage("ba:messagetype_contacts_message_player_cancel_call");

        // The commissary this contact belongs to.
        private static CommissarySite? Site()
        {
            var id = Current?.contact?.id;
            if (id == null || !id.StartsWith(Commissary.ContactPrefix, StringComparison.Ordinal)) return null;
            return DemandData.Commissaries?.Site(id.Substring(Commissary.ContactPrefix.Length));
        }

        // Null when this commissary takes an order now, else the closing line saying why not.
        private static DialogEntry? Refusal(CommissarySite? site)
        {
            var state = FoodTrucksSession.CurrentState;
            if (state == null || site == null || DemandData.Rules?.Commissary == null || !FoodTrucksSession.CanWrite || Commissary.Uninstalled)
                return Closing("foodtrucks:dialog_commissary_unavailable", null);
            if (!CommissaryOrdering.CanOrder(state.Commissary, site.Id))
                return Closing("foodtrucks:dialog_commissary_no_space", new { commissary = Commissary.Name(site) });
            if (!CommissaryOrderForm.Ready)
            {
                Log.Error("Commissary call: no order form (see the Commissary orders line at startup).");
                return Closing("foodtrucks:dialog_commissary_unavailable", null);
            }
            return null;
        }

        private static DialogEntry Start()
        {
            var site = Site();
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            var commissary = FoodTrucksSession.CurrentState!.Commissary;
            var data = new
            {
                commissary = Commissary.Name(site!),
                spaces = commissary.Spaces.Count(s => s.CommissaryId == site!.Id).ToString(),
                stock = (commissary.Stock.TryGetValue(site!.Id, out var lines) ? lines.Sum(l => (long)l.Units) : 0).ToString(),
                orders = commissary.Orders.Count(o => o.CommissaryId == site.Id).ToString()
            };
            Current.contact.SendMessage(new TextMessage("foodtrucks:dialog_commissary_start", ToDictionary(data), read: true, isNewInteraction: true));
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize("foodtrucks:dialog_commissary_start", data),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => Form(site.Id)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_order_stock", (object?)null),
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        // The game's phone order form, filled with what the commissary sells (CommissaryOrderForm).
        private static DialogEntry? Form(string siteId)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            var lines = Commissary.FormLines(FoodTrucksSession.CurrentState!, siteId);
            if (lines.Count == 0)
            {
                Log.Error($"Commissary order at {siteId}: nothing to sell (no catalogue item is a game item with a price).");
                return Closing("foodtrucks:dialog_commissary_nothing", null);
            }
            CommissaryOrderForm.Begin(Current.contact.id, siteId, lines);
            Log.Info($"Commissary order at {siteId}: the order form lists {lines.Count} items.");
            return new DialogEntry
            {
                headerKey = "foodtrucks:dialog_commissary_order_header",
                Template = DialogEntry.TemplateType.Input,
                InputTemplate = DialogEntry.InputTemplateName.FurnitureDeliverySettings,
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_order_check", (object?)null),
                OnConfirm = Guard(() => FormSet(siteId)),
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        // The form sent: the commissary reads the order back with its total. Nothing is booked or paid yet.
        private static DialogEntry? FormSet(string siteId)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            var rules = DemandData.Rules!.Commissary!;
            var form = Current.GetInputComponent<UI.Dialog.FurnitureDeliveryContractSettings>();
            var quote = CommissaryOrdering.Quote(rules, CommissaryOrderForm.Requests(form));
            switch (quote.Problem)
            {
                case CommissaryQuoteProblem.Empty:
                    CartSelling.Notify("foodtrucks:notification_commissary_order_empty");
                    return null;
                case CommissaryQuoteProblem.TooManyBoxes:
                    CartSelling.Notify("foodtrucks:notification_commissary_order_too_many", ("max", CommissaryOrdering.MaxBoxesPerOrder.ToString()));
                    return null;
            }
            // The order is read back as it stands now: the form and its item list are done with ("Change" opens a fresh one).
            CommissaryOrderForm.End();
            var items = string.Join("<br>", quote.Lines.Select(LineText));
            Current.contact.ReceivePlayerMessage(new TextMessage("foodtrucks:message_commissary_order_player", new Dictionary<string, string> { { "items", items } }, read: true));
            var data = new
            {
                items,
                goods = CartSelling.Money(quote.GoodsCents),
                fee = CartSelling.Money(quote.FeeCents),
                total = CartSelling.Money(quote.TotalCents),
                time = CityPitches.Clock(rules.DeliveryMinute)
            };
            Current.contact.SendMessage(new TextMessage("foodtrucks:dialog_commissary_quote", ToDictionary(data), read: true));
            Log.Info($"Commissary order at {siteId}: read back {quote.Boxes} boxes ({string.Join(", ", quote.Lines.Select(l => $"{l.Boxes} {l.ItemName}"))}), " +
                     $"{CartSelling.Money(quote.GoodsCents)} plus {CartSelling.Money(quote.FeeCents)} delivery.");
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize("foodtrucks:dialog_commissary_quote", data),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => Placed(siteId, quote)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_order_confirm", (object?)null),
                OnSecondOption = Guard(() => Form(siteId)),
                SecondOptionTextOverride = "foodtrucks:dialog_commissary_order_change",
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        // "3 boxes of Dumplings (240 items, $408 a box)"; equipment by the piece: "Griddle × 2, $2,000 each".
        private static string LineText(CommissaryQuoteLine line)
        {
            var piece = DemandData.Tables?.EquipmentForGameItem(line.ItemName) != null;
            var key = piece
                ? line.Boxes == 1 ? "foodtrucks:commissary_order_piece_one" : "foodtrucks:commissary_order_piece"
                : line.Boxes == 1 ? "foodtrucks:commissary_order_line_one" : "foodtrucks:commissary_order_line";
            return string.Format(LocalizorManager.GetLocalization(key, null),
                line.Boxes, LocalizorManager.GetLocalization(line.ItemName, null), line.Units, CartSelling.Money(line.BoxPriceCents));
        }

        // Confirmed: Core books the orders and the money moves; each order's text, as before, goes into the conversation.
        private static DialogEntry? Placed(string siteId, CommissaryQuote quote)
        {
            var site = DemandData.Commissaries?.Site(siteId);
            var refusal = Refusal(site);
            if (refusal != null) return refusal;
            var rules = DemandData.Rules!.Commissary!;
            var placed = Commissary.PlaceQuote(siteId, quote);
            if (placed.Count == 0) return Closing("foodtrucks:dialog_commissary_unavailable", null);
            var texts = new List<string>();
            foreach (var (order, paid) in placed)
            {
                var key = paid ? "foodtrucks:message_commissary_ordered" : "foodtrucks:message_commissary_ordered_unpaid";
                var data = new Dictionary<string, string>
                {
                    { "units", order.Units.ToString() },
                    { "item", LocalizorManager.GetLocalization(order.ItemName, null) },
                    { "total", CartSelling.Money(order.TotalCents) },
                    { "time", CityPitches.Clock(rules.DeliveryMinute) }
                };
                Current.contact.SendMessage(new TextMessage(key, data, read: true));
                texts.Add(LocalizorManager.Localize(key, data).ToString());
            }
            CommissaryOrderForm.End();
            return new DialogEntry
            {
                messageData = LocalizorManager.Localize("foodtrucks:dialog_commissary_text", new { text = string.Join("<br>", texts) }),
                Template = DialogEntry.TemplateType.Text,
                OnConfirm = Guard(() => Form(siteId)),
                ConfirmTextOverride = LocalizorManager.Localize("foodtrucks:dialog_commissary_order_more", (object?)null),
                OnCancel = HangUp,
                onCancelMessage = HangUpMessage()
            };
        }

        private static DialogEntry Closing(string key, object? data)
        {
            CommissaryOrderForm.End();
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
