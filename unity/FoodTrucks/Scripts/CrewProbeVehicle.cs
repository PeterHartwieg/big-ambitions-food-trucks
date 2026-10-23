#nullable enable
using System;
using System.Collections.Generic;
using HarmonyLib;
using Helpers;
using UnityEngine;
using NwhController = NWH.VehiclePhysics2.VehicleController;

namespace FoodTrucks
{
    // Native ownership for the disposable Gate A probe. No player entry, cloned vehicles or fuel writes.
    // Geometry measurement adapts Driven Rides NwhCar.MeasureLive at 90981d0061418a4d4742792d90c62c6a686f2a80.
    internal sealed class CrewProbeVehicle
    {
        private static readonly Action<CarController>? StartNative = Bind("StartEngine");
        private static readonly Action<CarController>? StopNative = Bind("StopEngine");
        internal readonly CarController Car;
        internal readonly NwhController Nwh;
        internal readonly Rigidbody Body;
        internal readonly CrewProbeBody Contract;
        internal readonly VehicleInstance Instance;
        private readonly GameInstance session;
        internal readonly float RearZ, Wheelbase, FrontAhead, RearBehind, Width, MaxSteer, TopMs;
        internal readonly Vector3 Centre, Half;
        internal readonly float FuelBefore, DamageBefore;
        private readonly bool autoInput, enabled, kinematic, engineIgnition, starter;
        private readonly float throttle, brake, steering, handbrake, addedAngle;
        private readonly int gear;
        private readonly RigidbodyConstraints constraints;
        private readonly RigidbodyInterpolation interpolation;
        private readonly List<(Behaviour Wheel, bool Enabled)> wheels = new List<(Behaviour, bool)>();
        private readonly Vector3 origin;
        private readonly ParkingState parking;
        private readonly string neighbourhood;
        private bool changed;

        private static Action<CarController>? Bind(string name)
        {
            try
            {
                var method = AccessTools.DeclaredMethod(typeof(CarController), name, Type.EmptyTypes);
                return method == null ? null : (Action<CarController>)Delegate.CreateDelegate(typeof(Action<CarController>), method);
            }
            catch (Exception) { return null; }
        }

        internal CrewProbeVehicle(CarController car)
        {
            if (StartNative == null || StopNative == null) throw new InvalidOperationException("Native engine bindings unavailable.");
            Car = car;
            Instance = car.vehicleInstance ?? throw new InvalidOperationException("Truck has no native instance.");
            session = SaveGameManager.Current;
            Nwh = car.vehicleController;
            if (Nwh == null || !Nwh.IsInitialized || Nwh.input == null) throw new InvalidOperationException("NWH has not initialized.");
            Body = Nwh.vehicleRigidbody;
            if (Body == null || Body.velocity.sqrMagnitude > .01f || Body.angularVelocity.sqrMagnitude > .01f)
                throw new InvalidOperationException("Truck must be stationary.");
            // An active controller may belong to another mod. A probe never steals it.
            // IsRunning can stay stale after native StopEngine, and a fresh parked prefab can
            // retain ignition=true. Neither flag proves activity with the controller disabled;
            // require native stationary handling and an inactive starter instead.
            if (car.controlledByPlayer || Nwh.enabled || !Body.isKinematic ||
                Nwh.powertrain.engine.starterActive)
                throw new InvalidOperationException("Truck must have completed native stationary handling with controller and starter off.");
            var minZ = float.MaxValue;
            var maxZ = float.MinValue;
            foreach (var wheel in Nwh.powertrain.wheels)
            {
                if (wheel?.wheelUAPI == null) continue;
                var local = car.transform.InverseTransformPoint(wheel.wheelUAPI.transform.position);
                minZ = Mathf.Min(minZ, local.z); maxZ = Mathf.Max(maxZ, local.z);
                wheels.Add((wheel.wheelUAPI, wheel.wheelUAPI.enabled));
            }
            if (wheels.Count < 4 || maxZ - minZ < 1f) throw new InvalidOperationException("Truck axle geometry unavailable.");
            RearZ = minZ; Wheelbase = maxZ - minZ;
            Contract = new CrewProbeBody(car);
            var min = Contract.Min; var max = Contract.Max;
            Centre = (min + max) * .5f; Half = (max - min) * .5f;
            var tilt = Contract.PlanarTiltPadding(RearZ);
            FrontAhead = max.z - RearZ + tilt; RearBehind = RearZ - min.z + tilt;
            Width = (Mathf.Max(Mathf.Abs(min.x), Mathf.Abs(max.x)) + tilt) * 2f;
            MaxSteer = Nwh.steering.maximumSteerAngle;
            TopMs = car.vehicleType.maxSpeed / 3.6f;
            autoInput = Nwh.input.autoSetInput; enabled = Nwh.enabled; kinematic = Body.isKinematic;
            engineIgnition = Nwh.powertrain.engine.ignition; starter = Nwh.powertrain.engine.starterActive;
            throttle = Nwh.input.Throttle; brake = Nwh.input.Brakes; steering = Nwh.input.Steering; handbrake = Nwh.input.Handbrake;
            addedAngle = Nwh.steering.externallyAddedAngle; gear = Nwh.powertrain.transmission.Gear;
            constraints = Body.constraints; origin = car.transform.position;
            interpolation = Body.interpolation;
            parking = Instance.parkingState; neighbourhood = Instance.parkingNeighbourhood;
            FuelBefore = car.fuelModule != null ? car.fuelModule.amount : Instance.fuel;
            DamageBefore = car.GetCurrentCondition();
        }

