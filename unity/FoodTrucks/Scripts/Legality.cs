#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
    internal static class Legality
    {
        private const float CrosswalkMetres = 3f;
        private const float DoorMetres = 3f;
        private const float SubwayMetres = 3f;

        public sealed class Result
        {
            public Result(string? ruleKey, string detail, bool roadBelow)
            {
                RuleKey = ruleKey;
                Detail = detail;
                RoadBelow = roadBelow;
            }

            public string? RuleKey { get; }
            public string Detail { get; }
            public bool RoadBelow { get; }
            public bool Legal => RuleKey == null;
        }

        public static Result Check(Vector3 position)
        {
            var road = RoadBelow(position);

            if (InParkingLane(position, 0.4f))
                return new Result("foodtrucks:legality_parking_lane", "inside a parking lane", road);

            if (NearCrosswalk(position, CrosswalkMetres))
                return new Result("foodtrucks:legality_crosswalk", $"within {CrosswalkMetres} m of a crosswalk", road);

            var door = World.DoorPositions().Select(d => Flat(d - position)).DefaultIfEmpty(float.MaxValue).Min();
            if (door < DoorMetres)
                return new Result("foodtrucks:legality_door", $"{door:F1} m from a building entrance", road);

            var stations = InstanceBehavior<CityManager>.Instance?.subwayStations;
            if (stations != null)
            {
                foreach (var station in stations)
                {
                    if (station == null) continue;
                    var distance = Flat(station.transform.position - position);
                    if (distance < SubwayMetres)
                        return new Result("foodtrucks:legality_subway", $"{distance:F1} m from a subway station", road);
                }
            }
            return new Result(null, $"legal; nearest entrance {door:F1} m", road);
        }

        private static float Flat(Vector3 v) => new Vector2(v.x, v.z).magnitude;

        public static bool NearCrosswalk(Vector3 position, float radius) =>
            Physics.OverlapSphere(position, radius, ~0, QueryTriggerInteraction.Collide).Any(h => h != null && h.CompareTag("Crosswalk"));

        public static bool InParkingLane(Vector3 position, float radius = 0.3f)
        {
            var hits = Physics.OverlapSphere(position, radius, LayerHelper.parkingAreaLayerMask, QueryTriggerInteraction.Collide);
            return hits.Any(h => h != null && h.GetComponentInParent<ParkingLaneGenerator>() != null);
        }

        public const float WheelBoxHalfWidth = 0.1f;
        public const float TruckWheelRadius = 0.38f;
        private const float TruckWheelControllerUp = 0.38f + 0.164f;
        private static readonly Collider[] probeHits = new Collider[16];

        public static bool LegalWheelAt(Vector3 point, Quaternion rotation, float halfHeight)
        {
            var count = Physics.OverlapBoxNonAlloc(point, new Vector3(WheelBoxHalfWidth, halfHeight, WheelBoxHalfWidth), probeHits, rotation,
                LayerHelper.parkingAreaLayerMask, QueryTriggerInteraction.Collide);
            if (count == 0 || probeHits[0] == null) return false;
            var lane = probeHits[0].GetComponentInParent<ParkingLaneGenerator>();
            return lane != null && !lane.isHandicapParking;
        }

        public static bool GameParkingReading(Transform vehicle, out string neighbourhood)
        {
            neighbourhood = string.Empty;
            var wheels = WheelsOf(vehicle);
            if (wheels.Length == 0) return LegalParkingAt(vehicle.position, vehicle.forward);
            foreach (var wheel in wheels)
            {
                var count = Physics.OverlapBoxNonAlloc(wheel.position - wheel.up * 0.1f, new Vector3(WheelBoxHalfWidth, TruckWheelRadius * 2f, WheelBoxHalfWidth),
                    probeHits, wheel.rotation, LayerHelper.parkingAreaLayerMask, QueryTriggerInteraction.Collide);
                if (count == 0 || probeHits[0] == null) return false;
                var lane = probeHits[0].GetComponentInParent<ParkingLaneGenerator>();
                if (lane == null || lane.isHandicapParking) return false;
                neighbourhood = lane.neighbourhood;
            }
            return true;
        }

        public static bool LegalParkingAt(Vector3 point, Vector3 along)
        {
            along.y = 0f;
            var rotation = along.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(along.normalized, Vector3.up) : Quaternion.identity;
            return LegalWheelAt(point, rotation, 1.0f);
        }

        public static bool ParkingNear(Vector3 point, float radius)
        {
            var count = Physics.OverlapBoxNonAlloc(point, new Vector3(radius, 50f, radius), probeHits, Quaternion.identity,
                LayerHelper.parkingAreaLayerMask, QueryTriggerInteraction.Collide);
            for (var i = 0; i < count; i++)
                if (probeHits[i] != null && probeHits[i].GetComponentInParent<ParkingLaneGenerator>() != null) return true;
            return false;
        }

        public static Transform[] WheelsOf(Transform vehicle) =>
            vehicle.GetComponentsInChildren<Transform>(true).Where(t => t.name.EndsWith("_WheelController", StringComparison.Ordinal)).ToArray();

        private static readonly LaneCache<ParkingLaneGenerator> laneCache = new LaneCache<ParkingLaneGenerator>();

        private const float ReadyCacheSeconds = 1f;
        private static float readyUntil = float.MinValue;
        private static bool readyAnswer;

        public static bool ParkingSceneReady()
        {
            var now = Time.unscaledTime;
            if (now < readyUntil) return readyAnswer;
            readyAnswer = laneCache.Ready(now, () => UnityEngine.Object.FindObjectsByType<ParkingLaneGenerator>(FindObjectsSortMode.None),
                l => l != null, l => l.gameObject.activeInHierarchy);
            readyUntil = now + ReadyCacheSeconds;
            return readyAnswer;
        }

        public static void ResetLanes()
        {
            laneCache.Clear();
            readyUntil = float.MinValue;
        }

        private static Vector3[]? truckWheels;
        private static WheelFootprint? truckFootprint;

        public static Vector3[] TruckWheels()
        {
            if (truckWheels != null) return truckWheels;
            try
            {
                var prefab = FoodTrucksMod.Bundle?.LoadAsset<GameObject>(FoodTrucksMod.TruckPrefabPath);
                var wheels = prefab == null ? Array.Empty<Transform>() : WheelsOf(prefab.transform);
                if (wheels.Length >= 3)
                {
                    truckWheels = wheels.Select(w => prefab!.transform.InverseTransformPoint(w.position)).ToArray();
                    Log.Info($"Commissary bays: the truck's wheels from its prefab: {string.Join(", ", truckWheels.Select(p => $"({p.z:+0.00;-0.00} along, {p.x:+0.00;-0.00} across, {p.y:F2} up)"))}.");
                    return truckWheels;
                }
                Log.Error($"Commissary bays: the truck prefab has {wheels.Length} wheel controllers; using TruckAssets' wheels (±1.80 m along, ±0.87 m across).");
            }
            catch (Exception ex) { Log.Error("Commissary bays, the truck's wheels: " + ex.Message + "; using TruckAssets' wheels."); }
            truckWheels = WheelFootprint.Truck.Wheels.Select(w => new Vector3((float)w.Across, TruckWheelControllerUp, (float)w.Along)).ToArray();
            return truckWheels;
        }

        public static WheelFootprint TruckFootprint() =>
            truckFootprint ??= new WheelFootprint(TruckWheels().Select(w => ((double)w.z, (double)w.x)));

        public static bool VehicleLegallyParked(VehicleInstance vehicle, out string how)
        {
            if (!ParkingSceneReady())
            {
                how = "the game's saved parking state (the city's parking lanes aren't loaded)";
                return vehicle.parkingState == ParkingState.Legal;
            }
            var controller = VehicleHelper.GetVehicleController(vehicle);
            if (controller == null)
            {
                how = "the game's wheel test at its saved pose (not in the world)";
                var position = (Vector3)vehicle.position;
                var rotation = (Quaternion)vehicle.rotation;
                var q = ((double)rotation.x, (double)rotation.y, (double)rotation.z, (double)rotation.w);
                foreach (var hub in TruckWheels())
                {
                    var p = PoseMath.WheelProbe((position.x, position.y, position.z), q, (hub.x, hub.y, hub.z));
                    if (!LegalWheelAt(new Vector3((float)p.X, (float)p.Y, (float)p.Z), rotation, TruckWheelRadius * 2f)) return false;
                }
                return true;
            }
            var wheels = WheelsOf(controller.transform);
            if (wheels.Length > 0)
            {
                how = "the game's wheel test";
                foreach (var wheel in wheels)
                    if (!LegalWheelAt(wheel.position - wheel.up * 0.1f, wheel.rotation, TruckWheelRadius * 2f)) return false;
                return true;
            }
            how = "its middle on legal parking";
            return LegalParkingAt(controller.transform.position, controller.transform.forward);
        }

        internal sealed class ParkingProbe
        {
            private static readonly Collider[] found = new Collider[512];
            private readonly List<(Collider Collider, Bounds Bounds)> lanes = new List<(Collider, Bounds)>();

            public ParkingProbe(Vector3 centre, float radius)
            {
                var count = Physics.OverlapBoxNonAlloc(centre, new Vector3(radius, 50f, radius), found, Quaternion.identity,
                    LayerHelper.parkingAreaLayerMask, QueryTriggerInteraction.Collide);
                for (var i = 0; i < count; i++)
                {
                    var c = found[i];
                    if (c == null || !c.enabled || c.GetComponentInParent<ParkingLaneGenerator>() == null) continue;
                    lanes.Add((c, c.bounds));
                }
                Truncated = count >= found.Length;
            }

            public int Count => lanes.Count;
            public bool Truncated { get; }

            private (Collider Collider, float Y, float HalfHeight)? Under(double x, double z)
            {
                foreach (var (c, b) in lanes)
                {
                    if (x < b.min.x - 0.15 || x > b.max.x + 0.15 || z < b.min.z - 0.15 || z > b.max.z + 0.15) continue;
                    var p = new Vector3((float)x, b.center.y, (float)z);
                    var closest = c.ClosestPoint(p);
                    if (new Vector2(closest.x - p.x, closest.z - p.z).sqrMagnitude > 0.15f * 0.15f) continue;
                    return (c, b.center.y, Mathf.Max(1.0f, b.extents.y + 0.3f));
                }
                return null;
            }

            public bool Legal(double x, double z, double alongX, double alongZ)
            {
                var under = Under(x, z);
                if (under == null) return false;
                var along = new Vector3((float)alongX, 0f, (float)alongZ);
                var rotation = along.sqrMagnitude > 1e-6f ? Quaternion.LookRotation(along.normalized, Vector3.up) : Quaternion.identity;
                return LegalWheelAt(new Vector3((float)x, under.Value.Y, (float)z), rotation, under.Value.HalfHeight);
            }

            public IEnumerable<(double X, double Z)> Axes(double x, double z)
            {
                var under = Under(x, z);
                if (under == null) return Array.Empty<(double, double)>();
                var t = under.Value.Collider.transform;
                return new[] { ((double)t.forward.x, (double)t.forward.z), ((double)t.right.x, (double)t.right.z) };
            }
        }

        public static bool RoadBelow(Vector3 position)
        {
            try
            {
                return Physics.Raycast(position + Vector3.up * 2f, Vector3.down, 5f, 1 << LayerHelper.RoadsLayerIndex, QueryTriggerInteraction.Ignore);
            }
            catch (Exception) { return false; }
        }
    }
}
