using FoodTrucks;
using Entities;

static class Program
{
    static void Equal(object? a, object? b) { if (!Equals(a, b)) throw new Exception($"Expected {a}, got {b}"); }
    static void Main()
    {
        var address = new Address { streetName = "ba:street_fourthavenue", streetNumber = 34 };
        World.Found = new BuildingController { building = new Building { Address = address } };
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
        World.Found = new BuildingController { building = new Building { Address = address } };
        var contact = new Contact();
        var old = new TextMessage { messageKey = "foodtrucks:message_bookings_booked", messageData = new() { ["day"] = "4", ["start"] = "10:00", ["kind"] = "party", ["place"] = "34 #ba:street_fourthavenue" } };
        contact.messagesQueue.Enqueue(old);
        var descriptions = new[] { new Dictionary<string,string> { ["day"] = "4", ["start"] = "10:00", ["kind"] = "party", ["place"] = "34 4th Avenue" } };
        EventLocations.RefreshMessages(contact, descriptions);
        Equal("34 4th Avenue", old.messageData["place"]);
        Equal(1, FoodTrucksSession.Changes);
        EventLocations.RefreshMessages(contact, descriptions);
        Equal(1, FoodTrucksSession.Changes);
        old.messageData["place"] = "keep ambiguous";
        var ambiguous = descriptions.Concat(new[] { new Dictionary<string,string>(descriptions[0]) { ["place"] = "35 4th Avenue" } });
        EventLocations.RefreshMessages(contact, ambiguous);
        Equal("keep ambiguous", old.messageData["place"]);
        old.messageData["day"] = "3";
        EventLocations.RefreshMessages(contact, descriptions);
        Equal("keep ambiguous", old.messageData["place"]);
        Console.WriteLine("PASS: native event addresses, missing data, catering and GPS");
    }
}
class Address { public string streetName = ""; public int streetNumber; }
class Building { public Address? Address; }
class BuildingController { public Building? building; }
namespace FoodTrucks
{
    static class World
    {
        internal static BuildingController? Found;
        internal static List<BuildingController> AllBuildings() => Found == null ? new() : new() { Found };
        internal static BuildingController? BuildingAt(string key) => key == "ba:street_fourthavenue#34" ? Found : null;
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
    class TextMessage { public string? messageKey; public Dictionary<string,string>? messageData; }
    class Contact { public Queue<TextMessage> messagesQueue = new(); }
}