        internal Vector3 RearAxle => Car.transform.TransformPoint(new Vector3(0, 0, RearZ));
        internal float Speed => Vector3.Dot(Body.velocity, Car.transform.forward);
        internal float LockAt(float speed) => MaxSteer * (Nwh.steering.speedSensitiveSteeringCurve == null ? 1f :
            Mathf.Clamp01(Nwh.steering.speedSensitiveSteeringCurve.Evaluate(Mathf.Abs(speed) / 50f)));

        internal void Begin()
        {
            changed = true; // Even a partially failing native StartEngine must be unwound.
            // Observers read the physical transform in FixedUpdate. Native interpolation otherwise exposes
            // last rendered poses across several physics steps and can hide/misreport boundary crossings.
            Body.interpolation = RigidbodyInterpolation.None;
            Nwh.input.autoSetInput = false;
            HoldInput();
            Instance.parkingState = ParkingState.Undefined;
            StartNative!(Car);
            Nwh.powertrain.transmission.ShiftInto(1);
        }

        internal void Command(float steerDegrees, float throttleValue, float brakeValue, float handbrakeValue)
        {
            var limit = LockAt(Speed);
            if (limit < 1f) throw new InvalidOperationException("Native steering limit unavailable.");
            Nwh.input.autoSetInput = false;
            Nwh.steering.externallyAddedAngle = 0;
            Nwh.input.Steering = Mathf.Clamp(steerDegrees, -4f, 4f) / limit;
            Nwh.input.Throttle = throttleValue;
            Nwh.input.Brakes = Nwh.powertrain.transmission.Gear < 0 ? 0 : brakeValue;
            Nwh.input.Handbrake = Nwh.powertrain.transmission.Gear < 0 ? 1 : handbrakeValue;
            if (throttleValue > 0 && Nwh.powertrain.transmission.Gear <= 0) Nwh.powertrain.transmission.ShiftInto(1);
        }

        internal void HoldInput()
        {
            Nwh.input.Throttle = 0; Nwh.input.Brakes = 0; Nwh.input.Steering = 0; Nwh.input.Handbrake = 1;
            Nwh.steering.externallyAddedAngle = 0;
        }

        internal void StationaryHold()
        {
            changed = true;
            HoldInput();
            if (!Body.isKinematic) { Body.velocity = Vector3.zero; Body.angularVelocity = Vector3.zero; }
            Body.isKinematic = true;
        }

        internal void Synchronize()
        {
            if (!ReferenceEquals(session, SaveGameManager.Current) ||
                !ReferenceEquals(VehicleHelper.GetVehicleController(Instance), Car)) return;
            Car.SavePosition(); // Native pose and actual tank. Does not flush DamageHandler.
            Instance.damage = Car.GetCurrentCondition();
            if ((Car.transform.position - origin).sqrMagnitude < .0001f)
            {
                Instance.parkingState = parking; Instance.parkingNeighbourhood = neighbourhood;
            }
            else
            {
                // Native parking geometry, without CarController.UpdateParkingZone's player HUD writes.
                var legal = !BuildingManager.IsInsideBuilding;
                var area = "";
                foreach (var wheel in Nwh.powertrain.wheels)
                {
                    if (wheel?.wheelUAPI == null) { legal = false; break; }
                    var t = wheel.wheelUAPI.transform;
                    var hits = Physics.OverlapBox(t.position - t.up * .1f,
                        new Vector3(.1f, wheel.wheelUAPI.Radius * 2f, .1f), t.rotation, LayerHelper.parkingAreaLayerMask);
                    var lane = hits.Length > 0 ? hits[0].GetComponentInParent<ParkingLaneGenerator>() : null;
                    if (lane == null || lane.isHandicapParking) { legal = false; break; }
                    area = lane.neighbourhood;
                }
                Instance.parkingState = legal ? ParkingState.Legal : ParkingState.Illegal;
                Instance.parkingNeighbourhood = legal ? area : "";
            }
            SaveGameManager.MarkChange();
        }

        internal void Restore()
        {
            if (!changed) return;
            // Called only by the exact owner, before native player entry can establish its own state.
            StationaryHold();
            StopNative!(Car);
            // Restore the captured command, never restart from the engine's stale running cache.
            Nwh.powertrain.engine.ignition = engineIgnition;
            Nwh.powertrain.engine.starterActive = starter;
            Nwh.enabled = enabled;
            foreach (var wheel in wheels) if (wheel.Wheel != null) wheel.Wheel.enabled = wheel.Enabled;
            Body.constraints = constraints; Body.isKinematic = kinematic;
            Body.interpolation = interpolation;
            Nwh.powertrain.transmission.ShiftInto(gear);
            Nwh.steering.externallyAddedAngle = addedAngle;
            Nwh.input.Throttle = throttle; Nwh.input.Brakes = brake; Nwh.input.Steering = steering; Nwh.input.Handbrake = handbrake;
            Nwh.input.autoSetInput = autoInput;
            changed = false;
        }
    }
}
