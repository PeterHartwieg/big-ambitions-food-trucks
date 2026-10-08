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
            var hits = Physics.OverlapSphere(position, 0.4f, LayerHelper.parkingAreaLayerMask, QueryTriggerInteraction.Collide);
            if (hits.Any(h => h != null && h.GetComponentInParent<ParkingLaneGenerator>() != null))
                return new Result("foodtrucks:legality_parking_lane", "inside a parking lane", road);

            var near = Physics.OverlapSphere(position, CrosswalkMetres, ~0, QueryTriggerInteraction.Collide);
            if (near.Any(h => h != null && h.CompareTag("Crosswalk")))
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

        private static bool RoadBelow(Vector3 position)
        {
            try
            {
                return Physics.Raycast(position + Vector3.up * 2f, Vector3.down, 5f, 1 << LayerHelper.RoadsLayerIndex, QueryTriggerInteraction.Ignore);
            }
            catch (Exception) { return false; }
        }
    }
}
