using System;
using System.Collections.Generic;

namespace FoodTrucks.Core.CrewDriving
{
    /// <summary>Transient identity. ControllerInstance identifies the actual native controller, not its vehicle.</summary>
    public readonly struct ProbeToken : IEquatable<ProbeToken>
    {
        internal readonly Guid Registry;
        public Guid Session { get; }
        public string NativeVehicleId { get; }
        public long ControllerInstance { get; }
        public long Generation { get; }

        internal ProbeToken(Guid registry, Guid session, string id, long instance, long generation)
        { Registry = registry; Session = session; NativeVehicleId = id; ControllerInstance = instance; Generation = generation; }

        public bool Equals(ProbeToken other) => Registry == other.Registry && Session == other.Session &&
            NativeVehicleId == other.NativeVehicleId && ControllerInstance == other.ControllerInstance && Generation == other.Generation;
        public override bool Equals(object? obj) => obj is ProbeToken other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Registry, Session, NativeVehicleId, ControllerInstance, Generation);
    }

    /// <summary>Main-thread registry, one per active save session. No global one-truck cap here.</summary>
    public sealed class ProbeOwnership
    {
        private readonly Guid registry = Guid.NewGuid();
        private readonly Dictionary<string, ProbeToken> owners = new Dictionary<string, ProbeToken>(StringComparer.Ordinal);
        private readonly HashSet<long> boundControllers = new HashSet<long>();
        private long generation;
        public Guid Session { get; }
        public int Count => owners.Count;

        public ProbeOwnership(Guid session)
        {
            if (session == Guid.Empty) throw new ArgumentException("A live session identity is required.", nameof(session));
            Session = session;
        }

        public bool TryAcquire(string nativeVehicleId, long controllerInstance, out ProbeToken token)
        {
            token = default;
            if (string.IsNullOrWhiteSpace(nativeVehicleId) || controllerInstance == 0 || generation == long.MaxValue)
                return false;
            // A replacement controller cannot bypass ownership of the same native vehicle.
            if (owners.ContainsKey(nativeVehicleId)) return false;
            token = new ProbeToken(registry, Session, nativeVehicleId, controllerInstance, ++generation);
            owners.Add(nativeVehicleId, token);
            return true;
        }

        public bool Owns(ProbeToken token) => token.Registry == registry && token.Session == Session &&
            token.NativeVehicleId != null && owners.TryGetValue(token.NativeVehicleId, out var live) && live.Equals(token);

        internal bool BindController(ProbeToken token) => Owns(token) && boundControllers.Add(token.Generation);

        /// <summary>Call AFTER revoking commands, holding/synchronizing the body and restoring captured settings.</summary>
        public bool Release(ProbeToken token)
        {
            if (!Owns(token)) return false;
            boundControllers.Remove(token.Generation);
            return owners.Remove(token.NativeVehicleId);
        }
    }
}
