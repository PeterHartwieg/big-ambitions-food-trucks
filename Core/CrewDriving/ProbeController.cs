using System;

namespace FoodTrucks.Core.CrewDriving
{
    public sealed class ProbeController
    {
        public const double StopMargin = 1;
        public const double CorridorTolerance = .6;
        public const double HeadingTolerance = .35;
        public const double PlannedDeceleration = 1.5;
        public const double BrakeDeceleration = 4;
        public const double LateralAcceleration = .8;
        public const double ReactionSeconds = .3;
        private const double StoppedSpeed = .05;
        private readonly ProbeOwnership ownership;
        private readonly ProbePath path;
        private readonly ProbeGeometry geometry;
        private readonly double cruise, corridorTolerance, headingTolerance;
        private bool observed;
        private double lastX, lastZ, lastHeading, lastSpeed, stopSpeed;
        private double clearance, boundary;
        private ProbeReason reason = ProbeReason.WaitingForNative;
        public ProbeToken Token { get; }
        public ProbeState State { get; private set; } = ProbeState.Starting;
        public double RearAlong { get; private set; }

        public ProbeController(ProbeOwnership ownership, ProbeToken token, ProbePath path, ProbeGeometry geometry,
            double corridorTolerance = CorridorTolerance, double headingTolerance = HeadingTolerance)
        {
            this.ownership = ownership ?? throw new ArgumentNullException(nameof(ownership));
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            this.geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            if (!ProbeMath.In(corridorTolerance, .05, CorridorTolerance) || !ProbeMath.In(headingTolerance, .01, HeadingTolerance))
                throw new ArgumentOutOfRangeException(nameof(corridorTolerance), "Invalid probe tracking tolerances.");
            this.corridorTolerance = corridorTolerance; this.headingTolerance = headingTolerance;
            path.ValidateGeometry(geometry);
            if (!ownership.BindController(token)) throw new ArgumentException("Token must own this native controller and must not already have a probe.", nameof(token));
            Token = token;
            boundary = path.Length;
            clearance = path.Length - geometry.Front;
            cruise = Math.Min(geometry.MaxSpeedMs, path.MaxCurvature > 1e-9 ? Math.Sqrt(LateralAcceleration / path.MaxCurvature) : geometry.MaxSpeedMs);
        }

