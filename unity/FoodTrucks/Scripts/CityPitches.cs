#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using Entities;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Pitches;
using FoodTrucks.Core.Saves;
using Localizor;
using UnityEngine;

namespace FoodTrucks
{
    // Premium spots and the city's events on the street (design: Pitches). Core's PremiumSpots and CityEvents
    // decide; this finds the buildings, claims places as vehicles open, and sends the city's messages.
    internal static class CityPitches
    {
        private static PitchTables? Tables => DemandData.Pitches;

        // Once per city load: how many of the data file's addresses the city has.
        public static void LogFound()
        {
            var tables = Tables;
            if (tables == null) return;
            var spots = tables.Spots.Count(s => World.EntrancesOf(s.Address) != null);
            var places = tables.Places.Count(p => World.EntrancesOf(p.Address) != null);
            Log.Info($"Pitches: {spots} of {tables.Spots.Count} premium spots and {places} of {tables.Places.Count} event places found in the city." +
                     (spots < tables.Spots.Count || places < tables.Places.Count
                         ? " Missing: " + string.Join(", ", tables.Spots.Where(s => World.EntrancesOf(s.Address) == null).Select(s => s.Address)
                             .Concat(tables.Places.Where(p => World.EntrancesOf(p.Address) == null).Select(p => p.Address))) + "."
                         : ""));
        }

        public static PremiumSpot? PremiumAt(Vector3 position)
        {
            var tables = Tables;
            if (tables == null) return null;
            return PremiumSpots.At(tables, s => World.EntrancesOf(s.Address)?.Select(d => ((double)d.x, (double)d.z)), position.x, position.z);
        }

        // The event running at that minute within reach of the position, nearest place first.
        public static (CityEvent Event, EventPlace Place, EventKind Kind)? EventAt(SaveState state, float x, float z, long minute)
        {
            var tables = Tables;
            if (tables == null) return null;
            foreach (var city in state.Events.Events.Where(e => CityEvents.Running(e, minute))
                         .Select(e => (Event: e, Place: tables.Place(e.PlaceId), Kind: tables.Kind(e.Kind)))
                         .Where(e => e.Place != null && e.Kind != null)
                         .OrderBy(e => World.DistanceTo(e.Place!.Address, x, z)))
            {
                if (World.DistanceTo(city.Place!.Address, x, z) <= tables.EventRadiusMetres) return (city.Event, city.Place, city.Kind!);
                break;
            }
            return null;
        }

        // Before a window opens at a running event: null when it may, else the message saying why not. Only
        // vehicles with a booked place sell there while it runs.
        public static string? EventProblem(SaveState state, string vehicleId, Vector3 position, long minute)
        {
            var here = EventAt(state, position.x, position.z, minute);
            if (here == null) return null;
            var city = here.Value.Event;
            if (CityEvents.CanClaim(city, vehicleId, minute)) return null;
            Log.Info($"Open: {vehicleId} refused at the {city.Kind} at {city.PlaceId}: {city.Booked} places booked, held by {string.Join(", ", city.Holds.Select(h => h.VehicleId).Distinct())}.");
            return city.Booked == 0 ? "foodtrucks:notification_event_not_booked" : "foodtrucks:notification_event_places_taken";
        }

        // The window has opened: the premium spot and a running event's place go to it if they are free. Returns
        // what the player is told, or null.
        public static (string Key, (string Key, string Value)[] Data)? Opened(SaveState state, VehicleState vehicle, Vector3 position, long minute)
        {
            (string, (string, string)[])? message = null;
            var spot = PremiumAt(position);
            if (spot != null)
            {
                var (claim, holder) = PremiumSpots.Claim(state, vehicle, spot.Id, minute);
                Log.Info($"Premium spot {spot.Id}: {(claim == PremiumClaim.Held ? $"held by {vehicle.VehicleId}" : $"taken by {holder}, {vehicle.VehicleId} sells as on any pitch")}.");
                var holderKind = StreetSales.Find(state, holder)?.VehicleType;
                message = claim == PremiumClaim.Held
                    ? ("foodtrucks:notification_premium_held", new[] { ("spot", Text(spot.Name)) })
                    : ("foodtrucks:notification_premium_taken", new[] { ("spot", Text(spot.Name)), ("other", Text("foodtrucks:noun_" + (string.IsNullOrEmpty(holderKind) ? "cart" : holderKind))) });
            }
            else PremiumSpots.Release(state, vehicle, minute);
            var here = EventAt(state, position.x, position.z, minute);
            if (here != null && CityEvents.Claim(here.Value.Event, vehicle.VehicleId, minute))
            {
                Log.Info($"Event {here.Value.Event.Id} ({here.Value.Kind.Id} at {here.Value.Place.Id}): place held by {vehicle.VehicleId}.");
                message = ("foodtrucks:notification_event_place", new[] { ("event", Text(here.Value.Kind.Name)), ("place", Text(here.Value.Place.Name)), ("end", Clock(here.Value.Event.End)) });
            }
            FoodTrucksSession.MarkChanged();
            return message;
        }

