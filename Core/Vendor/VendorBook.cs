using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Catering;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Vendor
{
    // Who sells at each opening: Street Food's own customers, or an owner (another mod) through VendorApi.Sell.
    // An owner asks for a vehicle; the next crew opening that starts in a later game hour, while the owner's lease
    // holds, is the owner's for as long as it stays open, the lease holds and the owner wants it. Every other opening
    // is Street Food's. With no owner ever (SaveState.Vendor null) nothing here writes anything.
    // Minutes are absolute game minutes, (day - 1) × 1440 + minute of the day.
    public static class VendorBook
    {
        public const int LeaseMinutes = 60;
        public const int MaxOwners = 8;
        public const int MaxVehicles = 64;
        public const int MaxItems = 8;
        public const int MaxVehicleIdLength = 128;
        public const int MaxOwnerLength = 64;
        // The save's JSON name of the block (SaveState.Vendor).
        public const string SaveName = "vendor";

        // Lower-case letters, digits, '.', '_' and '-', 1-64 of them.
        public static bool ValidOwner(string? owner) =>
            !string.IsNullOrEmpty(owner) && owner!.Length <= MaxOwnerLength &&
            owner.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-');

        public static bool ValidVehicleId(string? id) =>
            !string.IsNullOrEmpty(id) && id!.Length <= MaxVehicleIdLength && !id.Any(char.IsControl);

        // A vendor block written by a newer Street Food is kept unread in the save: nothing may write a new one over it.
        public static bool Locked(SaveState state) =>
            state.Vendor == null && state.ExtensionData != null && state.ExtensionData.ContainsKey(SaveName);

        public static VendorVehicle? Entry(SaveState state, string vehicleId) =>
            state.Vendor?.Vehicles.FirstOrDefault(v => string.Equals(v.VehicleId, vehicleId, StringComparison.Ordinal));

        public static VendorLease? Lease(SaveState state, string owner) =>
            state.Vendor?.Leases.FirstOrDefault(l => string.Equals(l.Owner, owner, StringComparison.Ordinal));

        public static VendorRequest? Request(SaveState state, string vehicleId) =>
            state.Vendor?.Requests.FirstOrDefault(r => string.Equals(r.VehicleId, vehicleId, StringComparison.Ordinal));

        public static bool LeaseHolds(SaveState state, string owner, long minute) => Lease(state, owner) is { } lease && minute < lease.Until;

        // The block, made on an owner's first call. An already open vehicle's running opening keeps ID 0: never granted.
        public static VendorState Ensure(SaveState state)
        {
            if (Locked(state)) throw new InvalidOperationException("The save holds vendor data from a newer Street Food version.");
            return state.Vendor ??= new VendorState();
        }

        // Records (want) or withdraws an owner's wish for a vehicle at game minute `now`. A repeated wish keeps the first
        // hour. Withdrawing hands an open opening of that owner back to Street Food at once.
        public static bool Ask(SaveState state, string owner, string vehicleId, bool want, long now, out string reason, ICollection<string>? notes = null)
        {
            reason = string.Empty;
            if (!want)
            {
                if (state.Vendor == null) return true;
                state.Vendor.Requests.RemoveAll(r => r.Owner == owner && r.VehicleId == vehicleId);
                if (Entry(state, vehicleId) is { } entry && entry.Owner == owner)
                    HandBack(state, entry, now, "the owner released it", notes);
                return true;
            }
            var book = Ensure(state);
            var request = Request(state, vehicleId);
            if (request != null && request.Owner != owner)
            {
                reason = "requested by another owner";
                return false;
            }
            if (request != null) return true;
            if (book.Requests.Count(r => r.Owner == owner) >= MaxVehicles)
            {
                reason = "too many requests";
                return false;
            }
            book.Requests.Add(new VendorRequest { Owner = owner, VehicleId = vehicleId, Hour = Hour(now) });
            return true;
        }

        // Extends the owner's lease to LeaseMinutes after `minute`. A lease that ran out first hands its openings back.
        public static long Renew(SaveState state, string owner, long minute, long now, out string reason, ICollection<string>? notes = null)
        {
            reason = string.Empty;
            var book = Ensure(state);
            Expire(state, now, notes);
            var lease = Lease(state, owner);
            if (lease == null)
            {
                if (book.Leases.Count >= MaxOwners)
                {
                    reason = "too many owners";
                    return -1;
                }
                book.Leases.Add(lease = new VendorLease { Owner = owner });
            }
            lease.Until = Math.Max(lease.Until, checked(minute + LeaseMinutes));
            return lease.Until;
        }

        // Drops the owner's lease and requests; every opening it holds goes back to Street Food.
        public static void ReleaseAll(SaveState state, string owner, long now, ICollection<string>? notes = null)
        {
            var book = state.Vendor;
            if (book == null) return;
            foreach (var entry in book.Vehicles.Where(v => v.Owner == owner).ToList())
                HandBack(state, entry, now, "the owner released all", notes);
            book.Requests.RemoveAll(r => r.Owner == owner);
            book.Leases.RemoveAll(l => l.Owner == owner);
        }

        // Leases over by `now` end; their openings go back to Street Food at the minute each lease ran out.
        public static int Expire(SaveState state, long now, ICollection<string>? notes = null)
        {
            var book = state.Vendor;
            if (book == null) return 0;
            var handed = 0;
            foreach (var lease in book.Leases.Where(l => l.Until <= now).ToList())
            {
                foreach (var entry in book.Vehicles.Where(v => v.Owner == lease.Owner).ToList())
                    if (HandBack(state, entry, Math.Max(entry.OpenedAt, lease.Until), "its lease ran out", notes)) handed++;
                book.Leases.Remove(lease);
            }
            return handed;
        }

        // A vehicle's window opens at `minute` (byCrew: a hire opened it at a route stop). Gives the opening its ID and
        // its owner. Returns that owner, "" for Street Food. Nothing happens without a vendor block.
        public static string Opened(SaveState state, string vehicleId, long minute, bool byCrew, ICollection<string>? notes = null)
        {
            var book = state.Vendor;
            if (book == null) return string.Empty;
            var entry = Entry(state, vehicleId);
            if (entry == null) book.Vehicles.Add(entry = new VendorVehicle { VehicleId = vehicleId });
            // Rising with the clock too, so IDs stay unique even if the counter were ever lost.
            entry.OpeningId = Math.Max(checked(entry.LastOpeningId + 1), checked((Math.Max(0, minute) + 1) * 16));
            entry.LastOpeningId = entry.OpeningId;
            entry.OpenedAt = minute;
            entry.Owner = string.Empty;
            var request = Request(state, vehicleId);
            if (request == null) return string.Empty;
            string? refusal = null;
            if (!byCrew) refusal = "the player opened it";
            else if (EventOrCatering(state, vehicleId)) refusal = "it is at a city event or a catering gig";
            else if (Hour(minute) <= request.Hour) refusal = "it opened in the hour of the request";
            else if (!LeaseHolds(state, request.Owner, minute)) refusal = "the owner's lease does not hold";
            if (refusal != null)
            {
                notes?.Add($"Vendor API: {vehicleId} opening {entry.OpeningId} stays with Street Food ({refusal}); requested by {request.Owner}.");
                return string.Empty;
            }
            entry.Owner = request.Owner;
            notes?.Add($"Vendor API: {vehicleId} opening {entry.OpeningId} at {Describe(minute)} granted to {entry.Owner}.");
            return entry.Owner;
        }

        // The window closed: its opening has no owner any more.
        public static void Closed(SaveState state, string vehicleId)
        {
            if (Entry(state, vehicleId) is { } entry) entry.Owner = string.Empty;
        }

        // Whether Street Food's own customers come to this open window at `minute`. False only while an owner holds the
        // opening; a lease that ran out, or a city event or catering gig taking the vehicle, hands it back here first.
        public static bool OwnCustomers(SaveState state, VehicleState vehicle, long minute, ICollection<string>? notes = null) =>
            OwnerAt(state, vehicle, minute, notes) == null;

        // The owner holding this open window at `minute`, or null for Street Food.
        public static string? OwnerAt(SaveState state, VehicleState vehicle, long minute, ICollection<string>? notes = null)
        {
            if (state.Vendor == null) return null;
            var entry = Entry(state, vehicle.VehicleId);
            if (entry == null || entry.Owner.Length == 0) return null;
            string? why = null;
            if (!vehicle.IsOpen) why = "it closed";
            else if (!LeaseHolds(state, entry.Owner, minute)) why = "its lease ran out";
            else if (EventOrCatering(state, vehicle.VehicleId)) why = "it is at a city event or a catering gig";
            if (why == null) return entry.Owner;
            HandBack(state, entry, minute, why, notes);
            return null;
        }

        // Street Food takes an opening back at `minute`: its own customers start from then, with a fresh queue.
        internal static bool HandBack(SaveState state, VendorVehicle entry, long minute, string why, ICollection<string>? notes)
        {
            if (entry.Owner.Length == 0) return false;
            var owner = entry.Owner;
            entry.Owner = string.Empty;
            var vehicle = StreetSales.Find(state, entry.VehicleId);
            if (vehicle != null && vehicle.IsOpen) vehicle.Service = SalesSimulator.Open(SeededRandom.SeedFor(entry.VehicleId, minute));
            notes?.Add($"Vendor API: {entry.VehicleId} opening {entry.OpeningId} handed back from {owner} to Street Food at {Describe(minute)} ({why}).");
            return true;
        }

        // The vehicle holds a place at a running city event, or is at a catering gig that has started.
        public static bool EventOrCatering(SaveState state, string vehicleId) =>
            state.Events?.Events?.Any(e => e?.Holds?.Any(h => h != null && h.VehicleId == vehicleId && h.Until == null) == true) == true ||
            state.Catering?.Gigs?.Any(g => g != null && g.VehicleId == vehicleId && g.Status == GigStatus.Started) == true;

        // The planned end of a crew opening that started at `openedAt`: the route's first open leg for the stop the crew
        // opened at that ends after it. Null when the player opened it or no plan covers it.
        public static long? PlannedClose(VehicleState vehicle, long openedAt)
        {
            if (!vehicle.IsOpen || vehicle.HireStop == null || vehicle.Shifts == null) return null;
            var leg = vehicle.Shifts.Where(p => p?.Legs != null).SelectMany(p => p.Legs)
                .Where(l => l != null && l.Kind == LegKind.Open && l.StopIndex == vehicle.HireStop && l.End > openedAt)
                .OrderBy(l => l.Start).FirstOrDefault();
            return leg?.End;
        }

        public static long Hour(long minute) => (long)Math.Floor(minute / 60.0);
        public static int DayOf(long minute) => GameDays.DayOf(minute);
        public static int MinuteOf(long minute) => (int)(minute - (DayOf(minute) - 1) * 1440L);
        public static long Absolute(int day, int minute) => (day - 1) * 1440L + minute;

        private static string Describe(long minute) => $"day {DayOf(minute)} {MinuteOf(minute) / 60:D2}:{MinuteOf(minute) % 60:D2}";

        // Load: the block is read apart from the rest, so a bad one never makes the whole save unreadable. A newer
        // layout is kept as it is (back in ExtensionData after the state is read) and the API stays refused; a bad
        // one is dropped, which hands every vehicle to Street Food.
        public static JToken? TakeOut(JObject document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            JToken? taken = null;
            foreach (var property in document.Properties().Where(p => string.Equals(p.Name, SaveName, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                property.Remove();
                if (property.Value.Type != JTokenType.Null) taken = property.Value;
            }
            return taken;
        }

        public static void PutBack(SaveState state, JToken? raw, JsonSerializer serializer, ICollection<string> notes)
        {
            state.Vendor = null;
            if (raw == null) return;
            try
            {
                if (!(raw is JObject block)) throw new JsonSerializationException("not an object");
                var version = block["version"];
                if (version == null || version.Type != JTokenType.Integer || version.Value<long>() < 1)
                    throw new JsonSerializationException("missing or invalid version");
                if (version.Value<long>() > VendorState.CurrentVersion)
                {
                    state.ExtensionData ??= new Dictionary<string, JToken>();
                    state.ExtensionData[SaveName] = raw;
                    notes.Add($"Vendor API: the save's vendor data is from a newer Street Food (layout {version}); kept unchanged, the API stays refused.");
                    return;
                }
                var vendor = block.ToObject<VendorState>(serializer) ?? throw new JsonSerializationException("empty");
                Validate(vendor);
                state.Vendor = vendor;
                // An owner only holds an open window; one left on a closed vehicle is cleared.
                foreach (var entry in vendor.Vehicles.Where(v => v.Owner.Length > 0))
                    if (StreetSales.Find(state, entry.VehicleId)?.IsOpen != true) entry.Owner = string.Empty;
            }
            catch (Exception ex) when (ex is JsonException || ex is OverflowException || ex is FormatException || ex is InvalidCastException || ex is ArgumentException)
            {
                notes.Add($"Vendor API: the save's vendor data was unreadable ({ex.Message}); dropped, every vehicle stays with Street Food.");
            }
        }

        public static void Validate(VendorState vendor)
        {
            if (vendor.Leases == null || vendor.Requests == null || vendor.Vehicles == null)
                throw new JsonSerializationException("Vendor leases, requests and vehicles must be lists.");
            if (vendor.Leases.Count > MaxOwners || vendor.Leases.Any(l => l == null || !ValidOwner(l.Owner) || l.Until < 0) ||
                vendor.Leases.Select(l => l.Owner).Distinct(StringComparer.Ordinal).Count() != vendor.Leases.Count)
                throw new JsonSerializationException("Vendor leases must be one per named owner.");
            if (vendor.Requests.Count > MaxOwners * MaxVehicles || vendor.Requests.Any(r => r == null || !ValidOwner(r.Owner) || !ValidVehicleId(r.VehicleId) || r.Hour < 0) ||
                vendor.Requests.Select(r => r.VehicleId).Distinct(StringComparer.Ordinal).Count() != vendor.Requests.Count)
                throw new JsonSerializationException("Vendor requests must be one per vehicle, each naming its owner.");
            if (vendor.Vehicles.Any(v => v == null || !ValidVehicleId(v.VehicleId) || v.LastOpeningId < 0 || v.OpeningId < 0 ||
                    v.OpeningId > v.LastOpeningId || v.OpenedAt < 0 || v.Sequence < 0 || v.Owner == null ||
                    v.Owner.Length > 0 && !ValidOwner(v.Owner)) ||
                vendor.Vehicles.Select(v => v.VehicleId).Distinct(StringComparer.Ordinal).Count() != vendor.Vehicles.Count)
                throw new JsonSerializationException("Vendor vehicles must be one per vehicle with counters not negative.");
        }
    }
}