        public ProbeCommand Step(ProbeToken token, ProbeObservation o)
        {
            if (!Current(token)) return Stop(ProbeReason.StaleOwnership, false, true);
            stopSpeed = ProbeMath.In(o.SpeedMs, -50, 50) ? Math.Abs(o.SpeedMs) : 50;
            if (State == ProbeState.Cancelled) return Stop(reason, true, true);
            if (!ValidNumbers(o)) return Fail(ProbeReason.InvalidObservation);

            boundary = o.StopBoundaryAlong.HasValue ? Math.Min(path.Length, o.StopBoundaryAlong.Value) : path.Length;
            clearance = BoundaryDistance(o, boundary);
            var stopReason = boundary < path.Length ? ProbeReason.Signal : ProbeReason.Endpoint;
            if (o.GapAhead.HasValue && o.GapAhead.Value < clearance)
            { clearance = o.GapAhead.Value; stopReason = ProbeReason.Obstacle; }
            if (clearance < -1e-6) return Fail(ProbeReason.BoundaryCrossed);
            if (!ProbeMath.In(o.Dt, .001, .1)) return Fail(ProbeReason.InvalidTimestep);
            if (!o.HasControl) return Fail(ProbeReason.ControlLost);
            if (o.Wrecked) return Fail(ProbeReason.Wrecked);
            if (o.FuelEnabled && o.FuelLitres <= 0) return Fail(ProbeReason.FuelEmpty);
            if (o.SpeedMs < -.02) return Fail(ProbeReason.WrongDirection);
            if (Math.Abs(o.WheelDegrees) > geometry.MaxSteerDegrees + .5) return Fail(ProbeReason.SteeringLimit);

            var poseTolerance = observed && lastSpeed <= StoppedSpeed && Math.Abs(o.SpeedMs) <= StoppedSpeed ? .11 : .03;
            var travel = observed ? Math.Max(lastSpeed, Math.Max(0, o.SpeedMs)) * o.Dt + 1.5 * o.Dt * o.Dt + poseTolerance : corridorTolerance;
            if (observed)
            {
                var dx = o.RearX - lastX; var dz = o.RearZ - lastZ;
                if (ProbeMath.Distance(dx, dz) > travel) return Fail(ProbeReason.PositionJump);
                if (dx * Math.Sin(lastHeading) + dz * Math.Cos(lastHeading) < -poseTolerance) return Fail(ProbeReason.WrongDirection);
                if (Math.Abs(ProbeMath.Wrap(o.Heading - lastHeading)) > Math.Max(lastSpeed, Math.Abs(o.SpeedMs)) * geometry.MaxCurvature * o.Dt + .03)
                    return Fail(ProbeReason.PositionJump);
            }
            if (!path.TryProject(o.RearX, o.RearZ, Math.Max(0, RearAlong - poseTolerance - .01), Math.Min(path.Length, RearAlong + travel), out var p))
                return Fail(ProbeReason.OutOfCorridor);
            if (p.Distance > corridorTolerance) return Fail(ProbeReason.OutOfCorridor);
            if (Math.Abs(ProbeMath.Wrap(o.Heading - p.Heading)) > headingTolerance) return Fail(ProbeReason.HeadingMismatch);
            if (p.Along < RearAlong - poseTolerance) return Fail(ProbeReason.WrongDirection);
            if (!observed && Math.Abs(o.SpeedMs) > .2) return Fail(ProbeReason.InvalidObservation);
            RearAlong = Math.Max(RearAlong, p.Along);
            lastX = o.RearX; lastZ = o.RearZ; lastHeading = o.Heading; lastSpeed = Math.Max(0, o.SpeedMs); observed = true;
            clearance = BoundaryDistance(o, boundary);
            if (o.GapAhead.HasValue) clearance = Math.Min(clearance, o.GapAhead.Value);
            if (clearance < -1e-6) return Fail(ProbeReason.BoundaryCrossed);
            if (State == ProbeState.Finished) return Stop(ProbeReason.Endpoint, true, false);
            if (!o.NativeReady)
            {
                if (State != ProbeState.Starting || lastSpeed > .2) return Fail(ProbeReason.ControlLost);
                return Stop(ProbeReason.WaitingForNative, true, true);
            }

            var room = Math.Max(0, clearance - StopMargin);
            var latency = ReactionSeconds + o.Dt;
            var safe = PlannedDeceleration * (Math.Sqrt(latency * latency + 2 * room / PlannedDeceleration) - latency);
            var desired = Math.Min(cruise, safe);
            if (room <= .2) desired = 0;

            var lookahead = Math.Max(geometry.Wheelbase + 1.5, 1.5 + .45 * lastSpeed);
            var target = path.Sample(RearAlong + lookahead);
            var tx = target.X - o.RearX; var tz = target.Z - o.RearZ;
            var alpha = ProbeMath.Wrap(Math.Atan2(tx, tz) - o.Heading);
            var curvature = 2 * Math.Sin(alpha) / Math.Max(1, ProbeMath.Distance(tx, tz));
            var steer = Math.Atan(geometry.Wheelbase * curvature) * 180 / Math.PI;
            if (Math.Abs(steer) > geometry.MaxSteerDegrees) return Fail(ProbeReason.SteeringLimit);
            var wheelCurvature = Math.Tan(Math.Max(Math.Abs(steer), Math.Abs(o.WheelDegrees)) * Math.PI / 180) / geometry.Wheelbase;
            if (wheelCurvature > 1e-9) desired = Math.Min(desired, Math.Sqrt(LateralAcceleration / wheelCurvature));

            if (desired == 0 && lastSpeed <= StoppedSpeed)
            {
                State = stopReason == ProbeReason.Endpoint ? ProbeState.Finished : ProbeState.Holding;
                reason = stopReason;
                return Stop(reason, true, false);
            }
            State = desired == 0 ? ProbeState.Holding : ProbeState.Driving;
            reason = desired < cruise ? stopReason : ProbeReason.None;
            var error = desired - lastSpeed;
            var throttle = error > 0 ? Math.Min(.5, .6 * error) : 0;
            var brake = error < 0 ? Math.Min(1, -error / (BrakeDeceleration * o.Dt)) : 0;
            if (room < lastSpeed * latency + lastSpeed * lastSpeed / (2 * PlannedDeceleration))
            {
                var need = lastSpeed * lastSpeed / (2 * Math.Max(.05, room - lastSpeed * latency));
                brake = Math.Max(brake, Math.Min(1, need / BrakeDeceleration));
                throttle = 0;
            }
            var handbrake = 0.0;
            if (desired == 0 && lastSpeed < 1) { brake = 0; handbrake = 1; }
            return Command(reason, true, false, steer, throttle, brake, handbrake, desired);
        }

