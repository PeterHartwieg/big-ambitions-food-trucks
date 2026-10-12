#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using IngameDebugConsole;
using Newtonsoft.Json.Linq;
using UI;
using UI.Load;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FoodTrucks
{
    // The bridge's commands (docs/test-bridge.md, "Commands"), run on the main thread. A refusal throws BridgeError,
    // which becomes ok: false with its message. Commands that change a save run only in an FT save: the loaded save's
    // name must start with "FT", and save, load and new_game take FT names only.
    internal static partial class TestBridgeCommands
    {
        internal static object? Run(string cmd, JObject args)
        {
            switch (cmd)
            {
                case "state": return TestBridgeState.Read();
                case "console": RequireFtCity(cmd); return Console(args);
                case "debug": RequireFtCity(cmd); return DebugAction(args);
                case "time": RequireFtCity(cmd); return Time(args);
                case "money": RequireFtCity(cmd); return Money(args);
                case "teleport": RequireFtCity(cmd); return Teleport(args);
                case "vehicle": RequireFtCity(cmd); return Vehicle(args);
                case "commissary": RequireFtCity(cmd); return CommissaryCommand(args);
                case "crew": RequireFtCity(cmd); return Crew(args);
                case "ui": return TestBridgeUi.Run(args);
                case "shoot": RequireCity(cmd); return TestBridgeShoot.Shoot(args);
                case "save": RequireFtCity(cmd); return Save(args);
                case "load": return Load(args);
                case "new_game": return NewGame(args);
                case "quit": return Quit();
                default:
                    throw new BridgeError($"Unknown command '{cmd}'. Commands: state, console, debug, time, money, teleport, vehicle, commissary, crew, ui, shoot, save, load, new_game, quit, op.");
            }
        }

        // ---- Scene, guard and arguments ----

        // "menu" (the main menu or the character creator), "loading" or "city".
        internal static string Scene()
        {
            if (LoadScene.isLoading) return "loading";
            bool menu = false, transition = false, city = false;
            for (var i = 0; i < SceneManager.sceneCount; i++)
            {
                var name = SceneManager.GetSceneAt(i).name;
                if (name == "MainMenu" || name == "Intro") menu = true;
                else if (name == "TransitionToSave") transition = true;
                else if (name == "MainScene") city = true;
            }
            if (menu) return "menu";
            if (transition) return "loading";
            return city && SaveGameManager.Current != null && !GameManager.isCitySceneBeingUnloaded ? "city" : "loading";
        }

        internal static bool IsFt(string? name) => name != null && name.StartsWith("FT", StringComparison.Ordinal);

        internal static void RequireCity(string cmd)
        {
            var scene = Scene();
            if (scene != "city") throw new BridgeError($"{cmd} works in the city only; the game is at the {scene} now.");
        }

        internal static void RequireFtCity(string cmd)
        {
            RequireCity(cmd);
            var name = SaveGameManager.Current?.SaveGameName;
            if (!IsFt(name))
                throw new BridgeError($"{cmd} changes the save, and the loaded save '{name}' doesn't start with FT: load an FT save first.");
            RequireFtCharacter(SaveGameManager.Current?.characterId);
        }

        // An FT save's character has FT saves only (the game's own recover saves aside): a series city saved under an FT
        // name keeps its character, whose other saves the game's autosave and save dialog can reach.
        private static readonly System.Text.RegularExpressions.Regex RecoverName =
            new System.Text.RegularExpressions.Regex(@"^Recover (#\d+|Midnight)$");
        private static readonly Dictionary<string, (float Until, string? Other)> characterChecks = new Dictionary<string, (float, string?)>();

        internal static void RequireFtCharacter(string? characterId)
        {
            var other = NonFtSave(characterId);
            if (other != null)
                throw new BridgeError($"This save's character also has saves not starting with FT ({other}); make FT saves with a character of their own.");
        }

        // The first save in the character's folder whose name doesn't start with FT, recover saves aside; read again
        // after a few seconds.
        private static string? NonFtSave(string? characterId)
        {
            if (string.IsNullOrEmpty(characterId)) return null;
            var now = UnityEngine.Time.realtimeSinceStartup;
            if (characterChecks.TryGetValue(characterId!, out var known) && now < known.Until) return known.Other;
            var folder = System.IO.Path.Combine(SaveGamePathHelper.CurrentVersionFolderPath(), characterId);
            string? other = null;
            if (System.IO.Directory.Exists(folder))
                other = System.IO.Directory.GetFiles(folder)
                    .Where(f => f.EndsWith(".hsg", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    .Select(System.IO.Path.GetFileNameWithoutExtension)
                    .Where(n => !IsFt(n) && !RecoverName.IsMatch(n))
                    .OrderBy(n => n, StringComparer.Ordinal)
                    .FirstOrDefault();
            characterChecks[characterId!] = (now + 5f, other);
            return other;
        }

        internal static string? Str(JObject args, string key) =>
            args[key] is JValue v && v.Type != JTokenType.Null ? v.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

        internal static double? Num(JObject args, string key) =>
            args[key] is JValue v && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float) ? v.Value<double>() : (double?)null;

        internal static bool? Bool(JObject args, string key) =>
            args[key] is JValue v && v.Type == JTokenType.Boolean ? v.Value<bool>() : (bool?)null;

        internal static string Need(JObject args, string key, string what) =>
            Str(args, key) ?? throw new BridgeError($"Missing \"{key}\": {what}.");

        // ---- The clock ----

        private static readonly FieldInfo? MultiplierField = AccessTools.Field(typeof(GameManager), "MinutesMultiplier");
        // Speeds above this skip too far in one frame for run_until to stop near its time.
        private const double MaxSpeed = 60;
        private static (double Target, string Text)? runUntil;
        // Where the last run_until stopped ("day D HH:MM"), until the next time command.
        private static string? arrivedAt;
        // A run_until counts as arrived this close to its minute (the clock is a float).
        private const double ArrivedMinutes = 0.001;

        internal static JObject Clock()
        {
            var game = SaveGameManager.Current;
            return new JObject
            {
                ["day"] = game.Day,
                ["weekday"] = World.DayOfWeekOf(game.Day).ToString(),
                ["time"] = $"{game.Hour:D2}:{(int)game.Minute:D2}",
                ["paused"] = Paused(),
                ["speed"] = Speed(),
                ["run_until"] = runUntil?.Text,
                ["arrived_at"] = arrivedAt,
            };
        }

        // What holds the clock now, or null: a time skip, or the screens open while the game's time control is off.
        internal static string? ClockHeldBy()
        {
            if (!InstanceBehavior<UIs>.IsInitialized) return null;
            var uis = InstanceBehavior<UIs>.Instance;
            if (uis.timeMachine != null && uis.timeMachine.isRunning) return "time skip";
            if (uis.gameSpeed == null || !uis.gameSpeed.isTimeControlDisabled) return null;
            var open = TestBridgeUi.Open();
            return open.Count == 0 ? "a screen" : string.Join(", ", open);
        }

        // Harmony prefix on GameManager.RunMainGameTick, only with the bridge on: while run_until runs, no step of the
        // clock goes past its minute (by more than a hair, so the minute itself and its hour's events are reached).
        internal static void ClampTickPrefix(ref float deltaTimeWithMultiplier)
        {
            if (runUntil == null) return;
            var game = SaveGameManager.Current;
            if (game == null) return;
            var most = (float)Math.Max(0.0, runUntil.Value.Target - NowMinutes(game) + ArrivedMinutes / 2);
            if (deltaTimeWithMultiplier > most) deltaTimeWithMultiplier = most;
        }

        internal static bool? Paused()
        {
            if (!InstanceBehavior<UIs>.IsInitialized) return null;
            var speed = InstanceBehavior<UIs>.Instance.gameSpeed;
            return speed == null ? (bool?)null : speed.Paused;
        }

        // The game's minutes per real second over the player's own speed setting (Options): 1 is normal play.
        private static double? Speed()
        {
            if (!(MultiplierField?.GetValue(null) is float multiplier)) return null;
            var normal = PlayerPrefSettings.GameSpeed;
            return Math.Round(multiplier / (normal > 0 ? normal : 1f), 2);
        }

        private static void SetSpeed(double speed)
        {
            var normal = PlayerPrefSettings.GameSpeed;
            GameManager.SetMinutesMultiplier((float)(speed * (normal > 0 ? normal : 1f)));
        }

        private static double NowMinutes(GameInstance game) => game.Day * 1440.0 + game.Hour * 60.0 + game.Minute;

        private static JToken Time(JObject args)
        {
            var uis = InstanceBehavior<UIs>.Instance;
            var control = uis == null ? null : uis.gameSpeed;
            if (uis == null || control == null) throw new BridgeError("The game's speed control isn't there.");
            if (uis.timeMachine != null && uis.timeMachine.isRunning)
                throw new BridgeError("A time skip (sleep, a paid activity) is running; wait for it to end.");
            var pause = Bool(args, "pause");
            var speed = Num(args, "speed");
            var until = args["run_until"] as JObject;
            if (pause == null && speed == null && until == null) throw new BridgeError("time needs \"pause\", \"speed\" or \"run_until\".");
            if (speed != null && (speed <= 0 || speed > MaxSpeed)) throw new BridgeError($"speed must be above 0 and at most {MaxSpeed}.");
            var unpausing = until != null || pause == false || (speed != null && pause != true);
            if (unpausing && control.isTimeControlDisabled)
                throw new BridgeError("A screen holds the clock (the phone, the map, a call, an activity or a menu): close it first (ui escape).");

            var game = SaveGameManager.Current;
            runUntil = null;
            arrivedAt = null;
            if (until != null)
            {
                var target = Target(game, until);
                if (target.Minutes <= NowMinutes(game))
                    throw new BridgeError($"{target.Text} has passed already: it is day {game.Day} {game.Hour:D2}:{(int)game.Minute:D2}.");
                runUntil = (target.Minutes, target.Text);
                SetSpeed(speed ?? 1);
                control.SetPlayerPause(false);
                Log.Info($"Test bridge: running the clock at speed {speed ?? 1} until {target.Text}.");
            }
            else
            {
                if (speed != null) SetSpeed(speed.Value);
                if (pause != null) control.SetPlayerPause(pause.Value);
                else if (speed != null) control.SetPlayerPause(false);
            }
            return Clock();
        }

        // run_until's target: "day" a number or "+N" (days from today, default today), "time" "HH:MM".
        private static (double Minutes, string Text) Target(GameInstance game, JObject until)
        {
            var dayText = Str(until, "day");
            int day;
            if (dayText == null) day = game.Day;
            else if (dayText.StartsWith("+", StringComparison.Ordinal) && int.TryParse(dayText.Substring(1), out var ahead)) day = game.Day + ahead;
            else if (!int.TryParse(dayText, out day)) throw new BridgeError("run_until.day must be a day number or \"+N\".");
            var timeText = Str(until, "time") ?? throw new BridgeError("run_until needs \"time\" (\"HH:MM\").");
            var parts = timeText.Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var hour) || !int.TryParse(parts[1], out var minute) || hour < 0 || hour > 23 || minute < 0 || minute > 59)
                throw new BridgeError("run_until.time must be \"HH:MM\".");
            return (day * 1440.0 + hour * 60 + minute, $"day {day} {hour:D2}:{minute:D2}");
        }

        // Every frame, from the pump: follows a load, stops run_until at its time.
        internal static void EveryFrame()
        {
            FollowLoad();
            if (runUntil == null) return;
            var game = SaveGameManager.Current;
            if (game == null || Scene() != "city")
            {
                runUntil = null;
                Log.Info("Test bridge: run_until dropped: the city was left.");
                return;
            }
            if (NowMinutes(game) < runUntil.Value.Target - ArrivedMinutes) return;
            var text = runUntil.Value.Text;
            runUntil = null;
            InstanceBehavior<UIs>.Instance?.gameSpeed?.SetPlayerPause(true);
            SetSpeed(1);
            arrivedAt = $"day {game.Day} {game.Hour:D2}:{(int)game.Minute:D2}";
            Log.Info($"Test bridge: run_until {text} reached at {arrivedAt}; paused, speed 1.");
        }

        // ---- Console, debug actions, money ----

        private static JToken Console(JObject args)
        {
            var line = Need(args, "line", "the console line to run");
            if (DebugLogManager.Instance == null) throw new BridgeError("The game's debug console isn't there.");
            var output = new List<string>();
            void Capture(string condition, string stackTrace, LogType type) =>
                output.Add(type == LogType.Log ? condition : $"{type}: {condition}");
            Application.logMessageReceived += Capture;
            try { DebugLogConsole.ExecuteCommand(line); }
            finally { Application.logMessageReceived -= Capture; }
            Log.Info($"Test bridge: console '{line}' ({output.Count} lines).");
            return new JObject { ["output"] = new JArray(output) };
        }

        private static JToken DebugAction(JObject args)
        {
            var action = Need(args, "action", "the debug action's name");
            if (!DebugActions.Names.Contains(action))
                throw new BridgeError($"No debug action '{action}'. Actions: {string.Join(", ", DebugActions.Names)}.");
            var notices = Notices.Capture(() => DebugActions.RunByName(action));
            return new JObject { ["action"] = action, ["notices"] = new JArray(notices) };
        }

        // A plain balance change: no transaction, so nothing in the books or the tax year.
        private static JToken Money(JObject args)
        {
            var value = Num(args, "set") ?? throw new BridgeError("money needs \"set\": the new balance.");
            var game = SaveGameManager.Current;
            var before = game.Money;
            game.Money = (float)value;
            SaveGameManager.MarkChange();
            Log.Info($"Test bridge: money set from {before:F2} to {game.Money:F2}.");
            return new JObject { ["money"] = Math.Round(game.Money, 2) };
        }

        // ---- Save, load, new game, quit ----

        private const float SaveSeconds = 60f;

        // What the game's save logged: serialisation runs in the call, compression on a thread of its own.
        private sealed class SaveWatch
        {
            public volatile bool SerializeFailed;
            public volatile bool Compressed;

            public void OnLog(string condition, string stackTrace, LogType type)
            {
                if (condition.StartsWith("Save game serialization failed", StringComparison.Ordinal)) SerializeFailed = true;
                else if (condition.StartsWith("Compressed to ", StringComparison.Ordinal)) Compressed = true;
            }
        }

        // The save's real outcome: the game's serialisation and compression lines (SaveGameManager.SerializeSaveGame,
        // CompressSaveGame), then the file itself, written since the call and not empty.
        private static object Save(JObject args)
        {
            var name = Need(args, "name", "the save's name");
            if (!IsFt(name)) throw new BridgeError($"The bridge saves only under names that start with FT, not '{name}'.");
            if (SaveGameManager.SavingGameInProgress) throw new BridgeError("A save is already being written; try again in a moment.");
            var started = DateTime.UtcNow.AddSeconds(-2);
            var watch = new SaveWatch();
            Application.logMessageReceivedThreaded += watch.OnLog;
            bool accepted;
            try { accepted = InstanceBehavior<GameManager>.Instance.SaveGame(name); }
            catch
            {
                Application.logMessageReceivedThreaded -= watch.OnLog;
                throw;
            }
            if (!accepted)
            {
                Application.logMessageReceivedThreaded -= watch.OnLog;
                throw new BridgeError("The game refused to save here (inside the interior designer, placing an item, at school or on the casino boat?).");
            }
            var game = SaveGameManager.Current;
            var saved = game.SaveGameName;
            var path = System.IO.Path.Combine(SaveGamePathHelper.CurrentVersionFolderPath(), game.characterId,
                saved + "." + (InstanceBehavior<GameManager>.Instance.useSaveGameTypeJson ? "json" : "hsg"));
            Log.Info($"Test bridge: saving as '{saved}' ({path}).");
            var deadline = UnityEngine.Time.realtimeSinceStartup + SaveSeconds;
            return new Deferred(() =>
            {
                if (SaveGameManager.SavingGameInProgress)
                {
                    if (UnityEngine.Time.realtimeSinceStartup <= deadline) return null;
                    Application.logMessageReceivedThreaded -= watch.OnLog;
                    throw new BridgeError($"The save didn't finish within {SaveSeconds:F0} seconds.");
                }
                Application.logMessageReceivedThreaded -= watch.OnLog;
                if (watch.SerializeFailed) throw new BridgeError("The game's save failed while writing the city (see Player.log: Save game serialization failed).");
                if (!watch.Compressed) throw new BridgeError("The game's save failed while compressing (no \"Compressed to\" line).");
                var file = new System.IO.FileInfo(path);
                if (!file.Exists) throw new BridgeError($"The save file isn't there: {path}.");
                if (file.LastWriteTimeUtc < started) throw new BridgeError($"The save file wasn't written by this save: {path}.");
                if (file.Length == 0) throw new BridgeError($"The save file is empty: {path}.");
                Log.Info($"Test bridge: saved '{saved}', {file.Length} bytes.");
                return new JObject { ["name"] = saved, ["path"] = path, ["bytes"] = file.Length };
            }, () => Application.logMessageReceivedThreaded -= watch.OnLog);
        }

        private static JToken Load(JObject args)
        {
            var name = Need(args, "name", "the save's name");
            if (!IsFt(name)) throw new BridgeError($"The bridge loads only saves whose name starts with FT, not '{name}'.");
            var scene = Scene();
            if (scene == "loading") throw new BridgeError("The game is loading; wait until /health says menu or city.");
            if (LoadStatus == "pending") throw new BridgeError($"A load of '{LoadTarget}' is under way; wait for it (state.load).");
            // Leaving a city skips the game's unsaved-changes question, so only an FT city may be left this way.
            var current = SaveGameManager.Current?.SaveGameName;
            if (scene == "city" && !IsFt(current))
                throw new BridgeError($"The loaded save '{current}' doesn't start with FT, and leaving it would skip the game's question about unsaved changes: " +
                                      "go to the main menu by hand, or load from an FT save.");
            var saves = SaveGamePathHelper.GetAllSaveGamesFromVersion();
            var save = saves.Where(s => s != null && s.name == name).OrderByDescending(s => s.lastPlayedDate).FirstOrDefault();
            if (save == null)
            {
                var ft = saves.Where(s => s != null && IsFt(s.name)).Select(s => s.name).Distinct().ToList();
                throw new BridgeError($"No save named '{name}'. FT saves: {(ft.Count == 0 ? "none" : string.Join(", ", ft))}.");
            }
            RequireFtCharacter(save.characterId);
            if (scene == "city") RequireFtCharacter(SaveGameManager.Current?.characterId);
            var pump = TestBridgePump.Instance ?? throw new BridgeError("The bridge's runner is gone.");
            if (scene == "menu" && !InstanceBehavior<MainMenuController>.IsInitialized)
                throw new BridgeError("load works from the main menu or the city, not the character creator.");
            BeginLoad(save.name);
            if (scene == "menu") StartLoad(save);
            else pump.StartCoroutine(LoadFromCity(save));
            return new JObject { ["loading"] = true, ["name"] = save.name, ["character"] = save.characterData?.name };
        }

        // The main menu's Load button without its two questions (a save never played with mods, a different mod list):
        // LoadGame.Load's LoadTransition with the save as it is.
        private static void StartLoad(SaveGameManager.SaveGameStruct save)
        {
            Log.Info($"Test bridge: loading '{save.name}' ({save.characterData?.name}, day {save.day}).");
            TransitionToSave.saveToLoadData = save;
            LoadScene.LoadTransitionToSave();
        }

        // From the city: the pause menu's Main menu (without its question), then the main menu's load.
        private static IEnumerator LoadFromCity(SaveGameManager.SaveGameStruct save)
        {
            Log.Info($"Test bridge: to the main menu, to load '{save.name}'.");
            yield return LoadScene.LoadMainMenuFromCity();
            var deadline = UnityEngine.Time.realtimeSinceStartup + 120f;
            while (Scene() != "menu" || !InstanceBehavior<MainMenuController>.IsInitialized)
            {
                if (UnityEngine.Time.realtimeSinceStartup > deadline)
                {
                    EndLoad("failed", "the main menu didn't come up within 120 seconds");
                    yield break;
                }
                yield return null;
            }
            // The menu's own Start runs in the frame after it loads.
            yield return null;
            yield return null;
            StartLoad(save);
        }

        // The bridge stops: nothing of this session's carries over to a start after a mod re-enable.
        internal static void Reset()
        {
            if (runUntil != null && SaveGameManager.Current != null) SetSpeed(1);
            runUntil = null;
            arrivedAt = null;
            LoadTarget = null;
            LoadStatus = null;
            loadError = null;
            characterChecks.Clear();
            TestBridgeUi.Forget();
        }

        // ---- Load progress: state.load and health.load ----

        internal static string? LoadTarget { get; private set; }
        // "pending", "done" or "failed"; null before any load.
        internal static string? LoadStatus { get; private set; }
        private static string? loadError;
        private static int loadAttachesAtStart;
        private static float loadDeadline;
        private const float LoadSeconds = 300f;

        internal static JObject? LoadJson() => LoadTarget == null ? null
            : new JObject { ["name"] = LoadTarget, ["status"] = LoadStatus, ["error"] = loadError };

        private static void BeginLoad(string name)
        {
            LoadTarget = name;
            LoadStatus = "pending";
            loadError = null;
            loadAttachesAtStart = FoodTrucksSession.Attaches;
            loadDeadline = UnityEngine.Time.realtimeSinceStartup + LoadSeconds;
            TransitionToSave.saveGameLoadErrored = false;
        }

        private static void EndLoad(string status, string? error)
        {
            LoadStatus = status;
            loadError = error;
            if (status == "done") Log.Info($"Test bridge: load '{LoadTarget}' done: day {SaveGameManager.Current?.Day}, the mod's save data attached.");
            else Log.Error($"Test bridge: load '{LoadTarget}' failed: {error}.");
        }

        // Done when the city is up with that save and the mod has attached to it since the load began (so reloading
        // the same name doesn't count the old city); failed when the game reports a broken save or time runs out.
        private static void FollowLoad()
        {
            if (LoadStatus != "pending") return;
            if (TransitionToSave.saveGameLoadErrored)
            {
                EndLoad("failed", "the game couldn't load the save (it went back to the main menu)");
                return;
            }
            if (Scene() == "city" && SaveGameManager.Current?.SaveGameName == LoadTarget &&
                FoodTrucksSession.Attaches > loadAttachesAtStart)
            {
                EndLoad("done", null);
                return;
            }
            if (UnityEngine.Time.realtimeSinceStartup > loadDeadline) EndLoad("failed", $"not in the city with it after {LoadSeconds:F0} seconds");
        }

        private static JToken NewGame(JObject args)
        {
            var name = Str(args, "name");
            if (name != null && !IsFt(name)) throw new BridgeError($"The bridge starts only games whose name starts with FT, not '{name}'.");
            throw new BridgeError("new_game isn't available: the game builds a new game's character only in the character creator's screens " +
                                  "(MainMenuController.StartNewGame, then the Intro scene), which the bridge can't fill in reliably. " +
                                  "Start a game by hand once, save it under an FT name, and use load.");
        }

        private static JToken Quit()
        {
            var pump = TestBridgePump.Instance ?? throw new BridgeError("The bridge's runner is gone.");
            Log.Info("Test bridge: quitting the game.");
            pump.StartCoroutine(QuitSoon());
            return new JObject();
        }

        // Late enough for the reply to leave; the save threads are joined as the game's own quit does.
        private static IEnumerator QuitSoon()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            SaveGameManager.JoinSaveGameThreads();
            Application.Quit();
        }
    }
}
