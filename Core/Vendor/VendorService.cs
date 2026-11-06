using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Saves;
using FoodTrucks.ModApi;

namespace FoodTrucks.Core.Vendor
{
    // What the game side reads for one food vehicle; VendorService adds the opening, owner and times from the save.
    public sealed class VendorFacts
    {
        public string VehicleId { get; set; } = string.Empty;
        public string Kind { get; set; } = "cart";
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float QueueX { get; set; }
        public float QueueY { get; set; }
        public float QueueZ { get; set; }
        public float QueueHeading { get; set; }
        public int MealsPerHour { get; set; }
        // The equipment menu with each item's price (0 when it has none) and sellable units.
        public IReadOnlyList<string> Items { get; set; } = Array.Empty<string>();
        public IReadOnlyList<long> PriceCents { get; set; } = Array.Empty<long>();
        public IReadOnlyList<int> Units { get; set; } = Array.Empty<int>();
    }

    // The game side of the vendor API (CartSelling in the mod). Main thread only.
    public interface IVendorHost
    {
        // Ready, NotYet (no save loaded, data not read yet) or Off (read-only mod data, uninstalled).
        VendorApiStatus Status { get; }
        // The writable state when Status is Ready.
        SaveState? State { get; }
        // The game minute now, (day - 1) × 1440 + minute of the day.
        long Now { get; }
        // Every food vehicle the player owns, in ordinal ID order.
        IReadOnlyList<VendorFacts> Facts();
        VendorFacts? FactsOf(string vehicleId);
        // Street Food's own one-item sale (CartSelling.SellItem with the crew batch flushed) at that game minute:
        // the cents taken, or -1 when it refused before any effect. May throw.
        long SellOne(string vehicleId, string item, long minute);
        // Mod data changed (SaveGameManager.MarkChange), and log lines to write.
        void Changed();
        void Log(string line);
    }

    // The API's behaviour (FoodTrucks.ModApi.VendorApi forwards here). Everything is checked before anything is written.
    public sealed class VendorService
    {
        // One counter for the whole process: a new session never repeats an earlier revision.
        private static long revisionCounter = 1;
        private static long NextRevision() => ++revisionCounter;

        private readonly IVendorHost host;
        private readonly List<string> notes = new List<string>();
        private VendorApiVehicle[] cache = Array.Empty<VendorApiVehicle>();
        private bool built;
        private bool overflow;

        public VendorService(IVendorHost host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            Revision = NextRevision();
        }

        public long Revision { get; private set; }

        // VendorApi.Revision: the consumer is polling, so the game side keeps the snapshot fresh from now on.
        public long Poll()
        {
            Active = true;
            return Revision;
        }

        // A consumer has used the API (Revision, Read or an owner call) since this service started: the game side then
        // keeps the snapshot fresh (Tick). Never set for a game without such a mod.
        public bool Active { get; private set; }

        // The game side's tick: leases that ran out hand back, and the snapshot follows the game while a consumer uses it.
        public void Tick()
        {
            if (host.Status != VendorApiStatus.Ready || !(host.State is { } state)) return;
            if (state.Vendor != null && VendorBook.Expire(state, host.Now, notes) > 0) host.Changed();
            Flush();
            if (Active) Refresh();
        }

        // The vehicle copies are rebuilt from the game side; a change gives a new revision.
        public void Refresh()
        {
            built = true;
            var state = host.State;
            var facts = host.Status == VendorApiStatus.Ready && state != null ? host.Facts() : Array.Empty<VendorFacts>();
            if (facts.Count > VendorBook.MaxVehicles)
            {
                if (!overflow || cache.Length > 0) Revision = NextRevision();
                overflow = true;
                cache = Array.Empty<VendorApiVehicle>();
                return;
            }
            var changed = overflow || facts.Count != cache.Length;
            overflow = false;
            var next = changed ? new VendorApiVehicle[facts.Count] : cache;
            for (var i = 0; i < facts.Count; i++)
            {
                var copy = Build(state!, facts[i]);
                if (!changed && Same(copy, cache[i])) continue;
                if (!changed)
                {
                    changed = true;
                    next = (VendorApiVehicle[])cache.Clone();
                }
                next[i] = copy;
            }
            if (!changed) return;
            cache = next;
            Revision = NextRevision();
        }

