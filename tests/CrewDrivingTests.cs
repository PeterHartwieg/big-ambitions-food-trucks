using System;
using FoodTrucks.Core.CrewDriving;
using Xunit;

namespace FoodTrucks.Core.Tests;

public class CrewDrivingTests
{
    internal static ProbeGeometry Truck(double speed = 8) => new ProbeGeometry(3, 4, 1.2, 2.5, 30, speed);
    internal static ProbePath Straight() => new ProbePath(new[] { new ProbePoint(0, 0), new ProbePoint(0, 100) });
    internal static ProbePath Arc(double radius)
    {
        // Exact circle chords <= .5 m, plus tangent straights sampled at the same spacing.
        var steps = (int)Math.Ceiling(radius * Math.PI / 2 / .5);
        var pts = new ProbePoint[41 + steps + 40];
        for (var i = 0; i <= 40; i++) pts[i] = new ProbePoint(0, i * .5);
        for (var i = 1; i <= steps; i++)
        {
            var a = i * Math.PI / 2 / steps;
            pts[40 + i] = new ProbePoint(radius * (1 - Math.Cos(a)), 20 + radius * Math.Sin(a));
        }
        for (var i = 1; i <= 40; i++) pts[40 + steps + i] = new ProbePoint(radius + i * .5, 20 + radius);
        return new ProbePath(pts);
    }

    // Physical pedal-driven bicycle. Deliberately never sets speed to the controller's desired speed.
    // Smooth/rate-limit the wheel, integrate braking/acceleration, and integrate the ACTUAL body pose.
    internal sealed class Rig
    {
        internal readonly ProbeOwnership Owners;
        internal readonly ProbeToken Token;
        internal readonly ProbeController Controller;
        internal readonly ProbeGeometry Geometry;
        internal readonly ProbePath Path;
        private readonly ProbeCommand[] delayed;
        private int delayIndex;
        internal double X, Z, Heading, Speed, Wheel, MaxSpeed;
        internal Rig(ProbeOwnership? owners = null, string id = "truck", ProbePath? path = null, double top = 8, int latencySteps = 0,
            ProbeGeometry? geometry = null, double corridor = ProbeController.CorridorTolerance, double headingTolerance = ProbeController.HeadingTolerance)
        {
            Owners = owners ?? new ProbeOwnership(Guid.NewGuid());
            Assert.True(Owners.TryAcquire(id, 17, out Token));
            Geometry = geometry ?? Truck(top); Path = path ?? Straight();
            Controller = new ProbeController(Owners, Token, Path, Geometry, corridor, headingTolerance);
            delayed = new ProbeCommand[latencySteps];
        }
        internal ProbeCommand Tick(double dt = .02, double? signal = null, double? obstacleZ = null)
        {
            var gap = obstacleZ.HasValue ? obstacleZ.Value - (Z + Geometry.Front * Math.Cos(Heading) + Geometry.Width / 2 * Math.Abs(Math.Sin(Heading))) : (double?)null;
            var command = Controller.Step(Token, new ProbeObservation(X, Z, Heading, Speed, dt,
                stopBoundaryAlong: signal, gapAhead: gap, wheelDegrees: Wheel));
            AssertFinite(command);
            var applied = command;
            if (delayed.Length != 0)
            {
                applied = delayed[delayIndex]; delayed[delayIndex] = command;
                delayIndex = (delayIndex + 1) % delayed.Length;
            }
            var change = Math.Clamp((applied.SteerDegrees - Wheel) * dt / .1, -90 * dt, 90 * dt);
            Wheel += change;
            var acceleration = 2 * applied.Throttle - 4 * applied.Brake - 4 * applied.Handbrake;
            var nextSpeed = Math.Max(0, Speed + acceleration * dt);
            var distance = (nextSpeed + Speed) * .5 * dt;
            var turn = distance * Math.Tan(Wheel * Math.PI / 180) / Geometry.Wheelbase;
            X += distance * Math.Sin(Heading + turn / 2);
            Z += distance * Math.Cos(Heading + turn / 2);
            Heading += turn; Speed = nextSpeed;
            MaxSpeed = Math.Max(MaxSpeed, Speed);
            return command;
        }
    }

