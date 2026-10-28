// Commissary address on the phone contact and in BizMan: the production postfix, location accessor and rented-space
// panel against controlled stand-ins for the game's contact header, map, guider and street names. Harmony is emulated:
// the postfixes Patches.cs registers on ContactsApp.SetUpHeader run after the game's header, their parameters bound
// by name as Harmony binds them (the original's parameter names, ___ plus a private field's name).
using System.Reflection;
using Entities;
using FoodTrucks;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Saves;
using UI.Smartphone.Apps.Contacts;

static class Program
{
    static int checks;
    static void Equal(object? expected, object? actual, string what)
    {
        checks++;
        if (!Equals(expected, actual)) throw new Exception($"{what}: expected {expected ?? "null"}, got {actual ?? "null"}");
    }

    static readonly Dictionary<string, string> StreetNames = new()
    {
        ["ba:street_twentyfirststreet"] = "21st Street", ["ba:street_twelfthstreet"] = "12th Street",
        ["ba:street_secondstreet"] = "2nd Street", ["ba:street_secondavenue"] = "2nd Avenue", ["ba:street_fifthstreet"] = "5th Street",
        ["ba:street_fourthavenue"] = "4th Avenue",
    };
    static readonly Dictionary<string, string> Expected = new()
    {
        ["industry-city"] = "2 21st Street", ["lower-manhattan"] = "5 12th Street", ["garment-district"] = "51 2nd Street",
        ["hells-kitchen"] = "8 2nd Avenue", ["murray-hill"] = "12 5th Street",
    };

    static void Main(string[] args)
    {
        var root = args[0];
        Localizor.LocalizorManager.Load(Path.Combine(root, "unity/FoodTrucks/Locales/en.json"));
        DemandData.Commissaries = CommissaryTables.Parse(File.ReadAllText(Path.Combine(root, "data/commissaries.json")));
        DemandData.Rules = new RulesStub { Commissary = new CommissaryRules() };
        Streets.AddressHelper.Names = StreetNames;
        foreach (var site in DemandData.Commissaries.Sites)
        {
            var hash = site.Address.LastIndexOf('#');
            World.Add(site.Address, new CityBuildingController { building = new Building { Address = new Address { streetName = site.Address[..hash], streetNumber = int.Parse(site.Address[(hash + 1)..]) } } });
        }
        var vanillaShop = new Address { streetName = "ba:street_fourthavenue", streetNumber = 34 };
        World.Add("ba:street_fourthavenue#34", new CityBuildingController { building = new Building { Address = vanillaShop } });
        Helpers.InstanceBehavior<CityManager>.Instance = new CityManager { cityMap = new CityMap() };
        Helpers.InstanceBehavior<UI.UIs>.Instance = new UI.UIs();
        Equal(5, Expected.Count, "sites under test");
        foreach (var id in Expected.Keys) Equal(true, DemandData.Commissaries.Site(id) != null, "shipped site " + id);

        Header(vanillaShop);
        BizMan();
        Messages();
        if (Log.Errors.Count > 0) throw new Exception("Errors logged: " + string.Join("; ", Log.Errors));
        Console.WriteLine($"PASS: commissary address header, BizMan and messages ({checks} checks)");
    }

    static Contact Commissary(string id) => new() { id = FoodTrucks.Commissary.ContactPrefix + id, description = "foodtrucks:commissary_description", callDialogTypeOverride = 9103 };