        // Each sales step of an open window: an event starting while a vehicle is already open there gives it a booked
        // place if one is free. False when it has none: it must close, as it couldn't have opened there.
        public static bool Tick(SaveState state, VehicleState vehicle, Spot spot, long minute)
        {
            var here = EventAt(state, spot.X, spot.Z, minute);
            if (here == null || Holds.Holding(here.Value.Event.Holds, vehicle.VehicleId)) return true;
            if (!CityEvents.Claim(here.Value.Event, vehicle.VehicleId, minute))
            {
                Log.Info($"Event {here.Value.Event.Id} ({here.Value.Kind.Id} at {here.Value.Place.Id}) started: {vehicle.VehicleId} has no place and closes.");
                return false;
            }
            FoodTrucksSession.MarkChanged();
            Log.Info($"Event {here.Value.Event.Id} ({here.Value.Kind.Id} at {here.Value.Place.Id}) started: place held by {vehicle.VehicleId}, open there.");
            CartSelling.Say("foodtrucks:notification_event_place", string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType,
                ("event", Text(here.Value.Kind.Name)), ("place", Text(here.Value.Place.Name)), ("end", Clock(here.Value.Event.End)));
            return true;
        }

        // What the pitch adds at that minute: the premium spot the window holds, and a running event it has a
        // place at.
        public static PitchBonus BonusFor(SaveState? state, VehicleState? vehicle, Spot spot, long minute)
        {
            var tables = Tables;
            if (tables == null || state == null || vehicle == null || !vehicle.IsOpen) return PitchBonus.None;
            var bonus = PitchBonus.None;
            var premium = tables.Spot(vehicle.PremiumSpot);
            if (premium != null) bonus = bonus.And(PitchBonus.Of(premium));
            var here = EventAt(state, spot.X, spot.Z, minute);
            if (here != null && Holds.HeldBy(here.Value.Event.Holds, vehicle.VehicleId, minute)) bonus = bonus.And(PitchBonus.Of(here.Value.Kind));
            return bonus;
        }

        // A new day: the city announces what is coming, drops what is over and reminds the player of today's
        // bookings. Also once after a load, which only catches up.
        public static void NewDay(SaveState state, int today, bool loading)
        {
            var tables = Tables;
            if (tables == null || !FoodTrucksSession.CanWrite) return;
            // Nothing a window still has to sell through goes: a catch-up can be days behind after a long skip.
            var soldThrough = CartSelling.SoldThrough();
            var dropped = CityEvents.Expire(state.Events, today, soldThrough);
            PremiumSpots.Prune(state, soldThrough);
            if (loading)
            {
                // Places held by vehicles that aren't open any more go back.
                var now = HireCrew.Now();
                foreach (var holder in state.Events.Events.SelectMany(e => e.Holds).Concat(state.PremiumHolds.Values.SelectMany(h => h))
                             .Where(h => h.Until == null && StreetSales.Find(state, h.VehicleId)?.IsOpen != true).ToList())
                    holder.Until = Math.Max(holder.From, now);
                foreach (var vehicle in state.Vehicles.Where(v => !v.IsOpen && v.PremiumSpot != null)) vehicle.PremiumSpot = null;
                DropUnknown(state, tables, now);
            }
            // Only places the city has; before its buildings are there, nothing is announced (the day is caught up later).
            if (!tables.Places.Any(p => World.EntrancesOf(p.Address) != null)) return;
            var added = new CityEvents(tables).Announce(state.Events, today, p => World.EntrancesOf(p.Address) != null);
            // The city writes to vendors: players with a food cart or truck.
            var vendor = CityLicensing.FoodVehicles(state).Count > 0;
            foreach (var city in added)
            {
                var kind = tables.Kind(city.Kind)!;
                var place = tables.Place(city.PlaceId)!;
                Log.Info($"City event {city.Id}: {kind.Id} at {place.Id} ({place.Address}) on day {city.Day}, {Clock(city.Start)} to {Clock(city.End)}, {city.Places} places at {CartSelling.Money(city.FeeCents)}{(vendor ? "" : "; no text, no food vehicle")}.");
                if (!vendor) continue;
                GameManager.SendTextMessage(CityLicensing.City(), "foodtrucks:message_city_event", new Dictionary<string, string>
                {
                    { "event", Text(kind.Name) },
                    { "place", Text(place.Name) },
                    { "weekday", Weekday(city.Day) },
                    { "day", city.Day.ToString() },
                    { "start", Clock(city.Start) },
                    { "end", Clock(city.End) },
                    { "places", city.Places.ToString() },
                    { "fee", CartSelling.Money(city.FeeCents) }
                });
            }
            foreach (var city in state.Events.Events.Where(e => e.Day == today && e.Booked > 0 && !e.Reminded))
            {
                city.Reminded = true;
                var kind = tables.Kind(city.Kind);
                var place = tables.Place(city.PlaceId);
                if (kind == null || place == null) continue;
                GameManager.SendTextMessage(CityLicensing.City(), "foodtrucks:message_city_event_today", new Dictionary<string, string>
                {
                    { "event", Text(kind.Name) },
                    { "place", Text(place.Name) },
                    { "start", Clock(city.Start) },
                    { "end", Clock(city.End) },
                    { "booked", city.Booked.ToString() }
                });
            }
            if (added.Count > 0 || dropped > 0 || !loading) FoodTrucksSession.MarkChanged();
            if (added.Count > 0 || dropped > 0) Log.Info($"City events on day {today}: {added.Count} announced, {dropped} over; {state.Events.Events.Count} coming.");
        }