        public VendorApiStatus Read(VendorApiVehicle[] into, out int count)
        {
            count = 0;
            Active = true;
            var status = Gate(out _, out _);
            if (status != VendorApiStatus.Ready) return status;
            try
            {
                if (!built) Refresh();
            }
            catch (Exception ex)
            {
                host.Log("Vendor API: reading the vehicles failed: " + ex);
                return VendorApiStatus.Failed;
            }
            if (overflow || into == null || into.Length < cache.Length) return VendorApiStatus.Refused;
            Array.Copy(cache, into, cache.Length);
            count = cache.Length;
            return VendorApiStatus.Ready;
        }

        public VendorApiStatus RequestOwner(string owner, string vehicleId, bool want, out string reason)
        {
            Active = true;
            var status = Gate(out var state, out reason);
            if (status != VendorApiStatus.Ready) return status;
            if (!VendorBook.ValidOwner(owner)) return Refuse("invalid owner", out reason);
            if (!VendorBook.ValidVehicleId(vehicleId)) return Refuse("invalid vehicle id", out reason);
            try
            {
                if (want && host.FactsOf(vehicleId) == null) return Refuse("no such food vehicle", out reason);
                var ok = VendorBook.Ask(state!, owner, vehicleId, want, host.Now, out var why, notes);
                Changed();
                return ok ? VendorApiStatus.Ready : Refuse(why, out reason);
            }
            catch (Exception ex)
            {
                host.Log("Vendor API: owner request failed: " + ex);
                reason = "failed";
                return VendorApiStatus.Failed;
            }
        }

        public VendorApiStatus Renew(string owner, int nativeDay, int nativeMinute, out int leaseUntilDay, out int leaseUntilMinute)
        {
            leaseUntilDay = -1;
            leaseUntilMinute = -1;
            Active = true;
            var status = Gate(out var state, out _);
            if (status != VendorApiStatus.Ready) return status;
            if (!VendorBook.ValidOwner(owner) || !ValidTime(nativeDay, nativeMinute)) return VendorApiStatus.Refused;
            var minute = VendorBook.Absolute(nativeDay, nativeMinute);
            var now = host.Now;
            // Renewed for the minute now, or one a catch-up still lags in; never ahead of the clock.
            if (minute > now || minute <= now - VendorBook.LeaseMinutes) return VendorApiStatus.Refused;
            try
            {
                var until = VendorBook.Renew(state!, owner, minute, now, out _, notes);
                Changed();
                if (until < 0) return VendorApiStatus.Refused;
                leaseUntilDay = VendorBook.DayOf(until);
                leaseUntilMinute = VendorBook.MinuteOf(until);
                return VendorApiStatus.Ready;
            }
            catch (Exception ex)
            {
                host.Log("Vendor API: renew failed: " + ex);
                return VendorApiStatus.Failed;
            }
        }

        public VendorApiStatus ReleaseAll(string owner)
        {
            Active = true;
            var status = Gate(out var state, out _);
            if (status != VendorApiStatus.Ready) return status;
            if (!VendorBook.ValidOwner(owner)) return VendorApiStatus.Refused;
            try
            {
                VendorBook.ReleaseAll(state!, owner, host.Now, notes);
                Changed();
                return VendorApiStatus.Ready;
            }
            catch (Exception ex)
            {
                host.Log("Vendor API: release failed: " + ex);
                return VendorApiStatus.Failed;
            }
        }

        public VendorApiStatus Sell(string owner, string vehicleId, long openingId, string item, long priceCents,
            int nativeDay, int nativeMinute, out VendorApiSale? sale, out bool uncertain, out string reason)
        {
            sale = null;
            uncertain = false;
            Active = true;
            var status = Gate(out var state, out reason);
            if (status != VendorApiStatus.Ready) return status;
            string? refusal;
            long minute;
            VehicleState? vehicle;
            int index, before;
            try
            {
                refusal = CheckSale(state!, owner, vehicleId, openingId, item, priceCents, nativeDay, nativeMinute,
                    out minute, out vehicle, out index, out before);
            }
            catch (Exception ex)
            {
                host.Log($"Vendor API: checking the sale of {item} at {vehicleId} failed: {ex}");
                reason = "failed before the sale";
                return VendorApiStatus.Failed;
            }
            if (refusal != null) return Refuse(refusal, out reason);

            var day = VendorBook.DayOf(minute);
            var lifetime = vehicle!.LifetimeSales;
            var pot = Pot(state!, vehicleId, day);
            long cents;
            try { cents = host.SellOne(vehicleId, item, minute); }
            catch (Exception ex)
            {
                // A sale whose ledger record was made counts as made, whatever failed after it.
                uncertain = vehicle.LifetimeSales != lifetime || Pot(state!, vehicleId, day) != pot;
                if (uncertain) Bump(vehicleId);
                host.Log($"Vendor API: sale of {item} at {vehicleId} failed {(uncertain ? "after" : "before")} the ledger record: {ex}");
                reason = uncertain ? "failed after the ledger record; do not retry" : "failed before the sale";
                Changed();
                return VendorApiStatus.Failed;
            }
            if (cents < 0)
            {
                uncertain = vehicle.LifetimeSales != lifetime || Pot(state!, vehicleId, day) != pot;
                Changed();
                if (!uncertain) return Refuse("sale refused", out reason);
                reason = "refused after the ledger record; do not retry";
                return VendorApiStatus.Failed;
            }
            var sequence = Bump(vehicleId);
            int after;
            try { after = UnitsOf(host.FactsOf(vehicleId), index, item); }
            catch (Exception ex)
            {
                host.Log($"Vendor API: units after the sale of {item} at {vehicleId} unreadable: {ex}");
                after = -1;
            }
            sale = new VendorApiSale(day, cents, before, after, sequence);
            reason = string.Empty;
            Changed();
            return VendorApiStatus.Ready;
        }

