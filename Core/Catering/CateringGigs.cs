using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Catering
{
    public enum GigCheckStatus { UnknownGig, InvalidStatus, Started, Completed, Failed, NotYet, NotEligible, Busy, NotReady }

    public readonly struct GigCheck
    {
        internal GigCheck(GigCheckStatus status, CateringGig? gig = null) { Status = status; Gig = gig; }
        public GigCheckStatus Status { get; }
        public bool Success => Status == GigCheckStatus.Started;
        public CateringGig? Gig { get; }
        public GigFailure? Failure => Gig?.Failure;
        public string? VehicleId => Gig?.VehicleId;
        public int ReservedUnits => Gig?.ReservedUnits ?? 0;
    }

    public readonly struct GigCompletion
    {
        internal GigCompletion(GigCheckStatus status, CateringGig? gig = null, int units = 0)
        {
            Status = status;
            Gig = gig;
            Units = units;
        }
        public GigCheckStatus Status { get; }
        public bool Success => Status == GigCheckStatus.Completed;
        public CateringGig? Gig { get; }
        public GigFailure? Failure => Gig?.Failure;
        public long FeeCents => Success ? Gig!.FeeCents : 0;
        public int Units { get; }
        public string? RequiredCategory => Success ? Gig!.RequiredCategory : null;
    }

    public static class CateringGigs
    {
        // An impossible destination is a cancellation, never a no-show. Started work keeps its normal settlement.
        public static IReadOnlyList<CateringGig> CancelUnlocatable(CateringState state, Func<string, bool> usable)
        {
            if (usable == null) throw new ArgumentNullException(nameof(usable));
            CateringValidation.Validate(state);
            state.Offers.RemoveAll(o => !usable(o.PlaceId));
            var cancelled = state.Gigs.Where(g => g.Status == GigStatus.Booked && !usable(g.PlaceId)).ToList();
            foreach (var gig in cancelled) state.Gigs.Remove(gig);
            return cancelled;
        }

        public static GigCheck Arrive(CateringRules rules, CateringState state, long gigId, string vehicleId, string vehicleType, int day, int minute,
            IReadOnlyCollection<string> menuItems, IReadOnlyDictionary<string, int> stockByItem, Func<string, string?> categoryOf)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            CateringValidation.Validate(state);
            var now = GameTime.Absolute(day, minute);
            if (menuItems == null) throw new ArgumentNullException(nameof(menuItems));
            if (stockByItem == null) throw new ArgumentNullException(nameof(stockByItem));
            if (categoryOf == null) throw new ArgumentNullException(nameof(categoryOf));
            var gig = state.Gigs.FirstOrDefault(g => g.Id == gigId);
            if (gig == null) return new GigCheck(GigCheckStatus.UnknownGig);
            if (gig.Status != GigStatus.Booked) return new GigCheck(GigCheckStatus.InvalidStatus, gig);
            if (string.IsNullOrEmpty(vehicleId)) throw new ArgumentException("Vehicle ID must not be empty.", nameof(vehicleId));
            if (!rules.VehicleTypes.Contains(vehicleType, StringComparer.Ordinal)) return new GigCheck(GigCheckStatus.NotEligible, gig);
            if (state.Gigs.Any(g => g.Status == GigStatus.Started && g.VehicleId == vehicleId)) return new GigCheck(GigCheckStatus.Busy, gig);
            var start = GameTime.Absolute(gig.GigDay, gig.StartMinute);
            if (now > start + rules.GraceMinutes)
            {
                Fail(rules, state, gig, GigFailure.Late);
                return new GigCheck(GigCheckStatus.Failed, gig);
            }
            if (now < start - rules.ArrivalWindowMinutes) return new GigCheck(GigCheckStatus.NotYet, gig);
            var items = menuItems.Distinct(StringComparer.Ordinal).Where(i => categoryOf(i) == gig.RequiredCategory).ToList();
            var units = Units(rules, gig);
            if (items.Count == 0 || items.Sum(i => stockByItem.TryGetValue(i, out var count) ? (long)Math.Max(0, count) : 0) < units)
                return new GigCheck(GigCheckStatus.NotReady, gig);
            gig.VehicleId = vehicleId;
            gig.ReservedUnits = units;
            gig.Status = GigStatus.Started;
            gig.ArrivedDay = day;
            gig.ArrivedMinute = minute;
            return new GigCheck(GigCheckStatus.Started, gig);
        }

        // day and minute are when the vehicle left the place, or the gig's end if it stayed that long,
        // not the time the adapter happens to report it: a truck that stayed through a sleep is paid on
        // waking. Leaving before the end fails. Unfinished (MissOverdue) only frees a truck whose Finish
        // never came.
        public static GigCompletion Finish(CateringRules rules, CateringState state, long gigId, int day, int minute)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            CateringValidation.Validate(state);
            var now = GameTime.Absolute(day, minute);
            var gig = state.Gigs.FirstOrDefault(g => g.Id == gigId);
            if (gig == null) return new GigCompletion(GigCheckStatus.UnknownGig);
            if (gig.Status != GigStatus.Started) return new GigCompletion(GigCheckStatus.InvalidStatus, gig);
            if (now < GameTime.Absolute(gig.GigDay, gig.StartMinute) + gig.DurationMinutes)
            {
                Fail(rules, state, gig, GigFailure.LeftEarly);
                return new GigCompletion(GigCheckStatus.Failed, gig);
            }
            // Older started gigs have no reservation; their stock draw still uses the rules.
            var units = gig.ReservedUnits > 0 ? gig.ReservedUnits : Units(rules, gig);
            gig.Status = GigStatus.Completed;
            state.BookingReputation = Math.Min(1, state.BookingReputation + rules.ReputationSuccess);
            state.Completed++;
            Archive(state, gig);
            return new GigCompletion(GigCheckStatus.Completed, gig, units);
        }

        // When time moves on (a tick, a sleep, a skip), the adapter first calls Finish for every Started
        // gig whose vehicle has left or whose end has passed with the vehicle still there (never during
        // the gig, which would fail it), then MissOverdue; otherwise a truck that stayed through a sleep
        // fails as Unfinished.
        public static IReadOnlyList<CateringGig> MissOverdue(CateringRules rules, CateringState state, int day, int minute)
        {
            if (rules == null) throw new ArgumentNullException(nameof(rules));
            CateringValidation.Validate(state);
            var now = GameTime.Absolute(day, minute);
            var missed = state.Gigs.Where(g => (g.Status == GigStatus.Booked
                && now > GameTime.Absolute(g.GigDay, g.StartMinute) + rules.GraceMinutes)
                || (g.Status == GigStatus.Started
                && now > GameTime.Absolute(g.GigDay, g.StartMinute) + g.DurationMinutes + rules.ArrivalWindowMinutes)).ToList();
            foreach (var gig in missed)
                Fail(rules, state, gig, gig.Status == GigStatus.Started ? GigFailure.Unfinished : GigFailure.NoShow);
            return missed.AsReadOnly();
        }

        private static int Units(CateringRules rules, CateringGig gig) => checked((int)Math.Ceiling(gig.Guests * rules.UnitsPerGuest));

        private static void Fail(CateringRules rules, CateringState state, CateringGig gig, GigFailure failure)
        {
            gig.Status = GigStatus.Failed;
            gig.Failure = failure;
            state.BookingReputation = Math.Max(rules.ReputationMin, state.BookingReputation - rules.ReputationFailure);
            state.Failed++;
            Archive(state, gig);
        }

        private static void Archive(CateringState state, CateringGig gig)
        {
            state.Gigs.Remove(gig);
            state.History.Add(gig);
            if (state.History.Count > 50) state.History.RemoveRange(0, state.History.Count - 50);
        }
    }

    internal static class GameTime
    {
        internal static long Absolute(int day, int minute)
        {
            if (day < 1) throw new ArgumentOutOfRangeException(nameof(day));
            if (minute < 0 || minute > 1439) throw new ArgumentOutOfRangeException(nameof(minute));
            return (day - 1L) * 1440 + minute;
        }
    }
}
