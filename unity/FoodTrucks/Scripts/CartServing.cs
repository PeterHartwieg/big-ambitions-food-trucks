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
            // The body/wheels extend 0.50 m sideways. A sampled point must leave room for the player's radius
            // on the NON-customer side; a large SamplePosition alone could snap across the cart or onto a floor.
            var clearance = 0.50f + agent.radius;
            var wanted = controller.transform.position - outward * Mathf.Max(BehindMetres, clearance);
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!ServingPoint(wanted, controller.transform.position, outward, clearance, filter, out var spot, out var fallback))
            {
                Log.Info($"Cart serving: {id}: no walkable serving point behind the cart within {FallbackMetres:F1} m of {wanted}; player left where they stand.");
                return;
            }
            if (fallback) Log.Info($"Cart serving: {id}: serving spot {wanted} is not walkable; nearest walkable fallback behind the cart is {spot} (player NavMesh agent {filter.agentTypeID}).");
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
            Log.Info($"Cart serving: player placed behind cart {id} at {spot}, opposite its {(side > 0 ? "right" : "left")} customer side, facing the window at {look}; idle stance held until walking or closing.");
        }

        // Nearest player-mesh point first. If that lands across the cart, search only the serving half-plane
        // and keep the nearest valid result to the wanted spot, within a quarter-metre sampling resolution.
        internal static bool ServingPoint(Vector3 wanted, Vector3 center, Vector3 outward, float clearance,
            NavMeshQueryFilter filter, out Vector3 spot, out bool fallback)
        {
            bool Valid(Vector3 p) => Vector3.Dot(center - p, outward) >= clearance - 0.01f &&
                                    Mathf.Abs(p.y - center.y) <= 0.5f && Vector3.Distance(p, wanted) <= FallbackMetres;
            if (NavMesh.SamplePosition(wanted, out var exact, 0.1f, filter) && Valid(exact.position))
            {
                spot = exact.position;
                fallback = false;
                return true;
            }
            Vector3? best = null;
            if (NavMesh.SamplePosition(wanted, out var nearest, FallbackMetres, filter) && Valid(nearest.position)) best = nearest.position;
            var along = Vector3.Cross(Vector3.up, outward);
            for (var back = clearance; back <= BehindMetres + FallbackMetres; back += SampleStep)
            for (var offset = -FallbackMetres; offset <= FallbackMetres; offset += SampleStep)
            {
                var probe = center - outward * back + along * offset;
                if (!NavMesh.SamplePosition(probe, out var hit, SampleStep, filter) || !Valid(hit.position)) continue;
                if (!best.HasValue || (hit.position - wanted).sqrMagnitude < (best.Value - wanted).sqrMagnitude) best = hit.position;
            }
            spot = best ?? wanted;
            fallback = true;
            return best.HasValue;
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
