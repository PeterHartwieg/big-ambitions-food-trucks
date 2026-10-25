using FoodTrucks;
using FoodTrucks.Core.Saves;

static class Program
{
    static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    static void Main()
    {
        var state = new SaveState();
        Catering.Reset();
        for (var minute = 0; minute < 5; minute++) Catering.Run(state);
        Check(SaveGameManager.Current.Contacts.Count == 0, "missing history must not create a contact");
        var contact = new Contact { id = "foodtrucks-bookings" };
        SaveGameManager.Current.Contacts.Add(contact);
        Catering.Run(state);
        Check(EventLocations.Radii == 1 && EventLocations.Addresses == 0, "radius migrates without native addresses");
        Catering.Run(state);
        Check(EventLocations.Radii == 1, "radius must not rescan each minute");
        EventLocations.Ready = true;
        for (var minute = 0; minute < 5; minute++) Catering.Run(state);
        Check(EventLocations.Radii == 1 && EventLocations.Addresses == 1, "address migration runs once when ready");
        Catering.Reset(); // Next load must migrate its own saved history.
        Catering.Run(state);
        Check(EventLocations.Radii == 2 && EventLocations.Addresses == 2, "load resets both migrations");
        Catering.Reset();
        Commissary.Uninstalled = true;
        Catering.Run(state);
        Check(EventLocations.Radii == 2 && EventLocations.Addresses == 2, "uninstall skips pending migration even with a contact");
        SaveGameManager.Current.Contacts.Clear();
        for (var minute = 0; minute < 5; minute++) Catering.Run(state);
        Check(SaveGameManager.Current.Contacts.Count == 0, "uninstall then wait must leave contact absent");
        Console.WriteLine("PASS: bookings migration lifecycle");
    }
}
namespace FoodTrucks
{
    static class Commissary { internal static bool Uninstalled; }
    class Contact { internal string id = ""; }
    class Game { internal List<Contact> Contacts = new(); }
    static class SaveGameManager { internal static Game Current = new(); }
    static class EventLocations
    {
        internal static bool Ready;
        internal static int Radii, Addresses;
        internal static string? ReadableAddress(string place) => "34 4th Avenue";
        internal static void RefreshBookingRadius(Contact contact) => Radii++;
        internal static void RefreshCateringHistory(Contact contact, IEnumerable<Dictionary<string, string>> gigs) { _ = gigs.ToList(); }
        internal static void RefreshMessages(Contact contact, IEnumerable<Dictionary<string, string>> descriptions)
        { _ = descriptions.ToList(); Addresses++; }
    }
}
