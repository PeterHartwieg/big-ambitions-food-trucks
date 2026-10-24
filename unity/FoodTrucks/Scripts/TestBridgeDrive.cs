#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using FoodTrucks.Core.Fleet;
using GleyTrafficSystem;
using Helpers;
using Newtonsoft.Json.Linq;
using UI;
using UnityEngine;

namespace FoodTrucks
{
    // Test-only player travel: advance the game clock for a distance-based journey, then park natively.
    // This is not physical crew driving. Legacy Self Driving diagnostics remain available through op=check.
    // No other mod is required for start; interruptions must not grant an early arrival.
    internal static partial class TestBridgeCommands
    {
        // The rig's ride: a speed of 1 to 3 times real time (its own clamp).
        private const int DriveMinSpeed = 1, DriveMaxSpeed = 3;
        // How long the rig may take to pick the command up (it polls each second), and the whole ride (its own limit is
        // 20 minutes; a minute more for the start and the result).
        private const float RigPickUpSeconds = 10f;
        private const float RideSeconds = 21f * 60f;
        // Lane waypoints round the truck tried for the pull-out, nearest first.
        private const float LaneSearchMetres = 40f;
        private const int LaneCandidates = 24;
        // Lane spots tried when Self Driving refuses a ride before it drives (its start check: a parked car in the way of
        // the truck joining the lane); then the truck goes back.
        private const int PullOutAttempts = 3;
        // The way ahead of a lane spot kept clear of parked cars: as far as the self-driving mod's join check looks (its
        // refusal named a parked car 43 m ahead), as wide as the truck with its margin.
        private const float ClearAheadMetres = 50f, ClearHalfWidth = 1.75f;
        // Closer than this to where it goes, the truck isn't driven, only parked.
        private const float NoRideMetres = 40f;
        // Further than this from where it goes after the ride (a ride cut short far away), it isn't parked by teleport.
        private const float ParkReachMetres = 150f;
        // As a crew's truck looks for a space at a stop (HireRoutes: 60 m, then 200 m).
        private const float KerbMetres = 60f, KerbWideMetres = 200f;

        private sealed class DriveRun
        {
            public string Id = "";
            public string Place = "";
            public string Phase = "";
            public int Speed;
            public Vector3 Goal;
            public string? Site;
            public Vector3 RideTarget;
            public Vector3 FromPosition;
            public float FromHeading;
            public float StartedReal;
            public double StartedGame;
            public JObject? PullOut;
            public readonly List<int> TriedWaypoints = new List<int>();
            public readonly JArray Attempts = new JArray();
            public string? RideBuilding;
            public JObject? Ride;
            public JObject? Park;
            public string? Error;
            public JObject? Final;
            public float? EndedReal;
            public double? EndedGame;
            public Coroutine? Routine;
            public bool Done => EndedReal != null;
            public bool Simulated;
        }

        // Driving is played for the player, as the minigames are: the self-driving ride ran people over and drove on
        // (attempt 6, days 8 and 12), and the driver is the player, so every bump and hit-and-run was theirs to pay. A
        // drive now lets the clock run for the trip at a city pace and then parks the vehicle at the place, as the
        // ride would have; nothing on the way is hit. 8 m a game minute is the truck in town (tools/balance
        // --drive-speed), and streets add about a third to the straight line.
        private const float SimMetresPerMinute = 8f;
        private const float SimDetour = 1.35f;

        private static DriveRun? drive;

        // A drive under way: the clock's run_until and a teleport of that truck wait for it.
        internal static bool Driving => drive != null && !drive.Done;
        internal static string? DrivingVehicle => Driving ? drive!.Id : null;

        private static object Drive(JObject args)
        {
            var op = Str(args, "op") ?? "start";
            switch (op)
            {
                case "check": return DriveCheck();
                case "status": return DriveStatus();
                case "start":
                    RequireFtCity("drive");
                    return DriveStart(args);
                default:
                    throw new BridgeError($"Unknown drive op '{op}': start, status or check.");
            }
        }

        // ---- The self-driving mod ----

        private static string RigFolder => Path.Combine(Application.persistentDataPath, "DrivenRides", "tests");
        private static bool? selfDrivingLogged;