    // The game's header, then the registered postfixes, as Harmony runs them.
    static void Open(ContactsApp app, Contact contact)
    {
        app.SetUpHeader(contact);
        foreach (var name in Hooks.HeaderPostfixes)
        {
            var method = typeof(ModContacts).GetMethod(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                ?? throw new Exception("Registered postfix not found: ModContacts." + name);
            var values = method.GetParameters().Select(p =>
            {
                if (p.Name == "contact") return contact;
                if (p.Name == "__instance") return app;
                if (p.Name!.StartsWith("___"))
                    return typeof(ContactsApp).GetField(p.Name[3..], BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(app)
                        ?? throw new Exception("No ContactsApp field " + p.Name[3..]);
                throw new Exception("Harmony cannot bind parameter " + p.Name);
            }).ToArray();
            method.Invoke(null, values);
        }
    }

    // What the header shows: address line, View on map, call button, opening hours.
    // Listeners count on a shown button; the game leaves old ones on a hidden one.
    static string Shown(ContactsApp app) =>
        $"address {(app.Address.activeSelf ? app.AddressLabel.Value : "hidden")}; map {Button(app.ViewOnMap)}; call {Button(app.Call)}; hours {(app.OpeningHours.activeSelf ? "shown" : "hidden")}";
    static string Button(UnityEngine.UI.Button b) => b.gameObject.activeSelf ? "shown x" + b.onClick.Count : "hidden";

    static string VanillaOnly(Contact contact)
    {
        var plain = new ContactsApp();
        plain.SetUpHeader(contact);
        return Shown(plain);
    }

    static void Header(Address vanillaShop)
    {
        var app = new ContactsApp();
        var map = Helpers.InstanceBehavior<CityManager>.Instance.cityMap!;
        var menu = Helpers.InstanceBehavior<UI.UIs>.Instance.fullMenu;
        // An Industry City contact from an older save: made by EnsurePermanentContact, so no address of its own, with a space
        // rented there; it shows its building's address without another visit.
        var state = new SaveState();
        state.Commissary.Unlocked.Add("industry-city");
        state.Commissary.Spaces.Add(new CommissarySpace { VehicleId = "cart-1", Kind = "cart", CommissaryId = "industry-city", SinceDay = 3 });
        var industry = Commissary("industry-city");
        Equal(true, industry.Address.IsUndefined(), "old contact has no address");
        Open(app, industry);
        Equal("address 2 21st Street; map shown x1; call shown x1; hours hidden", Shown(app), "Industry City header");
        Equal(true, app.AddressLabel.Key == null, "address label is plain text");
        Equal(true, industry.Address.IsUndefined(), "contact address left unset");
        // The call is still the game's, through the contact's call dialog override.
        app.Call.onClick.Invoke();
        Equal("foodtrucks-commissary-industry-city open", app.Calls.Last(), "call usable");
        // View on map, map closed: the phone closes and the map opens on the building with its card.
        CityMap.IsOpen = false; menu.Shown = null;
        app.ViewOnMap.onClick.Invoke();
        Equal(false, menu.Shown, "phone closed for the map");
        Equal(1, map.Toggles, "map opened");
        Equal(World.BuildingAt("ba:street_twentyfirststreet#2"), map.Focused, "focused Industry City building");
        // Map already open: the game's FocusOnBuilding.
        map.Focused = null;
        app.ViewOnMap.onClick.Invoke();
        Equal(1, map.Toggles, "open map not toggled again");
        Equal(World.BuildingAt("ba:street_twentyfirststreet#2"), map.Focused, "open map refocused");

        // Every shipped commissary, Lower Manhattan among them.
        foreach (var (id, address) in Expected)
        {
            Open(app, Commissary(id));
            Equal($"address {address}; map shown x1; call shown x1; hours hidden", Shown(app), id + " header");
            CityMap.IsOpen = false; map.Focused = null;
            app.ViewOnMap.onClick.Invoke();
            var site = DemandData.Commissaries!.Site(id)!;
            Equal(World.BuildingAt(site.Address), map.Focused, id + " focus");
        }

        // Switching: an addressless game contact keeps none of the commissary's listener; a business contact gets only the
        // game's own; the next commissary's button goes to its own building.
        Open(app, Commissary("industry-city"));
        var fred = new Contact { id = "uncle_fred" };
        Open(app, fred);
        Equal(VanillaOnly(fred), Shown(app), "addressless game contact as the game shows it");
        Equal("address hidden; map hidden; call shown x1; hours hidden", Shown(app), "addressless game contact");
        Equal(0, app.ViewOnMap.onClick.Count, "no stale commissary listener on the hidden button");
        Open(app, Commissary("industry-city"));
        var shop = new Contact { id = "ba:contact_some_shop", Address = vanillaShop };
        Open(app, shop);
        Equal(VanillaOnly(shop), Shown(app), "business contact as the game shows it");
        Equal("34 4th Avenue", app.AddressLabel.Value, "business address");
        map.Focused = null; app.VanillaMap.Clear();
        app.ViewOnMap.onClick.Invoke();
        Equal(null, map.Focused, "commissary focus gone after switching");
        Equal("ba:contact_some_shop", app.VanillaMap.Single(), "game's own View on map");
        Open(app, Commissary("industry-city"));
        Open(app, Commissary("lower-manhattan"));
        Equal("address 5 12th Street; map shown x1; call shown x1; hours hidden", Shown(app), "Lower Manhattan after Industry City");
        CityMap.IsOpen = false; map.Focused = null;
        app.ViewOnMap.onClick.Invoke();
        Equal(World.BuildingAt("ba:street_twelfthstreet#5"), map.Focused, "switched contact focuses its own building");

        // Other contacts: untouched, the mod's bookings contact and commissary IDs the data no longer has included.
        foreach (var other in new[] { new Contact { id = "foodtrucks-bookings", callDialogTypeOverride = 9101 }, new Contact { id = "foodtrucks-commissary-gone", callDialogTypeOverride = 9103 },
                     new Contact { id = "speedy_bites" }, new Contact { id = "foodtrucks-commissaryindustry-city" } })
        {
            Open(app, Commissary("industry-city"));
            Open(app, other);
            Equal(VanillaOnly(other), Shown(app), other.id + " untouched");
        }

        // Unresolved: no building, or its street name not loaded yet. "Address unavailable", no View on map, call usable.
        foreach (var unresolved in new Action<bool>[] { on => World.Hide("ba:street_twentyfirststreet#2", on), on => Streets.AddressHelper.Broken = on ? "ba:street_twentyfirststreet" : null })
        {
            Open(app, Commissary("lower-manhattan"));
            unresolved(true);
            Open(app, Commissary("industry-city"));
            Equal("address Address unavailable; map hidden; call shown x1; hours hidden", Shown(app), "unresolved address");
            app.Call.onClick.Invoke();
            Equal("foodtrucks-commissary-industry-city open", app.Calls.Last(), "call usable while unresolved");
            // Resolves on the next open, with no new visit.
            unresolved(false);
            Open(app, Commissary("industry-city"));
            Equal("address 2 21st Street; map shown x1; call shown x1; hours hidden", Shown(app), "resolved later");
        }

        // Uninstalled: the game's header only, and a click left from before does nothing.
        Open(app, Commissary("industry-city"));
        var stale = app.ViewOnMap.onClick.Listeners.ToList();
        FoodTrucks.Commissary.Uninstalled = true;
        Open(app, Commissary("industry-city"));
        Equal(VanillaOnly(Commissary("industry-city")), Shown(app), "inert after uninstall");
        map.Focused = null; map.Toggles = 0;
        foreach (var listener in stale) listener();
        Equal(null, map.Focused, "stale click inert after uninstall");
        Equal(0, map.Toggles, "map not opened after uninstall");
        FoodTrucks.Commissary.Uninstalled = false;
    }

    static void BizMan()
    {
        var state = new SaveState();
        state.Commissary.Unlocked.AddRange(new[] { "industry-city", "lower-manhattan" });
        state.Commissary.Spaces.Add(new CommissarySpace { VehicleId = "cart-1", Kind = "cart", CommissaryId = "industry-city", SinceDay = 3 });
        state.Commissary.Spaces.Add(new CommissarySpace { VehicleId = "truck-1", Kind = "truck", CommissaryId = "lower-manhattan", SinceDay = 4 });
        foreach (var (vehicle, kind, address, key) in new[] { ("cart-1", "cart", "2 21st Street", "ba:street_twentyfirststreet#2"), ("truck-1", "truck", "5 12th Street", "ba:street_twelfthstreet#5") })
        {
            var box = Panel(state, vehicle, kind);
            Equal(address, box.RowValue("Address"), vehicle + " BizMan address");
            Equal(true, box.Paragraphs.Any(p => p.Contains("beside", StringComparison.OrdinalIgnoreCase)), vehicle + " bay described as beside the building");
            var go = box.Buttons.Single(b => b.Caption == "Set destination");
            Equal(true, go.Interactable, vehicle + " Set destination enabled");
            UI.Guiders.GuidersManager.Target = null;
            go.Click();
            Equal(World.BuildingAt(key)!.building!.Address, UI.Guiders.GuidersManager.Target, vehicle + " GPS to the building");
            Equal(true, box.Buttons.Any(b => b.Caption == "Call commissary"), vehicle + " call kept");
        }
        // Unresolved: "Address unavailable", Set destination disabled and inert.
        World.Hide("ba:street_twentyfirststreet#2", true);
        var missing = Panel(state, "cart-1", "cart");
        Equal("Address unavailable", missing.RowValue("Address"), "BizMan unresolved address");
        var disabled = missing.Buttons.Single(b => b.Caption == "Set destination");
        Equal(false, disabled.Interactable, "Set destination disabled");
        UI.Guiders.GuidersManager.Target = null;
        // Pressed anyway (its action, as wired): nothing happens while the address is unresolved.
        disabled.Click();
        Equal(null, UI.Guiders.GuidersManager.Target, "no GPS while unresolved");
        World.Hide("ba:street_twentyfirststreet#2", false);
        // No space: the panel as before, no address.
        var none = Panel(state, "cart-2", "cart");
        Equal(null, none.RowValue("Address"), "no address without a space");
        Equal(false, none.Buttons.Any(b => b.Caption == "Set destination"), "no destination without a space");
    }

    static BizManParts.Box Panel(SaveState state, string vehicle, string kind)
    {
        BizManParts.Box.Last = null;
        BizManVehiclePage.CommissarySpace(new UnityEngine.Transform(), state, vehicle, kind);
        return BizManParts.Box.Last!;
    }

    // Texts: "<key>_address" with the address when it resolves, the original key otherwise (saved messages keep theirs).
    static void Messages()
    {
        var location = typeof(ModContacts).Assembly.GetType("FoodTrucks.CommissaryLocation") ?? throw new Exception("No CommissaryLocation");
        var key = location.GetMethod("MessageKey", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        Equal("foodtrucks:message_commissary_welcome_phone_address", key.Invoke(null, new object?[] { "foodtrucks:message_commissary_welcome_phone", "2 21st Street" }), "welcome key");
        Equal("foodtrucks:message_commissary_welcome_phone", key.Invoke(null, new object?[] { "foodtrucks:message_commissary_welcome_phone", null }), "unresolved welcome key");
        var address = location.GetMethod("Address", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)!;
        Equal("2 21st Street", address.Invoke(null, new object?[] { DemandData.Commissaries!.Site("industry-city") }), "accessor");
        var text = Localizor.LocalizorManager.GetLocalization("foodtrucks:dialog_commissary_start_standing_address",
            new Dictionary<string, string> { ["commissary"] = "Industry City Commissary", ["address"] = "2 21st Street", ["spaces"] = "1 space", ["stock"] = "no items", ["orders"] = "no orders", ["standing"] = "no standing orders" });
        Equal(true, text.StartsWith("Industry City Commissary, 2 21st Street."), "call text with address");
        // An old saved welcome payload (no address) still reads in full.
        var old = Localizor.LocalizorManager.GetLocalization("foodtrucks:message_commissary_welcome_phone",
            new Dictionary<string, string> { ["commissary"] = "Industry City Commissary", ["cart"] = "$300", ["truck"] = "$1,250" });
        Equal(false, old.Contains('{'), "old welcome renders");
    }
}

// ---- Stand-ins for the game ----
class Address
{
    public string streetName = ""; public int streetNumber;
    public bool IsUndefined() => string.IsNullOrEmpty(streetName);
}
class Building { public Address? Address; }
class CityBuildingController { public Building? building; }
class CityManager { public CityMap? cityMap; }
class CityMap
{
    public static bool IsOpen;
    public CityBuildingController? buildingToFocus, Focused;
    public int Toggles;
    // The game's Toggle opens the map on buildingToFocus and selects its card.
    public void Toggle() { Toggles++; IsOpen = !IsOpen; if (IsOpen) { Focused = buildingToFocus; buildingToFocus = null; } }
    public void FocusOnBuilding(CityBuildingController controller) => Focused = controller;
}
namespace Helpers { static class InstanceBehavior<T> { public static T Instance = default!; } }
namespace UI
{
    class UIs { public FullMenu fullMenu = new(); }
    class FullMenu { public bool? Shown; public void Toggle(bool show) => Shown = show; }
}
namespace UI.Notification { enum NotificationType { Success } }
namespace UI.Guiders
{
    enum DirectionGuiderType { Destination }
    static class GuidersManager
    {
        internal static Address? Target;
        internal static void SetGuiderTarget(Address a, DirectionGuiderType type) => Target = a;
    }
}
namespace Streets
{
    static class AddressHelper
    {
        internal static Dictionary<string, string> Names = new();
        internal static string? Broken;
        internal static IEnumerable<string> StreetDataNames => Names.Keys;
        // A street whose name hasn't loaded comes back as its data ID.
        internal static string GetStreetNameLocalized(this string key) => key == Broken ? key : Names.TryGetValue(key, out var n) ? n : key;
        internal static string GetStreetNameLocalized(this Address a) => a.streetName.GetStreetNameLocalized();
        internal static string ToFormattedString(this Address a) => $"{a.streetNumber} {a.GetStreetNameLocalized()}";
    }
}
namespace UnityEngine
{
    class GameObject { public bool activeSelf = true; public void SetActive(bool value) => activeSelf = value; }
    class Component { public GameObject gameObject = new(); }
    class Transform { }
}
namespace UnityEngine.Events
{
    delegate void UnityAction();
    class UnityEvent
    {
        public readonly List<UnityAction> Listeners = new();
        public int Count => Listeners.Count;
        public void AddListener(UnityAction a) => Listeners.Add(a);
        public void RemoveListener(UnityAction a) => Listeners.Remove(a);
        public void RemoveAllListeners() => Listeners.Clear();
        public void Invoke() { foreach (var a in Listeners.ToList()) a(); }
    }
}
namespace UnityEngine.UI { class Button : UnityEngine.Component { public UnityEngine.Events.UnityEvent onClick = new(); public bool interactable = true; } }
namespace Localizor
{
    static class LocalizorManager
    {
        static Dictionary<string, string> table = new();
        public static void Load(string path) => table = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path))!;
        public static string GetLocalization(string key, object? data)
        {
            var text = table.TryGetValue(key, out var t) ? t : key;
            if (data is Dictionary<string, string> values) foreach (var (k, v) in values) text = text.Replace("{" + k + "}", v);
            return text;
        }
    }
}
namespace Localizor.LanguageChangeEvent
{
    class TextLocalizationComponent
    {
        public string? Key = "contacts_address"; public string Value = "";
        public void SetValue(string text, bool clearKey = false) { Value = text; if (clearKey) Key = null; }
    }
}
namespace Entities
{
    class Timestamp { public int Day; }
    class TextMessage { public string? messageKey; public Dictionary<string, string>? messageData; public Timestamp? timestamp; }
    class Contact
    {
        public string id = ""; public string? description; public int callDialogTypeOverride;
        public Address Address = new();
        public Queue<TextMessage> messagesQueue = new();
    }
}
namespace UI.Smartphone.Apps.Contacts
{
    // ContactsApp.SetUpHeader's address, call and View on map handling as BigAmbitions.dll has it (the fields by their
    // game names; test_commissary_address checks them against the assembly when it is available).
    class ContactsApp
    {
        private UnityEngine.UI.Button callButton = new();
        private UnityEngine.UI.Button viewOnMapButton = new();
        private UnityEngine.GameObject addressGameObject = new();
        private Localizor.LanguageChangeEvent.TextLocalizationComponent addressLabel = new();
        private UnityEngine.GameObject openingHoursGameObject = new();
        static readonly List<string> contactIdsToWithCallButton = new() { "auto_tow_service_ny", "uncle_fred", "speedy_bites" };
        public readonly List<string> Calls = new(), VanillaMap = new();
        public UnityEngine.UI.Button Call => callButton;
        public UnityEngine.UI.Button ViewOnMap => viewOnMapButton;
        public UnityEngine.GameObject Address => addressGameObject;
        public UnityEngine.GameObject OpeningHours => openingHoursGameObject;
        public Localizor.LanguageChangeEvent.TextLocalizationComponent AddressLabel => addressLabel;

