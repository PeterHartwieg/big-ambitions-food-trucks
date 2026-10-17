#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BAModAPI;
using FoodTrucks.Core.Saves;
using UI.Load;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FoodTrucks
{
    // Loads with every city scene and owns the per-save session.
    [ModEntryOnCityLoad]
    public sealed class FoodTrucksCity : IModBigAmbitions
    {
        private bool loaded;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            loaded = true;
            // The game clears its icon address list on every city load; the mod's item icons go back in.
            try { ModItems.RegisterIcons("city load"); }
            catch (Exception ex) { Log.Error("Item icons: " + ex); }
            // GlobalEvents.Init() clears subscribers before each city load, so subscribe per city scope.
            GlobalEvents.RegisterOnGameLoadedLateCallback(OnCityReady);
            FoodTrucksSession.SubscribeGameEvents();
            return Task.CompletedTask;
        }

        private void OnCityReady()
        {
            if (!loaded || LoadScene.isLoading || SaveGameManager.Current == null) return;
            // Again once the city is ready, in case the address list was rebuilt after this mod's city load.
            try { ModItems.RegisterIcons("city ready"); }
            catch (Exception ex) { Log.Error("Item icons: " + ex); }
            try { FoodTrucksSession.Attach(SaveGameManager.Current); }
            catch (Exception ex) { Log.Error(ex); }
        }

        public Task OnUnloadAsync()
        {
            loaded = false;
            FoodTrucksSession.UnsubscribeGameEvents();
            FoodTrucksSession.Detach();
            return Task.CompletedTask;
        }
    }

    // The mod's state for the loaded save, read from and written to GameInstance.modData.
    internal static class FoodTrucksSession
    {
        public const string SaveKey = "foodtrucks:state";
        private static readonly SaveCodec Codec = new SaveCodec();

        private static GameInstance? game;
        private static SaveState? state;
        private static bool canWrite;
        private static GameObject? runtimeObject;

        public static LoadStatus LastLoadStatus { get; private set; }

        // The block each loaded save came with, and the last one the mod wrote, so a save never goes out without it
        // (SaveGuard).
        internal static readonly SaveBlockKeeper Keeper = new SaveBlockKeeper();

        // Whether the session is on this game instance, also read-only (data from a newer mod version, or unreadable:
        // state is null then, and the block stays in the save untouched).
        public static bool IsAttachedTo(GameInstance? current) =>
            current != null && game != null && ReferenceEquals(game, current);

        // The game's save and unload events. GlobalEvents.Init() clears subscribers before each load, so the city part
        // and a late attach subscribe again; removing first keeps one subscription each.
        public static void SubscribeGameEvents()
        {
            GlobalEvents.onSaveGame -= OnSaveGame;
            GlobalEvents.onSaveGame += OnSaveGame;
            GlobalEvents.onGameUnloaded -= OnGameUnloaded;
            GlobalEvents.onGameUnloaded += OnGameUnloaded;
        }

        public static void UnsubscribeGameEvents()
        {
            GlobalEvents.onSaveGame -= OnSaveGame;
            GlobalEvents.onGameUnloaded -= OnGameUnloaded;
        }

        private static void OnSaveGame()
        {
            // Runs synchronously before the game serializes the save. Never throw into the game's save.
            // Sales Core has made but the queue view still holds are paid first, so the save has them.
            // Apart, so a failed payment never stops the mod's data from being written.
            try { CartSelling.PayAllHeldNow(); }
            catch (Exception ex) { Log.Error(ex); }
            try { WriteToSave(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void OnGameUnloaded() => Detach();

        // False for data written by a newer mod version: it is read for tax but never changed.
        public static bool CanWrite => canWrite && CurrentState != null;

        // Null unless a save is attached and its mod data was readable.
        public static SaveState? CurrentState =>
            game != null && ReferenceEquals(game, SaveGameManager.Current) ? state : null;

        // How many times a save was attached this session (the test bridge's load completion).
        internal static int Attaches { get; private set; }

        public static void Attach(GameInstance current)
        {
            // Twice on one game (the city part after a late attach) would read the save's block again over the
            // session's unsaved changes.
            if (IsAttachedTo(current))
            {
                Log.Info("Save data: already attached to this game; nothing read again.");
                return;
            }
            Detach();
            Attaches++;
            if (current.modData == null) current.modData = new Dictionary<string, string>();
            current.modData.TryGetValue(SaveKey, out var json);
            if (string.IsNullOrWhiteSpace(json) && Keeper.BlockFor(current) is string known)
            {
                // Something took the block out of the game since its load: read the one it came with, never a fresh start.
                Log.Error($"Save data: the game's Food Trucks block went missing after the load; put back the one read with the save ({SaveBlockKeeper.Describe(known)}).");
                current.modData[SaveKey] = known;
                json = known;
            }

            var result = Codec.Load(json);
            foreach (var note in result.Notes) Log.Info(note);
            LastLoadStatus = result.Status;
            game = current;
            state = result.State;
            // Data written by a newer mod version, or unreadable data, stays in the save untouched.
            var newerWriter = state != null && ModVersion.IsNewerThan(state.WrittenByModVersion, FoodTrucksMod.Version);
            canWrite = state != null && !newerWriter;

            if (newerWriter)
                Log.Error($"Save data was written by Food Trucks {state!.WrittenByModVersion}, newer than {FoodTrucksMod.Version}. The mod will read it but not change it.");
            else if (canWrite)
                Log.Info($"Save data {result.Status}: schema {state!.SchemaVersion}, written by {Empty(state.WrittenByModVersion)}, save count {state.SaveCount}, street food days {state.DailySales.Count}.");
            else
                Log.Error($"Save data {result.Status}: {result.Error ?? "written by a newer version"}. The mod will not change it.");

            // Crew hours are logged from the day after this load at the latest, even if the first hourly count
            // only comes after midnight (a save loaded at 23:30).
            if (canWrite) CrewLog.Begin(state!, current.Day);

            runtimeObject = new GameObject("FoodTrucks.Runtime");
            Object.DontDestroyOnLoad(runtimeObject);
            runtimeObject.AddComponent<CartSelling>();
            runtimeObject.AddComponent<TruckWindow>();
            runtimeObject.AddComponent<CartQueueView>();
            runtimeObject.AddComponent<HireRoutes>();
            runtimeObject.AddComponent<Catering>();
            runtimeObject.AddComponent<DebugActions>();
            runtimeObject.AddComponent<CartParts>();
            runtimeObject.AddComponent<VehicleDisplays>();
            runtimeObject.AddComponent<Commissary>();
            runtimeObject.AddComponent<VehicleTodos>();
            try { PrefabFixes.Apply(); }
            catch (Exception ex) { Log.Error("Prefab fixes: " + ex); }
            try { CartParts.LogLanes(); }
            catch (Exception ex) { Log.Error("Parking lanes: " + ex); }
            // The game refills its course list when its data loads, so the course goes back in at every load.
            try { FoodSafetyCourse.Register(); }
            catch (Exception ex) { Log.Error("Food Safety course: " + ex); }
            try { CityLicensing.Attach(); }
            catch (Exception ex) { Log.Error("City of New York: " + ex); }
            try { Catering.Attach(); }
            catch (Exception ex) { Log.Error("Catering: " + ex); }
            try { Commissary.Attach(); }
            catch (Exception ex) { Log.Error("Commissary: " + ex); }
            try { LogisticsCommissary.Attach(); }
            catch (Exception ex) { Log.Error("Commissary logistics: " + ex); }
            // The Bakery mod's goods sell from the display case and the coffee machine when it is installed.
            try
            {
                var bakery = BigAmbitions.Items.ItemsGetter.GetByName("bakeryxx:itemname_bread", suppressError: true) != null;
                Log.Info($"Bakery mod: {(bakery ? "found; its bread, buns, cookies, lattes and americanos sell from the display case and the coffee machine" : "not installed")}.");
            }
            catch (Exception ex) { Log.Error("Bakery mod check: " + ex); }
        }

        public static void WriteToSave()
        {
            if (!canWrite || state == null || game == null || !ReferenceEquals(game, SaveGameManager.Current)) return;
            // The count and version change only if serialization succeeds, so a failed write
            // leaves both the save's previous mod data and the in-memory count untouched.
            var json = Codec.SerializeForSave(state, FoodTrucksMod.Version);
            if (game.modData == null) game.modData = new Dictionary<string, string>();
            game.modData[SaveKey] = json;
            Keeper.Written(game, json);
            Log.Info($"Save data written: save count {state.SaveCount}.");
        }

        public static void MarkChanged() => SaveGameManager.MarkChange();

        public static void Detach()
        {
            World.Reset();
            CityLicensing.Detach();
            Catering.Detach();
            Commissary.Detach();
            StreetFoodBooks.ResetParkingHours();
            MapFilters.Clear();
            TruckHatch.Forget();
            WindowCrew.Reset();
            HireRoutes.Reset();
            StopPicker.Reset();
            BizManVehiclePage.Reset();
            BizManWeekPlan.Reset();
            VehicleSchedule.Reset();
            VehiclePageDrawings.Reset();
            BizManFleet.Reset();
            if (runtimeObject != null) Object.Destroy(runtimeObject);
            runtimeObject = null;
            game = null;
            state = null;
            canWrite = false;
        }

        private static string Empty(string value) => string.IsNullOrEmpty(value) ? "(none)" : value;
    }
}