        private static Assembly? SelfDriving()
        {
            Assembly? found = null;
            try { found = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == "DrivenRides"); }
            catch (Exception ex) { Log.Error("Test bridge, Self Driving link: " + ex.Message); }
            // Once per state, as a patch's startup line: Active when the mod is loaded, Not active when it isn't.
            var active = found != null;
            if (selfDrivingLogged != active)
            {
                selfDrivingLogged = active;
                Log.Info(active
                    ? $"Self Driving link (DrivenRides test rig, {RigFolder}): Active."
                    : "Self Driving link (DrivenRides test rig): Not active (HART. Self Driving isn't loaded).");
            }
            return found;
        }

        // The truck's drivable prefab, found the way the self-driving mod looks for one (Vehicles/PlayerVehicles/<id>, by
        // the game's prefab helper, which falls back to a file name in any mod's bundle), with an NWH VehicleController in
        // it, and its wheels.
        private static (bool Found, string? Name, int Wheels) DrivablePrefab()
        {
            // "foodtrucks:vehicletype_foodtruck" -> "foodtruck", as the self-driving mod cuts a type name (NwhCar.Id).
            var id = FoodTrucksMod.TruckTypeName.Substring(FoodTrucksMod.VehicleTypePrefix.Length);
            var prefab = PrefabHelper.LoadPrefabAssetByName("Vehicles/PlayerVehicles/" + id);
            if (prefab == null) return (false, null, 0);
            var nwh = HarmonyLib.AccessTools.TypeByName("NWH.VehiclePhysics2.VehicleController");
            var wheel = HarmonyLib.AccessTools.TypeByName("NWH.Common.Vehicles.WheelUAPI");
            var hasNwh = nwh != null && prefab.GetComponentInChildren(nwh, true) != null;
            var wheels = wheel == null ? 0 : prefab.GetComponentsInChildren(wheel, true).Length;
            return (hasNwh, prefab.name, wheels);
        }

        // { "op": "check" }: whether a drive can go at all: the self-driving mod loaded, its rig's folder there, and the truck
        // found as a drivable car by its prefab. Reads only.
        private static JToken DriveCheck()
        {
            var mod = SelfDriving();
            var (drivable, prefab, wheels) = DrivablePrefab();
            var folder = RigFolder;
            JObject? last = null;
            try { last = LastRideLine(Path.Combine(folder, "ride-results.jsonl")); }
            catch (Exception ex) { Log.Info("Test bridge: drive check, results file: " + ex.Message); }
            var problems = new JArray();
            if (mod == null) problems.Add("HART. Self Driving isn't loaded: switch it on in the Mods menu.");
            else if (!Directory.Exists(folder)) problems.Add("Self Driving's test rig is off: start the game with -foodtrucks-test.");
            if (!drivable) problems.Add(prefab == null
                ? "No drivable prefab for the truck under Vehicles/PlayerVehicles: Self Driving can't plan it."
                : $"The truck's prefab {prefab} has no NWH VehicleController: Self Driving can't plan it.");
            return new JObject
            {
                ["self_driving"] = mod != null,
                ["rig_folder"] = folder,
                ["rig_ready"] = mod != null && Directory.Exists(folder),
                ["truck_type"] = FoodTrucksMod.TruckTypeName,
                ["drivable_prefab"] = drivable,
                ["prefab"] = prefab,
                ["wheels"] = wheels,
                ["last_ride"] = last,
                ["problems"] = problems,
            };
        }

        private static JObject? LastRideLine(string path)
        {
            if (!File.Exists(path)) return null;
            var lines = ReadShared(path, 0).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            for (var i = lines.Count - 1; i >= 0; i--)
            {
                try { return JObject.Parse(lines[i]); }
                catch (Exception) { }
            }
            return null;
        }