        public void SetUpHeader(Contact contact)
        {
            if (contact.Address.IsUndefined())
            {
                addressGameObject.SetActive(false);
                openingHoursGameObject.SetActive(false);
                if (contact.callDialogTypeOverride != 0 || contactIdsToWithCallButton.Contains(contact.id))
                {
                    callButton.onClick.RemoveAllListeners();
                    callButton.onClick.AddListener(() => Calls.Add(contact.id + " open"));
                    callButton.gameObject.SetActive(true);
                }
                else callButton.gameObject.SetActive(false);
                viewOnMapButton.gameObject.SetActive(false);
                return;
            }
            addressLabel.Key = "contacts_address";
            addressLabel.SetValue(Streets.AddressHelper.ToFormattedString(contact.Address), clearKey: true);
            addressGameObject.SetActive(true);
            // The host business's opening hours decide whether the call counts as open.
            openingHoursGameObject.SetActive(true);
            callButton.onClick.RemoveAllListeners();
            callButton.onClick.AddListener(() => Calls.Add(contact.id + " by opening hours"));
            callButton.gameObject.SetActive(true);
            viewOnMapButton.onClick.RemoveAllListeners();
            viewOnMapButton.onClick.AddListener(() => VanillaMap.Add(contact.id));
            viewOnMapButton.gameObject.SetActive(true);
        }
    }
}

// ---- Stand-ins for the rest of the mod ----
namespace FoodTrucks
{
    static class Log
    {
        public static readonly List<string> Errors = new();
        public static void Info(string s) { }
        public static void Error(string s) => Errors.Add(s);
    }
    static class Commissary
    {
        public const string ContactPrefix = "foodtrucks-commissary-";
        public static bool Uninstalled;
        internal static string Name(CommissarySite site) => Localizor.LocalizorManager.GetLocalization(site.Name, null);
    }
    class RulesStub { public CommissaryRules? Commissary; }
    static class DemandData { public static CommissaryTables? Commissaries; public static RulesStub? Rules; }
    static class World
    {
        static readonly Dictionary<string, CityBuildingController> all = new();
        static readonly HashSet<string> hidden = new();
        internal static void Add(string key, CityBuildingController c) => all[key] = c;
        internal static void Hide(string key, bool on) { if (on) hidden.Add(key); else hidden.Remove(key); }
        internal static List<CityBuildingController> AllBuildings() => all.Values.ToList();
        internal static CityBuildingController? BuildingAt(string key) => !hidden.Contains(key) && all.TryGetValue(key, out var c) ? c : null;
    }
    static class FoodTrucksSession { internal static bool CanWrite = true; internal static void MarkChanged() { } }
    static class Notices { internal static void Show(UI.Notification.NotificationType type, string key, Dictionary<string, string> data, float seconds) { } }
    static class CityPitches { internal static string Text(string key) => Localizor.LocalizorManager.GetLocalization(key, null); }
    static class Catering { public const float PlaceMetres = 60; }
    static class CartSelling { internal static string Money(long cents) => "$" + (cents / 100m).ToString("N2"); }
    static class BizManParts
    {
        internal class Box
        {
            public static Box? Last;
            public readonly List<(string Label, string Value)> Rows = new();
            public readonly List<string> Paragraphs = new();
            public readonly List<(string Caption, Action Click, bool Interactable)> ButtonList = new();
            public IEnumerable<(string Caption, Action Click, bool Interactable)> Buttons => ButtonList;
            internal Box(UnityEngine.Transform parent, string title) => Last = this;
            internal object? Row(string first, string second, string third = "") { Rows.Add((first, second)); return null; }
            internal void Paragraph(string text) => Paragraphs.Add(text);
            internal object? Button(string caption, Action click, bool interactable = true) { ButtonList.Add((caption, click, interactable)); return null; }
            public string? RowValue(string label) => Rows.Where(r => r.Label == label).Select(r => r.Value).FirstOrDefault();
        }
    }
    static partial class BizManVehiclePage
    {
        internal static string Text(string key) => Localizor.LocalizorManager.GetLocalization(key, null);
        internal static string Format(string key, params (string Key, string Value)[] values) =>
            Localizor.LocalizorManager.GetLocalization(key, values.ToDictionary(v => v.Key, v => v.Value));
        static void Call(string siteId) { }
        internal static string ShortName(CommissarySite site) => Commissary.Name(site);
    }
}
