using FoodTrucks;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Pitches;
using FoodTrucks.Core.Saves;

static class Program
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static void Main(string[] args)
    {
        string Read(string name) => File.ReadAllText(Path.Combine(args[0], "data", name + ".json"));
        DemandData.Pitches = PitchTables.Parse(Read("pitches"), DemandTables.Parse(Read("demand"), Read("economy"), Read("zones")));
        var tables = DemandData.Pitches;
        var place = tables.Places[0]; var kind = tables.Kind(place.Kinds[0])!;
        var state = new SaveState(); FoodTrucksSession.CurrentState = state;
        state.Events.LastAnnouncedDay = 4;
        state.Vehicles.Add(new VehicleState { VehicleId = "closed", IsOpen = false, PremiumSpot = "pitch" });
        state.Events.Events.Add(new CityEvent { Id = 1, Kind = kind.Id, PlaceId = place.Id, Day = 1, End = 960 }); // Expiry waits for validation.
        var live = new CityEvent { Id = 2, Kind = kind.Id, PlaceId = place.Id, Day = 4, Start = 600, End = 960, Booked = 1 };
        live.Holds.Add(new PlaceHold { VehicleId = "closed", From = 4000 }); state.Events.Events.Add(live);
        var invalid = new CityEvent { Id = 3, Kind = kind.Id, PlaceId = "missing", Day = 5, Start = 600, End = 960, Booked = 2, FeeCents = 8000 };
        state.Events.Events.Add(invalid);
        state.PremiumHolds["expired"] = new() { new PlaceHold { VehicleId = "closed", From = 1, Until = 2 } };
        state.PremiumHolds["pitch"] = new() { new PlaceHold { VehicleId = "closed", From = 4000 } };
        CityPitches.NewDay(state, 4, loading: true);
        Check(state.Events.Events.Any(e => e.Id == 1), "expiry must wait for address validation");
        Check(!state.PremiumHolds.ContainsKey("expired"), "premium prune must run before ready");
        Check(live.Holds[0].Until == HireCrew.Now(), "load-only event hold release must run before ready");
        Check(state.PremiumHolds["pitch"][0].Until == HireCrew.Now(), "load-only premium release must run before ready");
        Check(state.Vehicles[0].PremiumSpot == null, "closed vehicle premium marker cleared");
        Check(state.Events.Events.Contains(invalid) && GameManager.Paid == 0, "address judgement deferred");
        Check(Notices.Messages.Count == 0 && EventLocations.Refreshes == 0, "messages deferred");
        EventLocations.Ready = true;
        CityPitches.RetryAddresses();
        Check(!state.Events.Events.Contains(invalid) && GameManager.Paid == 16000, "deferred cancellation refunds once ready");
        Check(EventLocations.Refreshes == 1, "repair retried once ready");
        Check(!state.Events.Events.Any(e => e.Id == 1), "valid ended event expires after validation");
        var reminder = Notices.Messages.Single(m => m.Key == "foodtrucks:message_city_event_today");
        Check(reminder.Data["day"] == "4", "new reminders must carry day");
        Check(reminder.Data["bookingId"] == "2" && reminder.Data["locationId"] == place.Address, "stable identity on reminders");
        CityPitches.RetryAddresses();
        Check(EventLocations.Refreshes == 1 && GameManager.Paid == 16000, "no repeated work after successful retry");
        EventLocations.Ready = false; CityPitches.NewDay(state, 4, loading: false); CityPitches.Reset();
        EventLocations.Ready = true; CityPitches.RetryAddresses();
        Check(EventLocations.Refreshes == 1, "reset clears pending work");
        foreach (var day in new[] { 1, 4, 5 })
            foreach (var claimed in new[] { 0, 1 })
                foreach (var failure in new[] { "missing-place", "unreadable-address", "missing-kind" })
                    RefundRetry(place, kind, day, claimed, failure);
        foreach (var ready in new[] { false, true })
        {
            LoadReleaseDirty(place, kind, ready);
            foreach (var open in new[] { false, true })
                foreach (var heldMinutes in new[] { 0, 1 })
                    RefundOpenHold(kind, ready, open, heldMinutes);
        }
        Console.WriteLine("PASS: real city NewDay readiness, expiry, prune, load cleanup, retry, refund and reminder payload");
    }
    static void LoadReleaseDirty(EventPlace place, EventKind kind, bool ready)
    {
        HireCrew.Minute = 4500;
        var state = new SaveState(); FoodTrucksSession.CurrentState = state; CityPitches.Reset();
        state.Events.LastAnnouncedDay = 4;
        var city = new CityEvent { Id = 20, Kind = kind.Id, PlaceId = place.Id, Day = 4, Start = 120, End = 960, Reminded = true };
        var hold = new PlaceHold { VehicleId = "closed", From = HireCrew.Now() };
        city.Holds.Add(hold); state.Events.Events.Add(city);
        EventLocations.Ready = ready;
        var changes = FoodTrucksSession.Changes;
        CityPitches.NewDay(state, 4, loading: true);
        Check(hold.Until == hold.From, "load releases same-minute closed-vehicle hold");
        Check(FoodTrucksSession.Changes == changes + 1, "hold release alone must dirty the save, ready or deferred");
        CityPitches.NewDay(state, 4, loading: true);
        Check(FoodTrucksSession.Changes == changes + 1, "repeat load without release must stay clean");
    }
    static void RefundOpenHold(EventKind kind, bool ready, bool open, int heldMinutes)
    {
        HireCrew.Minute = 4500;
        var state = new SaveState(); FoodTrucksSession.CurrentState = state; CityPitches.Reset();
        state.Events.LastAnnouncedDay = 4;
        state.Vehicles.Add(new VehicleState { VehicleId = "holder", IsOpen = open });
        var city = new CityEvent { Id = 30, Kind = kind.Id, PlaceId = "missing", Day = 4,
            Start = 120, End = 960, Booked = 2, FeeCents = 8000, Reminded = true };
        city.Holds.Add(new PlaceHold { VehicleId = "holder", From = HireCrew.Now() - heldMinutes });
        state.Events.Events.Add(city);
        EventLocations.Ready = ready; GameManager.Paid = 0;
        CityPitches.NewDay(state, 4, loading: true);
        EventLocations.Ready = true;
        HireCrew.Minute += 3; // Readiness can arrive later; a closed hold must retain its load-time end.
        CityPitches.RetryAddresses();
        var expected = !open && heldMinutes == 0 ? 16000 : 8000;
        Check(!state.Events.Events.Contains(city) && GameManager.Paid == expected,
            $"immediate/deferred refund agrees: ready={ready}, open={open}, heldMinutes={heldMinutes}");
        CityPitches.NewDay(state, 4, loading: true); CityPitches.RetryAddresses();
        Check(GameManager.Paid == expected, "open-hold cancellation refunds exactly once");
    }
    static void RefundRetry(EventPlace place, EventKind kind, int eventDay, int claimed, string failure)
    {
        // Now really varies: ended, running and future invalid bookings all follow the cancellation path.
        HireCrew.Minute = eventDay == 4 ? 5000 : 4500;
        var state = new SaveState(); FoodTrucksSession.CurrentState = state; CityPitches.Reset();
        state.Events.LastAnnouncedDay = 4;
        var invalid = new CityEvent { Id = 10, Kind = failure == "missing-kind" ? "missing" : kind.Id,
            PlaceId = failure == "missing-place" ? "missing" : place.Id,
            Day = eventDay, Start = 600, End = 960, Booked = 2, FeeCents = 8000, Reminded = true };
        if (claimed > 0) invalid.Holds.Add(new PlaceHold { VehicleId = "used", From = CityEvents.StartsAt(invalid), Until = CityEvents.StartsAt(invalid) + 1 });
        state.Events.Events.Add(invalid);
        EventLocations.Unreadable = failure == "unreadable-address" ? place.Address : null;
        EventLocations.Ready = false; GameManager.Paid = 0;
        var changes = FoodTrucksSession.Changes;
        CityPitches.NewDay(state, 4, loading: true);
        CityPitches.NewDay(state, 4, loading: false);
        Check(state.Events.Events.Contains(invalid) && GameManager.Paid == 0, "paid booking retained before validation: " + failure);
        Check(FoodTrucksSession.Changes == changes, "unchanged deferred load/retry must not dirty the save");
        EventLocations.Ready = true; CityPitches.RetryAddresses();
        Check(!state.Events.Events.Contains(invalid) && GameManager.Paid == (2 - claimed) * 8000, "refund after readiness: " + failure);
        changes = FoodTrucksSession.Changes;
        CityPitches.RetryAddresses();
        CityPitches.NewDay(state, 4, loading: true);
        Check(GameManager.Paid == (2 - claimed) * 8000, "refund exactly once on repeat retry/day");
        Check(FoodTrucksSession.Changes == changes, "unchanged ready load must not dirty the save");
        EventLocations.Unreadable = null;
    }

}
namespace FoodTrucks
{
    static class DemandData { internal static PitchTables? Pitches; }
    static class FoodTrucksSession
    {
        internal static SaveState? CurrentState; internal static bool CanWrite = true; internal static int Changes;
        internal static void MarkChanged() => Changes++;
    }
    static class EventLocations
    {
        internal static bool Ready; internal static int Refreshes; internal static string? Unreadable;
        internal static string? ReadableAddress(string key) => key == "missing" || key == Unreadable ? null : "34 4th Avenue";
        internal static string Name(string key) => ReadableAddress(key)!;
        internal static void RefreshMessages(object contact, IEnumerable<Dictionary<string, string>> d) { _ = d.ToList(); Refreshes++; }
    }
    static class CartSelling { internal static long SoldThrough() => HireCrew.Now(); internal static string Money(long cents) => cents.ToString(); }
    static class HireCrew { internal static long Minute = 4500; internal static long Now() => Minute; }
    static class CityLicensing { internal static object City() => new(); internal static List<object> FoodVehicles(SaveState s) => new() { new() }; }
    static class StreetSales { internal static VehicleState? Find(SaveState s, string id) => s.Vehicles.Find(v => v.VehicleId == id); }
    static class Notices
    {
        internal static List<(string Key, Dictionary<string, string> Data)> Messages = new();
        internal static void SendText(object c, string key, Dictionary<string, string> data) => Messages.Add((key, data));
    }
    static class Log { internal static void Info(string s) { } }
    static class SaveGameManager { internal static Game Current = new(); }
    class Game { internal int Day = 4; }
    class TransactionInfo
    {
        internal TransactionInfo(string k, Dictionary<string, string> d) { }
        internal void SetTaxDeductibleName(string k) { }
    }
    static class GameManager
    {
        internal static long Paid;
        internal static void ChangeMoneySafe(float value, TransactionInfo i, object? a, object? b, bool force, bool showNotification) => Paid += (long)Math.Round(value * 100);
    }
    static class SalesLedger { internal static void RecordEventPlaces(SaveState s, int day, long amount) { } }
}