        // The file from a byte offset, read while the other mod may append to it.
        private static string ReadShared(string path, long from)
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (from > stream.Length) from = 0;
            stream.Seek(from, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static long FileLength(string path)
        {
            try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
            catch (Exception) { return 0; }
        }

        // ---- start ----

        // { "op": "start", "id", "place": "bay:<site>" | "address:<ba:street_…#N>" | "stop:<N>", "speed"?: 1..3 }
        private static JToken DriveStart(JObject args)
        {
            var id = Need(args, "id", "the truck's id");
            var place = Need(args, "place", "bay:<site>, address:<address> or stop:<N> (today's route)");
            var speedArg = Num(args, "speed");
            if (speedArg != null && (speedArg != Math.Floor(speedArg.Value) || speedArg < DriveMinSpeed || speedArg > DriveMaxSpeed))
                throw new BridgeError($"speed is a whole number from {DriveMinSpeed} to {DriveMaxSpeed} (times real time).");
            var speed = (int)(speedArg ?? DriveMaxSpeed);
            if (Driving) throw new BridgeError($"A drive is under way ({drive!.Id} to {drive.Place}): wait for it (drive status).");
            var vehicle = DrivenVehicle(id);
            var food = FoodTrucksMod.KindOf(vehicle.vehicleTypeName);
            if (food == "cart") throw new BridgeError($"{id} is a cart: a cart is pushed (walk), not driven.");
            if (food == null && !place.StartsWith("address:", StringComparison.Ordinal))
                throw new BridgeError($"{id} isn't a food truck: it drives to an address (address:ba:street_<name>#<number>), not a bay or a route stop.");
            var game = SaveGameManager.Current;
            if (game.ActiveVehicleId != id) throw new BridgeError($"You're not in {id}: get in first (vehicle {id} enter).");
            var state = FoodTrucksSession.CurrentState ?? throw new BridgeError("The mod's save data isn't loaded.");
            if (FoodTrucks.Core.Saves.StreetSales.Find(state, id)?.IsOpen == true) throw new BridgeError($"{id} is open for business: close it first (vehicle close).");
            if (BuildingManager.IsInsideBuilding) throw new BridgeError("The truck is inside a building: drive it out into the street first.");
            if (Paused() == true) throw new BridgeError("The game is paused: driving takes real time. Set a speed first.");
            if (runUntil != null) throw new BridgeError("run_until is running, and a time skip would end the ride: wait for it to stop.");
            var machine = InstanceBehavior<UIs>.Instance?.timeMachine;
            if (machine != null && machine.isRunning) throw new BridgeError("A time skip (sleep, a paid activity) is running; wait for it to end.");
            var controller = Spawned(vehicle);

            var run = new DriveRun { Id = id, Place = place, Speed = speed, Phase = "pulling out" };
            ResolveGoal(run, vehicle, state);
            run.FromPosition = controller.transform.position;
            run.FromHeading = controller.transform.eulerAngles.y;
            run.StartedReal = UnityEngine.Time.realtimeSinceStartup;
            run.StartedGame = NowMinutes(game);
            var pump = TestBridgePump.Instance ?? throw new BridgeError("The bridge's pump isn't running.");

            run.Simulated = true;
            if (Flat(controller.transform.position - run.Goal) > NoRideMetres)
            {
                run.PullOut = null;
                run.Phase = "riding";
            }
            else
            {
                run.PullOut = null;
                run.Phase = "parking";
            }
            drive = run;
            run.Routine = pump.StartCoroutine(DriveSteps(run));
            Log.Info($"Test bridge: drive {id} to {place} (x {run.Goal.x:F1}, z {run.Goal.z:F1}) at {speed}x: {run.Phase}.");
            return DriveStatus();
        }

        // Where the drive goes: the point to park by, and the building the self-driving mod drives to (it takes the
        // building nearest the point it is given).
        private static void ResolveGoal(DriveRun run, VehicleInstance vehicle, FoodTrucks.Core.Saves.SaveState state)
        {
            var colon = run.Place.IndexOf(':');
            var kind = colon < 0 ? run.Place : run.Place.Substring(0, colon);
            var what = colon < 0 ? "" : run.Place.Substring(colon + 1);
            switch (kind)
            {
                case "bay":
                {
                    var site = Site(what);
                    var bay = Bay(what);
                    var building = World.BuildingAt(site.Address) ?? throw new BridgeError($"The building of {what} ({site.Address}) isn't in this city.");
                    run.Site = what;
                    run.Goal = bay.Point;
                    RideTargetNear(run, bay.Point);
                    return;
                }
                case "address":
                {
                    if (!AddressForm.IsMatch(what))
                        throw new BridgeError($"address: takes a building's address as the game writes it, ba:street_<name>#<number>; got '{what}'.");
                    var building = World.BuildingAt(what) ?? throw new BridgeError($"no building at {what} in this city");
                    var doors = World.EntrancesOf(what);
                    run.Goal = doors != null && doors.Length > 0 ? doors[0] : building.GetNavMeshTargetPosition();
                    RideTargetNear(run, run.Goal);
                    return;
                }
                case "stop":
                {
                    if (!int.TryParse(what, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < 1)
                        throw new BridgeError($"stop: takes the stop's number on today's route, from 1; got '{what}'.");
                    var record = FoodTrucks.Core.Saves.StreetSales.Find(state, vehicle.id);
                    var schedule = record?.Schedule;
                    var today = FoodTrucks.Core.Common.GameDays.Weekday(Math.Max(1, HireRunner.DayOf(HireCrew.Now())));
                    var stops = WeekPlan.StopsOf(schedule, today);
                    if (number > stops.Count)
                        throw new BridgeError($"{vehicle.id} has {stops.Count} stop{(stops.Count == 1 ? "" : "s")} on today's route ({today}); no stop {number}.");
                    var stop = schedule!.Stops[stops[number - 1]];
                    // A stop in the truck's own commissary's bay (as crews count one, Commissary.CrewBayStops) is parked in
                    // the bay, not at the kerb nearby, so the truck isn't fined at 04:00 (review round 1, Sol).
                    var space = state.Commissary.Spaces.FirstOrDefault(s => s.VehicleId == vehicle.id);
                    if (space != null && global::FoodTrucks.Commissary.CrewBayStops(state, record!).Contains(stops[number - 1]))
                    {
                        run.Site = space.CommissaryId;
                        run.Goal = Bay(space.CommissaryId).Point;
                    }
                    else run.Goal = Ground((float)stop.X, (float)stop.Z);
                    RideTargetNear(run, run.Goal);
                    return;
                }
                default:
                    throw new BridgeError($"Unknown place '{run.Place}': use bay:<site>, address:<address> or stop:<N> (today's route).");
            }
        }

        // The self-driving rig drives to the building whose position is nearest the x, z it is given (all of the city's
        // buildings, outside a gated community), and stops by that building's way in. Some buildings' controllers stand at
        // the world's origin (live check, 17 October 2026: 37 4th Avenue's did, and the rig took 15 2nd Avenue), so the
        // building picked is the one whose way in is nearest the goal among those the rig can tell apart by position, and
        // its own position is sent.
        private static void RideTargetNear(DriveRun run, Vector3 goal)
        {
            var all = UnityEngine.Object.FindObjectsOfType<CityBuildingController>(true)
                .Where(b => b != null && b.building != null && b.building.locatedInsidePrivateFenceIndex < 0).ToList();
            var alone = all.GroupBy(b => (Mathf.Round(b.transform.position.x), Mathf.Round(b.transform.position.z)))
                .Where(g => g.Count() == 1).Select(g => g.First());
            CityBuildingController? best = null;
            var bestMetres = float.MaxValue;
            foreach (var b in alone)
            {
                float metres;
                try { metres = Flat(b.GetNavMeshTargetPosition() - goal); }
                catch (Exception) { continue; }
                if (metres < bestMetres) { bestMetres = metres; best = b; }
            }
            if (best == null) throw new BridgeError("No building near the goal that Self Driving can drive to.");
            run.RideTarget = best.transform.position;
            run.RideBuilding = $"{best.building.StreetNumber} {best.building.StreetName}";
        }

        // The truck onto the nearest driving lane it fits on, facing with traffic, its rear axle on a lane waypoint as the
        // self-driving mod's start check wants it (the route starts under the car). Lanes the truck doesn't fit on (another
        // of the player's vehicles, a building, a person) are passed over; the lanes' parked cars and traffic there are
        // cleared by the game's own clean-up, as for a spawned vehicle.
        private static JObject PullOut(VehicleInstance vehicle, VehicleController controller, List<int> tried)
        {
            if (!TrafficManager.HasInstance || !TrafficManager.IsInitialized) throw new BridgeError("The city's traffic isn't running yet: try again in a few seconds.");
            var manager = TrafficManager.Instance;
            var from = controller.transform.position;
            var near = new List<Waypoint>();
            manager.GetClosestWaypoint(from, LaneSearchMetres, w =>
            {
                if (w != null && w.neighbors != null && w.neighbors.Count > 0 && w.allowedCars != null && w.allowedCars.Contains(VehicleTypes.Car)
                    && TrafficManager.WithinHeight(w, from.y, 3f) && TrafficManager.IsNotIntersection(w)) near.Add(w);
                return false;
            });
            if (near.Count == 0) throw new BridgeError($"No driving lane within {LaneSearchMetres:F0} m of {vehicle.id}.");
            // The rear axle's place along the truck (its wheels' rearmost, local z).
            var rearZ = Legality.TruckWheels().Min(w => w.z);
            var vans = Vans();
            var refused = new List<string>();
            // Nearest first, those with the way ahead clear of parked cars before the rest; not a spot tried already, nor
            // one within 10 m of it on the same lane.
            var candidates = near.OrderBy(w => (w.position - from).sqrMagnitude).Take(LaneCandidates)
                .Where(w => !tried.Any(t => manager.GetWaypoint(t) is Waypoint old && (old.position - w.position).sqrMagnitude < 100f
                                            && Quaternion.Angle(manager.GetWaypointOrientation(old), manager.GetWaypointOrientation(w)) < 30f))
                .Select(w => (Lane: w, Clear: ClearAhead(manager, w))).OrderBy(c => c.Clear ? 0 : 1).Select(c => c.Lane).ToList();
            foreach (var w in candidates)
            {
                var rotation = manager.GetWaypointOrientation(w);
                rotation = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
                var root = w.position - rotation * new Vector3(0f, 0f, rearZ);
                Vector3 ground;
                try { ground = Ground(root.x, root.z); }
                catch (BridgeError) { continue; }
                if (Mathf.Abs(ground.y - w.position.y) > 1.5f) ground = new Vector3(root.x, w.position.y, root.z);
                var blockers = InTheWay(controller, ground, rotation, vans).Where(b => !(b.Thing is GameObject g && IsPlayerObject(g))).ToList();
                var fixedOnes = blockers.Where(b => b.LaneCar == null && !b.Traffic).ToList();
                if (fixedOnes.Count > 0)
                {
                    if (refused.Count < 3) refused.Add($"waypoint {w.listIndex}: {string.Join(", ", fixedOnes.Select(b => b.What))}");
                    continue;
                }
                var reply = TeleportVehicle(vehicle.id, ground, rotation.eulerAngles.y, true, blockers.Where(b => b.LaneCar != null).ToList(), vans);
                // The traffic there, as the game clears a spawned car's place.
                VehicleHelper.DestroyBlockingVehicles(controller.gameObject, controller.vehicleType, controller.transform);
                tried.Add(w.listIndex);
                var pose = reply as JObject ?? new JObject();
                pose["waypoint"] = w.listIndex;
                pose["clear_ahead"] = ClearAhead(manager, w);
                pose["metres"] = Math.Round(Flat(controller.transform.position - from), 1);
                Log.Info($"Test bridge: drive {vehicle.id}: out of its space onto the lane at waypoint {w.listIndex}, {pose["metres"]} m away.");
                return pose;
            }
            throw new BridgeError($"No free place on a driving lane within {LaneSearchMetres:F0} m of {vehicle.id}" +
                                  (refused.Count > 0 ? $" ({string.Join("; ", refused)})." : "."));
        }

        // No parked car in the strip ahead of a lane waypoint, along its direction, as wide as the truck with its margin.
        private static bool ClearAhead(TrafficManager manager, Waypoint w)
        {
            var forward = manager.GetWaypointOrientation(w) * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-4f) return true;
            forward.Normalize();
            var centre = w.position + forward * (ClearAheadMetres / 2f) + Vector3.up * 1f;
            return !Physics.CheckBox(centre, new Vector3(ClearHalfWidth, 0.8f, ClearAheadMetres / 2f), Quaternion.LookRotation(forward),
                1 << LayerHelper.ParkedVehiclesLayerIndex, QueryTriggerInteraction.Collide);
        }

        private static bool IsPlayerObject(GameObject g) => g.GetComponentInParent<PlayerController>() != null;

        // ---- the ride and the parking ----

        private static IEnumerator DriveSteps(DriveRun run)
        {
            if (run.Phase == "riding" && run.Simulated)
            {
                var metres = Flat(run.FromPosition - run.Goal) * SimDetour;
                var minutes = Math.Max(2.0, metres / SimMetresPerMinute);
                var game = SaveGameManager.Current;
                var end = Math.Ceiling(game.Day * 1440.0 + game.Hour * 60 + game.Minute + minutes);
                bool started;
                try { TestBridgeCommands.RunClockUntil((int)(end / 1440), (int)(end % 1440), forDrive: true); started = true; }
                catch (Exception ex) { Fail(run, "the trip's clock: " + ex.Message); yield break; }
                if (!started) yield break;
                while (TestBridgeCommands.RunningUntil) { if (run.Done) yield break; yield return null; }
                if (run.Done) yield break;
                if (!ReferenceEquals(game, SaveGameManager.Current) || NowMinutes(game) < end)
                { Fail(run, "the trip's clock was cancelled before arrival"); yield break; }
                run.Ride = new JObject { ["end"] = "Complete: arrived", ["game_minutes"] = Math.Round(minutes), ["metres"] = Math.Round(metres) };
                Log.Info($"Test bridge: drive {run.Id} to {run.Place} simulated: {metres:F0} m, {minutes:F0} game minutes.");
            }
            else if (run.Phase == "riding")
            {
                var ride = Rides(run);
                while (true)
                {
                    bool more;
                    try { more = ride.MoveNext(); }
                    catch (Exception ex) { Fail(run, "the ride: " + ex.Message); yield break; }
                    if (!more) break;
                    if (run.Done) yield break;
                    yield return ride.Current;
                }
                if (run.Done) yield break;
            }
            run.Phase = "parking";
            try { Park(run); }
            catch (Exception ex) { Fail(run, "parking: " + (ex is BridgeError ? ex.Message : ex.ToString())); yield break; }
            Finish(run, null);
        }

        private static IEnumerator Ride(DriveRun run)
        {
            var folder = RigFolder;
            var results = Path.Combine(folder, "ride-results.jsonl");
            var command = Path.Combine(folder, "command.txt");
            var offset = FileLength(results);
            var inv = CultureInfo.InvariantCulture;
            var line = string.Format(inv, "ride kind=own x={0:0.0} z={1:0.0} speed={2} autopark=off", run.RideTarget.x, run.RideTarget.z, run.Speed);
            File.WriteAllText(command, line + "\nend\n");
            Log.Info($"Test bridge: drive {run.Id}: sent Self Driving '{line}'.");
            var sent = UnityEngine.Time.realtimeSinceStartup;
            while (File.Exists(command))
            {
                if (LeftCity(run)) yield break;
                if (UnityEngine.Time.realtimeSinceStartup - sent > RigPickUpSeconds)
                {
                    try { File.Delete(command); }
                    catch (Exception) { }
                    PutBack(run, $"Self Driving didn't take the ride within {RigPickUpSeconds:F0} s (is it on, with -foodtrucks-test?)");
                    yield break;
                }
                yield return null;
            }
            var buffer = "";
            while (true)
            {
                if (LeftCity(run)) yield break;
                if (UnityEngine.Time.realtimeSinceStartup - sent > RideSeconds)
                {
                    Fail(run, $"no result from Self Driving after {RideSeconds / 60f:F0} minutes; the truck stands where the ride left it");
                    yield break;
                }
                if (FileLength(results) > offset)
                {
                    buffer += ReadShared(results, offset);
                    offset = FileLength(results);
                    var complete = buffer.LastIndexOf('\n');
                    if (complete >= 0)
                    {
                        foreach (var text in buffer.Substring(0, complete).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0))
                        {
                            JObject? result = null;
                            try { result = JObject.Parse(text); }
                            catch (Exception) { }
                            if (result == null || (string?)result["kind"] != "own") continue;
                            run.Ride = result;
                            break;
                        }
                        buffer = buffer.Substring(complete + 1);
                    }
                }
                if (run.Ride != null) break;
                yield return new WaitForSecondsRealtime(0.5f);
            }
            Log.Info($"Test bridge: drive {run.Id}: Self Driving says {run.Ride.ToString(Newtonsoft.Json.Formatting.None)}");
        }