        public ProbeCommand Cancel(ProbeToken token)
        {
            if (!Current(token)) return Stop(ProbeReason.StaleOwnership, false, true);
            if (State != ProbeState.Cancelled && State != ProbeState.Finished)
            { State = ProbeState.Cancelled; reason = ProbeReason.Cancelled; }
            return Stop(reason, true, true);
        }

        private bool Current(ProbeToken token) => Token.Equals(token) && ownership.Owns(token);
        private ProbeCommand Fail(ProbeReason why)
        { State = ProbeState.Cancelled; reason = why; return Stop(why, true, true); }
        private ProbeCommand Stop(ProbeReason why, bool apply, bool stationary) =>
            Command(why, apply, stationary, 0, 0, stopSpeed >= 1 ? 1 : 0, stopSpeed < 1 ? 1 : 0, 0);
        private ProbeCommand Command(ProbeReason why, bool apply, bool stationary, double steer, double throttle, double brake, double handbrake, double desired) =>
            new ProbeCommand(State, why, apply, stationary, steer, throttle, brake, handbrake, desired, RearAlong, boundary, clearance);

        private bool ValidNumbers(ProbeObservation o) =>
            ProbeMath.In(o.RearX, -100000, 100000) && ProbeMath.In(o.RearZ, -100000, 100000) &&
            ProbeMath.In(o.Heading, -1000 * Math.PI, 1000 * Math.PI) &&
            ProbeMath.In(o.SpeedMs, -50, geometry.MaxSpeedMs * 1.3 + 1) &&
            ProbeMath.In(o.WheelDegrees, -90, 90) && ProbeMath.In(o.FuelLitres, 0, 10000) &&
            (!o.StopBoundaryAlong.HasValue || ProbeMath.In(o.StopBoundaryAlong.Value, 0, path.Length)) &&
            (!o.GapAhead.HasValue || ProbeMath.In(o.GapAhead.Value, -1000, 10000));

        private double BoundaryDistance(ProbeObservation o, double at)
        {
            var point = path.Sample(at);
            var reach = geometry.Front + geometry.Width / 2 + 2;
            var tangent = path.HeadingAt(at);
            var tx = Math.Sin(tangent); var tz = Math.Cos(tangent);
            var hx = Math.Sin(o.Heading); var hz = Math.Cos(o.Heading);
            var frontX = o.RearX + hx * geometry.Front;
            var frontZ = o.RearZ + hz * geometry.Front;
            var foremostCorner = (frontX - point.X) * tx + (frontZ - point.Z) * tz +
                geometry.Width / 2 * Math.Abs(hz * tx - hx * tz);
            if (RearAlong < at - reach && ProbeMath.Distance(o.RearX - point.X, o.RearZ - point.Z) > reach)
                return Math.Min(-foremostCorner, at - RearAlong - geometry.Front - geometry.Width / 2);
            return -foremostCorner;
        }
    }
}
