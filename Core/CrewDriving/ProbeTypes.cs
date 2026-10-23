using System;

namespace FoodTrucks.Core.CrewDriving
{
    public enum ProbeState { Starting, Driving, Holding, Finished, Cancelled }
    public enum ProbeReason
    {
        None, WaitingForNative, Endpoint, Signal, Obstacle, Cancelled, StaleOwnership,
        InvalidObservation, InvalidTimestep, ControlLost, FuelEmpty, Wrecked, WrongDirection,
        PositionJump, HeadingMismatch, OutOfCorridor, SteeringLimit, BoundaryCrossed
    }

    public readonly struct ProbePoint
    {
        public double X { get; }
        public double Z { get; }
        public ProbePoint(double x, double z) { X = x; Z = z; }
    }

    /// <summary>Measured metres, degrees, m/s. Front/rear are bumper distances FROM THE REAR AXLE.</summary>
    public sealed class ProbeGeometry
    {
        public double Wheelbase { get; }
        public double Front { get; }
        public double Rear { get; }
        public double Width { get; }
        /// <summary>Minimum usable road-wheel lock across the entire configured speed range.</summary>
        public double MaxSteerDegrees { get; }
        public double MaxSpeedMs { get; }
        public double MaxCurvature => Math.Tan(MaxSteerDegrees * Math.PI / 180) / Wheelbase;

        public ProbeGeometry(double wheelbase, double front, double rear, double width, double maxSteerDegrees, double maxSpeedMs)
        {
            if (!ProbeMath.In(wheelbase, 1, 10) || !ProbeMath.In(front, wheelbase, 15) ||
                !ProbeMath.In(rear, 0, 8) || !ProbeMath.In(width, 1, 4) ||
                !ProbeMath.In(maxSteerDegrees, .5, 50) || !ProbeMath.In(maxSpeedMs, .2, 15))
                throw new ArgumentOutOfRangeException(nameof(wheelbase), "Invalid measured truck geometry or operating limit.");
            Wheelbase = wheelbase; Front = front; Rear = rear; Width = width;
            MaxSteerDegrees = maxSteerDegrees; MaxSpeedMs = maxSpeedMs;
        }
    }

    /// <summary>One physics observation. Heading is atan2(forwardX,forwardZ) radians; speed is signed forward m/s.</summary>
    public readonly struct ProbeObservation
    {
        public double RearX { get; }
        public double RearZ { get; }
        public double Heading { get; }
        public double SpeedMs { get; }
        public double Dt { get; }
        public double WheelDegrees { get; }
        public bool NativeReady { get; }
        public bool HasControl { get; }
        public bool FuelEnabled { get; }
        public double FuelLitres { get; }
        public bool Wrecked { get; }
        /// <summary>Rear-axle path coordinate (metres) of the plane the vehicle FRONT must not cross; null = green.</summary>
        public double? StopBoundaryAlong { get; }
        /// <summary>Free metres from the actual foremost body to nearest obstacle; negative = penetration.</summary>
        public double? GapAhead { get; }

        public ProbeObservation(double rearX, double rearZ, double heading, double speedMs, double dt,
            bool nativeReady = true, bool hasControl = true, bool fuelEnabled = true, double fuelLitres = 1,
            bool wrecked = false, double? stopBoundaryAlong = null, double? gapAhead = null, double wheelDegrees = 0)
        {
            RearX = rearX; RearZ = rearZ; Heading = heading; SpeedMs = speedMs; Dt = dt;
            NativeReady = nativeReady; HasControl = hasControl; FuelEnabled = fuelEnabled;
            FuelLitres = fuelLitres; Wrecked = wrecked; StopBoundaryAlong = stopBoundaryAlong;
            GapAhead = gapAhead; WheelDegrees = wheelDegrees;
        }
    }

    /// <summary>Forward-only controls. A stale command MUST NOT be applied, including its stop pedals.</summary>
    public readonly struct ProbeCommand
    {
        public ProbeState State { get; }
        public ProbeReason Reason { get; }
        public bool MayApply { get; }
        public bool RequiresStationaryHold { get; }
        public double SteerDegrees { get; }
        public double Throttle { get; }
        public double Brake { get; }
        public double Handbrake { get; }
        public double DesiredSpeedMs { get; }
        public double RearAlong { get; }
        public double StopBoundaryAlong { get; }
        /// <summary>Conservative distant route clearance; actual front-corner plane clearance near/beyond the boundary.
        /// Negative always measures a physical crossing, independent of clamped rear progress.</summary>
        public double FrontBoundaryDistance { get; }
        public double BoundaryViolationMetres => Math.Max(0, -FrontBoundaryDistance);

        internal ProbeCommand(ProbeState state, ProbeReason reason, bool mayApply, bool stationaryHold,
            double steer, double throttle, double brake, double handbrake, double desired,
            double along, double boundary, double clearance)
        {
            State = state; Reason = reason; MayApply = mayApply; RequiresStationaryHold = stationaryHold;
            SteerDegrees = steer; Throttle = throttle; Brake = brake; Handbrake = handbrake;
            DesiredSpeedMs = desired; RearAlong = along; StopBoundaryAlong = boundary; FrontBoundaryDistance = clearance;
        }
    }

    internal static class ProbeMath
    {
        internal static bool Finite(double n) => !double.IsNaN(n) && !double.IsInfinity(n);
        internal static bool In(double n, double min, double max) => Finite(n) && n >= min && n <= max;
        internal static double Clamp(double n, double min, double max) => Math.Max(min, Math.Min(max, n));
        internal static double Wrap(double radians) => Math.Atan2(Math.Sin(radians), Math.Cos(radians));
        internal static double Distance(double x, double z) => Math.Sqrt(x * x + z * z);
    }
}