        // Null when the sale may go ahead; otherwise why not. Reads only.
        private string? CheckSale(SaveState state, string owner, string vehicleId, long openingId, string item, long priceCents,
            int nativeDay, int nativeMinute, out long minute, out VehicleState? vehicle, out int index, out int units)
        {
            minute = 0;
            vehicle = null;
            index = -1;
            units = 0;
            if (!VendorBook.ValidOwner(owner)) return "invalid owner";
            if (!VendorBook.ValidVehicleId(vehicleId)) return "invalid vehicle id";
            if (string.IsNullOrEmpty(item)) return "invalid item";
            if (priceCents <= 0) return "invalid price";
            if (!ValidTime(nativeDay, nativeMinute)) return "invalid time";
            minute = VendorBook.Absolute(nativeDay, nativeMinute);
            var now = host.Now;
            if (minute > now) return "minute not reached yet";
            if (VendorBook.DayOf(minute) != VendorBook.DayOf(now)) return "minute of a past day";
            vehicle = StreetSales.Find(state, vehicleId);
            if (vehicle == null || !vehicle.IsOpen) return "vehicle closed";
            var entry = VendorBook.Entry(state, vehicleId);
            if (entry == null || entry.OpeningId == 0 || entry.OpeningId != openingId) return "opening changed";
            // Hand-backs due by this minute happen first, so a lapsed lease is never sold through.
            if (VendorBook.OwnerAt(state, vehicle, minute, notes) != owner)
            {
                Changed();
                return "not the owner of this opening";
            }
            if (minute < entry.OpenedAt) return "minute before the opening";
            if (VendorBook.PlannedClose(vehicle, entry.OpenedAt) is { } closes && minute >= closes) return "minute after the planned close";
            var facts = host.FactsOf(vehicleId);
            if (facts == null) return "vehicle missing";
            if (facts.MealsPerHour <= 0) return "nobody serving";
            if (facts.Items.Count > VendorBook.MaxItems) return "menu overflow";
            index = IndexOf(facts.Items, item);
            if (index < 0) return "item not on the menu";
            if (facts.PriceCents[index] != priceCents) return "price changed";
            units = facts.Units[index];
            if (units <= 0) return "sold out";
            var day = VendorBook.DayOf(minute);
            if (!StreetSales.CanRecord(state, vehicleId, day, priceCents, 1) || !CrewTakings.CanAccrue(state, vehicleId, day, priceCents))
                return "the street food record is full";
            return null;
        }

        private VendorApiStatus Gate(out SaveState? state, out string reason)
        {
            state = null;
            reason = string.Empty;
            var status = host.Status;
            if (status != VendorApiStatus.Ready)
            {
                reason = status == VendorApiStatus.NotYet ? "no save loaded yet" : "Street Food's data is read-only";
                return status;
            }
            state = host.State;
            if (state == null)
            {
                reason = "no save loaded yet";
                return VendorApiStatus.NotYet;
            }
            if (VendorBook.Locked(state))
            {
                reason = "the save's vendor data is from a newer Street Food version";
                return VendorApiStatus.Refused;
            }
            return VendorApiStatus.Ready;
        }

