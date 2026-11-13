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
    public static class VendorBook
    {
        public const int LeaseMinutes = 60;
        public const int MaxOwners = 8;
        public const int MaxVehicles = 64;
        public const int MaxItems = 8;
        public const int MaxVehicleIdLength = 128;
        public const int MaxOwnerLength = 64;
        public const string SaveName = "vendor";

        public static bool ValidOwner(string? owner) =>
            !string.IsNullOrEmpty(owner) && owner!.Length <= MaxOwnerLength &&
            owner.All(c => c >= 'a' && c <= 'z' || c >= '0' && c <= '9' || c == '.' || c == '_' || c == '-');

        public static bool ValidVehicleId(string? id) =>
            !string.IsNullOrEmpty(id) && id!.Length <= MaxVehicleIdLength && !id.Any(char.IsControl);

        public static bool Locked(SaveState state) =>
            state.Vendor == null && state.ExtensionData != null && state.ExtensionData.ContainsKey(SaveName);

        public static VendorVehicle? Entry(SaveState state, string vehicleId) =>
            state.Vendor?.Vehicles.FirstOrDefault(v => string.Equals(v.VehicleId, vehicleId, StringComparison.Ordinal));

        public static VendorLease? Lease(SaveState state, string owner) =>
            state.Vendor?.Leases.FirstOrDefault(l => string.Equals(l.Owner, owner, StringComparison.Ordinal));

        public static VendorRequest? Request(SaveState state, string vehicleId) =>
            state.Vendor?.Requests.FirstOrDefault(r => string.Equals(r.VehicleId, vehicleId, StringComparison.Ordinal));

        public static bool LeaseHolds(SaveState state, string owner, long minute) => Lease(state, owner) is { } lease && minute < lease.Until;

        public static VendorState Ensure(SaveState state)
        {
            if (Locked(state)) throw new InvalidOperationException("The save holds vendor data from a newer Street Food version.");
            return state.Vendor ??= new VendorState();
        }

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

        public static int Forget(SaveState state, string owner, Func<string, bool> gone) =>
            state.Vendor?.Requests.RemoveAll(r => r.Owner == owner && gone(r.VehicleId)) ?? 0;

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

        public static void ReleaseAll(SaveState state, string owner, long now, ICollection<string>? notes = null)
        {
            var book = state.Vendor;
            if (book == null) return;
            foreach (var entry in book.Vehicles.Where(v => v.Owner == owner).ToList())
                HandBack(state, entry, now, "the owner released all", notes);
            book.Requests.RemoveAll(r => r.Owner == owner);
            book.Leases.RemoveAll(l => l.Owner == owner);
        }

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

        public static string Opened(SaveState state, string vehicleId, long minute, bool byCrew, ICollection<string>? notes = null)
        {
            var book = state.Vendor;
            if (book == null) return string.Empty;
            var entry = Entry(state, vehicleId);
            if (entry == null) book.Vehicles.Add(entry = new VendorVehicle { VehicleId = vehicleId });
            entry.OpeningId = Math.Max(checked(entry.LastOpeningId + 1), checked((Math.Max(0, minute) + 1) * 16));
            entry.LastOpeningId = entry.OpeningId;
            entry.OpenedAt = minute;
            entry.Owner = string.Empty;
            entry.HandedBackAt = null;
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

        public static void Closed(SaveState state, string vehicleId)
        {
            if (Entry(state, vehicleId) is { } entry) entry.Owner = string.Empty;
        }

        public static bool OwnCustomers(SaveState state, VehicleState vehicle, long minute, ICollection<string>? notes = null)
        {
            if (OwnerAt(state, vehicle, minute, notes) != null) return false;
            var entry = Entry(state, vehicle.VehicleId);
            return !(vehicle.IsOpen && entry?.HandedBackAt is { } back && minute < back && minute >= entry.OpenedAt);
        }

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

        internal static bool HandBack(SaveState state, VendorVehicle entry, long minute, string why, ICollection<string>? notes)
        {
            if (entry.Owner.Length == 0) return false;
            var owner = entry.Owner;
            if (Lease(state, owner) is { } lease && lease.Until <= minute) minute = Math.Max(entry.OpenedAt, lease.Until);
            entry.Owner = string.Empty;
            entry.HandedBackAt = minute;
            var vehicle = StreetSales.Find(state, entry.VehicleId);
            if (vehicle != null && vehicle.IsOpen) vehicle.Service = SalesSimulator.Open(SeededRandom.SeedFor(entry.VehicleId, minute));
            notes?.Add($"Vendor API: {entry.VehicleId} opening {entry.OpeningId} handed back from {owner} to Street Food at {Describe(minute)} ({why}).");
            return true;
        }

        public static bool EventOrCatering(SaveState state, string vehicleId) =>
            state.Events?.Events?.Any(e => e?.Holds?.Any(h => h != null && h.VehicleId == vehicleId && h.Until == null) == true) == true ||
            state.Catering?.Gigs?.Any(g => g != null && g.VehicleId == vehicleId && g.Status == GigStatus.Started) == true;

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
                    v.OpeningId > v.LastOpeningId || v.OpenedAt < 0 || v.Sequence < 0 || v.HandedBackAt < 0 || v.Owner == null ||
                    v.Owner.Length > 0 && !ValidOwner(v.Owner)) ||
                vendor.Vehicles.Select(v => v.VehicleId).Distinct(StringComparer.Ordinal).Count() != vendor.Vehicles.Count)
                throw new JsonSerializationException("Vendor vehicles must be one per vehicle with counters not negative.");
        }
    }
}
