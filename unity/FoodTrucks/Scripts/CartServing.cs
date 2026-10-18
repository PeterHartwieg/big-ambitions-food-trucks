#nullable enable
using System;
using FoodTrucks.Core.Saves;
using Helpers;
using Parking.UndergroundParking;
using PlayerActivity;
using UI.Load;
using UnityEngine;
using UnityEngine.AI;

namespace FoodTrucks
{
    // The cart's idle serving stance, owned by CartSelling. Unlike the enclosed truck hatch, this is on
    // walkable ground: keep the live player agent/collider and let the game's next navigation command release
    // the stance. Never use ThirdPersonCharacter.ForceToPosition for the player: it leaves physics kinematic
    // and the agent's updatePosition/updateRotation off (1.0). No attachment or navigation blocker is needed.
    internal sealed class CartServing
    {
        private const float BehindMetres = 0.85f; // same side/clearance as the cart's WindowCrew
        private const float HalfLength = 0.77f; // body/handle extents, art/README.md; leave the player's radius beyond them
        private const float FallbackMetres = 3f;
        private const float SampleStep = 0.25f;
        private PlayerController? player;
        private VehicleController? cart;
        private string? vehicleId;
        private Vector3 position;
        private Quaternion rotation;
        private Vector3 cartPosition;
        private Quaternion cartRotation;

        public bool PlayerAt(string id) => vehicleId == id;

        public void Open(VehicleController controller)
        {
            Release("another window opened");
            try { Place(controller); }
            catch (Exception ex)
            {
                Release("placement failed");
                Log.Error("Cart serving: " + ex);
            }
        }

        private void Place(VehicleController controller)
        {
            var who = PlayerHelper.PlayerController;
            var agent = who?.Character?.navmeshAgent;
            var id = controller.vehicleInstance.id;
            if (who == null || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh ||
                !string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId) || TruckWindow.Phase() != null ||
                BuildingManager.IsInsideBuilding || UndergroundParkingManager.IsInsideParking || CartSelling.Skipping ||
                LoadScene.isLoading || PlayerActivityUI.IsPanelOpen)
            {
                Log.Info($"Cart serving: {id}: player not placed (not free on foot outdoors on the player NavMesh).");
                return;
            }
            var window = CartQueueView.PrepareServing(controller);
            var side = window?.Side ?? 1;
            if (window == null) Log.Info($"Cart serving: {id}: no visible queue side; using the cart's right window side.");
            var outward = controller.transform.right * side;
            outward.y = 0f;
            outward.Normalize();
            var forward = controller.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            var lanes = TrafficLanes.Near(controller.transform.position, 30f);
            if (!ServingPoint(controller.transform.position, outward, forward, agent.radius, who.transform.position,
                filter, lanes, out var spot, out var placement))
            {
                Log.Info($"Cart serving: {id}: no safe reachable serving point behind the cart or at either end; player left where they stand.");
                return;
            }
            var look = window?.Window ?? controller.transform.position + outward;
            var facing = look - spot;
            facing.y = 0f;
            var turn = Quaternion.LookRotation(facing);
            var target = new GameObject("FoodTrucks.CartServingTarget");
            try
            {
                target.transform.SetPositionAndRotation(spot, turn);
                who.ResetNavigation(); // discard the old handle/storage/interaction goal before the warp
                PlayerHelper.Teleport(target.transform); // 1.0: NavMeshAgent.Warp plus player rotation
            }
            finally { UnityEngine.Object.Destroy(target); }
            if (!agent.isOnNavMesh || Vector3.Distance(who.transform.position, spot) > 0.1f)
            {
                Log.Info($"Cart serving: {id}: player NavMesh warp to {spot} failed; no stance held.");
                return;
            }
            player = who;
            cart = controller;
            vehicleId = id;
            position = spot;
            rotation = turn;
            cartPosition = controller.transform.position;
            cartRotation = controller.transform.rotation;
            who.Character.velocity = Vector3.zero;
            agent.velocity = Vector3.zero;
            who.PlayerChangedNavigation.AddListener(OnNavigation);
            CartQueueView.Recheck();
            Log.Info($"Cart serving: {id}: player placed {placement} at {spot}, queue on the {(side > 0 ? "right" : "left")}, facing the window at {look}; pedestrian ground, off street, complete player path (agent {filter.agentTypeID}); idle stance held until walking or closing.");
        }

