using FoodTrucks;
using Entities;
using FoodTrucks.Core.Catering;

static class Program
{
    static void Equal(object? a, object? b) { if (!Equals(a, b)) throw new Exception($"Expected {a}, got {b}"); }
    static void Main(string[] args)
    {
        var address = new Address { streetName = "ba:street_fourthavenue", streetNumber = 34 };
        World.Found = new CityBuildingController { building = new Building { Address = address } };
        Equal("34 4th Avenue", EventLocations.ReadableAddress("ba:street_fourthavenue#34"));
        Equal("34 4th Avenue", Catering.PlaceName("ba:street_fourthavenue#34"));
        EventLocations.SetDestination("ba:street_fourthavenue#34");
        Equal(address, UI.Guiders.GuidersManager.Target);
        Equal("Location unavailable", Catering.PlaceName("ba:street_fourthavenue#35"));
        Equal("Location unavailable", Catering.PlaceName("broken-id"));
        foreach (var street in new[] { "ba:street_missing", "", "{street}", "<sprite=5>", "#12" })
        {
            Streets.AddressHelper.Street = street;
            Equal(null, EventLocations.ReadableAddress("ba:street_fourthavenue#34"));
            UI.Guiders.GuidersManager.Target = null;
            EventLocations.SetDestination("ba:street_fourthavenue#34");
            Equal(null, UI.Guiders.GuidersManager.Target);
        }
        Streets.AddressHelper.Street = "4th Avenue";
        address.streetNumber = 0;
        Equal(null, EventLocations.ReadableAddress("ba:street_fourthavenue#34"));
        World.Found = null;
        Equal(null, EventLocations.ReadableAddress("ba:street_fourthavenue#34"));
        World.Found = new CityBuildingController { building = new Building { Address = address } };
        RepairHistory();
        LegacyReminders(args[0]);
        SavedBookingRadius();
        OrphanHistory();
        Console.WriteLine("PASS: native event addresses, missing data, catering and GPS");
    }
    static Dictionary<string, string> Description(string number = "34", string id = "1") => new()
    {
        ["bookingId"] = id, ["locationId"] = "ba:street_fourthavenue#" + number,
        ["day"] = "4", ["start"] = "10:00", ["end"] = "14:00", ["kind"] = "party",
        ["guests"] = "40", ["fee"] = "$400", ["place"] = number + " 4th Avenue"
    };
    static TextMessage Message(Dictionary<string, string> data, string key = "foodtrucks:message_bookings_offer") =>
        new() { messageKey = key, messageData = new(data), timestamp = new() { Day = 4 } };
    static void Repair(Contact c, params Dictionary<string, string>[] descriptions) => EventLocations.RefreshMessages(c, descriptions);
    static void RepairHistory()
    {
        var a = Description(); var b = Description("35", "2");
        foreach (var disposition in new[] { "declined", "missed", "cancelled" })
        {
            // Legacy offers retain guests/end/fee and the old address, but no stable IDs.
            var oldA = new Dictionary<string, string>(a); oldA.Remove("bookingId"); oldA.Remove("locationId");
            oldA["place"] = "34 #ba:street_fourthavenue";
            var oldB = new Dictionary<string, string>(oldA) { ["place"] = "35 ba:street_fourthavenue" };
            var messageA = Message(oldA); var messageB = Message(oldB);
            var readable = Message(new(a) { ["place"] = "34 Fourth Avenue" });
            var c = new Contact(); c.messagesQueue.Enqueue(messageA); c.messagesQueue.Enqueue(messageB); c.messagesQueue.Enqueue(readable);
            Repair(c, a, b);
            Equal("34 4th Avenue", messageA.messageData!["place"]);
            Equal("35 4th Avenue", messageB.messageData!["place"]);
            // Exercise the actual decline/miss/cancel paths, retaining the phone history.
            var state = CateringState.New(42); state.NextOfferId = 3;
            foreach (var d in new[] { a, b }) state.Offers.Add(new CateringOffer
            {
                Id = long.Parse(d["bookingId"]), PlaceId = d["locationId"], Kind = "office", PostedDay = 1,
                ReplyByDay = 2, GigDay = 4, StartMinute = 600, DurationMinutes = 240,
                Guests = 40, RequiredCategory = "fastfood", FeeCents = 40000
            });
            var rules = new CateringRules();
            if (disposition == "declined") Equal(true, CateringOffers.Decline(state, 1));
            else if (disposition == "missed")
            {
                Equal(true, CateringOffers.Accept(rules, state, 1, 1, 0, 2).Success);
                Equal(true, CateringOffers.Accept(rules, state, 2, 1, 0, 2).Success);
                Equal(true, CateringGigs.Arrive(rules, state, 2, "truck-b", "truck", 4, 600,
                    new[] { "hotdog" }, new Dictionary<string, int> { ["hotdog"] = 100 }, _ => "fastfood").Success);
                Equal(1, CateringGigs.MissOverdue(rules, state, 4, 700).Count);
            }
            else
            {
                Equal(true, CateringOffers.Accept(rules, state, 1, 1, 0, 2).Success);
                Equal(1, CateringGigs.CancelUnlocatable(state, p => p != a["locationId"]).Count);
            }
            Equal("2", string.Join(",", state.Offers.Concat<CateringOffer>(state.Gigs).Select(o => o.Id)));
            messageA.messageData["place"] = oldA["place"];
            Repair(c, state.Offers.Concat<CateringOffer>(state.Gigs).Select(o => o.Id == 1 ? a : b).ToArray());
            Equal(oldA["place"], messageA.messageData["place"]);
            Equal("34 Fourth Avenue", readable.messageData!["place"]);
            foreach (var key in new[] { "missed", "paid", "unpaid", "location_cancelled" })
            {
                var terminal = Message(new(b) { ["place"] = "Location unavailable" }, "foodtrucks:message_bookings_" + key);
                c.messagesQueue.Enqueue(terminal); Repair(c, b);
                Equal("Location unavailable", terminal.messageData!["place"]);
            }
        }
        var unknown = Message(new(a) { ["place"] = "Location unavailable" });
        unknown.messageData!.Remove("bookingId"); unknown.messageData.Remove("locationId");
        var contact = new Contact(); contact.messagesQueue.Enqueue(unknown);
        Repair(contact, a, b); Repair(contact, b);
        Equal("Location unavailable", unknown.messageData["place"]);
        var stable = Message(new(a) { ["place"] = "Location unavailable" }); contact.messagesQueue.Enqueue(stable);
        Repair(contact, b); Equal("Location unavailable", stable.messageData!["place"]);
        foreach (var field in new[] { "guests", "end", "fee" })
        {
            var mismatch = new Dictionary<string, string>(a) { [field] = "different" };
            Repair(contact, mismatch); Equal("Location unavailable", stable.messageData["place"]);
        }
        Repair(contact, a); Equal("34 4th Avenue", stable.messageData["place"]);
        var ambiguous = Message(new(a) { ["place"] = "Location unavailable" }); contact.messagesQueue.Enqueue(ambiguous);
        Repair(contact, a, new(a) { ["place"] = "35 4th Avenue" });
        Equal("Location unavailable", ambiguous.messageData!["place"]);
        var before = FoodTrucksSession.Changes;
        Repair(contact, a); var after = FoodTrucksSession.Changes; Repair(contact, a);
        Equal(before + 1, after); Equal(after, FoodTrucksSession.Changes);
        unknown.messageData["place"] = "4th Avenue"; Repair(contact, a);
        Equal("4th Avenue", unknown.messageData["place"]);
    }
    static void LegacyReminders(string root)
    {
        var locale = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(Path.Combine(root, "unity/FoodTrucks/Locales/en.json")));
        var label = (string)locale["foodtrucks:pitch_midtown-fourth-avenue"]!;
        var otherLabel = (string)locale["foodtrucks:pitch_lower-sixth-avenue"]!;
        if (string.IsNullOrWhiteSpace(otherLabel)) throw new Exception("Missing real locale label");
        Equal("Fourth Avenue, Midtown", label);
        // Exact old reminder shape: event/place/start/end/booked; no day or stable IDs.
        var legacy = new Dictionary<string, string> { ["event"] = "weekend market", ["place"] = label,
            ["start"] = "10:00", ["end"] = "14:00", ["booked"] = "2" };
        var message = Message(legacy, "foodtrucks:message_city_event_today");
        var c = new Contact(); c.messagesQueue.Enqueue(message);
        var candidate = new Dictionary<string, string> { ["event"] = "weekend market", ["day"] = "4", ["place"] = "34 4th Avenue",
            ["legacyPlace"] = label, ["start"] = "10:00", ["end"] = "14:00", ["fee"] = "$80" };
        message.timestamp!.Day = 3; Repair(c, candidate); Equal(label, message.messageData!["place"]);
        message.timestamp = null; Repair(c, candidate); Equal(label, message.messageData["place"]);
        message.timestamp = new() { Day = 0 }; Repair(c, candidate); Equal(label, message.messageData["place"]);
        message.timestamp.Day = 4;
        Repair(c, candidate, new(candidate) { ["place"] = "35 4th Avenue" });
        Equal(label, message.messageData["place"]);
        foreach (var field in new[] { "day", "start", "event", "end" })
        {
            Repair(c, new Dictionary<string, string>(candidate) { [field] = "different" }); Equal(label, message.messageData["place"]);
        }
        Repair(c, candidate); Equal("34 4th Avenue", message.messageData["place"]);
        Equal(false, message.messageData.ContainsKey("day"));
        foreach (var eventName in new[] { (string)locale["foodtrucks:event_market"]!, (string)locale["foodtrucks:event_festival"]! })
        foreach (var key in new[] { "foodtrucks:message_city_event", "foodtrucks:message_city_event_booked", "foodtrucks:message_city_event_today" })
        {
            // Match the 0.10.7 payloads: booked has no end/fee; today has no day/fee.
            var data = new Dictionary<string, string>(legacy) { ["event"] = eventName };
            if (key != "foodtrucks:message_city_event_today")
            {
                data["day"] = "4"; data["weekday"] = "Thursday";
            }
            if (key == "foodtrucks:message_city_event")
            {
                data.Remove("booked"); data["places"] = "4"; data["fee"] = "$80";
            }
            if (key == "foodtrucks:message_city_event_booked") data.Remove("end");
            var own = Message(data, key); var readable = Message(new(data) { ["place"] = "34 4th Avenue" }, key);
            // Even stable IDs must not authorize overwriting another place's readable history.
            var different = Message(new(data) { ["place"] = otherLabel, ["bookingId"] = "1" }, key);
            var history = new Contact(); history.messagesQueue.Enqueue(own); history.messagesQueue.Enqueue(readable); history.messagesQueue.Enqueue(different);
            var identified = new Dictionary<string, string>(candidate) { ["bookingId"] = "1", ["event"] = eventName };
            var conflict = key == "foodtrucks:message_city_event" ? "fee" : "event";
            Repair(history, new Dictionary<string, string>(identified) { [conflict] = conflict == "fee" ? "$120" : eventName == "weekend market" ? "street festival" : "weekend market" });
            Equal(label, own.messageData!["place"]);
            Repair(history, identified, new(identified)); Equal(label, own.messageData["place"]);
            Repair(history, identified); Equal("34 4th Avenue", own.messageData["place"]);
            Equal(otherLabel, different.messageData!["place"]); Equal("34 4th Avenue", readable.messageData!["place"]);
            var changes = FoodTrucksSession.Changes; Repair(history, identified); Equal(changes, FoodTrucksSession.Changes);
        }
        // Other keys retain the unreadable-place gate even if a matching legacy label is supplied.
        var catering = Message(new(Description()) { ["place"] = label }); c.messagesQueue.Enqueue(catering);
        Repair(c, new Dictionary<string, string>(Description()) { ["legacyPlace"] = label }); Equal(label, catering.messageData!["place"]);
    }
    static void SavedBookingRadius()
    {
        // No live gig or native address data is needed, even for a readable or absent address.
        World.Found = null; Equal(false, EventLocations.Ready);
        foreach (var place in new[] { "34 4th Avenue", "34 ba:street_fourthavenue", "Location unavailable", "" })
        {
            var contact = new Contact();
            var saved = Message(new() { ["place"] = place }, "foodtrucks:message_bookings_booked");
            contact.messagesQueue.Enqueue(saved);
            var existing = Message(new() { ["metres"] = "75", ["place"] = place }, "foodtrucks:message_bookings_booked");
            contact.messagesQueue.Enqueue(existing);
            var offer = Message(new() { ["place"] = place }); contact.messagesQueue.Enqueue(offer);
            var changes = FoodTrucksSession.Changes;
            EventLocations.RefreshBookingRadius(contact);
            Equal("60", saved.messageData!["metres"]); Equal(place, saved.messageData["place"]);
            Equal("75", existing.messageData!["metres"]); Equal(false, offer.messageData!.ContainsKey("metres"));
            Equal(changes + 1, FoodTrucksSession.Changes);
            EventLocations.RefreshBookingRadius(contact); Equal(changes + 1, FoodTrucksSession.Changes);
        }
    }

    static void OrphanHistory()
    {
        World.Found = null; // History's own location, never a surviving gig/building.
        foreach (var raw in new[] { "ba:street_fifthavenue#19", "19 ba:street_fifthavenue", "19 #ba:street_fifthavenue" })
        {
            var contact = new Contact();
            var offer = Message(new(Description()) { ["place"] = raw });
            offer.messageData!.Remove("locationId"); offer.messageData.Remove("bookingId");
            var booked = Message(new(offer.messageData), "foodtrucks:message_bookings_booked");
            contact.messagesQueue.Enqueue(offer); contact.messagesQueue.Enqueue(booked);
            Streets.AddressHelper.Street = "5th Avenue";
            EventLocations.RefreshBookingRadius(contact);
            EventLocations.RefreshCateringHistory(contact, Array.Empty<Dictionary<string,string>>());
            Equal("19 5th Avenue", offer.messageData["place"]);
            Equal("19 5th Avenue", booked.messageData!["place"]);
            Equal("60", booked.messageData["metres"]);
            Equal("This booking is no longer active.", booked.messageData["guidance"]);
            Equal(2, contact.messagesQueue.Count);
            var changes = FoodTrucksSession.Changes;
            // Serializing/reloading the actual payload must require no second dirty write.
            booked.messageData = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string,string>>(
                Newtonsoft.Json.JsonConvert.SerializeObject(booked.messageData));
            EventLocations.RefreshBookingRadius(contact);
            EventLocations.RefreshCateringHistory(contact, Array.Empty<Dictionary<string,string>>());
            Equal(changes, FoodTrucksSession.Changes);
        }
        foreach (var street in new[] { "ba:street_missing", "", "{street}", "<sprite=5>", "THROW" })
        {
            Streets.AddressHelper.Street = street;
            Equal("11 10th Avenue", EventLocations.HistoricalAddress("11 ba:street_tenthavenue"));
            Equal("19 5th Avenue", EventLocations.HistoricalAddress("ba:street_fifthavenue#19"));
            Equal("10 Broadway", EventLocations.HistoricalAddress("ba:street_broadwaystreet#10"));
            Equal("9 Unknown Street", EventLocations.HistoricalAddress("ba:street_unknownstreet#9"));
            Equal(null, EventLocations.HistoricalAddress("unidentifiable"));
        }
        Streets.AddressHelper.Street = "THROW";
        foreach (var example in new[] { ("thirteenthstreet", "13th Street"), ("twentyfirstavenue", "21st Avenue"),
            ("42ndstreet", "42nd Street"), ("west42ndstreet", "West 42nd Street"), ("westbroadway", "West Broadway") })
            Equal("9 " + example.Item2, EventLocations.HistoricalAddress("ba:street_" + example.Item1 + "#9"));
        foreach (var malformed in new[] { "ba:street_odd-key#9", "ba:street_fifthavenue#0", "ba:street_42oops#3" })
        {
            var bad = Message(new(Description()) { ["place"] = malformed, ["locationId"] = "" });
            var history = new Contact(); history.messagesQueue.Enqueue(bad);
            EventLocations.RefreshCateringHistory(history, Array.Empty<Dictionary<string,string>>());
            Equal("Location unavailable", bad.messageData!["place"]);
        }
        // Safe upgrade only for text that this migration explicitly marked as fallback.
        var fallback = Message(new(Description()) { ["place"] = "34 ba:street_fourthavenue" });
        var upgrades = new Contact(); upgrades.messagesQueue.Enqueue(fallback);
        EventLocations.RefreshCateringHistory(upgrades, Array.Empty<Dictionary<string,string>>());
        Equal("34 4th Avenue", fallback.messageData!["place"]);
        Streets.AddressHelper.Street = "Fourth Avenue";
        EventLocations.RefreshCateringHistory(upgrades, Array.Empty<Dictionary<string,string>>());
        Equal("34 Fourth Avenue", fallback.messageData["place"]); Equal(false, fallback.messageData.ContainsKey("addressFallback"));
        Streets.AddressHelper.Street = "4th Avenue";
        var c = new Contact();
        var live = Message(Description(), "foodtrucks:message_bookings_booked"); c.messagesQueue.Enqueue(live);
        EventLocations.RefreshCateringHistory(c, new[] { Description() });
        Equal("Find the building on the city map: Street food > Catering bookings.", live.messageData!["guidance"]);
        EventLocations.RefreshCateringHistory(c, Array.Empty<Dictionary<string,string>>());
        Equal("This booking is no longer active.", live.messageData["guidance"]);
        var stable = Message(new(Description()) { ["place"] = "Location unavailable" }); c.messagesQueue.Enqueue(stable);
        EventLocations.RefreshCateringHistory(c, Array.Empty<Dictionary<string,string>>());
        Equal("34 4th Avenue", stable.messageData!["place"]); // Explicit locationId survives the gig.
        var readable = Message(new(Description()) { ["place"] = "34 Fourth Avenue" }); c.messagesQueue.Enqueue(readable);
        EventLocations.RefreshCateringHistory(c, Array.Empty<Dictionary<string,string>>());
        Equal("34 Fourth Avenue", readable.messageData!["place"]);
    }

}
class Address { public string streetName = ""; public int streetNumber; }
class Building { public Address? Address; }
class CityBuildingController { public Building? building; }
namespace FoodTrucks
{
    static class World
    {
        internal static CityBuildingController? Found;
        internal static List<CityBuildingController> AllBuildings() => Found == null ? new() : new() { Found };
        internal static CityBuildingController? BuildingAt(string key) => key == "ba:street_fourthavenue#34" ? Found : null;
    }
    static class FoodTrucksSession { internal static bool CanWrite=true; internal static int Changes; internal static void MarkChanged() => Changes++; }
    static class Notices
    {
        internal static void Show(UI.Notification.NotificationType type, string key, Dictionary<string, string> data, float seconds) { }
    }
    static class CityPitches { internal static string Text(string key) => key switch {
        "foodtrucks:booking_inactive" => "This booking is no longer active.",
        "foodtrucks:booking_map_hint" => "Find the building on the city map: Street food > Catering bookings.",
        _ => "Location unavailable" }; }
}
namespace Streets
{
    static class AddressHelper
    {
        internal static string Street = "4th Avenue";
        internal static IEnumerable<string> StreetDataNames => new[] { "ba:street_fourthavenue" };
        internal static string GetStreetNameLocalized(this Address a) => Street;
        internal static string GetStreetNameLocalized(this string key) => Street == "THROW" ? throw new Exception("missing street data") : Street;
        internal static string ToFormattedString(this Address a) => $"{a.streetNumber} {a.GetStreetNameLocalized()}";
    }
}
namespace UI.Guiders
{
    enum DirectionGuiderType { Destination }
    static class GuidersManager
    {
        internal static Address? Target;
        internal static void SetGuiderTarget(Address a, DirectionGuiderType type) => Target = a;
    }
}

namespace UI.Notification { enum NotificationType { Success } }

namespace Entities
{
    class Timestamp { public int Day; }
    class TextMessage { public string? messageKey; public Dictionary<string,string>? messageData; public Timestamp? timestamp; }
    class Contact { public Queue<TextMessage> messagesQueue = new(); }
}
