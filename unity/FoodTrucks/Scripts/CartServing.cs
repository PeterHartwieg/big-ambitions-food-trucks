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
    // The cart's idle serving stance, owned by CartSelling. Unlike the enclosed truck hatch, this is on
    // walkable ground: keep the live player agent/collider and let the game's next navigation command release
    // the stance. Never use ThirdPersonCharacter.ForceToPosition for the player: it leaves physics kinematic
    // and the agent's updatePosition/updateRotation off (1.0). No attachment or navigation blocker is needed.
    // The place is Core's CartStance: behind the cart straight across from the cash register, facing the customers,
    // worked out in the cart's frame and carried into the world by its live transform, as WindowCrew does for the crew.
    internal sealed class CartServing
    {
        private PlayerController? player;
        private VehicleController? cart;
        private string? vehicleId;
        private Vector3 position;
        private Quaternion rotation;
        private Vector3 cartPosition;
        private Quaternion cartRotation;

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
                Log.Info($"Cart serving: {id}: no safe reachable serving point behind the cart or at either end; player left where they stand.");
                return;
            }
            // Behind the cart level with the register: straight across the counter at the customers. Further along
            // (a fallback past an obstacle) or at an end: towards the first customer.
            var look = behindCart && CartStance.FacesAcross(CartStanceWorld.Along(controller.transform, spot))
                ? spot + CartStanceWorld.Facing(controller.transform, side)
                : window?.Window ?? CartStanceWorld.Point(controller.transform, CartStance.Window(side, CartStance.WindowOut));
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

        // Prefer the opposite side across from the register, then the front and rear ends beside the window. A
        // kerbside cart's opposite side can be the road: player-mesh membership alone is not evidence of pavement.
        // Every result must also be on the NPC mesh, pass the queue's street exclusions and have a complete path
        // from the live player. behindCart: the spot is on the side opposite the customers.
        internal static bool ServingPoint(Transform cart, int side, float radius, Vector3 start,
            NavMeshQueryFilter filter, TrafficLanes lanes, out Vector3 spot, out string placement, out bool behindCart)
        {
            var path = new NavMeshPath();
            return CartGroundPlacement.Find(cart, side, radius, CartStance.Server(side), filter, lanes,
                p => NavMesh.CalculatePath(start, p, filter, path) && path.status == NavMeshPathStatus.PathComplete,
                out spot, out placement, out behindCart);
        }

        // PlayerController emits this for both click/SetGoal navigation and WASD starting. Release without
        // ResetNavigation: the new destination/action belongs to the game and must be allowed to finish.
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