        // Prefer the opposite side, then the front and rear ends beside the window. A kerbside cart's opposite
        // side can be the road: player-mesh membership alone is not evidence of pavement. Every result must also
        // be on the NPC mesh, pass the queue's street exclusions and have a complete path from the live player.
        internal static bool ServingPoint(Vector3 center, Vector3 outward, Vector3 forward, float radius, Vector3 start,
            NavMeshQueryFilter filter, TrafficLanes lanes, out Vector3 spot, out string placement)
        {
            var path = new NavMeshPath(); // reused for exact samples and every fallback, on all three sides
            bool Safe(Vector3 p) =>
                // The two meshes are separate bakes whose heights rarely agree to the centimetre, so the queue's own
                // PlaceAt tolerances apply: no more than 0.1 m sideways (no snapping from road to nearby pavement).
                NavMesh.SamplePosition(p, out var pedestrian, 0.5f, Extensions.NavMeshHelper.NpcNavMeshFilter) &&
                (p.x - pedestrian.position.x) * (p.x - pedestrian.position.x) + (p.z - pedestrian.position.z) * (p.z - pedestrian.position.z) <= 0.01f &&
                Mathf.Abs(p.y - pedestrian.position.y) <= 0.3f &&
                QueueLine.OnStreet(p, lanes) == null &&
                NavMesh.CalculatePath(start, p, filter, path) && path.status == NavMeshPathStatus.PathComplete;

            bool Search(Vector3 wanted, Func<Vector3, bool> geometry, out Vector3 found, out bool fallback)
            {
                bool Valid(Vector3 p) => geometry(p) && Mathf.Abs(p.y - center.y) <= 0.5f &&
                    Vector3.Distance(p, wanted) <= FallbackMetres && Safe(p);
                if (NavMesh.SamplePosition(wanted, out var exact, 0.1f, filter) && Valid(exact.position))
                {
                    found = exact.position;
                    fallback = false;
                    return true;
                }
                Vector3? best = null;
                if (NavMesh.SamplePosition(wanted, out var nearest, FallbackMetres, filter) && Valid(nearest.position)) best = nearest.position;
                for (var across = -FallbackMetres; across <= FallbackMetres; across += SampleStep)
                for (var along = -FallbackMetres; along <= FallbackMetres; along += SampleStep)
                {
                    var probe = wanted + outward * across + forward * along;
                    if (!NavMesh.SamplePosition(probe, out var hit, SampleStep, filter)) continue;
                    // CalculatePath is synchronous: only prove candidates that could improve the nearest safe result.
                    if (best.HasValue && (hit.position - wanted).sqrMagnitude >= (best.Value - wanted).sqrMagnitude) continue;
                    if (Valid(hit.position)) best = hit.position;
                }
                found = best ?? wanted;
                fallback = true;
                return best.HasValue;
            }

            // The body/wheels extend 0.50 m sideways. Keep the player's radius clear of the hull on each side.
            var clearance = 0.50f + radius;
            var behind = center - outward * Mathf.Max(BehindMetres, clearance);
            if (Search(behind, p => Vector3.Dot(center - p, outward) >= clearance - 0.01f, out spot, out var usedFallback))
            {
                placement = usedFallback ? "at a fallback behind the cart, opposite the queue" : "behind the cart, opposite the queue";
                return true;
            }
            var endClearance = HalfLength + radius;
            foreach (var direction in new[] { 1, -1 })
            {
                var end = center + forward * (direction * endClearance);
                bool AtEnd(Vector3 p) => Vector3.Dot(p - center, forward) * direction >= endClearance - 0.01f &&
                    Vector3.Dot(p - center, outward) >= -0.01f &&
                    Vector3.Dot(p - center, outward) <= Mathf.Max(BehindMetres, clearance);
                if (!Search(end, AtEnd, out spot, out usedFallback)) continue;
                placement = $"at the {(direction > 0 ? "front" : "rear")} end beside the window" + (usedFallback ? " (fallback)" : "");
                return true;
            }
            spot = start;
            placement = "left where they stand";
            return false;
        }

        // PlayerController emits this for both click/SetGoal navigation and WASD starting. Release without
        // ResetNavigation: the new destination/action belongs to the game and must be allowed to finish.
        private void OnNavigation() => Release("player started walking or interacting");

        public void Closed(string id)
        {
            if (vehicleId == id) Release("the cart closed");
        }

        public void Release(string reason)
        {
            if (player != null) player.PlayerChangedNavigation.RemoveListener(OnNavigation);
            if (vehicleId != null)
            {
                Log.Info($"Cart serving: {vehicleId}: stance released ({reason}).");
                CartQueueView.Recheck();
            }
            vehicleId = null;
            player = null;
            cart = null;
        }

        public void LateUpdate()
        {
            if (vehicleId == null) return;
            try
            {
                var who = player;
                var controller = cart;
                var agent = who?.Character?.navmeshAgent;
                if (who == null || who != PlayerHelper.PlayerController || controller == null || LoadScene.isLoading ||
                    StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId)?.IsOpen != true || CartSelling.Skipping ||
                    !string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId) || BuildingManager.IsInsideBuilding ||
                    UndergroundParkingManager.IsInsideParking || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh ||
                    Vector3.Distance(controller.transform.position, cartPosition) > 0.1f || Quaternion.Angle(controller.transform.rotation, cartRotation) > 1f ||
                    Vector3.Distance(who.transform.position, position) > 0.5f)
                {
                    Release("window, player or cart moved/changed");
                    return;
                }
                if (agent.hasPath || agent.pathPending || who.hasOnGoalReachedAction || who.Character.velocity.sqrMagnitude > 0.001f)
                {
                    OnNavigation();
                    return;
                }
                // Keep idle avoidance/physics from turning or nudging the server out of their place. All agent,
                // collider and physics flags remain the game's; the next navigation event ends this immediately.
                if ((who.transform.position - position).sqrMagnitude > 0.0001f && !agent.Warp(position))
                {
                    Release("the serving point is no longer walkable");
                    return;
                }
                agent.velocity = Vector3.zero;
                who.transform.rotation = rotation;
            }
            catch (Exception ex)
            {
                Release("stance failed");
                Log.Error("Cart serving: " + ex);
            }
        }
    }
}