        // The ride from lane spot to lane spot: refused before it drove (its start check found something in the way of
        // the truck joining the lane), the truck goes to the next spot and the ride is sent again; after the last, back into
        // the space it pulled out of.
        private static IEnumerator Rides(DriveRun run)
        {
            for (var attempt = 1; ; attempt++)
            {
                run.Ride = null;
                var ride = Ride(run);
                while (ride.MoveNext())
                {
                    if (run.Done) yield break;
                    yield return ride.Current;
                }
                if (run.Done || run.Ride == null) yield break;
                var refused = (string?)run.Ride["refused"] ?? (string?)run.Ride["error"];
                if (refused == null || run.Ride["seconds"] != null) yield break;
                run.Attempts.Add(new JObject { ["pull_out"] = run.PullOut, ["refused"] = refused });
                if (attempt >= PullOutAttempts)
                {
                    PutBack(run, $"Self Driving refused the ride from {attempt} places on the lane: {refused}");
                    yield break;
                }
                try
                {
                    var vehicle = DrivenVehicle(run.Id);
                    run.PullOut = PullOut(vehicle, Spawned(vehicle), run.TriedWaypoints);
                }
                catch (BridgeError ex)
                {
                    PutBack(run, $"Self Driving refused the ride: {refused}; no other place on the lane ({ex.Message})");
                    yield break;
                }
                // Settled on its wheels before the next try (the rig waits for a standing car too).
                var until = UnityEngine.Time.realtimeSinceStartup + 1f;
                while (UnityEngine.Time.realtimeSinceStartup < until) yield return null;
            }
        }

