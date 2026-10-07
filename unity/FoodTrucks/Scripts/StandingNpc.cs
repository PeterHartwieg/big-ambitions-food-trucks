#nullable enable
using UnityEngine;
using UnityEngine.AI;

namespace FoodTrucks
{
    // A pooled game character that walks to a point and stays there until destroyed.
    // Spike 5; the queue view in Milestone 3 builds on it.
    internal sealed class StandingNpc : MonoBehaviour
    {
        private ThirdPersonCharacterPool? pool;
        private ThirdPersonCharacter? character;
        private Quaternion finalFacing;
        private bool walking;

        public static StandingNpc? Create(Vector3 spawn, Vector3 destination, Quaternion facing)
        {
            var owner = new GameObject("FoodTrucks.StandingNpc");
            owner.transform.position = spawn;
            var npc = owner.AddComponent<StandingNpc>();
            if (npc.Initialize(spawn, destination, facing)) return npc;
            Destroy(owner);
            return null;
        }

        private bool Initialize(Vector3 spawn, Vector3 destination, Quaternion facing)
        {
            pool = ScriptableObject.CreateInstance<ThirdPersonCharacterPool>();
            pool.CreatePool(transform);
            character = pool.GetPoolHandler().Get();
            character.isPlayer = false;
            character.appearanceSetter.SetRandomAppearance();

            var agent = character.navmeshAgent;
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
            if (!NavMesh.SamplePosition(spawn, out var start, 2f, filter) ||
                !NavMesh.SamplePosition(destination, out var end, 2f, filter) ||
                !agent.Warp(start.position))
            {
                Log.Error($"Test customer: no NavMesh near {spawn} or {destination}.");
                return false;
            }

            agent.isStopped = false;
            agent.ResetPath();
            character.SetWalkingSpeed(ThirdPersonCharacter.WalkingSpeed.Walk);
            var path = new NavMeshPath();
            if (!agent.CalculatePath(end.position, path) || path.status != NavMeshPathStatus.PathComplete || !agent.SetPath(path))
            {
                Log.Error("Test customer: no complete path.");
                return false;
            }

            finalFacing = facing;
            walking = true;
            Log.Info($"Test customer walking {Vector3.Distance(start.position, end.position):F1} m.");
            return true;
        }

        private void FixedUpdate()
        {
            if (!walking || character == null) return;
            var agent = character.navmeshAgent;
            if (!agent.isOnNavMesh || agent.pathPending) return;
            if (agent.hasPath && agent.remainingDistance > agent.stoppingDistance + 0.05f) return;

            agent.isStopped = true;
            agent.ResetPath();
            agent.velocity = Vector3.zero;
            character.LookTarget = Vector3.zero;
            character.Move(Vector3.zero);
            character.transform.rotation = finalFacing;
            walking = false;
            Log.Info("Test customer arrived and is standing.");
        }

        private void OnDestroy()
        {
            if (pool == null) return;
            if (character != null) pool.GetPoolHandler().Release(character);
            pool.Dispose();
            Destroy(pool);
        }
    }
}
