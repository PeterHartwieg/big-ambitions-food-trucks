#nullable enable
using System;
using System.Linq;
using Helpers;
using UnityEngine;

namespace FoodTrucks
{
    // Where a cart may open: NYC's siting rules, cut down to what the game has data for
    // (design: Pitches). The game has no hydrants, driveways or no-stopping zones, and nothing
    // marks the pavement, so those rules are left out.
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

            // Null when the spot is legal; otherwise the locale key naming the rule.
            public string? RuleKey { get; }
            public string Detail { get; }
            // Unverified signal, logged only: the game's Roads layer is confirmed on bridges alone.
            public bool RoadBelow { get; }
            public bool Legal => RuleKey == null;
        }

        public static Result Check(Vector3 position)
        {
            var road = RoadBelow(position);

            // A parking lane is the road side of the kerb.
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

        // Legal parking for a wheel standing at this point, by the game's own rule (CarController.UpdateParkingZone): a
        // small box round the wheel touches the parking layer, and the first lane it finds isn't a disabled bay. The box
        // is taller than the game's (it is sized by the wheel there) so a point at pavement or road height both reach.
        public static bool LegalParkingAt(Vector3 point)
        {
            var hits = Physics.OverlapBox(point, new Vector3(0.1f, 1.0f, 0.1f), Quaternion.identity, LayerHelper.parkingAreaLayerMask, QueryTriggerInteraction.Collide);
            if (hits.Length == 0 || hits[0] == null) return false;
            var lane = hits[0].GetComponentInParent<ParkingLaneGenerator>();
            return lane != null && !lane.isHandicapParking;
        }

        // The flat axes of the parking collider at this point (its box's forward and right), the ways a vehicle parked
        // there can line up. Empty off parking.
        public static (double X, double Z)[] ParkingAxesAt(Vector3 point)
        {
            var hits = Physics.OverlapBox(point, new Vector3(0.1f, 1.0f, 0.1f), Quaternion.identity, LayerHelper.parkingAreaLayerMask, QueryTriggerInteraction.Collide);
            if (hits.Length == 0 || hits[0] == null) return Array.Empty<(double, double)>();
            var t = hits[0].transform;
            return new[] { ((double)t.forward.x, (double)t.forward.z), ((double)t.right.x, (double)t.right.z) };
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
