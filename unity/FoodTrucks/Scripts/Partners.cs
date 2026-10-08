#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Pitches;
using FoodTrucks.Core.Saves;
using UnityEngine;

namespace FoodTrucks
{
    // Partner deals between a vehicle and one of the player's own venues (design: Partnerships with your own
    // businesses). Core's Partnerships decides; this finds the venues, counts the minutes worked outside them and
    // gives the venue its satisfaction bonus through the game's daily update.
    internal static class Partners
    {
        private static PartnershipRules? Rules => DemandData.Pitches?.Partnerships;

        // The player's venues of a partner kind within reach of the position, nearest first.
        public static List<(string Address, BuildingRegistration Registration, VenueKind Kind, double Distance)> VenuesNear(Vector3 position)
        {
            var list = new List<(string, BuildingRegistration, VenueKind, double)>();
            var rules = Rules;
            var live = DemandData.Rules?.LiveCrowds;
            if (rules == null || live == null || SaveGameManager.Current == null) return list;
            foreach (var pair in World.Registrations())
            {
                var registration = pair.Value;
                if (!registration.RentedByPlayer) continue;
                var kind = live.KindOf(registration.businessTypeName);
                if (kind == VenueKind.Other || !rules.Venues.Contains(kind)) continue;
                var address = World.AddressKey(pair.Key.Item1, pair.Key.Item2);
                var distance = World.DistanceTo(address, position.x, position.z);
                if (distance <= rules.RadiusMetres) list.Add((address, registration, kind, distance));
            }
            return list.OrderBy(v => v.Item4).ThenBy(v => v.Item1, StringComparer.Ordinal).ToList();
        }

        public static string? VenueOf(string vehicleId) => Partnerships.Of(FoodTrucksSession.CurrentState!, vehicleId)?.Venue;

        // The venue's name as the game shows it, else its address.
        public static string Name(string address)
        {
            var registration = Registration(address);
            if (registration != null && !string.IsNullOrEmpty(registration.BusinessName)) return registration.BusinessName;
            var (street, number) = PitchTables.SplitAddress(address);
            return $"{number} {CityPitches.Text(street)}";
        }

        private static BuildingRegistration? Registration(string address)
        {
            if (SaveGameManager.Current == null) return null;
            var (street, number) = PitchTables.SplitAddress(address);
            return World.Registrations().TryGetValue((street, number), out var registration) ? registration : null;
        }

        // Partnerships whose vehicle is gone, or whose venue is no longer the player's partner kind, end with a
        // message. At load and at each new day.
        public static void Prune(SaveState state)
        {
            var rules = Rules;
            var live = DemandData.Rules?.LiveCrowds;
            if (rules == null || live == null || !FoodTrucksSession.CanWrite || state.Partnerships.Count == 0) return;
            var vehicles = new HashSet<string>(SaveGameManager.Current.VehicleInstances.Where(v => v != null).Select(v => v.id), StringComparer.Ordinal);
            var ended = new List<PartnershipState>();
            Partnerships.Prune(state, p =>
            {
                var registration = Registration(p.Venue);
                var keep = vehicles.Contains(p.VehicleId) && registration != null && registration.RentedByPlayer &&
                           live.KindOf(registration.businessTypeName) is var kind && kind != VenueKind.Other && rules.Venues.Contains(kind);
                if (!keep) ended.Add(p);
                return keep;
            });
            foreach (var p in ended)
            {
                Log.Info($"Partnership: {p.VehicleId} and {p.Venue} ended (vehicle or venue gone).");
                CartSelling.Notify("foodtrucks:notification_partner_ended", ("venue", Name(p.Venue)));
            }
            if (ended.Count > 0) FoodTrucksSession.MarkChanged();
        }

        public static void Sign(SaveState state, VehicleState vehicle, string venue, string kind)
        {
            var day = SaveGameManager.Current.Day;
            var problem = Partnerships.Sign(state, vehicle.VehicleId, venue, day);
            if (problem != PartnerProblem.None)
            {
                Log.Info($"Partnership: {vehicle.VehicleId} with {venue} refused: {problem}.");
                return;
            }
            FoodTrucksSession.MarkChanged();
            Log.Info($"Partnership: {kind} {vehicle.VehicleId} signed with {Name(venue)} ({venue}) on day {day}.");
            CartSelling.Say("foodtrucks:notification_partner_signed", kind, ("venue", Name(venue)));
        }

        public static void End(SaveState state, VehicleState vehicle, string kind)
        {
            var venue = VenueOf(vehicle.VehicleId);
            if (venue == null || !Partnerships.End(state, vehicle.VehicleId)) return;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Partnership: {kind} {vehicle.VehicleId} and {venue} ended by the player.");
            CartSelling.Say("foodtrucks:notification_partner_ended_player", kind, ("venue", Name(venue)));
        }

        // Minutes someone worked the window: those outside the partner venue count towards its day.
        public static void Worked(SaveState state, VehicleState vehicle, Spot spot, long from, int minutes)
        {
            var rules = Rules;
            var partnership = Partnerships.Of(state, vehicle.VehicleId);
            if (rules == null || partnership == null || minutes <= 0) return;
            if (World.DistanceTo(partnership.Venue, spot.X, spot.Z) > rules.RadiusMetres) return;
            var before = partnership.LastWorkedDay;
            Partnerships.Worked(partnership, rules, (int)(from / 1440) + 1, minutes);
            if (partnership.LastWorkedDay != before)
                Log.Info($"Partnership: {vehicle.VehicleId} worked day {partnership.LastWorkedDay} outside {partnership.Venue}.");
        }

        // Harmony, around BusinessHelper.UpdateSatisfaction: when the game wrote a new satisfaction for a partner
        // venue whose vehicle worked outside it yesterday or today, its facility part gets the bonus.
        public static void SatisfactionPrefix(BuildingRegistration registration, out Satisfaction? __state) =>
            __state = registration?.satisfaction;

        public static void SatisfactionPostfix(BuildingRegistration registration, Satisfaction? __state)
        {
            try
            {
                var state = FoodTrucksSession.CurrentState;
                var rules = Rules;
                if (state == null || rules == null || registration?.satisfaction == null || ReferenceEquals(registration.satisfaction, __state)) return;
                var address = registration.Address;
                if (address == null || !registration.RentedByPlayer) return;
                var partnership = Partnerships.ForVenue(state, World.AddressKey(address.streetName, address.streetNumber));
                var today = SaveGameManager.Current.Day;
                if (partnership == null || !Partnerships.BonusDue(partnership, today)) return;
                // Only a facility score the game worked out from paying customers: with none, it copies the old one,
                // bonus included, and another bonus would stack on it.
                if (registration.unprocessedCompletedOrders == null || !registration.unprocessedCompletedOrders.Exists(o =>
                        o != null && o.completed && o.timestamp != null && o.timestamp.Day >= today - 1 && o.entries.Exists(e => e.paid))) return;
                var s = registration.satisfaction;
                var (facility, overall) = Partnerships.WithBonus(s.customerService, s.pricing, s.cleanliness, s.facility, rules);
                Log.Info($"Partnership: {partnership.Venue} facility satisfaction {s.facility} -> {facility}, overall {s.overall} -> {overall} ({partnership.VehicleId} worked day {partnership.LastWorkedDay}).");
                s.facility = facility;
                s.overall = overall;
                Partnerships.BonusGiven(partnership);
                FoodTrucksSession.MarkChanged();
            }
            catch (Exception ex) { Log.Error("Partnership satisfaction: " + ex); }
        }
    }
}
