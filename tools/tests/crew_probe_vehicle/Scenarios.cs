#nullable enable
using System;
using FoodTrucks;
using UnityEngine;

internal static class Scenarios
{
    private static void Check(bool ok, string why) { if (!ok) throw new Exception(why); }
    private static CarController Setup(bool cached, bool ignition = false)
    {
        var car = new CarController();
        Helpers.VehicleHelper.Car = car;
        var n = car.vehicleController;
        for (var i=0; i<4; i++)
        {
            var entry = new NWH.VehiclePhysics2.Entry();
            entry.wheelUAPI.transform.position = new Vector3(i%2 == 0 ? -1 : 1,0,i<2 ? -1.5f : 1.5f);
            entry.wheelUAPI.enabled = i%2 == 0;
            n.powertrain.wheels.Add(entry);
        }
        n.input.autoSetInput = true;
        n.input.Throttle = .12f; n.input.Brakes = .23f; n.input.Steering = -.34f; n.input.Handbrake = .45f;
        n.steering.externallyAddedAngle = 2;
        n.powertrain.transmission.ShiftInto(-1);
        n.vehicleRigidbody.constraints = RigidbodyConstraints.FreezeRotationX;
        n.vehicleRigidbody.interpolation = RigidbodyInterpolation.Interpolate;
        car.vehicleInstance.parkingState = ParkingState.Legal;
        car.vehicleInstance.parkingNeighbourhood = "test area";
        n.powertrain.engine.ignition = ignition;
        if (cached) n.powertrain.engine.ObserveRunning();
        Writes.Count = 0; CrewProbeBody.Constructions = 0;
        return car;
    }
    private static void Parked(CarController car, bool ignition = false)
    {
        var n = car.vehicleController;
        Check(!n.enabled && n.vehicleRigidbody.isKinematic, "native ownership must be returned");
        Check(n.powertrain.engine.ignition == ignition && !n.powertrain.engine.starterActive, "captured ignition restored with starter off");
    }
    private static void Restored(CarController car, bool ignition = false)
    {
        Parked(car, ignition);
        var n = car.vehicleController;
        Check(n.input.autoSetInput && n.input.Throttle == .12f && n.input.Brakes == .23f &&
            n.input.Steering == -.34f && n.input.Handbrake == .45f, "captured input restored");
        Check(n.steering.externallyAddedAngle == 2 && n.powertrain.transmission.Gear == -1, "steering and gear restored");
        Check(n.vehicleRigidbody.constraints == RigidbodyConstraints.FreezeRotationX &&
            n.vehicleRigidbody.interpolation == RigidbodyInterpolation.Interpolate, "body settings restored");
        for (var i=0; i<4; i++) Check(n.powertrain.wheels[i].wheelUAPI.enabled == (i%2 == 0), "wheel enablement restored");
        Check(n.vehicleRigidbody.velocity.sqrMagnitude == 0 && n.vehicleRigidbody.angularVelocity.sqrMagnitude == 0,
            "handback stationary");
    }
    private static void Admission()
    {
        var car = Setup(true);
        var adapter = new CrewProbeVehicle(car);
        Check(Writes.Count == 0 && car.NativeStarts == 0 && car.NativeStops == 0, "admission is read only despite stale running cache");
        Check(CrewProbeBody.Constructions == 1, "admission reached body certification");
        adapter.Restore();
        Check(Writes.Count == 0, "restore before Begin is read only");
        Parked(car);
        Check(car.vehicleController.powertrain.engine.IsRunning, "admission must not refresh cached state");
    }
    private static void Lifecycle()
    {
        var car = Setup(false);
        var engine = car.vehicleController.powertrain.engine;
        for (var generation=1; generation<=3; generation++)
        {
            Writes.Count = 0;
            var adapter = new CrewProbeVehicle(car);
            Check(Writes.Count == 0, "reacquisition does not write");
            adapter.Begin();
            Check(engine.ignition && engine.starterActive && car.vehicleController.enabled && !adapter.Body.isKinematic,
                "Begin acquires native driving");
            Check(!car.vehicleController.input.autoSetInput && car.vehicleInstance.parkingState == ParkingState.Undefined,
                "Begin acquires input and releases parking");
            engine.ObserveRunning();
            adapter.Command(2,.5f,.2f,0);
            adapter.Body.velocity = new Vector3(0,0,1);
            adapter.Body.angularVelocity = new Vector3(0,.1f,0);
            adapter.Restore();
            Restored(car);
            Check(engine.IsRunning, "Stop must leave the cache stale, reproducing native behavior");
            Check(engine.Starts == generation && engine.Stops == generation && car.NativeStarts == generation &&
                car.NativeStops == generation, "one native start/stop per generation; never restart from cached running state");
            var writes = Writes.Count;
            adapter.Restore();
            Check(Writes.Count == writes, "duplicate restore is inert");
        }
    }
    private static void Refusal(string state)
    {
        // A false cache must never hide an active starter or owner.
        var car = Setup(false);
        var n = car.vehicleController;
        switch (state)
        {
            case "ignition-controller": n.powertrain.engine.ignition = true; n.enabled = true; break;
            case "starter": n.powertrain.engine.starterActive = true; break;
            case "controller": n.enabled = true; break;
            case "player": car.controlledByPlayer = true; break;
            case "dynamic": n.vehicleRigidbody.isKinematic = false; break;
            case "linear": n.vehicleRigidbody.velocity = new Vector3(.11f,0,0); break;
            case "angular": n.vehicleRigidbody.angularVelocity = new Vector3(0,.11f,0); break;
            default: throw new Exception("unknown unsafe state");
        }
        Writes.Count = 0;
        try { _ = new CrewProbeVehicle(car); }
        catch (InvalidOperationException)
        {
            Check(Writes.Count == 0 && car.NativeStarts == 0 && car.NativeStops == 0 && CrewProbeBody.Constructions == 0,
                "refusal must happen before any native write or body work");
            return;
        }
        throw new Exception("unsafe state admitted: " + state);
    }
    private static void Partial()
    {
        var car = Setup(true);
        var adapter = new CrewProbeVehicle(car);
        car.FailStart = true;
        try { adapter.Begin(); throw new Exception("expected native start failure"); }
        catch (InvalidOperationException ex) when (ex.Message == "injected partial native start") { }
        adapter.Restore();
        Restored(car);
        Check(car.vehicleController.powertrain.engine.Starts == 1 && car.NativeStops == 1, "partial start unwound without restart");
    }
    private static void FreshDefault()
    {
        // FoodTruck's parked prefab command is ignition=true, without a flying start.
        var car = Setup(false, ignition: true);
        var engine = car.vehicleController.powertrain.engine;
        Check(!engine.IsRunning, "fresh parked truck has no running cache yet");
        for (var generation=1; generation<=3; generation++)
        {
            Writes.Count = 0;
            var adapter = new CrewProbeVehicle(car);
            Check(Writes.Count == 0 && engine.Starts == generation-1 && car.NativeStarts == generation-1,
                "parked ignition=true admission and reacquisition must be read only");
            Parked(car, ignition: true);
            adapter.Restore();
            Check(Writes.Count == 0, "restore before Begin preserves fresh parked default without writes");
            adapter.Begin();
            Check(engine.ignition && engine.starterActive && car.vehicleController.enabled && !adapter.Body.isKinematic,
                "Begin starts the admitted parked truck");
            engine.ObserveRunning();
            adapter.Restore();
            Restored(car, ignition: true);
            Check(engine.IsRunning, "handback does not refresh stale running cache");
            Check(engine.Starts == generation && car.NativeStarts == generation &&
                engine.Stops == generation && car.NativeStops == generation,
                "restoring ignition=true must not call an extra engine StartEngine");
        }
    }
    private static void Main(string[] args)
    {
        switch (args[0])
        {
            case "admission": Admission(); break;
            case "lifecycle": Lifecycle(); break;
            case "refusal": Refusal(args[1]); break;
            case "partial": Partial(); break;
            case "fresh-default": FreshDefault(); break;
            default: throw new Exception("unknown scenario");
        }
        Console.WriteLine("PASS: " + string.Join(" ",args));
    }
}
