#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
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
            if (city.Vehicles.Contains(vehicleId) || city.Vehicles.Count < city.Booked) return null;
            Log.Info($"Open: {vehicleId} refused at the {city.Kind} at {city.PlaceId}: {city.Booked} places booked, held by {string.Join(", ", city.Vehicles)}.");
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
                var (claim, holder) = PremiumSpots.Claim(state, vehicle, spot.Id);
                Log.Info($"Premium spot {spot.Id}: {(claim == PremiumClaim.Held ? $"held by {vehicle.VehicleId}" : $"taken by {holder}, {vehicle.VehicleId} sells as on any pitch")}.");
                var holderKind = StreetSales.Find(state, holder)?.VehicleType;
                message = claim == PremiumClaim.Held
                    ? ("foodtrucks:notification_premium_held", new[] { ("spot", Text(spot.Name)) })
                    : ("foodtrucks:notification_premium_taken", new[] { ("spot", Text(spot.Name)), ("other", Text("foodtrucks:noun_" + (string.IsNullOrEmpty(holderKind) ? "cart" : holderKind))) });
            }
            else PremiumSpots.Release(vehicle);
            var here = EventAt(state, position.x, position.z, minute);
            if (here != null && CityEvents.Claim(here.Value.Event, vehicle.VehicleId))
            {
                Log.Info($"Event {here.Value.Event.Id} ({here.Value.Kind.Id} at {here.Value.Place.Id}): place held by {vehicle.VehicleId}.");
                message = ("foodtrucks:notification_event_place", new[] { ("event", Text(here.Value.Kind.Name)), ("place", Text(here.Value.Place.Name)), ("end", Clock(here.Value.Event.End)) });
            }
            FoodTrucksSession.MarkChanged();
            return message;
        }

        // Each sales step of an open window: an event starting while a booked vehicle is already open there gives
        // it its place.
        public static void Tick(SaveState state, VehicleState vehicle, Spot spot, long minute)
        {
            var here = EventAt(state, spot.X, spot.Z, minute);
            if (here == null || here.Value.Event.Vehicles.Contains(vehicle.VehicleId)) return;
            if (!CityEvents.Claim(here.Value.Event, vehicle.VehicleId)) return;
            FoodTrucksSession.MarkChanged();
            Log.Info($"Event {here.Value.Event.Id} ({here.Value.Kind.Id} at {here.Value.Place.Id}) started: place held by {vehicle.VehicleId}, open there.");
            CartSelling.Say("foodtrucks:notification_event_place", string.IsNullOrEmpty(vehicle.VehicleType) ? "cart" : vehicle.VehicleType,
                ("event", Text(here.Value.Kind.Name)), ("place", Text(here.Value.Place.Name)), ("end", Clock(here.Value.Event.End)));
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
            if (here != null && here.Value.Event.Vehicles.Contains(vehicle.VehicleId)) bonus = bonus.And(PitchBonus.Of(here.Value.Kind));
            return bonus;
        }

        // A new day: the city announces what is coming, drops what is over and reminds the player of today's
        // bookings. Also once after a load, which only catches up.
        public static void NewDay(SaveState state, int today, bool loading)
        {
            var tables = Tables;
            if (tables == null || !FoodTrucksSession.CanWrite) return;
            var dropped = CityEvents.Expire(state.Events, today);
            var added = new CityEvents(tables).Announce(state.Events, today);
            foreach (var city in added)
            {
                var kind = tables.Kind(city.Kind)!;
                var place = tables.Place(city.PlaceId)!;
                Log.Info($"City event {city.Id}: {kind.Id} at {place.Id} ({place.Address}) on day {city.Day}, {Clock(city.Start)} to {Clock(city.End)}, {city.Places} places at {CartSelling.Money(city.FeeCents)}.");
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
