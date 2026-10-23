#nullable enable
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using FoodTrucks.Core.CrewDriving;
using FoodTrucks.Core.Saves;
using Helpers;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace FoodTrucks
{
    // Dev-only Gate A, never a route backend. Start is reachable only through the FT test bridge.
    // One native body here; the pure ownership/control seam deliberately supports independent controllers.
    internal sealed class CrewDriveProbe : MonoBehaviour
    {
        private sealed class ReadyMarker { }
        private static readonly ConditionalWeakTable<CarController, ReadyMarker> Ready = new ConditionalWeakTable<CarController, ReadyMarker>();
        private static readonly ConditionalWeakTable<CarController, GameInstance> Probed = new ConditionalWeakTable<CarController, GameInstance>();
        private static ProbeOwnership owners = new ProbeOwnership(Guid.NewGuid());
        private bool beganNative;
        private static CrewDriveProbe? active;
        private static JObject? last;
        private static CrewProbeTraffic? lastTraffic;
        internal static bool HooksReady;
        internal static bool Active => active != null;
        internal static bool Owns(string? id) => active != null && id != null && active.token.NativeVehicleId == id;
        internal static bool Owns(VehicleController? car) => car != null && active != null && ReferenceEquals(active.car, car);

        private CarController car = null!;
        private GameInstance game = null!;
        private ProbeToken token;
        private int[] waypoints = Array.Empty<int>();
        private float requestedSpeed, startedAt;
        private CrewProbeVehicle? native;
        private CrewProbeTraffic? traffic;
        private ProbeController? controller;
        private ProbeCommand command;
        private bool ending, released, forcedHold, commandsRevoked;
        private string? wheelFault;
        private string reason = "Waiting for native initialization";
        private ProbeState phase = ProbeState.Starting;
        private int steps, contacts;
        private double totalMilliseconds, worstMilliseconds, wheelGuardMilliseconds, wheelGuardWorstMilliseconds;
        private long wheelGuardCalls;
        private float nextWheelCleanup;
        private float distance;
        private float? interruptionSpeed, interruptionAngularSpeed;
        private Vector3 precontactVelocity;
        private float precontactAngularSpeed;
        private readonly JObject[] contactLog = new JObject[32];

        internal static void StationaryReady(CarController __instance)
        {
            if (__instance.vehicleInstance?.vehicleTypeName != FoodTrucksMod.TruckTypeName) return;
            Ready.GetValue(__instance, _ => new ReadyMarker());
        }

        internal static JObject StartProbe(string id, int[] path, float maxSpeed)
        {
            if (!TestBridge.Running || !HooksReady) throw new BridgeError("Crew probe is unavailable: test bridge and all ownership hooks must be active.");
            TestBridgeCommands.RequireFtCity("crew_drive_probe");
            if (!FoodTrucksSession.CanWrite) throw new BridgeError("Crew probe requires writable Food Trucks state.");
            if (active != null || owners.Count != 0 || CrewProbeTraffic.RecordCount != 0)
                throw new BridgeError("A probe or retained physical obstruction still owns native resources; clear it first.");
            if (!ClockSuitable()) throw new BridgeError("Crew probe requires normal unpaused outdoor time.");
            if (path.Length < 2 || path.Length > 512 || !Finite(maxSpeed) || maxSpeed < .2f || maxSpeed > 2f)
                throw new BridgeError("Supply 2–512 connected native waypoint IDs and max_speed_ms from 0.2 to 2 for the straight-drive profile.");
            var game = SaveGameManager.Current;
            var instance = game.VehicleInstances.FirstOrDefault(v => v != null && v.id == id);
            var car = instance == null ? null : VehicleHelper.GetVehicleController(instance) as CarController;
            if (car == null || instance!.vehicleTypeName != FoodTrucksMod.TruckTypeName)
                throw new BridgeError("Probe needs a spawned owned food truck.");
            if (game.ActiveVehicleId == id || car.controlledByPlayer || TruckWindow.PlayerAt(id) || TestBridgeCommands.DrivingVehicle == id)
                throw new BridgeError("Truck is already controlled by a player, hatch activity or drive.");
            if (!Eligible(id)) throw new BridgeError("Use a closed disposable truck without crew, schedule, transit or pending arrival.");
            if (car.GetComponent<CrewDriveProbe>() != null) throw new BridgeError("Previous probe component has not finished removal.");
            if (!owners.TryAcquire(id, car.GetInstanceID(), out var token)) throw new BridgeError("Truck ownership unavailable.");
            try
            {
                var probe = car.gameObject.AddComponent<CrewDriveProbe>();
                probe.car = car; probe.game = game; probe.token = token;
                probe.waypoints = (int[])path.Clone(); probe.requestedSpeed = maxSpeed;
                probe.startedAt = Time.realtimeSinceStartup;
                active = probe;
                Log.Info($"Crew probe {id} generation {token.Generation}: Starting.");
                return probe.Snapshot();
            }
            catch { owners.Release(token); throw; }
        }

        private static bool Eligible(string id)
        {
            var state = StreetSales.Find(FoodTrucksSession.CurrentState, id);
            return state == null || (!state.IsOpen && state.Transit == null && state.Arrival == null && state.Crew.Count == 0 &&
                (state.Schedule == null || (state.Schedule.Stops.Count == 0 && state.Schedule.CrewShifts.Count == 0)));
        }

        private static bool ClockSuitable() => !GameManager.isCitySceneBeingUnloaded && !BuildingManager.IsInsideBuilding &&
            TestBridgeCommands.Paused() == false && TestBridgeCommands.TimeSkipRunning() == false &&
            TestBridgeCommands.ProbeClockNormal &&
            Mathf.Abs(Time.timeScale - 1f) < .01f && Time.fixedDeltaTime <= .05f;

        private bool IsOwner => !released && ReferenceEquals(active, this) && owners.Owns(token) &&
            ReferenceEquals(game, SaveGameManager.Current) && car != null && car.GetInstanceID() == token.ControllerInstance &&
            ReferenceEquals(VehicleHelper.GetVehicleController(car.vehicleInstance), car);

        // A replacement registered under the same vehicle ID does not authorize touching the replacement.
        // The captured old native body still needs a hold while this exact token owns it.
        private bool MayCleanCapturedBody => !released && ReferenceEquals(active, this) && owners.Owns(token) &&
            ReferenceEquals(game, SaveGameManager.Current) && car != null && car.GetInstanceID() == token.ControllerInstance;

        private bool RetryWheelCleanup()
        {
            if (wheelFault == null || released || ending) return false;
            if (Time.realtimeSinceStartup >= nextWheelCleanup)
            {
                nextWheelCleanup = Time.realtimeSinceStartup + .5f;
                End(wheelFault, true);
            }
            return true;
        }

        private void Update()
        {
            if (RetryWheelCleanup()) return;
            if (released || ending || commandsRevoked || active != this) return;
            try
            {
                if (!ClockSuitable()) { End("Clock, interior or unload interruption", true); return; }
                if (!IsOwner) { End("Native controller or save changed", true); return; }
                if (!Eligible(token.NativeVehicleId)) { End("Truck gained conflicting work", true); return; }
                if (car.controlledByPlayer || game.ActiveVehicleId == token.NativeVehicleId) { End("Player ownership changed", true); return; }
                if (native != null) return;
                if (!Ready.TryGetValue(car, out _) || !car.vehicleController.IsInitialized)
                {
                    if (Time.realtimeSinceStartup - startedAt > 15f) End("Native startup did not complete", true);
                    return;
                }
                native = new CrewProbeVehicle(car);
                var speed = Mathf.Min(requestedSpeed, native.TopMs);
                // A minimum across the permitted speed range; no optimistic full-lock turn plan at high speed.
                var steering = Mathf.Min(4f, native.MaxSteer);
                for (var i = 0; i <= 32; i++) steering = Mathf.Min(steering, native.LockAt(speed * i / 32));
                var geometry = new ProbeGeometry(native.Wheelbase, native.FrontAhead, native.RearBehind, native.Width, steering, speed);
                ProbePath? path = null;
                if (!CrewProbeTraffic.TryCreate(car, waypoints, native.RearZ, native.FrontAhead, native.RearBehind, native.Width,
                    out traffic, out var points, out var error, validated => {
                        var heading = (validated[1] - validated[0]); heading.y = 0; heading.Normalize();
                        for (var i = 1; i < validated.Length; i++)
                        {
                            var segment = validated[i] - validated[i - 1]; segment.y = 0;
                            if (Vector3.Dot(heading, segment.normalized) < .99995f)
                                throw new InvalidOperationException("Straight-drive profile refuses a turn.");
                        }
                        var prepared = ProbeRoadPath.Prepare(validated.Select(p => new ProbePoint(p.x, p.z)).ToArray(), geometry);
                        path = prepared.Path;
                        return prepared;
                    }, native.Contract)) throw new InvalidOperationException(error);
                controller = new ProbeController(owners, token, path!, geometry, .2, .05);
                // Validate current placement before enabling native physics.
                var rear = native.RearAxle;
                command = controller.Step(token, Observation(rear, null, null));
                if (command.RequiresStationaryHold || !command.MayApply || command.State == ProbeState.Cancelled)
                    throw new InvalidOperationException("Initial placement refused: " + command.Reason);
                beganNative = true;
                Probed.Remove(car);
                Probed.Add(car, game);
                native.Begin();
                phase = command.State;
                reason = command.Reason.ToString();
                Log.Info($"Crew probe {token.NativeVehicleId}: native engine started, {points.Length} path points, {speed:0.0} m/s cap.");
            }
            catch (Exception ex) { Log.Error("Crew probe startup/update: " + ex); End(ex.Message, true); }
        }

        private ProbeObservation Observation(Vector3 rear, float? stop, float? gap) => new ProbeObservation(
            rear.x, rear.z, Mathf.Atan2(car.transform.forward.x, car.transform.forward.z), native!.Speed, Time.fixedDeltaTime,
            nativeReady: true, hasControl: IsOwner, fuelEnabled: car.fuelModule != null && car.fuelModule.useFuel,
            fuelLitres: car.fuelModule != null ? car.fuelModule.amount : car.vehicleInstance.fuel,
            wrecked: car.GetCurrentCondition() >= .999f, stopBoundaryAlong: stop, gapAhead: gap, wheelDegrees: native.Nwh.steering.angle);

        private void FixedUpdate()
        {
            if (RetryWheelCleanup()) return;
            if (ending || released || commandsRevoked || native == null || controller == null || traffic == null) return;
            var began = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
                if (!IsOwner || !ClockSuitable()) { End("Ownership or clock interruption", true); return; }
                precontactVelocity = native.Body.velocity;
                precontactAngularSpeed = native.Body.angularVelocity.magnitude;
                traffic.Observe(distance, Mathf.Max(0, native.Speed), out var stop, out var gap, out var error);
                if (error != null) { End(error, true); return; }
                command = controller.Step(token, Observation(native.RearAxle, stop, gap));
                distance = (float)command.RearAlong; phase = command.State; reason = command.Reason.ToString();
                if (!command.MayApply) { End("Stale controller command", true); return; }
                if (command.RequiresStationaryHold || phase == ProbeState.Cancelled) { End(reason, true); return; }
                native.Command((float)command.SteerDegrees, (float)command.Throttle, (float)command.Brake, (float)command.Handbrake);
                if (phase == ProbeState.Finished) End("Endpoint reached by physical braking", false);
            }
            catch (Exception ex) { Log.Error("Crew probe control: " + ex); End(ex.Message, true); }
            finally
            {
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                steps++; totalMilliseconds += ms; worstMilliseconds = Math.Max(worstMilliseconds, ms);
            }
        }

        // NWH computes Ackermann angles after our control tick. Guard the actual native step,
        // then unwind from Update/FixedUpdate after the powertrain's wheel iteration has returned.
        internal static bool BeforeWheelStep(NWH.WheelController3D.WheelController wheel)
        {
            var probe = active;
            if (probe?.native == null || !probe.beganNative || probe.released) return true;
            var native = probe.native;
            if (!native.Contract.ContainsWheel(wheel) && wheel.ParentRigidbody != native.Body) return true;
            if (!probe.MayCleanCapturedBody)
            {
                probe.commandsRevoked = true;
                probe.wheelFault = "Native wheel guard lost captured-body authority";
                return true; // Another save/owner's native physics is outside this token's authority.
            }
            var began = System.Diagnostics.Stopwatch.GetTimestamp();
            try
            {
            if (probe.commandsRevoked || probe.wheelFault != null) return false;
            try
            {
                var error = native.Contract.ValidateWheelStep(wheel);
                if (error == null && probe.IsOwner && ClockSuitable()) return true;
                probe.wheelFault = "Native wheel guard: " + (error ?? "ownership or clock changed");
            }
            catch (Exception ex) { probe.wheelFault = "Native wheel guard: " + ex.Message; }
            probe.commandsRevoked = true;
            probe.forcedHold = true;
            probe.interruptionSpeed = native.Body.velocity.magnitude;
            probe.interruptionAngularSpeed = native.Body.angularVelocity.magnitude;
            try { native.StationaryHold(); }
            catch (Exception ex)
            {
                Log.Error("Crew probe wheel-guard hold: " + ex.Message);
                // A failed input adapter must not let the original wheel step execute.
                native.Body.isKinematic = true;
            }
            return false; // Later captured wheels in this same native update are skipped as well.
            }
            finally
            {
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - began) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                probe.wheelGuardCalls++; probe.wheelGuardMilliseconds += ms;
                probe.wheelGuardWorstMilliseconds = Math.Max(probe.wheelGuardWorstMilliseconds, ms);
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (released || native == null) return;
            if (!NWH.VehiclePhysics2.Damage.DamageHandler.IsCollisionValid(collision)) return;
            var entry = new JObject {
                ["step"] = steps, ["other"] = collision.collider != null ? collision.collider.name : "destroyed",
                ["relative_speed_ms"] = collision.relativeVelocity.magnitude, ["precontact_desired_ms"] = command.DesiredSpeedMs,
                ["precontact_velocity"] = new JArray(precontactVelocity.x, precontactVelocity.y, precontactVelocity.z),
                ["precontact_angular_speed"] = precontactAngularSpeed,
                ["throttle"] = command.Throttle, ["brake"] = command.Brake, ["handbrake"] = command.Handbrake,
                ["phase"] = phase.ToString(), ["boundary_distance_m"] = command.FrontBoundaryDistance,
                ["attribution"] = "requires inspection" };
            contactLog[contacts++ % contactLog.Length] = entry;
            Log.Info("Crew probe contact: " + entry.ToString(Newtonsoft.Json.Formatting.None));
            End("Contact; inspect attribution", true);
        }

        private bool End(string why, bool interrupt)
        {
            if (released) return true;
            if (ending) return false;
            if (!interrupt && native != null && !(native.Body.velocity.sqrMagnitude <= .01f && native.Body.angularVelocity.sqrMagnitude <= .0025f))
            {
                interrupt = true;
                why = "Endpoint has residual lateral or angular motion";
            }
            ending = true; reason = why;
            commandsRevoked = true;
            controller?.Cancel(token);
            var mayRestore = MayCleanCapturedBody;
            var failed = false;
            try
            {
                // Stop accepting commands before touching native state. No later physics frame is required.
                if (beganNative && native != null && car != null && mayRestore)
                {
                    forcedHold |= interrupt && beganNative;
                    if (interrupt && beganNative && interruptionSpeed == null)
                    {
                        interruptionSpeed = Mathf.Sqrt(native.Body.velocity.sqrMagnitude);
                        interruptionAngularSpeed = Mathf.Sqrt(native.Body.angularVelocity.sqrMagnitude);
                    }
                    try { native.StationaryHold(); if (IsOwner) native.Synchronize(); }
                    catch (Exception ex) { failed = true; Log.Error("Crew probe hold/sync: " + ex); }
                }
                try { traffic?.Dispose(); }
                catch (Exception ex) { failed = true; Log.Error("Crew probe traffic cleanup: " + ex); }
                if (beganNative && native != null && car != null && mayRestore)
                {
                    try { native.Restore(); }
                    catch (Exception ex) { failed = true; Log.Error("Crew probe restore: " + ex); }
                }
                if (failed)
                {
                    phase = ProbeState.Holding; reason = "Cleanup failed: " + why;
                    // Retain ownership and refuse a new probe until a retry safely completes.
                    try { if (beganNative && mayRestore) native?.StationaryHold(); } catch (Exception ex) { Log.Error(ex); }
                    last = Snapshot();
                    return false;
                }
                phase = interrupt ? ProbeState.Cancelled : ProbeState.Finished;
                owners.Release(token);
                released = true;
                if (ReferenceEquals(active, this)) active = null;
                last = Snapshot();
                lastTraffic = traffic;
                Log.Info($"Crew probe {token.NativeVehicleId} generation {token.Generation}: {phase}, {why}; forced hold={forcedHold}, contacts={contacts}.");
                Destroy(this); // Only our component. The owned physical truck remains where it stopped.
                return true;
            }
            finally { ending = false; }
        }

        internal static bool StopAll(string reason) => active == null || active.End(reason, true);
        // An active probe without an observation must not expose an older probe's blocker.
        internal static CrewProbeTraffic? ObstacleTraffic => active != null ? active.traffic : lastTraffic;
        internal static bool StopVehicle(VehicleController? vehicle, string reason) => !Owns(vehicle) || active!.End(reason, true);
        internal static JObject Status()
        {
            if (active != null) return active.Snapshot();
            if (last == null) return new JObject { ["phase"] = "Idle", ["owners"] = owners.Count, ["traffic_records"] = CrewProbeTraffic.RecordCount };
            last["junction_registrations"] = lastTraffic?.Registrations ?? 0;
            last["collider_overrides"] = lastTraffic?.ColliderOverrides ?? 0;
            last["retained_body_record"] = lastTraffic?.Retained ?? false;
            last["traffic_failure"] = lastTraffic?.Failure;
            last["traffic_records"] = CrewProbeTraffic.RecordCount;
            return last;
        }

        // DamageHandler and this component receive separate Unity collision callbacks in unspecified order.
        // The damage postfix flushes the actual result even when our earlier callback already ended the probe.
        internal static void FlushObservedDamage(CarController? car)
        {
            try
            {
                if (car == null || car.vehicleController == null || !car.vehicleController.IsInitialized ||
                    !Probed.TryGetValue(car, out var savedGame) || !ReferenceEquals(savedGame, SaveGameManager.Current) ||
                    !ReferenceEquals(VehicleHelper.GetVehicleController(car.vehicleInstance), car)) return;
                car.vehicleInstance.damage = car.GetCurrentCondition();
                if (last != null && (long?)last["controller_instance"] == car.GetInstanceID()) last["damage_now"] = car.vehicleInstance.damage;
            }
            catch (Exception ex) { Log.Error("Crew probe observed damage sync: " + ex.Message); }
        }
        internal static JObject StopProbe(Guid session, long generation)
        {
            if (active == null) return Status();
            if (active.token.Session != session || active.token.Generation != generation)
                throw new BridgeError("Stale session/generation; inspect crew_drive_probe status.");
            active.End("Explicit stop", true);
            return Status();
        }

        internal static void ResetSession()
        {
            if (!StopAll("Save detached")) Log.Error("Crew probe failed cleanup during unload; native scene removal must finish disposal.");
            if (active == null) { owners = new ProbeOwnership(Guid.NewGuid()); last = null; lastTraffic = null; }
        }

        private JObject Snapshot()
        {
            var logs = new JArray();
            for (var i = Math.Max(0, contacts - contactLog.Length); i < contacts; i++) logs.Add(contactLog[i % contactLog.Length]);
            return new JObject {
                ["wheel_guard_calls"] = wheelGuardCalls,
                ["wheel_guard_mean_ms"] = wheelGuardCalls == 0 ? 0 : wheelGuardMilliseconds / wheelGuardCalls,
                ["wheel_guard_worst_ms"] = wheelGuardWorstMilliseconds,
                ["body_full_validations"] = native?.Contract.FullValidations,
                ["body_mesh_vertex_reads"] = native?.Contract.MeshVertexReads,
                ["profile"] = "straight-2ms-wheel5deg", ["wheel_guard_failure"] = wheelFault,
                ["id"] = token.NativeVehicleId, ["session"] = token.Session.ToString(), ["generation"] = token.Generation,
                ["controller_instance"] = token.ControllerInstance, ["phase"] = phase.ToString(), ["reason"] = reason,
                ["owner"] = released ? "native" : "probe", ["owners"] = owners.Count,
                ["rear_along_m"] = distance, ["speed_ms"] = native != null && native.Body != null ? native.Speed : 0,
                ["stop_boundary_m"] = command.StopBoundaryAlong, ["physical_front_clearance_m"] = command.FrontBoundaryDistance,
                ["boundary_violation_m"] = command.BoundaryViolationMetres,
                ["fuel_before_l"] = native?.FuelBefore, ["fuel_now_l"] = car != null ? car.fuelModule?.amount : null,
                ["damage_before"] = native?.DamageBefore, ["damage_now"] = car != null && native != null ? car.GetCurrentCondition() : (float?)null,
                ["forced_interruption_hold"] = forcedHold, ["steps"] = steps,
                ["interruption_speed_ms"] = interruptionSpeed, ["interruption_angular_speed_rads"] = interruptionAngularSpeed,
                ["geometry"] = native == null ? null : new JObject {
                    ["rear_axle_local_z"] = native.RearZ, ["wheelbase_m"] = native.Wheelbase,
                    ["front_ahead_m"] = native.FrontAhead, ["rear_behind_m"] = native.RearBehind,
                    ["width_m"] = native.Width, ["max_steer_degrees"] = native.MaxSteer },
                ["control_mean_ms"] = steps > 0 ? totalMilliseconds / steps : 0, ["control_worst_ms"] = worstMilliseconds,
                ["junction_registrations"] = traffic?.Registrations ?? 0, ["collider_overrides"] = traffic?.ColliderOverrides ?? 0,
                ["retained_body_record"] = traffic?.Retained ?? false, ["traffic_failure"] = traffic?.Failure,
                ["native_traffic_id"] = traffic?.NativeTrafficId,
                ["observation_ms"] = traffic?.ObservationMilliseconds, ["observed_blockers"] = traffic?.Observed,
                ["blocker_id"] = traffic?.BlockerId, ["blocker_layer"] = traffic?.BlockerLayer,
                ["blocker_query"] = traffic?.BlockerQuery, ["blocker_gap_m"] = traffic?.BlockerGap,
                ["blocker_position"] = traffic?.BlockerId != null ? new JObject { ["x"] = traffic.BlockerPosition.x, ["y"] = traffic.BlockerPosition.y, ["z"] = traffic.BlockerPosition.z } : null,
                ["signal_stop_changes"] = traffic?.SignalStopChanges, ["signal_change_speed_ms"] = traffic?.SignalChangeSpeed,
                ["signal_change_front_clearance_m"] = traffic?.SignalChangeClearance,
                ["probe_save_subscriptions"] = 0, ["contacts"] = contacts, ["contact_log"] = logs,
                ["validation"] = "development probe; native traffic, braking and performance remain unverified" };
        }

        private void OnDisable() { if (!released && active == this) End("Probe disabled", true); }
        private void OnDestroy() { if (!released && active == this) End("Probe destroyed", true); }
        private static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