        private VendorApiVehicle Build(SaveState state, VendorFacts facts)
        {
            var vehicle = StreetSales.Find(state, facts.VehicleId);
            var open = vehicle?.IsOpen == true;
            var entry = VendorBook.Entry(state, facts.VehicleId);
            var counted = open && entry != null && entry.OpeningId > 0;
            var openedAt = counted ? entry!.OpenedAt : -1;
            var closes = open ? VendorBook.PlannedClose(vehicle!, counted ? openedAt : host.Now) : null;
            var overflowed = facts.Items.Count > VendorBook.MaxItems;
            var order = overflowed ? Array.Empty<int>() : Enumerable.Range(0, facts.Items.Count)
                .OrderBy(i => facts.Items[i], StringComparer.Ordinal).ToArray();
            return new VendorApiVehicle(facts.VehicleId, facts.Kind == "truck" ? "truck" : "cart", open,
                open && entry != null ? entry.OpeningId : 0, counted ? entry!.Owner : string.Empty,
                open && vehicle!.HireStop != null, open && VendorBook.EventOrCatering(state, facts.VehicleId),
                openedAt < 0 ? -1 : VendorBook.DayOf(openedAt), openedAt < 0 ? -1 : VendorBook.MinuteOf(openedAt),
                closes == null ? -1 : VendorBook.DayOf(closes.Value), closes == null ? -1 : VendorBook.MinuteOf(closes.Value),
                facts.X, facts.Y, facts.Z, facts.QueueX, facts.QueueY, facts.QueueZ, facts.QueueHeading,
                open ? Math.Max(0, facts.MealsPerHour) : 0, overflowed,
                order.Select(i => facts.Items[i]).ToArray(), order.Select(i => Math.Max(0, facts.PriceCents[i])).ToArray(),
                order.Select(i => Math.Max(0, facts.Units[i])).ToArray());
        }

        private static bool Same(VendorApiVehicle a, VendorApiVehicle b) =>
            a.VehicleId == b.VehicleId && a.Kind == b.Kind && a.Open == b.Open && a.OpeningId == b.OpeningId &&
            a.OpeningOwner == b.OpeningOwner && a.OpenedByCrew == b.OpenedByCrew && a.EventOrCatering == b.EventOrCatering &&
            a.OpenedDay == b.OpenedDay && a.OpenedMinute == b.OpenedMinute && a.ClosesDay == b.ClosesDay && a.ClosesMinute == b.ClosesMinute &&
            a.X.Equals(b.X) && a.Y.Equals(b.Y) && a.Z.Equals(b.Z) && a.QueueX.Equals(b.QueueX) && a.QueueY.Equals(b.QueueY) &&
            a.QueueZ.Equals(b.QueueZ) && a.QueueHeading.Equals(b.QueueHeading) && a.MealsPerHour == b.MealsPerHour &&
            a.MenuOverflow == b.MenuOverflow && a.Items.SequenceEqual(b.Items, StringComparer.Ordinal) &&
            a.PriceCents.SequenceEqual(b.PriceCents) && a.Units.SequenceEqual(b.Units);

        private long Bump(string vehicleId)
        {
            var entry = VendorBook.Entry(host.State!, vehicleId);
            if (entry == null) return 0;
            return ++entry.Sequence;
        }

        private static long Pot(SaveState state, string vehicleId, int day) =>
            state.CrewTakings.Where(t => t.VehicleId == vehicleId && t.Day == day).Sum(t => t.Cents);

        private static int IndexOf(IReadOnlyList<string> items, string item)
        {
            for (var i = 0; i < items.Count; i++)
                if (string.Equals(items[i], item, StringComparison.Ordinal)) return i;
            return -1;
        }

        private static int UnitsOf(VendorFacts? facts, int index, string item)
        {
            if (facts == null) return -1;
            var at = index < facts.Items.Count && facts.Items[index] == item ? index : IndexOf(facts.Items, item);
            return at < 0 ? 0 : facts.Units[at];
        }

        private static bool ValidTime(int day, int minute) => day >= 1 && minute >= 0 && minute < 1440;

        private static VendorApiStatus Refuse(string why, out string reason)
        {
            reason = why;
            return VendorApiStatus.Refused;
        }

        // Never throws: it runs after a sale, which must be reported as made.
        private void Changed()
        {
            try
            {
                host.Changed();
                Flush();
                Refresh();
            }
            catch (Exception ex)
            {
                try { host.Log("Vendor API: refreshing after a change failed: " + ex); }
                catch (Exception) { }
            }
        }

        private void Flush()
        {
            foreach (var line in notes) host.Log(line);
            notes.Clear();
        }
    }

    // The one service the public API forwards to; the game side attaches it with each loaded save.
    public static class VendorRuntime
    {
        public static VendorService? Current { get; private set; }

        public static VendorService Attach(IVendorHost host) => Current = new VendorService(host);

        public static void Detach() => Current = null;

        // A fixed value while nothing is attached; a new service always starts at a new one.
        public static long Revision() => Current?.Poll() ?? 0;
    }
}