    internal static void AssertFinite(ProbeCommand c)
    {
        Assert.True(double.IsFinite(c.SteerDegrees));
        Assert.True(double.IsFinite(c.DesiredSpeedMs));
        Assert.True(double.IsFinite(c.RearAlong));
        Assert.True(double.IsFinite(c.StopBoundaryAlong));
        Assert.True(double.IsFinite(c.FrontBoundaryDistance));
        Assert.InRange(c.Throttle, 0, 1); Assert.InRange(c.Brake, 0, 1); Assert.InRange(c.Handbrake, 0, 1);
        Assert.True(c.Throttle == 0 || c.Brake == 0);
    }

    [Fact]
    public void NarrowStraightProfileRecoversTwentyCentimetresAndStops()
    {
        var rig = new Rig(geometry: new ProbeGeometry(3.6, 4.45, 1.35, 2.52, 4, 2), corridor: .2, headingTolerance: .05) { X = .2 };
        for (var i = 0; i < 15000 && rig.Controller.State != ProbeState.Finished; i++)
        {
            var c = rig.Tick();
            Assert.True(c.State != ProbeState.Cancelled, c.Reason.ToString());
            Assert.InRange(Math.Abs(rig.X), 0, .200001);
            Assert.InRange(Math.Abs(rig.Heading), 0, .050001);
        }
        Assert.Equal(ProbeState.Finished, rig.Controller.State);
        Assert.InRange(Math.Abs(rig.X), 0, .01);
        // Native NWH Ackermann formula for the captured truck's track and wheelbase.
        var angle = 4 * Math.PI / 180;
        var actual = Math.Atan(4 * 1.74 * Math.Sin(angle) / (2 * 3.6 * Math.Cos(angle) - 1.74 * Math.Sin(angle))) * 180 / Math.PI;
        Assert.InRange(actual, 0, 5);
    }

    [Theory]
    [InlineData(.201, 0, ProbeReason.OutOfCorridor)]
    [InlineData(0, .051, ProbeReason.HeadingMismatch)]
    public void NarrowProfileRejectsPoseOutsideItsSweepAllowance(double x, double heading, ProbeReason reason)
    {
        var rig = new Rig(geometry: new ProbeGeometry(3.6, 4.45, 1.35, 2.52, 4, 2), corridor: .2, headingTolerance: .05) { X = x, Heading = heading };
        Assert.Equal(reason, rig.Tick().Reason);
    }

    [Theory]
    [InlineData(.02, 3)]
    [InlineData(.05, 8)]
    [InlineData(.1, 15)]
    public void StraightPedalPhysicsStopsBeforeFrontBoundary(double dt, double speed)
    {
        var rig = new Rig(top: speed);
        ProbeCommand command = default;
        for (var i = 0; i < 20000 && rig.Controller.State != ProbeState.Finished; i++)
        {
            command = rig.Tick(dt);
            Assert.NotEqual(ProbeState.Cancelled, command.State);
            Assert.True(rig.Z + rig.Geometry.Front <= 100);
            Assert.Equal(0, command.BoundaryViolationMetres);
        }
        Assert.Equal(ProbeState.Finished, command.State);
        Assert.Equal(ProbeReason.Endpoint, command.Reason);
        Assert.InRange(100 - rig.Z - rig.Geometry.Front, 1, 1.6);
        Assert.Equal(0, command.Throttle);
        Assert.Equal(1, command.Handbrake);
        Assert.True(rig.MaxSpeed > speed * .65);
        Assert.Equal(1, rig.Owners.Count);
        for (var i = 0; i < 20; i++) Assert.Equal(ProbeState.Finished, rig.Tick(dt).State);
    }

    [Theory]
    [InlineData(.02, 10)]
    [InlineData(.1, 3)]
    public void StraightStoppingRetainsMarginWithModelledNativeCommandLatency(double dt, int latencySteps)
    {
        var rig = new Rig(top: 15, latencySteps: latencySteps);
        for (var i = 0; i < 20000 && rig.Controller.State != ProbeState.Finished; i++)
        {
            var c = rig.Tick(dt);
            Assert.True(c.State != ProbeState.Cancelled, c.Reason.ToString());
            Assert.True(rig.Z + rig.Geometry.Front <= 100);
        }
        Assert.Equal(ProbeState.Finished, rig.Controller.State);
        Assert.InRange(100 - rig.Z - rig.Geometry.Front, .8, 1.7);
        for (var i = 0; i < 100; i++) Assert.Equal(ProbeState.Finished, rig.Tick(dt).State);
    }

