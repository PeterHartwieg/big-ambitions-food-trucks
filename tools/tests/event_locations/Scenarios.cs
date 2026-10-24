using FoodTrucks;
using Entities;
using FoodTrucks.Core.Catering;

static class Program
{
    static void Equal(object? a, object? b) { if (!Equals(a, b)) throw new Exception($"Expected {a}, got {b}"); }
    static void Main()
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
        LegacyReminders();
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
    static void LegacyReminders()
    {
        // Exact legacy shape: event/place/start/end/booked; no day or stable IDs.
        var legacy = new Dictionary<string, string> { ["event"] = "market", ["place"] = "#foodtrucks:pitch_midtown-fourth-avenue",
            ["start"] = "10:00", ["end"] = "14:00", ["booked"] = "2" };
        var message = Message(legacy, "foodtrucks:message_city_event_today");
        var c = new Contact(); c.messagesQueue.Enqueue(message);
        var candidate = new Dictionary<string, string> { ["event"] = "market", ["day"] = "4", ["place"] = "34 4th Avenue",
            ["legacyPlace"] = legacy["place"], ["start"] = "10:00", ["end"] = "14:00", ["fee"] = "$80" };
        message.timestamp!.Day = 3; Repair(c, candidate); Equal(legacy["place"], message.messageData!["place"]);
        message.timestamp.Day = 4;
        Repair(c, candidate, new(candidate) { ["place"] = "35 4th Avenue" });
        Equal(legacy["place"], message.messageData["place"]);
        Repair(c, candidate); Equal("34 4th Avenue", message.messageData["place"]);
        Equal(false, message.messageData.ContainsKey("day"));
        message.messageData["place"] = legacy["place"]; message.timestamp = null;
        Repair(c, candidate); Equal(legacy["place"], message.messageData["place"]);
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
    static class FoodTrucksSession { internal static int Changes; internal static void MarkChanged() => Changes++; }
    static class Notices
    {
        internal static void Show(UI.Notification.NotificationType type, string key, Dictionary<string, string> data, float seconds) { }
    }
    static class CityPitches { internal static string Text(string key) => "Location unavailable"; }
}
namespace Streets
{
    static class AddressHelper
    {
        internal static string Street = "4th Avenue";
        internal static IEnumerable<string> StreetDataNames => new[] { "ba:street_fourthavenue" };
        internal static string GetStreetNameLocalized(this Address a) => Street;
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