        // The city was left (a load, the main menu) while the drive ran: it ends there.
        private static bool LeftCity(DriveRun run)
        {
            if (Scene() == "city" && SaveGameManager.Current != null) return false;
            Fail(run, "the city was left during the drive");
            return true;
        }

        // A drive that never got going: the truck back where it stood, so it isn't left in a driving lane.
        private static void PutBack(DriveRun run, string why)
        {
            string back;
            try
            {
                TeleportVehicle(run.Id, run.FromPosition, run.FromHeading, true);
                back = "the truck is back where it stood";
            }
            catch (Exception ex)
            {
                back = "the truck couldn't be put back (" + ex.Message + ") and stands on the lane";
                Log.Error($"Test bridge: drive {run.Id}: {back}.");
            }
            Fail(run, $"{why}; {back}");
        }

        private static void Park(DriveRun run)
        {
            var vehicle = DrivenVehicle(run.Id);
            var controller = Spawned(vehicle);
            var short_ = Flat(controller.transform.position - run.Goal);
            if (short_ > ParkReachMetres && !run.Simulated)
            {
                run.Park = new JObject { ["parked"] = false, ["why"] = $"the truck stopped {short_:F0} m from {run.Place}, too far to park there", ["metres_short"] = Math.Round(short_) };
                return;
            }
            if (run.Site != null)
            {
                var token = TeleportToBay(run.Id, run.Site, null, true);
                var pose = token as JObject ?? new JObject();
                pose["parked"] = true;
                pose["where"] = "bay:" + run.Site;
                run.Park = pose;
                return;
            }
            var spot = HireRoutes.FreeParkingNear(run.Goal, KerbMetres, vehicle) ?? HireRoutes.FreeParkingNear(run.Goal, KerbWideMetres, vehicle)
                       ?? (run.Simulated ? HireRoutes.FreeParkingNear(run.Goal, KerbWideMetres * 3f, vehicle) : null);
            if (spot == null)
            {
                if (run.Simulated) throw new BridgeError($"no free parking space within {KerbWideMetres * 3f:F0} m of {run.Place}; the vehicle stays where it was");
                run.Park = new JObject { ["parked"] = false, ["why"] = $"no free parking space within {KerbWideMetres:F0} m of {run.Place}; the truck stands in the lane" };
                return;
            }
            var (position, rotation, _) = spot.Value;
            var reply = TeleportVehicle(run.Id, position, rotation.eulerAngles.y, true) as JObject ?? new JObject();
            reply["parked"] = true;
            reply["where"] = "kerb";
            reply["from_goal"] = Math.Round(Flat(controller.transform.position - run.Goal), 1);
            run.Park = reply;
        }

