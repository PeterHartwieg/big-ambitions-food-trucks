#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BigAmbitions.Items;
using BigAmbitions.SaveSystem;
using UnityEngine;

namespace FoodTrucks
{
    // The mod's own game items (plan, "New foods"): dumplings, tacos, chopped cheese and grilled corn, and the
    // dumpling steamer, griddle and corn grill that make them. Built by the build editor's ItemAssets.
    //
    // Registered once at mod load, as the Bakery mod registers its goods; the game re-adds registered mod items
    // whenever it reloads its item list (ItemsGetter.OnItemsLoaded). Icons go into the game's private icon caches
    // by reflection: ItemHelper.ItemIconsCache (item name to sprite) and AddressableResolver.CachedAddressLookups,
    // which says the icon's address exists so the item panel asks the cache instead of showing a cardboard box.
    // The game clears the address lookups on every city load (AddressableResolver.Init), so the icons go back in
    // at every city load too. If the mod is removed, the game takes these items out of saves and refunds what was
    // paid for them (CompatibilityItemValidator; only stock with a price per unit is refunded).
    internal static class ModItems
    {
        private const string Folder = "Assets/Mods/FoodTrucks/Items/";
        internal const string Prefix = "foodtrucks:itemname_";

        public static readonly string[] Foods =
        {
            Prefix + "dumplings", Prefix + "tacos", Prefix + "choppedcheese", Prefix + "grilledcorn",
        };

        public static readonly string[] Machines =
        {
            Prefix + "dumplingsteamer", Prefix + "griddle", Prefix + "corngrill",
        };

        // Item name, asset file in Items/.
        private static readonly (string Name, string Asset)[] Assets =
        {
            (Prefix + "dumplings", "Dumplings"),
            (Prefix + "tacos", "Tacos"),
            (Prefix + "choppedcheese", "ChoppedCheese"),
            (Prefix + "grilledcorn", "GrilledCorn"),
            (Prefix + "dumplingsteamer", "DumplingSteamer"),
            (Prefix + "griddle", "Griddle"),
            (Prefix + "corngrill", "CornGrill"),
        };

        private static readonly List<string> registered = new List<string>();
        private static readonly Dictionary<string, Sprite> icons = new Dictionary<string, Sprite>();

        public static bool IsRegistered(string itemName) => registered.Contains(itemName);

        // The part of an item name after "itemname_", which the game uses for its prefab and icon file names.
        private static string Id(string itemName) => itemName.Substring(Prefix.Length);

        // The game's icon address for an item (PlayerHelper.GetItemNameIconPath).
        private static string IconAddress(string itemName) => "ItemIcons/" + Id(itemName) + ".png";

        public static void Register(AssetBundle bundle)
        {
            foreach (var (name, asset) in Assets)
            {
                try { RegisterOne(bundle, name, asset); }
                catch (Exception ex) { Log.Error($"Item {name}: not registered: {ex.Message}"); }
            }
            RegisterIcons("mod load");
        }

        private static void RegisterOne(AssetBundle bundle, string name, string asset)
        {
            var item = bundle.LoadAsset<Item>(Folder + asset + ".asset");
            if (item == null)
            {
                Log.Error($"Item {name}: not registered: {Folder}{asset}.asset is missing from the bundle.");
                return;
            }
            if (item.itemName != name)
            {
                Log.Error($"Item {name}: not registered: the asset is named '{item.itemName}'.");
                return;
            }
            // The game keeps its own item when a name collides, and only logs a warning.
            if (ItemsGetter.AllItems?.Any(i => i != null && i.itemName == name) == true && !ItemsGetter.IsModItem(name))
            {
                Log.Error($"Item {name}: not registered: the game already has an item with this name.");
                return;
            }
            ItemsGetter.RegisterModItem(item);
            if (!ItemsGetter.IsModItem(name))
            {
                Log.Error($"Item {name}: not registered: the game refused it (see the warning above).");
                return;
            }
            registered.Add(name);
            var icon = bundle.LoadAsset<Sprite>(Folder + "Icons/" + Id(name) + ".png");
            if (icon != null) icons[name] = icon;
            Log.Info($"Item {name}: registered ({(item.isFurniture ? "machine" : "food")}, price {item.DefaultMarketPrice:0.##}, " +
                     $"wholesale {item.wholesalePrice:0.##}, box {item.boxSize}, icon {(icon != null ? "yes" : "missing")}).");
        }

        // Puts the icons into the game's caches; safe to call any number of times.
        public static void RegisterIcons(string when)
        {
            if (registered.Count == 0) return;
            const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            var cache = typeof(ItemHelper).GetField("ItemIconsCache", Static)?.GetValue(null) as Dictionary<string, Sprite>;
            var lookups = typeof(AddressableResolver).GetField("CachedAddressLookups", Static)?.GetValue(null) as Dictionary<string, string>;
            if (cache == null || lookups == null)
            {
                Log.Error($"Item icons ({when}): not registered: ItemHelper.ItemIconsCache {(cache == null ? "not found" : "found")}, " +
                          $"AddressableResolver.CachedAddressLookups {(lookups == null ? "not found" : "found")}. The items show as boxes.");
                return;
            }
            var count = 0;
            foreach (var name in registered)
            {
                if (!icons.TryGetValue(name, out var icon) || icon == null) continue;
                // The cache first: once the address is known, the game reads the cache and never Addressables.
                cache[name] = icon;
                var address = IconAddress(name);
                lookups[address.ToLowerInvariant()] = address;
                count++;
            }
            Log.Info($"Item icons ({when}): {count} of {registered.Count} registered.");
        }

        public static void Unregister()
        {
            const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;
            var cache = typeof(ItemHelper).GetField("ItemIconsCache", Static)?.GetValue(null) as Dictionary<string, Sprite>;
            var lookups = typeof(AddressableResolver).GetField("CachedAddressLookups", Static)?.GetValue(null) as Dictionary<string, string>;
            foreach (var name in registered)
            {
                ItemsGetter.UnregisterModItem(name);
                lookups?.Remove(IconAddress(name).ToLowerInvariant());
                cache?.Remove(name);
            }
            registered.Clear();
            icons.Clear();
        }
    }
}
