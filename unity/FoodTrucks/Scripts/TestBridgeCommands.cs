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
                ["keep_needs"] = keepNeeds,
            };
        }

        internal static bool? TimeSkipRunning()
        {
            if (!InstanceBehavior<UIs>.IsInitialized) return null;
            var machine = InstanceBehavior<UIs>.Instance.timeMachine;
            return machine == null ? (bool?)null : machine.isRunning;
        }

        // ---- The player's needs ----

        // Set by a time call (on by default for run_until): while the clock runs, energy and hunger are topped up
        // through the game's own needs API when either falls below NeedsTopUp.Below, so a long run never ends in a
        // collapse (the hospital sets the clock to 08:00 the next day and bills $2,000; live session 4).
        private static bool keepNeeds;
        private static readonly System.Reflection.PropertyInfo? EnergySettingsProperty = AccessTools.Property(typeof(Helpers.EnergyHelper), "Settings");

        private static Helpers.EnergySettings? NeedSettings()
        {
            try { return EnergySettingsProperty?.GetValue(null) as Helpers.EnergySettings; }
            catch (Exception) { return null; }
        }

        // The needs the game tracks for the player: energy (a collapse when it reaches the hospital threshold while on
        // foot), hunger (below the threshold energy burns faster; with both at 0 the player shuffles) and happiness
        // (low happiness burns energy faster and slows its regain) [code, 1.0: EnergyHelper, EnergySettings].
        internal static JObject Needs(GameInstance game)
        {
            var settings = NeedSettings();
            return new JObject
            {
                ["energy"] = Math.Round(game.Energy, 1),
                ["hunger"] = Math.Round(game.Hunger, 1),
                ["happiness"] = Math.Round(game.Happiness, 1),
                ["max"] = settings == null ? (JToken?)null : settings.MaxEnergyHungerHappinessValue,
                ["collapse_at"] = settings == null ? (JToken?)null : settings.HospitalizationEnergyThreshold,
                ["energy_off"] = game.gameVariables?.disableEnergy,
                ["going_to_hospital"] = Helpers.EnergyHelper.goingToHospital,
                ["kept_up"] = keepNeeds,
            };
        }

        // The top-ups change the save, so they hold only while it is an FT save of a character with FT saves only, as
        // every changing command requires (the character check is cached for a few seconds). A save renamed to a non-FT
        // name, or a non-FT save made for the same character, stops them (review round 6).
        private static bool NeedsMayChange(GameInstance game)
        {
            string? why = null;
            if (!IsFt(game.SaveGameName)) why = $"the save is now '{game.SaveGameName}', which doesn't start with FT";
            else
            {
                var other = NonFtSave(game.characterId);
                if (other != null) why = $"the save's character now also has a save not starting with FT ({other})";
            }
            if (why == null) return true;
            Log.Info($"Test bridge: needs top-ups stopped: {why}.");
            return false;
        }

        // Every frame while keepNeeds is on and the clock runs.
        private static void KeepNeedsUp(GameInstance game)
        {
            if (!keepNeeds || Paused() == true || Helpers.EnergyHelper.goingToHospital || game.gameVariables?.disableEnergy == true) return;
            if (!FoodTrucks.Core.Common.NeedsTopUp.Due(game.Energy, game.Hunger)) return;
            var max = NeedSettings()?.MaxEnergyHungerHappinessValue ?? 100f;
            var (energy, hunger) = (game.Energy, game.Hunger);
            // GenerateEnergy is what a bed or a drink adds (it takes a share off for low happiness and stops at the
            // top); twice the scale always reaches the top. SetCurrentHunger is what eating sets.
            Helpers.EnergyHelper.GenerateEnergy(max * 2f);
            Helpers.EnergyHelper.SetCurrentHunger(max);
            Log.Info($"Test bridge: needs topped up while the clock runs (energy {energy:F1} to {game.Energy:F1}, hunger {hunger:F1} to {game.Hunger:F1}), day {game.Day} {game.Hour:D2}:{(int)game.Minute:D2}.");
        }

        // A collapse: the rising edge of EnergyHelper.goingToHospital, which UpdateEnergy sets as it starts the hospital
        // sequence. Read every frame rather than through GlobalEvents.onHospitalRespawnStarts, which GlobalEvents.Init
        // clears on every save load (review round 6: a subscription made at the mod's start was gone before the first
        // city) [code, 1.0: SaveGameManager.TryPrepareLoad, EnergyHelper.UpdateEnergy]. A doctor's visit and plastic
        // surgery set the same flag with energy to spare, so only a rise with energy at the hospital threshold counts.
        private static bool wasGoingToHospital;

        private static void WatchCollapse()
        {
            var game = SaveGameManager.Current;
            var going = game != null && Scene() == "city" && Helpers.EnergyHelper.goingToHospital;
            var rising = going && !wasGoingToHospital;
            wasGoingToHospital = going;
            if (!rising || game == null) return;
            var threshold = NeedSettings()?.HospitalizationEnergyThreshold ?? -20f;
            if (game.Energy > threshold + 0.5f) return;
            Log.Info($"Test bridge: the player collapsed at day {game.Day} {game.Hour:D2}:{(int)game.Minute:D2} (energy {game.Energy:F1}, hunger {game.Hunger:F1}, happiness {game.Happiness:F1}); " +
                     "the game takes them to hospital, sets the clock to 08:00 and bills the stay.");
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
            var keep = Bool(args, "keep_needs");
            if (args["keep_needs"] != null && keep == null) throw new BridgeError("keep_needs must be true or false.");
            if (pause == null && speed == null && until == null) throw new BridgeError("time needs \"pause\", \"speed\" or \"run_until\".");
            if (speed != null && (speed <= 0 || speed > MaxSpeed)) throw new BridgeError($"speed must be above 0 and at most {MaxSpeed}.");
            var unpausing = until != null || pause == false || (speed != null && pause != true);
            if (unpausing && control.isTimeControlDisabled)
                throw new BridgeError("A screen holds the clock (the phone, the map, a call, an activity or a menu): close it first (ui escape).");

            var game = SaveGameManager.Current;
            // The whole request is checked before anything changes: a refused call leaves the clock, run_until and
            // keep_needs as they were (review round 6: a refused run_until used to leave the needs top-ups on).
            (double Minutes, string Text)? target = null;
            if (until != null)
            {
                target = Target(game, until);
                if (target.Value.Minutes <= NowMinutes(game))
                    throw new BridgeError($"{target.Value.Text} has passed already: it is day {game.Day} {game.Hour:D2}:{(int)game.Minute:D2}.");
            }
            runUntil = null;
            arrivedAt = null;
            // On by default for run_until, off otherwise (docs/test-bridge.md, "time"); a pause turns it off.
            keepNeeds = unpausing && pause != true && (keep ?? until != null);
            if (target != null)
            {
                runUntil = target;
                SetSpeed(speed ?? 1);
                control.SetPlayerPause(false);
                Log.Info($"Test bridge: running the clock at speed {speed ?? 1} until {target.Value.Text}.");
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
            WatchLoadingScreens();
            FollowLoad();
            WatchCollapse();
            var current = SaveGameManager.Current;
            if (keepNeeds && (current == null || Scene() != "city")) keepNeeds = false;
            else if (keepNeeds && !NeedsMayChange(current!)) keepNeeds = false;
            else if (keepNeeds) KeepNeedsUp(current!);
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
            keepNeeds = false;
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

        // A notice can come a frame or two after its action (the hatch's step-out notice did): a command that reports
        // notices keeps collecting them for this many frames and this long in real time before it replies.
        private const int NoticeFrames = 5;
        private const float NoticeSeconds = 0.25f;

        // Runs the action with the notice window open; the reply is built when the window closes, from what it caught.
        internal static Deferred WithNotices(Action action, Func<List<string>, JObject> reply)
        {
            var caught = Notices.BeginCapture();
            try { action(); }
            catch
            {
                Notices.EndCapture(caught);
                throw;
            }
            var frame = UnityEngine.Time.frameCount;
            var until = UnityEngine.Time.realtimeSinceStartup + NoticeSeconds;
            return new Deferred(() =>
            {
                if (UnityEngine.Time.frameCount - frame < NoticeFrames || UnityEngine.Time.realtimeSinceStartup < until) return null;
                Notices.EndCapture(caught);
                return reply(caught);
            }, () => Notices.EndCapture(caught));
        }

        private static object DebugAction(JObject args)
        {
            var action = Need(args, "action", "the debug action's name");
            if (!DebugActions.Names.Contains(action))
                throw new BridgeError($"No debug action '{action}'. Actions: {string.Join(", ", DebugActions.Names)}.");
            return WithNotices(() => DebugActions.RunByName(action),
                notices => new JObject { ["action"] = action, ["notices"] = new JArray(notices) });
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

        // The bridge's save in progress: the game's serialisation and compression results for its paths, from the
        // finalizers on SaveGameSerializationHelper (bridge only), and the failure lines the game logs, as a fallback.
        private sealed class SaveWatch
        {
            public string TempPath = "";
            public string OutputPath = "";
            // Null until the method ran for this save's path.
            public volatile object? Serialized;
            public volatile object? Compressed;
            public volatile bool FailureLogged;

            public void OnLog(string condition, string stackTrace, LogType type)
            {
                // "Save game serialization failed" (SaveGameManager), "SaveGame serialization failed" and "SaveGame compression
                // failed" (SaveGameSerializationHelper, which returns false and lets compression run anyway).
                if (condition.StartsWith("Save game serialization failed", StringComparison.Ordinal) ||
                    condition.StartsWith("SaveGame serialization failed", StringComparison.Ordinal) ||
                    condition.StartsWith("SaveGame compression failed", StringComparison.Ordinal))
                    FailureLogged = true;
            }
        }

        private static volatile SaveWatch? saving;
        // How many of the three save patches are active; with all three the outcome comes from their results.
        internal static int SaveHooks;

        internal static Exception? SerializeFinalizer(string path, bool __result, Exception? __exception)
        {
            var watch = saving;
            if (watch != null && SamePath(path, watch.TempPath)) watch.Serialized = __exception == null && __result;
            return __exception;
        }

        internal static Exception? CompressFinalizer(string path, string outputPath, bool __result, Exception? __exception)
        {
            var watch = saving;
            if (watch != null && SamePath(outputPath, watch.OutputPath)) watch.Compressed = __exception == null && __result;
            return __exception;
        }

        private static bool SamePath(string? a, string b) =>
            a != null && string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        // The save's real outcome: with the bridge's patches, the game's serialise and compress both returned true for
        // this save's paths; then the file itself, written since the call and not empty.
        private static object Save(JObject args)
        {
            var name = Need(args, "name", "the save's name");
            if (!IsFt(name)) throw new BridgeError($"The bridge saves only under names that start with FT, not '{name}'.");
            if (SaveGameManager.SavingGameInProgress || saving != null) throw new BridgeError("A save is already being written; try again in a moment.");
            var game = SaveGameManager.Current;
            var started = DateTime.UtcNow.AddSeconds(-2);
            // The paths SaveGameManager.Save uses: the temp file it serialises to, the save it compresses into.
            var path = System.IO.Path.Combine(SaveGamePathHelper.CurrentVersionFolderPath(), game.characterId,
                Blueprints.FileSystemHelper.MakeValidFilename(name) + "." + (InstanceBehavior<GameManager>.Instance.useSaveGameTypeJson ? "json" : "hsg"));
            var watch = new SaveWatch { TempPath = SaveGamePathHelper.GetTempSavePath(), OutputPath = path };
            void Unwatch()
            {
                Application.logMessageReceivedThreaded -= watch.OnLog;
                if (ReferenceEquals(saving, watch)) saving = null;
            }
            saving = watch;
            Application.logMessageReceivedThreaded += watch.OnLog;
            bool accepted;
            try { accepted = InstanceBehavior<GameManager>.Instance.SaveGame(name); }
            catch
            {
                Unwatch();
                throw;
            }
            if (!accepted)
            {
                Unwatch();
                throw new BridgeError("The game refused to save here (inside the interior designer, placing an item, at school or on the casino boat?).");
            }
            var saved = game.SaveGameName;
            Log.Info($"Test bridge: saving as '{saved}' ({path}).");
            var deadline = UnityEngine.Time.realtimeSinceStartup + SaveSeconds;
            return new Deferred(() =>
            {
                if (SaveGameManager.SavingGameInProgress)
                {
                    if (UnityEngine.Time.realtimeSinceStartup <= deadline) return null;
                    Unwatch();
                    throw new BridgeError($"The save didn't finish within {SaveSeconds:F0} seconds.");
                }
                Unwatch();
                if (SaveHooks == 3)
                {
                    if (!(watch.Serialized is bool serialized)) throw new BridgeError("The game's save didn't serialise the city for this save.");
                    if (!serialized) throw new BridgeError("The game's save failed while writing the city (SaveGame serialization failed; see Player.log).");
                    if (!(watch.Compressed is bool compressed)) throw new BridgeError($"The game's save didn't compress into {path}.");
                    if (!compressed) throw new BridgeError("The game's save failed while compressing (see Player.log).");
                }
                if (watch.FailureLogged) throw new BridgeError("The game logged a save failure (see Player.log).");
                var file = new System.IO.FileInfo(path);
                if (!file.Exists) throw new BridgeError($"The save file isn't there: {path}.");
                if (file.LastWriteTimeUtc < started) throw new BridgeError($"The save file wasn't written by this save: {path}.");
                if (file.Length == 0) throw new BridgeError($"The save file is empty: {path}.");
                Log.Info($"Test bridge: saved '{saved}', {file.Length} bytes.");
                return new JObject { ["name"] = saved, ["path"] = path, ["bytes"] = file.Length };
            }, Unwatch);
        }

        private static JToken Load(JObject args)
        {
            var name = Need(args, "name", "the save's name");
            if (!IsFt(name)) throw new BridgeError($"The bridge loads only saves whose name starts with FT, not '{name}'.");
            var scene = Scene();
            if (scene == "loading")
                throw new BridgeError(LoadScene.isLoading && !LoadingScreenUp()
                    ? $"The game's loading flag is on with no loading screen; the bridge can't tell whether a load is still starting, so it leaves the flag alone: wait, or restart the game. {Where()}."
                    : "The game is loading; wait until /health says menu or city.");
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
            pump.StartCoroutine(scene == "menu" ? LoadFromMenu(save) : LoadFromCity(save));
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

        // Whether one of the game's loading screens is still up (fading in, loading, or fading out after its load).
        private static bool LoadingScreenUp() => UnityEngine.Object.FindAnyObjectByType<LoadingScreen>() != null;

        // From the main menu: once no loading screen is left, the menu's load.
        //
        // Live session 4 (13 October 2026): a load from the city hung at the main menu twice. The game's LoadingScreen is
        // a singleton (InstanceBehavior): LoadScene.LoadScenes clears isLoading before the screen fades out, and the
        // screen destroys itself only after its fade (up to 1.6 s). The bridge used to start the menu's load two frames
        // after the menu came up, inside that fade, so the new loading screen met the old one in Awake and destroyed
        // itself; its coroutine died after its first yield, and LoadScene.isLoading stayed true for good, with nothing
        // loading [code, 1.0: LoadScene, LoadingScreen, InstanceBehavior.Awake]. A player never clicks Load that fast.
        private static IEnumerator LoadFromMenu(SaveGameManager.SaveGameStruct save)
        {
            var deadline = UnityEngine.Time.realtimeSinceStartup + MenuSeconds;
            while (LoadScene.isLoading || LoadingScreenUp() || !InstanceBehavior<MainMenuController>.IsInitialized)
            {
                if (LoadStatus != "pending") yield break;
                if (UnityEngine.Time.realtimeSinceStartup > deadline)
                {
                    EndLoad("failed", $"the main menu's last loading screen didn't go within {MenuSeconds:F0} seconds; {Where()}");
                    yield break;
                }
                yield return null;
            }
            // The menu's own Start runs in the frame after it loads.
            yield return null;
            yield return null;
            if (LoadStatus != "pending") yield break;
            if (LoadScene.isLoading)
            {
                // LoadScenes would return without loading anything.
                EndLoad("failed", $"the game started another load first; {Where()}");
                yield break;
            }
            StartLoad(save);
        }

        private const float MenuSeconds = 120f;

        // From the city: the pause menu's Main menu (without its question), then the main menu's load.
        private static IEnumerator LoadFromCity(SaveGameManager.SaveGameStruct save)
        {
            // The city's own loading screen may still be fading out (a load called as soon as the last one was done):
            // the game clears its loading flag and runs its loaded callbacks before that fade, so the main menu's loading
            // screen would meet it the same way (review round 6).
            var deadline = UnityEngine.Time.realtimeSinceStartup + MenuSeconds;
            while (LoadScene.isLoading || LoadingScreenUp())
            {
                if (LoadStatus != "pending") yield break;
                if (UnityEngine.Time.realtimeSinceStartup > deadline)
                {
                    EndLoad("failed", $"the city's last loading screen didn't go within {MenuSeconds:F0} seconds; {Where()}");
                    yield break;
                }
                yield return null;
            }
            Log.Info($"Test bridge: to the main menu, to load '{save.name}'.");
            yield return LoadScene.LoadMainMenuFromCity();
            deadline = UnityEngine.Time.realtimeSinceStartup + MenuSeconds;
            while (Scene() != "menu")
            {
                if (LoadStatus != "pending") yield break;
                if (UnityEngine.Time.realtimeSinceStartup > deadline)
                {
                    EndLoad("failed", $"the main menu didn't come up within {MenuSeconds:F0} seconds; {Where()}");
                    yield break;
                }
                yield return null;
            }
            Log.Info($"Test bridge: at the main menu; loading '{save.name}' once its loading screen has gone.");
            yield return LoadFromMenu(save);
        }

        // Where the game is, for a failed load's reply: the scene, the loading flag and whether a loading screen is up.
        private static string Where()
        {
            var scene = Scene();
            var flag = LoadScene.isLoading ? "on" : "off";
            var screen = LoadingScreenUp() ? "a loading screen is up" : "no loading screen";
            return $"the game is at the {scene} (its loading flag {flag}, {screen})";
        }

        // A load that can't run: the bridge's own load started a loading screen while another was still up, and the new
        // one removed itself (InstanceBehavior.Awake), taking the load's coroutine with it, so the game's loading flag
        // would stay on for good. The proof comes from a postfix on LoadingScreen.StartLoading (test bridge only): the
        // screen asked to load isn't the game's registered one. Only then, once no loading screen is left, the menu is
        // up and no city is being unloaded (a quit, or the way to the menu), does the bridge clear the flag. A loading
        // flag on with no screen for any other reason (the screen's prefab still loading, the pause menu's quit, a load
        // the player started) is only reported, never cleared (review round 6).
        private static bool doomedLoad;
        private static readonly System.Reflection.PropertyInfo? MainInstance =
            AccessTools.Property(typeof(InstanceBehavior<LoadingScreen>), "IsMainInstance");

        internal static void StartLoadingPostfix(LoadingScreen __instance)
        {
            try
            {
                if (LoadStatus != "pending" || MainInstance == null || __instance == null) return;
                if (MainInstance.GetValue(__instance) is bool main && !main)
                {
                    doomedLoad = true;
                    Log.Info("Test bridge: the bridge's load started a loading screen while another was still up; the game removes the new one, so this load can't run.");
                }
            }
            catch (Exception ex) { Log.Error("Test bridge, loading screen check: " + ex.Message); }
        }

        private static bool MenuSceneLoaded()
        {
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).name == "MainMenu") return true;
            return false;
        }

        private static void WatchLoadingScreens()
        {
            if (!doomedLoad) return;
            if (!LoadScene.isLoading)
            {
                doomedLoad = false;
                return;
            }
            if (LoadingScreenUp() || GameManager.isCitySceneBeingUnloaded || !MenuSceneLoaded()) return;
            doomedLoad = false;
            LoadScene.isLoading = false;
            Log.Error($"Test bridge: the bridge's load died with its loading screen (another was still up); cleared the game's loading flag, so loads work again. {Where()}.");
            if (LoadStatus == "pending") EndLoad("failed", $"the game's load never ran (its loading screen met another one still up; the bridge cleared the loading flag); {Where()}: load again");
        }

        // The bridge stops: nothing of this session's carries over to a start after a mod re-enable.
        internal static void Reset()
        {
            if (runUntil != null && SaveGameManager.Current != null) SetSpeed(1);
            runUntil = null;
            arrivedAt = null;
            keepNeeds = false;
            wasGoingToHospital = false;
            LoadTarget = null;
            LoadStatus = null;
            loadError = null;
            doomedLoad = false;
            characterChecks.Clear();
            saving = null;
            SaveHooks = 0;
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
            doomedLoad = false;
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
            if (UnityEngine.Time.realtimeSinceStartup > loadDeadline) EndLoad("failed", $"not in the city with it after {LoadSeconds:F0} seconds; {Where()}");
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