        private static void Fail(DriveRun run, string why) => Finish(run, why);

        private static void Finish(DriveRun run, string? error)
        {
            if (run.Done) return;
            run.Error = error;
            run.EndedReal = UnityEngine.Time.realtimeSinceStartup;
            var game = SaveGameManager.Current;
            run.EndedGame = game != null ? NowMinutes(game) : (double?)null;
            run.Phase = error == null ? "done" : "failed";
            try
            {
                var state = FoodTrucksSession.CurrentState;
                var vehicle = game?.VehicleInstances.FirstOrDefault(v => v != null && v.id == run.Id);
                if (state != null && vehicle != null) run.Final = TestBridgeState.Vehicle(state, vehicle);
            }
            catch (Exception ex) { Log.Error($"Test bridge: drive {run.Id}, reading the truck: {ex.Message}"); }
            if (error == null) Log.Info($"Test bridge: drive {run.Id} to {run.Place}: done after {run.EndedReal - run.StartedReal:F0} s.");
            else Log.Info($"Test bridge: drive {run.Id} to {run.Place}: failed: {error}.");
        }

        // {} or { "op": "status" }: the drive under way, or the last one (with its result).
        private static JToken DriveStatus()
        {
            var run = drive;
            if (run == null) return new JObject { ["running"] = false, ["phase"] = "none" };
            var now = run.EndedReal ?? UnityEngine.Time.realtimeSinceStartup;
            JObject? where = null;
            try
            {
                var vehicle = SaveGameManager.Current?.VehicleInstances.FirstOrDefault(v => v != null && v.id == run.Id);
                var controller = vehicle == null ? null : VehicleHelper.GetVehicleController(vehicle);
                if (controller != null) where = Pose(controller.transform.position, controller.transform.eulerAngles.y);
            }
            catch (Exception) { }
            var game = SaveGameManager.Current;
            var gameNow = run.EndedGame ?? (game != null ? NowMinutes(game) : (double?)null);
            return new JObject
            {
                ["running"] = !run.Done,
                ["simulated"] = run.Simulated,
                ["phase"] = run.Phase,
                ["id"] = run.Id,
                ["place"] = run.Place,
                ["speed"] = run.Speed,
                ["goal"] = new JObject { ["x"] = TestBridgeState.Round(run.Goal.x), ["z"] = TestBridgeState.Round(run.Goal.z) },
                ["seconds"] = Math.Round(now - run.StartedReal, 1),
                ["game_minutes"] = gameNow == null ? null : (JToken)Math.Round(gameNow.Value - run.StartedGame, 1),
                ["from"] = Pose(run.FromPosition, run.FromHeading),
                ["pull_out"] = run.PullOut,
                ["attempts"] = run.Attempts,
                ["ride_to"] = run.RideBuilding,
                ["ride"] = run.Ride,
                ["park"] = run.Park,
                ["error"] = run.Error,
                ["truck"] = where,
                ["vehicle"] = run.Final,
            };
        }

        // The bridge stops: the drive is forgotten (a ride under way in the other mod runs to its own end).
        internal static void ResetDrive()
        {
            var run = drive;
            drive = null;
            if (run?.Routine != null && TestBridgePump.Instance != null)
            {
                try { TestBridgePump.Instance.StopCoroutine(run.Routine); }
                catch (Exception) { }
            }
        }
    }
}
