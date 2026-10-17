#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BAModAPI;
using BigAmbitions.ModsInternal;
using FoodTrucks.Core.Saves;
using UI.Load;
using UnityEngine.SceneManagement;

namespace FoodTrucks
{
    // The mod's data in a save when the city part didn't attach, and the late attach itself. These are game-start patches,
    // so they work whatever the game's mod loader did with the city part.
    //
    // Blind playtest 2 (17 October 2026): a load from the city came up with neither Food Trucks' nor Driven Rides' city
    // part loaded, and with no error from the game. The likely cause, from the game's code (not yet seen in a log; the
    // late attach logs the loader's state to tell): the game's mod discovery runs again when its window gets the focus
    // after a file changed in ModsLocal or the Workshop folder, and it empties its list of mod entries while it awaits
    // Steam (ModDiscoveryRegistry.OnApplicationFocusChanged, DiscoverAllModsAsync). A loading screen that reads that list
    // meanwhile skips the mods: the way to the main menu doesn't unload the city part, so the next city load takes the
    // old one as still loaded and returns, or the city load finds no city entries at all [code, 1.0: LoadingScreen.
    // LoadAsyncScenes, ModLifecycleLoader.LoadScopeAsync]. That can happen in normal play as well, after a mod install
    // or a Workshop update and a click back into the game during a load.
    //
    // The game keeps GameInstance.modData as it was read, so a save in that state still holds the block the save was
    // loaded with. The guard makes sure of it before every save file is written, and says so loudly.
    internal static class SaveGuard
    {
        // The game instance the late-attach notice and the unattached-save notice were last shown for.
        private static GameInstance? lateNoticeFor;
        private static GameInstance? saveNoticeFor;

        // SaveGameManager.InitializeLoadedSave: the block the save file came with.
        public static void LoadedPostfix()
        {
            try
            {
                var current = SaveGameManager.Current;
                if (current == null) return;
                string? block = null;
                current.modData?.TryGetValue(FoodTrucksSession.SaveKey, out block);
                FoodTrucksSession.Keeper.Loaded(current, block);
                Log.Info($"Save data in the save file: {SaveBlockKeeper.Describe(block)}.");
            }
            catch (Exception ex) { Log.Error("Save data on load: " + ex); }
        }

        // SaveGameSerializationHelper.SerializeJsonData and SerializeBinaryData, before the game writes the save: the
        // game's own instance holds the mod's block, the one written this save or else the last one read or written.
        // A copy the game writes on another thread (an old save's move to its character folder) is a copy of a loaded
        // save as read, so it is left alone.
        public static void SerializePrefix(GameInstance instance)
        {
            try
            {
                if (instance == null || !ReferenceEquals(instance, SaveGameManager.Current)) return;
                string? current = null;
                instance.modData?.TryGetValue(FoodTrucksSession.SaveKey, out current);
                var outcome = FoodTrucksSession.Keeper.BeforeSave(instance, current, out var restore);
                if (outcome == SaveBlockOutcome.Restored)
                {
                    instance.modData ??= new Dictionary<string, string>();
                    instance.modData[FoodTrucksSession.SaveKey] = restore!;
                    Log.Error($"Save data: the game had no Food Trucks block to save; put back the last one read or written ({SaveBlockKeeper.Describe(restore)}).");
                }
                if (FoodTrucksSession.IsAttachedTo(instance)) return;

                var kept = outcome == SaveBlockOutcome.None ? "no Food Trucks block (none was loaded or written for this game)"
                    : $"the Food Trucks block as loaded or last written ({SaveBlockKeeper.Describe(outcome == SaveBlockOutcome.Restored ? restore : current)})";
                Log.Error($"Save data: '{instance.SaveGameName}' is being saved while the mod isn't attached to this game ({LoaderState()}); " +
                          $"it keeps {kept}, and nothing the mod would have done since is in it.");
                if (!ReferenceEquals(saveNoticeFor, instance))
                {
                    saveNoticeFor = instance;
                    CartSelling.Notify("foodtrucks:notification_saved_detached");
                }
            }
            catch (Exception ex) { Log.Error("Save data before saving: " + ex); }
        }

        // GlobalEvents.InvokeOnGameLoaded, after the game's loaded callbacks (the city part's attach among them): a city up
        // with a game the mod isn't attached to gets the session now.
        public static void GameLoadedPostfix()
        {
            try
            {
                var current = SaveGameManager.Current;
                if (!CityUp(current) || FoodTrucksSession.IsAttachedTo(current)) return;
                Log.Error($"Late attach: the city is up with '{current!.SaveGameName}' but the mod's city part didn't attach ({LoaderState()}). Attaching now.");
                try { ModItems.RegisterIcons("late attach"); }
                catch (Exception ex) { Log.Error("Item icons: " + ex); }
                FoodTrucksSession.SubscribeGameEvents();
                FoodTrucksSession.Attach(current);
                if (FoodTrucksSession.IsAttachedTo(current))
                {
                    Log.Error("Late attach: done; the mod runs for this game. Other mods' city parts may be missing too: load the save again if anything else looks wrong.");
                    if (!ReferenceEquals(lateNoticeFor, current))
                    {
                        lateNoticeFor = current;
                        CartSelling.Notify("foodtrucks:notification_late_attach");
                    }
                }
                else Log.Error("Late attach: failed; saves keep the block the save was loaded with.");
            }
            catch (Exception ex) { Log.Error("Late attach: " + ex); }
        }

        // ModDiscoveryRegistry.DiscoverAllModsAsync: the game's mod discovery empties its list of mod entries until it
        // ends; a loading screen that reads the list meanwhile skips the mods. Logged with its end, so a log can tell
        // whether it overlapped a load.
        public static void DiscoveryPrefix()
        {
            try { Log.Info($"Game mod discovery: running again (its mod list is empty until it ends); loading {(LoadScene.isLoading ? "on" : "off")}."); }
            catch (Exception) { }
        }

        public static void DiscoveryUpdated()
        {
            try { Log.Info($"Game mod discovery: done, {ModDiscoveryRegistry.GetEntries(ModActivationScope.City).Count} city entries; loading {(LoadScene.isLoading ? "on" : "off")}."); }
            catch (Exception) { }
        }

        private static bool CityUp(GameInstance? current) =>
            current != null && !LoadScene.isLoading && !GameManager.isCitySceneBeingUnloaded &&
            SceneManager.GetSceneByName("MainScene").isLoaded;

        // The game's mod loader for the log: whether its city scope counts as loaded, with this mod in it, and how many
        // city entries its discovery holds.
        private static string LoaderState()
        {
            try
            {
                var scopeLoaded = ModLifecycleLoader.IsScopeLoaded(ModActivationScope.City);
                var ours = FoodTrucksMod.ModId != null && ModLifecycleLoader.ActiveModIdsByScope.TryGetValue(ModActivationScope.City, out var ids) &&
                           ids.Contains(FoodTrucksMod.ModId);
                var entries = ModDiscoveryRegistry.GetEntries(ModActivationScope.City);
                var oursFound = entries.Any(e => e?.ModId == FoodTrucksMod.ModId);
                return $"game's city mod scope loaded {scopeLoaded}, this mod in it {ours}, {entries.Count} city entries discovered, this mod's {(oursFound ? "among them" : "not among them")}";
            }
            catch (Exception ex)
            {
                return "game's mod loader state unknown: " + ex.Message;
            }
        }
    }
}
