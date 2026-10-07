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
            // GlobalEvents.Init() clears subscribers before each city load, so subscribe per city scope.
            GlobalEvents.RegisterOnGameLoadedLateCallback(OnCityReady);
            GlobalEvents.onSaveGame += OnSaveGame;
            GlobalEvents.onGameUnloaded += OnGameUnloaded;
            return Task.CompletedTask;
        }

        private void OnCityReady()
        {
            if (!loaded || LoadScene.isLoading || SaveGameManager.Current == null) return;
            try { FoodTrucksSession.Attach(SaveGameManager.Current); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void OnSaveGame()
        {
            // Runs synchronously before the game serializes the save. Never throw into the game's save.
            try { FoodTrucksSession.WriteToSave(); }
            catch (Exception ex) { Log.Error(ex); }
        }

        private static void OnGameUnloaded() => FoodTrucksSession.Detach();

        public Task OnUnloadAsync()
        {
            loaded = false;
            GlobalEvents.onSaveGame -= OnSaveGame;
            GlobalEvents.onGameUnloaded -= OnGameUnloaded;
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

        // Null unless a save is attached and its mod data was readable.
        public static SaveState? CurrentState =>
            game != null && ReferenceEquals(game, SaveGameManager.Current) ? state : null;

        public static void Attach(GameInstance current)
        {
            Detach();
            if (current.modData == null) current.modData = new Dictionary<string, string>();
            current.modData.TryGetValue(SaveKey, out var json);

            var result = Codec.Load(json);
            LastLoadStatus = result.Status;
            game = current;
            state = result.State;
            // Data written by a newer mod version, or unreadable data, stays in the save untouched.
            canWrite = result.State != null;

            if (canWrite)
                Log.Info($"Save data {result.Status}: schema {state!.SchemaVersion}, written by {Empty(state.WrittenByModVersion)}, save count {state.SaveCount}, street food days {state.DailySales.Count}.");
            else
                Log.Error($"Save data {result.Status}: {result.Error ?? "written by a newer version"}. The mod will not change it.");

            runtimeObject = new GameObject("FoodTrucks.Runtime");
            Object.DontDestroyOnLoad(runtimeObject);
            runtimeObject.AddComponent<DebugActions>();
            DebugActions.LogDealerRoster();
        }

        public static void WriteToSave()
        {
            if (!canWrite || state == null || game == null || !ReferenceEquals(game, SaveGameManager.Current)) return;
            state.SaveCount++;
            state.WrittenByModVersion = FoodTrucksMod.Version;
            if (game.modData == null) game.modData = new Dictionary<string, string>();
            game.modData[SaveKey] = Codec.Serialize(state);
            Log.Info($"Save data written: save count {state.SaveCount}.");
        }

        public static void MarkChanged() => SaveGameManager.MarkChange();

        public static void Detach()
        {
            if (runtimeObject != null) Object.Destroy(runtimeObject);
            runtimeObject = null;
            game = null;
            state = null;
            canWrite = false;
        }

        private static string Empty(string value) => string.IsNullOrEmpty(value) ? "(none)" : value;
    }
}