    [Fact]
    public void TruckTurnsOnSmoothCurveWithLowLateralAccelerationAndNoCornerSpeedFloor()
    {
        var rig = new Rig(path: Arc(20));
        var maxSteer = 0.0;
        ProbeCommand c = default;
        for (var i = 0; i < 20000 && rig.Controller.State != ProbeState.Finished; i++)
        {
            c = rig.Tick();
            maxSteer = Math.Max(maxSteer, c.SteerDegrees);
            Assert.True(c.State != ProbeState.Cancelled, c.Reason.ToString());
            Assert.InRange(Math.Abs(c.SteerDegrees), 0, rig.Geometry.MaxSteerDegrees);
            Assert.InRange(rig.Speed * rig.Speed * Math.Abs(Math.Tan(rig.Wheel * Math.PI / 180)) / 3, 0, .9);
        }
        Assert.Equal(ProbeState.Finished, c.State);
        Assert.True(maxSteer > 5);
        Assert.InRange(rig.MaxSpeed, 2, 4.01);
        Assert.InRange(rig.Heading, 1.5, 1.65);
        // At radius 10, truck corner cap is below Driven Rides' copied 12 km/h minimum.
        var tight = new Rig(path: Arc(10));
        for (var i = 0; i < 500; i++) tight.Tick();
        Assert.True(tight.MaxSpeed < 3);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void StopsForSignalOrObstacleAndResumesOnlyAfterRelease(bool signal)
    {
        var rig = new Rig();
        ProbeCommand c = default;
        for (var i = 0; i < 10000; i++)
        {
            c = rig.Tick(signal: signal ? 35 : null, obstacleZ: signal ? null : 35);
            Assert.NotEqual(ProbeState.Cancelled, c.State);
            Assert.True(rig.Z + rig.Geometry.Front <= 35);
            if (c.State == ProbeState.Holding && rig.Speed == 0) break;
        }
        Assert.Equal(ProbeState.Holding, c.State);
        Assert.Equal(signal ? ProbeReason.Signal : ProbeReason.Obstacle, c.Reason);
        var stopped = rig.Z;
        for (var i = 0; i < 100; i++)
        {
            c = rig.Tick(signal: signal ? 35 : null, obstacleZ: signal ? null : 35);
            Assert.Equal(0, c.Throttle); Assert.Equal(0, c.Brake); Assert.Equal(1, c.Handbrake);
        }
        Assert.Equal(stopped, rig.Z);
        Assert.True(rig.Tick().Throttle > 0);
        for (var i = 0; i < 15000 && rig.Controller.State != ProbeState.Finished; i++) rig.Tick();
        Assert.Equal(ProbeState.Finished, rig.Controller.State);
    }

    [Fact]
    public void ActualFrontOvershootIsReportedBeforeClampedProjectionOrJumpCanHideIt()
    {
        var rig = new Rig(); rig.Tick();
        var c = rig.Controller.Step(rig.Token, new ProbeObservation(0, 98, 0, 2, .02));
        Assert.Equal(ProbeReason.BoundaryCrossed, c.Reason);
        Assert.Equal(ProbeState.Cancelled, c.State);
        Assert.Equal(2, c.BoundaryViolationMetres, 8);
        Assert.Equal(0, c.RearAlong); // rejected jump cannot advance logical progress
        Assert.Equal(0, c.Throttle); Assert.Equal(1, c.Brake);
        Assert.True(c.RequiresStationaryHold);
    }

    [Theory]
    [InlineData(102, 6)]
    [InlineData(150, 54)]
    public void EvenOvershootFarBeyondPathDoesNotCollapseToItsClampedEndpoint(double rearZ, double overshoot)
    {
        var rig = new Rig(); rig.Tick();
        var c = rig.Controller.Step(rig.Token, new ProbeObservation(0, rearZ, 0, 3, .02));
        Assert.Equal(ProbeReason.BoundaryCrossed, c.Reason);
        Assert.Equal(overshoot, c.BoundaryViolationMetres, 8);
        Assert.Equal(0, c.RearAlong);
    }

    [Fact]
    public void OffCorridorJumpCannotHideThePhysicalBoundaryPlaneCrossingEither()
    {
        var rig = new Rig();
        var c = rig.Controller.Step(rig.Token, new ProbeObservation(30, 150, 0, 0, .02));
        Assert.Equal(ProbeReason.BoundaryCrossed, c.Reason);
        Assert.Equal(54, c.BoundaryViolationMetres, 8);
        Assert.Equal(0, c.RearAlong);
    }

    [Fact]
    public void StationarySignalHoldToleratesBoundedSuspensionRockWithoutProgressDrift()
    {
        var rig = new Rig();
        for (var i = 0; i < 1000; i++)
        {
            var c = rig.Controller.Step(rig.Token, new ProbeObservation(0, i % 2 == 0 ? .05 : -.05,
                0, 0, .02, stopBoundaryAlong: 5));
            Assert.Equal(ProbeState.Holding, c.State);
            Assert.InRange(c.RearAlong, 0, .05);
        }
        var reverse = rig.Controller.Step(rig.Token, new ProbeObservation(0, 0, 0, -.03, .02, stopBoundaryAlong: 5));
        Assert.Equal(ProbeReason.WrongDirection, reverse.Reason);
    }

    [Fact]
    public void MovingBackwardsWithPositiveReportedSpeedStillFailsDirectionCheck()
    {
        var rig = new Rig();
        for (var i = 0; i < 500; i++) rig.Tick();
        rig.Controller.Step(rig.Token, new ProbeObservation(rig.X, rig.Z, rig.Heading, rig.Speed, .02));
        var c = rig.Controller.Step(rig.Token, new ProbeObservation(rig.X, rig.Z - .04, rig.Heading, rig.Speed, .02));
        Assert.Equal(ProbeReason.WrongDirection, c.Reason);
        Assert.Equal(0, c.Throttle);
    }

    [Fact]
    public void SuddenCloseObstacleCommandsFullBrakeWithoutClaimingItCanStopInTime()
    {
        var rig = new Rig();
        for (var i = 0; i < 500; i++) rig.Tick();
        var c = rig.Controller.Step(rig.Token, new ProbeObservation(rig.X, rig.Z, rig.Heading, rig.Speed, .02, gapAhead: 2));
        Assert.Equal(0, c.Throttle); Assert.Equal(1, c.Brake);
        Assert.NotEqual(ProbeState.Finished, c.State);
    }

    [Fact]
    public void FrontCornerRatherThanJustBumperCentreCanViolateBoundary()
    {
        var rig = new Rig();
        var c = rig.Controller.Step(rig.Token, new ProbeObservation(0, 96, .3, 0, .02));
        Assert.Equal(ProbeReason.BoundaryCrossed, c.Reason);
        Assert.True(c.BoundaryViolationMetres > .18);
        Assert.Equal(0, c.RearAlong);
    }

    [Theory]
    [InlineData(0, 0, 0, -1, .02, ProbeReason.WrongDirection)]
    [InlineData(2, 0, 0, 0, .02, ProbeReason.PositionJump)]
    [InlineData(0, 2, 0, 0, .02, ProbeReason.PositionJump)]
    [InlineData(0, 0, 3.14, 0, .02, ProbeReason.PositionJump)]
    [InlineData(0, 0, 0, 0, 0, ProbeReason.InvalidTimestep)]
    [InlineData(0, 0, 0, 0, -.02, ProbeReason.InvalidTimestep)]
    [InlineData(0, 0, 0, 0, .2, ProbeReason.InvalidTimestep)]
    [InlineData(0, 0, 0, 0, double.NaN, ProbeReason.InvalidTimestep)]
    [InlineData(double.NaN, 0, 0, 0, .02, ProbeReason.InvalidObservation)]
    [InlineData(0, double.PositiveInfinity, 0, 0, .02, ProbeReason.InvalidObservation)]
    public void InvalidOrDiscontinuousObservationsCancelWithFiniteStop(double x, double z, double h, double speed, double dt, ProbeReason expected)
    {
        var rig = new Rig(); rig.Tick();
        var c = rig.Controller.Step(rig.Token, new ProbeObservation(x, z, h, speed, dt));
        Assert.Equal(expected, c.Reason); Assert.Equal(ProbeState.Cancelled, c.State);
        Assert.Equal(0, c.Throttle); Assert.True(c.RequiresStationaryHold);
        AssertFinite(c);
        Assert.Equal(0, rig.Tick().Throttle); // latched cancellation
    }

    [Fact]
    public void InitialOffPathAndHeadingMismatchCannotBecomeSilentJoin()
    {
        var offPath = new Rig();
        Assert.Equal(ProbeReason.OutOfCorridor, offPath.Controller.Step(offPath.Token, new ProbeObservation(1, 0, 0, 0, .02)).Reason);
        var reversed = new Rig();
        Assert.Equal(ProbeReason.HeadingMismatch, reversed.Controller.Step(reversed.Token, new ProbeObservation(0, 0, Math.PI, 0, .02)).Reason);
        var mid = new Rig();
        Assert.Equal(ProbeReason.OutOfCorridor, mid.Controller.Step(mid.Token, new ProbeObservation(0, 50, 0, 0, .02)).Reason);
    }

    [Fact]
    public void SmallContinuousSidewaysDriftOrYawCannotEvadeCorridorAndHeadingChecks()
    {
        var side = new Rig();
        ProbeCommand c = default;
        for (var i = 0; i <= 61; i++)
            c = side.Controller.Step(side.Token, new ProbeObservation(i * .01, 0, 0, 0, .02));
        Assert.Equal(ProbeReason.OutOfCorridor, c.Reason);
        Assert.Equal(0, c.RearAlong);
        var yaw = new Rig();
        for (var i = 0; i <= 36; i++)
            c = yaw.Controller.Step(yaw.Token, new ProbeObservation(0, 0, i * .01, 0, .02));
        Assert.Equal(ProbeReason.HeadingMismatch, c.Reason);
        Assert.Equal(0, c.Throttle);
    }

    [Theory]
    [InlineData(false, 1, false, ProbeReason.ControlLost)]
    [InlineData(true, 0, false, ProbeReason.FuelEmpty)]
    [InlineData(true, 1, true, ProbeReason.Wrecked)]
    public void NativeControlFuelAndWreckFailuresLatchStop(bool control, double fuel, bool wreck, ProbeReason expected)
    {
        var rig = new Rig();
        for (var i = 0; i < 200; i++) rig.Tick();
        var c = rig.Controller.Step(rig.Token, new ProbeObservation(rig.X, rig.Z, rig.Heading, rig.Speed, .02,
            hasControl: control, fuelLitres: fuel, wrecked: wreck));
        Assert.Equal(expected, c.Reason); Assert.Equal(0, c.Throttle); Assert.Equal(1, c.Brake);
        Assert.True(c.RequiresStationaryHold); Assert.True(c.MayApply);
        Assert.Equal(ProbeState.Cancelled, c.State);
    }

    [Fact]
    public void DisabledFuelDoesNotRequireTankAndBadObserverBoundariesFailClosed()
    {
        var rig = new Rig();
        Assert.True(rig.Controller.Step(rig.Token, new ProbeObservation(0, 0, 0, 0, .02, fuelEnabled: false, fuelLitres: 0)).Throttle > 0);
        var invalid = new Rig();
        var c = invalid.Controller.Step(invalid.Token, new ProbeObservation(0, 0, 0, 0, .02, stopBoundaryAlong: double.NaN));
        Assert.Equal(ProbeReason.InvalidObservation, c.Reason); AssertFinite(c);
        var penetrating = new Rig();
        c = penetrating.Controller.Step(penetrating.Token, new ProbeObservation(0, 0, 0, 0, .02, gapAhead: -.2));
        Assert.Equal(ProbeReason.BoundaryCrossed, c.Reason);
        Assert.Equal(.2, c.BoundaryViolationMetres);
    }

    [Fact]
    public void SteadyControllerAndObserverQueriesAllocateNothing()
    {
        var rig = new Rig();
        var o = new ProbeObservation(0, 0, 0, 0, .02);
        for (var i = 0; i < 1000; i++) rig.Controller.Step(rig.Token, o);
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 10000; i++)
        {
            rig.Controller.Step(rig.Token, o);
            rig.Path.TryProject(0, 0, 0, 10, out _);
            rig.Path.Sample(5);
        }
        var bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, bytes);
    }
}