        // Events whose place or kind the data file no longer has (an edited pitches.json) can't be held: they go, and
        // places booked for one not over yet are paid back.
        private static void DropUnknown(SaveState state, PitchTables tables, long now)
        {
            foreach (var city in state.Events.Events.Where(e => tables.Place(e.PlaceId) == null || tables.Kind(e.Kind) == null).ToList())
            {
                state.Events.Events.Remove(city);
                var refund = now < CityEvents.EndsAt(city) ? city.Booked * city.FeeCents : 0;
                Log.Info($"City event {city.Id} ({city.Kind} at {city.PlaceId}) dropped: not in pitches.json; {city.Booked} places booked, {CartSelling.Money(refund)} paid back.");
                if (refund <= 0) continue;
                var info = new TransactionInfo("foodtrucks:transaction_event_refund", new Dictionary<string, string>());
                // Under the fees' label for the record; the game's tax tracking ignores money coming in, so the fee's
                // deduction stands (documented in the plan).
                info.SetTaxDeductibleName("foodtrucks:transaction_cityfees_label");
                GameManager.ChangeMoneySafe(refund / 100f, info, null, null, force: true, showNotification: true);
                GameManager.SendTextMessage(CityLicensing.City(), "foodtrucks:message_city_event_cancelled", new Dictionary<string, string> { { "fee", CartSelling.Money(refund) } });
            }
        }

        // Debug key for throwaway saves: an event at the nearest event place, running from now for three hours,
        // with one place booked free.
        public static void EventHereForTesting(Vector3 position)
        {
            var state = FoodTrucksSession.CurrentState;
            var tables = Tables;
            if (state == null || tables == null || !FoodTrucksSession.CanWrite) return;
            var place = tables.Places.OrderBy(p => World.DistanceTo(p.Address, position.x, position.z)).FirstOrDefault();
            if (place == null) return;
            var kind = tables.Kind(place.Kinds[0])!;
            var now = HireCrew.Now();
            var day = (int)(now / 1440) + 1;
            var start = (int)(now % 1440);
            var city = new CityEvent
            {
                Id = state.Events.NextId++, Kind = kind.Id, PlaceId = place.Id, AnnouncedDay = day, Day = day,
                Start = start, End = Math.Min(1439, start + 180), Places = kind.Places, FeeCents = kind.FeeCents, Booked = 1, Reminded = true
            };
            state.Events.Events.Add(city);
            FoodTrucksSession.MarkChanged();
            var distance = World.DistanceTo(place.Address, position.x, position.z);
            Log.Info($"Debug: event {city.Id} ({kind.Id}) at {place.Id} ({place.Address}, {distance:F0} m away) from {Clock(city.Start)} to {Clock(city.End)}, one place booked.");
            CartSelling.Notify("foodtrucks:notification_debug_event", ("event", Text(kind.Name)), ("place", Text(place.Name)), ("distance", distance.ToString("F0")), ("end", Clock(city.End)));
        }

        internal static string Clock(int minute) => $"{minute / 60 % 24:D2}:{minute % 60:D2}";

        internal static string Weekday(int day) => Text("foodtrucks:weekday_" + GameDays.Weekday(day).ToString().ToLowerInvariant());

        internal static string Text(string key) => LocalizorManager.GetLocalization(key, null);
    }
}
