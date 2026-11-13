#nullable enable
using System;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Helpers;
using Parking.UndergroundParking;
using PlayerActivity;
using UI.Load;
using UnityEngine;
using UnityEngine.AI;

namespace FoodTrucks
{
    internal sealed class CartServing
    {
        private PlayerController? player;
        private VehicleController? cart;
        private string? vehicleId;
        private Vector3 position;
        private Quaternion rotation;
        private Vector3 cartPosition;
        private Quaternion cartRotation;
        private double workAlong;

        internal string? VehicleId => vehicleId;

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
            var side = CartStance.Side(window?.Side ?? 1);
            if (window == null) Log.Info($"Cart serving: {id}: no visible queue side; using the cart's right window side.");
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            var lanes = TrafficLanes.Near(controller.transform.position, 30f);
            if (!ServingPoint(controller.transform, side, agent.radius, who.transform.position,
                filter, lanes, out var spot, out var placement, out var behindCart))
            {
                Log.Info($"Cart serving: {id}: no safe reachable serving point behind the counter; player left where they stand.");
                return;
            }
            var centre = CartStanceWorld.WorkAlong(controller.transform);
            var look = behindCart && CartStance.FacesAcross(CartStanceWorld.Along(controller.transform, spot), centre)
                ? spot + CartStanceWorld.Facing(controller.transform, side)
                : window?.Window ?? CartStanceWorld.Point(controller.transform, CartStance.Window(side, CartStance.WindowOut, centre));
            var facing = look - spot;
            facing.y = 0f;
            var turn = Quaternion.LookRotation(facing);
            var target = new GameObject("FoodTrucks.CartServingTarget");
            try
            {
                target.transform.SetPositionAndRotation(spot, turn);
                who.ResetNavigation();
                PlayerHelper.Teleport(target.transform);
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
            workAlong = centre;
            who.Character.velocity = Vector3.zero;
            agent.velocity = Vector3.zero;
            who.PlayerChangedNavigation.AddListener(OnNavigation);
            CartQueueView.Recheck();
            Log.Info($"Cart serving: {id}: player placed {placement} at {spot}, queue on the {(side > 0 ? "right" : "left")}, facing the window at {look}; pedestrian ground, off street, complete player path (agent {filter.agentTypeID}); idle stance held until walking or closing.");
        }

        internal static bool ServingPoint(Transform cart, int side, float radius, Vector3 start,
            NavMeshQueryFilter filter, TrafficLanes lanes, out Vector3 spot, out string placement, out bool behindCart)
        {
            var path = new NavMeshPath();
            return CartGroundPlacement.Find(cart, side, radius, CartStance.Server(side, along: CartStanceWorld.WorkAlong(cart)), filter, lanes,
                p => NavMesh.CalculatePath(start, p, filter, path) && path.status == NavMeshPathStatus.PathComplete,
                out spot, out placement, out behindCart);
        }

        private void OnNavigation()
        {
            if (vehicleId != null) ServingWork.BeforeLeaving(vehicleId);
            Release("player started walking or interacting");
        }

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
                    StreetSales.Find(FoodTrucksSession.CurrentState, vehicleId)?.IsOpen != true || (CartSelling.Skipping && !ServingWork.IsSkipping(vehicleId)) ||
                    !string.IsNullOrEmpty(SaveGameManager.Current?.ActiveVehicleId) || BuildingManager.IsInsideBuilding ||
                    UndergroundParkingManager.IsInsideParking || agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh ||
                    Vector3.Distance(controller.transform.position, cartPosition) > 0.1f || Quaternion.Angle(controller.transform.rotation, cartRotation) > 1f ||
                    Math.Abs(CartStanceWorld.WorkAlong(controller.transform) - workAlong) > 0.01 ||
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
