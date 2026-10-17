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
                case "walk": RequireFtCity(cmd); return Walk(args);
                case "drive": RequireCity(cmd); return Drive(args);
                case "vehicle": RequireFtCity(cmd); return Vehicle(args);
                case "commissary": RequireFtCity(cmd); return CommissaryCommand(args);
                case "crew": RequireFtCity(cmd); return Crew(args);
                case "fixture": RequireFtCity(cmd); return Fixture(args);
                case "logistics": RequireCity(cmd); return Logistics(args);
                case "parks": RequireCity(cmd); return Parks();
                case "day_summary": RequireCity(cmd); return DaySummaryCommand(args);
                case "places": RequireCity(cmd); return PlacesCommand(args);
                case "map": RequireCity(cmd); return MapCommand(args);
                case "route": RequireFtCity(cmd); return RouteCommand(args);
                case "price": RequireFtCity(cmd); return PriceCommand(args);
                case "use": return Use(args);
                case "taxi": RequireFtCity(cmd); return Taxi(args);
                case "phone": RequireFtCity(cmd); return Phone(args);
                case "call": return Call(args);
                case "eat": RequireFtCity(cmd); return Eat(args);
                case "place": RequireFtCity(cmd); return Place(args);
                case "drop": RequireFtCity(cmd); return Drop(args);
                case "pickup": RequireFtCity(cmd); return Pickup(args);
                case "handtruck": return HandTruckCommand(args);
                case "ui": return TestBridgeUi.Run(args);
                case "shoot": RequireCity(cmd); return TestBridgeShoot.Shoot(args);
                case "save": RequireFtCity(cmd); return Save(args);
                case "load": return Load(args);
                case "new_game": return NewGame(args);
                case "quit": return Quit();
                default:
                    throw new BridgeError($"Unknown command '{cmd}'. Commands: state, console, debug, time, money, teleport, walk, drive, vehicle, commissary, crew, route, price, use, taxi, phone, call, eat, place, drop, pickup, handtruck, fixture, logistics, parks, day_summary, places, map, ui, shoot, save, load, new_game, quit, op.");
            }
        }

        // ---- parks ----

        // How far from a park's edge, and how many, the nearest building entrances listed.
        private const double ParkEntranceRadius = 150;
        private const int ParkEntrances = 5;

        // {}: the game's park triggers as ParkAreas found them (kept and skipped), each with its bounds, neighbourhood and the
        // nearest building entrances. Reads only; one park that fails gives an "error" entry, not a refusal.
        private static JToken Parks()
        {
            var (kept, skipped) = ParkAreas.ForTesting();
            var buildings = World.AllBuildings();
            var parks = new JArray();
            foreach (var (type, name, colliders) in kept)
            {
                try { parks.Add(Park(type, name, colliders, buildings)); }
                catch (Exception ex) { parks.Add(new JObject { ["name"] = name, ["kind"] = type, ["error"] = ex.Message }); }
            }
            Log.Info($"Test bridge: parks: {kept.Count} kept, {skipped.Count} skipped, {buildings.Count} buildings searched.");
            return new JObject
            {
                ["entrance_radius"] = ParkEntranceRadius,
                ["parks"] = parks,
                ["skipped"] = new JArray(skipped.Select(s => new JObject { ["name"] = s.Name, ["kind"] = s.Type, ["reason"] = s.Reason })),
            };
        }

        private static JObject Park(string type, string name, Collider[] colliders, List<(CityBuildingController Controller, Vector3[] Doors)> buildings)
        {
            var usable = colliders.Where(ParkAreas.Usable).ToArray();
            if (usable.Length == 0) throw new InvalidOperationException("no usable trigger collider now (switched off since the city loaded)");
            var bounds = usable[0].bounds;
            foreach (var c in usable.Skip(1)) bounds.Encapsulate(c.bounds);
            var min = bounds.min;
            var max = bounds.max;
            // The neighbourhood of the building with the entrance nearest the centre, as World.ReadSpot reads it.
            string? neighbourhood = null;
            var nearest = double.MaxValue;
            var near = new List<(string Address, Vector3 Door, double Distance)>();
            foreach (var (controller, doors) in buildings)
            {
                var building = controller == null ? null : controller.building;
                if (building == null) continue;
                foreach (var door in doors)
                {
                    var toCentre = Flat(door, bounds.center);
                    if (toCentre < nearest) { nearest = toCentre; neighbourhood = building.Neighbourhood; }
                }
                var address = building.Address;
                if (address == null) continue;
                (Vector3 Door, double Distance)? best = null;
                foreach (var door in doors)
                {
                    // The bounds first, so only doors near the park are measured to its colliders.
                    var dx = Math.Max(Math.Max(min.x - door.x, 0), door.x - max.x);
                    var dz = Math.Max(Math.Max(min.z - door.z, 0), door.z - max.z);
                    if (Math.Sqrt(dx * dx + dz * dz) > ParkEntranceRadius) continue;
                    var distance = ParkAreas.DistanceTo(usable, door);
                    if (distance <= ParkEntranceRadius && (best == null || distance < best.Value.Distance)) best = (door, distance);
                }
                if (best != null) near.Add((World.AddressKey(address.streetName, address.streetNumber), best.Value.Door, best.Value.Distance));
            }
            static JToken R(double v) => Math.Round(v, 2);
            return new JObject
            {
                ["name"] = name,
                ["kind"] = type,
                ["neighbourhood"] = neighbourhood,
                ["colliders"] = usable.Length,
                ["concave_colliders"] = usable.Count(c => c is MeshCollider mesh && !mesh.convex),
                ["bounds"] = new JObject
                {
                    ["min"] = new JObject { ["x"] = R(min.x), ["y"] = R(min.y), ["z"] = R(min.z) },
                    ["max"] = new JObject { ["x"] = R(max.x), ["y"] = R(max.y), ["z"] = R(max.z) },
                },
                ["centre"] = new JObject { ["x"] = R(bounds.center.x), ["z"] = R(bounds.center.z) },
                ["entrances"] = new JArray(near.OrderBy(n => n.Distance).Take(ParkEntrances).Select(n => new JObject
                {
                    ["address"] = n.Address, ["x"] = R(n.Door.x), ["z"] = R(n.Door.z), ["distance"] = R(n.Distance),
                })),
            };
        }

        private static double Flat(Vector3 a, Vector3 b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.z - b.z) * (a.z - b.z));

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
        // The last clock step of a run_until goes this far past its minute (a float clock lands a hair either side of
        // where it is sent), and it counts as arrived only once the minute the game shows is the target: live session 5
        // stopped at 16:04 for a 16:05 target, from a tolerance that took 16:04.999 as arrived.
        private const double OvershootMinutes = 0.01;

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
                // Happiness never causes a collapse. The game rebuilds it every hour as the sum of these modifiers
                // (HappinessHelper.UpdateHappiness), so the bridge only shows it: a value set by hand wouldn't hold.
                // Low happiness burns energy up to 25% faster and slows its regain, and a hire whose job demands include
                // a minimum player happiness (50 by default) loses satisfaction below it (live session 5: 100 to 40 in a
                // two-day run) [code, 1.0: EnergySettings, MinimumPlayerHappiness].
                ["happiness_modifiers"] = new JArray((game.happinessModifiers ?? new List<Helpers.HappinessModifierData>())
                    .Where(m => m != null).Select(HappinessModifierJson)),
                ["max"] = settings == null ? (JToken?)null : settings.MaxEnergyHungerHappinessValue,
                ["collapse_at"] = settings == null ? (JToken?)null : settings.HospitalizationEnergyThreshold,
                ["energy_off"] = game.gameVariables?.disableEnergy,
                ["going_to_hospital"] = Helpers.EnergyHelper.goingToHospital,
                ["kept_up"] = keepNeeds,
                ["warnings"] = new JArray(NeedWarnings(game, settings)),
            };
        }

        // A happiness modifier as the phone's Persona app lists it (CharacterStats.SetUpHappinessModifiers): its name, its
        // amount (happiness is their sum, held to 0 to 100, HappinessHelper.UpdateHappiness), and the time left unless the
        // game hides it (-1 or hideDuration: no time shown).
        private static readonly FieldInfo? HappinessModifiersField = AccessTools.Field(typeof(Helpers.HappinessHelper), "Modifiers");

        private static JObject HappinessModifierJson(Helpers.HappinessModifierData m)
        {
            int? amount = null;
            try
            {
                // The game's table is private; GetHappinessModifierFromType would log an error for an unknown type.
                if (HappinessModifiersField?.GetValue(null) is Dictionary<string, Helpers.HappinessModifier> table && m.type != null &&
                    table.TryGetValue(m.type, out var known) && known != null)
                    amount = known.amount;
            }
            catch (Exception) { }
            return new JObject
            {
                ["type"] = m.type,
                ["name"] = string.IsNullOrEmpty(m.type) ? null : Localizor.LocalizorManager.GetLocalization(m.type, null),
                ["amount"] = amount,
                ["hours_left"] = m.hoursLeft,
                ["shows_time"] = m.hoursLeft != -1 && !m.hideDuration,
            };
        }

        // The game's own warnings about the player's needs: the expressions the character shows below the low and too-low
        // thresholds (EnergyHelper.ShowHungerEmojis and ShowEnergyEmojis), out of energy at 0 (no running, the faint
        // vignette, notification_low_energy_while_playing), and the hospital below that [code, 1.0].
        private static IEnumerable<string> NeedWarnings(GameInstance game, Helpers.EnergySettings? settings)
        {
            var low = settings?.LowEnergyHungerExpressionThreshold ?? 25f;
            var tooLow = settings?.TooLowEnergyHungerExpressionThreshold ?? 5f;
            if (game.Hunger <= tooLow) yield return "very_hungry";
            else if (game.Hunger <= low) yield return "hungry";
            if (game.Energy <= 0f) yield return "out_of_energy";
            else if (game.Energy <= tooLow) yield return "exhausted";
            else if (game.Energy <= low) yield return "tired";
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
            var most = (float)Math.Max(0.0, runUntil.Value.Target - NowMinutes(game) + OvershootMinutes);
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

        // The minute the game shows (its clock drops the fraction).
        private static double ShownMinutes(GameInstance game) => game.Day * 1440.0 + game.Hour * 60.0 + (int)game.Minute;

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

            // A time skip ends a Self Driving ride (drive).
            if (until != null && Driving) throw new BridgeError($"A drive is under way ({DrivingVehicle}), and run_until would end its ride: wait for it (drive status).");
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
            if (ShownMinutes(game) < runUntil.Value.Target) return;
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

        // At patch time: without the reflection lookup the dead-load proof can't be made (review round 8).
        internal static void ReportLoadCheck()
        {
            if (MainInstance == null)
                Log.Error("Test bridge: InstanceBehavior<LoadingScreen>.IsMainInstance isn't there; a load that dies with its loading screen can't be detected, so the game's loading flag is only reported, never cleared.");
        }

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
            ResetDrive();
            RestoreAllInterpolation();
            runUntil = null;
            arrivedAt = null;
            keepNeeds = false;
            wasGoingToHospital = false;
            LoadTarget = null;
            LoadStatus = null;
            loadError = null;
            doomedLoad = false;
            newGame = false;
            newGameStep = null;
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
            : new JObject { ["name"] = LoadTarget, ["status"] = LoadStatus, ["error"] = loadError, ["new_game"] = newGameStep };

        private static void BeginLoad(string name)
        {
            LoadTarget = name;
            LoadStatus = "pending";
            loadError = null;
            doomedLoad = false;
            newGame = false;
            newGameStep = null;
            loadAttachesAtStart = FoodTrucksSession.Attaches;
            loadDeadline = UnityEngine.Time.realtimeSinceStartup + LoadSeconds;
            TransitionToSave.saveGameLoadErrored = false;
        }

        private static void EndLoad(string status, string? error)
        {
            newGame = false;
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
                if (!newGame)
                {
                    EndLoad("done", null);
                    return;
                }
                if (newGameStep != "saving" && TestBridgePump.Instance != null)
                {
                    newGameStep = "saving";
                    TestBridgePump.Instance.StartCoroutine(NewGameSave(LoadTarget!));
                }
            }
            if (UnityEngine.Time.realtimeSinceStartup > loadDeadline)
                EndLoad("failed", $"not in the city with it after {(newGame ? NewGameSeconds : LoadSeconds):F0} seconds; {Where()}");
        }

        // ---- new_game ----

        // The new game's character: the creator has one name field, so first name "FT", last name "Playtest".
        internal const string NewGameCharacter = "FT Playtest";
        private const float NewGameSeconds = 600f;
        // Set while the pending load is a new game: the city's arrival saves it under LoadTarget before the load is done.
        private static bool newGame;
        // Where a new game is ("main menu", "character creator", "city", "saving", "saved"), for state.load.new_game.
        private static string? newGameStep;
        private static readonly FieldInfo? NameField = AccessTools.Field(typeof(Character.Customization.NamePicker), "nameField");

        // From the main menu: the menu's Start new game as a custom game with the Normal preset's values (the custom
        // panel's own SetPreset turns the tutorial off and marks the difficulty custom), then the character creator's
        // Start with its random look and the name "FT Playtest"; with the tutorial off the intro's transition goes
        // straight to the city [code, 1.0: MainMenuController.StartNewGame, CustomGameOptionsHandler.SetPreset,
        // IntroCharacterCustomizer.StartGame, IntroToGameTransition.OnEnable]. Once the city is up the clock is paused
        // and the game saved under the name; state.load is done after that save.
        private static JToken NewGame(JObject args)
        {
            var name = Need(args, "name", "the new game's save name");
            if (!IsFt(name)) throw new BridgeError($"The bridge starts only games whose name starts with FT, not '{name}'.");
            if (Blueprints.FileSystemHelper.MakeValidFilename(name).Trim() != name)
                throw new BridgeError($"'{name}' has characters a save's file name can't hold.");
            var scene = Scene();
            if (scene != "menu" || !InstanceBehavior<MainMenuController>.IsInitialized)
                throw new BridgeError($"new_game works from the main menu only; the game is at the {(scene == "menu" ? "character creator" : scene)} now.");
            if (LoadStatus == "pending") throw new BridgeError($"A load of '{LoadTarget}' is under way; wait for it (state.load).");
            if (SaveGamePathHelper.GetAllSaveGamesFromVersion().Any(s => s != null && s.name == name))
                throw new BridgeError($"A save named '{name}' exists already; new_game never writes over a save: pick another FT name.");
            var pump = TestBridgePump.Instance ?? throw new BridgeError("The bridge's runner is gone.");
            var (variables, preset) = NewGameVariables();
            BeginLoad(name);
            newGame = true;
            newGameStep = "main menu";
            loadDeadline = UnityEngine.Time.realtimeSinceStartup + NewGameSeconds;
            pump.StartCoroutine(NewGameFlow(name, variables));
            return new JObject { ["loading"] = true, ["name"] = name, ["character"] = NewGameCharacter, ["preset"] = preset };
        }

        // The Normal preset's values as the custom game panel takes them: difficulty custom, tutorial off.
        private static (GameVariables Variables, string Preset) NewGameVariables()
        {
            var settings = InstanceBehavior<GlobalReferences>.Instance?.difficultySettings;
            if (settings == null || settings.Length == 0) throw new BridgeError("The game's difficulty presets aren't there.");
            var setting = settings.FirstOrDefault(s => s != null && s.key != null && s.key.IndexOf("normal", StringComparison.OrdinalIgnoreCase) >= 0)
                          ?? settings.FirstOrDefault(s => s != null && Convert.ToInt32(s.difficulty) == 2)
                          ?? settings.First(s => s != null);
            var variables = setting.ToGameVariables();
            variables.difficulty = default;
            variables.tutorialEnabled = false;
            return (variables, setting.key);
        }

        private static IEnumerator NewGameFlow(string name, GameVariables variables)
        {
            // As a load from the menu: the menu's last loading screen gone first (LoadFromMenu).
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
            yield return null;
            yield return null;
            if (LoadStatus != "pending") yield break;
            if (LoadScene.isLoading)
            {
                EndLoad("failed", $"the game started another load first; {Where()}");
                yield break;
            }
            Log.Info($"Test bridge: new game '{name}' (custom, Normal values, tutorial off), to the character creator.");
            newGameStep = "character creator";
            InstanceBehavior<MainMenuController>.Instance.StartNewGame(variables);

            // The creator, with its loading screen gone (its Start picks the body panel the frame after it wakes).
            Intro.IntroCharacterCustomizer? creator = null;
            deadline = UnityEngine.Time.realtimeSinceStartup + MenuSeconds;
            while (true)
            {
                if (LoadStatus != "pending") yield break;
                if (UnityEngine.Time.realtimeSinceStartup > deadline)
                {
                    EndLoad("failed", $"the character creator didn't come up within {MenuSeconds:F0} seconds; {Where()}");
                    yield break;
                }
                if (!LoadScene.isLoading && !LoadingScreenUp())
                {
                    creator = UnityEngine.Object.FindAnyObjectByType<Intro.IntroCharacterCustomizer>();
                    if (creator != null && creator.isActiveAndEnabled) break;
                }
                yield return null;
            }
            for (var i = 0; i < 10; i++) yield return null;
            if (LoadStatus != "pending") yield break;
            if (creator == null || creator.characterNamePicker == null || NameField == null || !(NameField.GetValue(creator.characterNamePicker) is object field))
            {
                EndLoad("failed", "the character creator's name field isn't there (NamePicker.nameField)");
                yield break;
            }
            var text = field.GetType().GetProperty("text");
            if (text == null)
            {
                EndLoad("failed", "the character creator's name field has no text property");
                yield break;
            }
            text.SetValue(field, NewGameCharacter);
            newGameStep = "city";
            try { creator.StartGame(); }
            catch (Exception ex)
            {
                EndLoad("failed", "the character creator's Start failed: " + ex.Message);
                yield break;
            }
            var game = SaveGameManager.Current;
            if (game?.charactersData == null || game.charactersData.Count == 0 || game.charactersData[0].name != NewGameCharacter)
            {
                EndLoad("failed", "the character creator didn't take the character (no name set?)");
                yield break;
            }
            // The creator names the save after the character; the bridge's name instead, before the city is built.
            game.SaveGameName = name;
            Log.Info($"Test bridge: new game character '{NewGameCharacter}' made; to the city as '{name}'.");
            // With the tutorial off the intro's transition starts the city's load itself; if it didn't, its Continue.
            var started = UnityEngine.Time.realtimeSinceStartup;
            while (UnityEngine.Time.realtimeSinceStartup - started < 3f)
            {
                if (LoadStatus != "pending" || LoadScene.isLoading || Scene() != "menu") yield break;
                yield return null;
            }
            var transition = UnityEngine.Object.FindAnyObjectByType<Intro.IntroToGameTransition>(FindObjectsInactive.Include);
            if (transition == null)
            {
                EndLoad("failed", $"the city didn't start loading after the character creator, and the intro's transition isn't there; {Where()}");
                yield break;
            }
            Log.Info("Test bridge: the intro didn't go on by itself; its Continue.");
            transition.Continue();
        }

        // The city is up with the new game: the clock paused, then the save under the bridge's name, then done.
        private static IEnumerator NewGameSave(string name)
        {
            while (LoadScene.isLoading || LoadingScreenUp())
            {
                if (LoadStatus != "pending") yield break;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(2f);
            if (LoadStatus != "pending") yield break;
            try { InstanceBehavior<UIs>.Instance?.gameSpeed?.SetPlayerPause(true); }
            catch (Exception ex) { Log.Error("Test bridge: new game, pausing: " + ex.Message); }
            // The game refuses a save while something holds it (a placement, a screen): a few tries a few seconds apart.
            Deferred? write = null;
            string? refusal = null;
            for (var attempt = 0; attempt < 10 && write == null; attempt++)
            {
                if (LoadStatus != "pending") yield break;
                try { write = Save(new JObject { ["name"] = name }) as Deferred; }
                catch (Exception ex) { refusal = ex.Message; }
                if (write == null) yield return new WaitForSecondsRealtime(3f);
            }
            if (write == null)
            {
                EndLoad("failed", $"the new game is in the city as '{SaveGameManager.Current?.SaveGameName}', but the bridge couldn't save it: {refusal}");
                yield break;
            }
            while (true)
            {
                if (LoadStatus != "pending")
                {
                    write.OnStop?.Invoke();
                    yield break;
                }
                JToken? result;
                try { result = write.Poll(); }
                catch (Exception ex)
                {
                    write.OnStop?.Invoke();
                    EndLoad("failed", $"the new game is in the city, but its save failed: {ex.Message}");
                    yield break;
                }
                if (result != null)
                {
                    newGameStep = "saved";
                    Log.Info($"Test bridge: new game saved as '{name}' ({result["bytes"]} bytes); paused.");
                    EndLoad("done", null);
                    yield break;
                }
                yield return null;
            }
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
