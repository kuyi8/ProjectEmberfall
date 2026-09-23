using UnityEngine;
using UnityEngine.AI;

namespace Emberfall.AI.Unity
{
    /// <summary>Offline encounter spatial policy; never resets health or owns progression.</summary>
    [DisallowMultipleComponent]
    public sealed class EncounterLeash : MonoBehaviour
    {
        [SerializeField] private CombatEncounterCoordinator _encounter;
        private NavMeshAgent _agent;
        private NavMeshPath _path;
        private readonly Vector3[] _corners = new Vector3[64];
        private Vector3 _lastInside;
        private bool _hasInside;
        private Vector3 _lastRequested;
        private float _nextPathTime;
        private bool _hasRequest;

        public CombatEncounterCoordinator Encounter => _encounter;
        public void Configure(CombatEncounterCoordinator encounter) => _encounter = encounter;
        private void Awake()
        {
            _agent = GetComponent<NavMeshAgent>();
            _path = new NavMeshPath();
        }

        public bool AllowsTarget(Vector3 position) => _encounter == null || Contains(position, -_encounter.TelemetryActivationMargin);

        public bool Contains(Vector3 position, float inset = 0f)
        {
            if (_encounter == null) return true;
            Vector3 offset = position - _encounter.ArenaCenter;
            Vector2 half = _encounter.ArenaHalfExtents;
            return Mathf.Abs(offset.x) <= half.x - inset && Mathf.Abs(offset.z) <= half.y - inset;
        }

        public Vector3 ClampDestination(Vector3 position)
        {
            if (_encounter == null) return position;
            Vector3 center = _encounter.ArenaCenter;
            Vector2 half = _encounter.ArenaHalfExtents;
            float inset = _agent == null ? 0.6f : _agent.radius + 0.2f;
            position.x = Mathf.Clamp(position.x, center.x - half.x + inset, center.x + half.x - inset);
            position.z = Mathf.Clamp(position.z, center.z - half.y + inset, center.z + half.y - inset);
            return position;
        }

        public bool SetDestination(Vector3 desired)
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return false;
            desired = ClampDestination(desired);
            // Reuse validated steering between bounded replans instead of calculating and
            // replacing the full path on every render frame.
            if (_hasRequest && Time.time < _nextPathTime &&
                (desired - _lastRequested).sqrMagnitude < 0.25f && _agent.hasPath)
            {
                _agent.isStopped = false;
                return true;
            }
            _lastRequested = desired;
            _nextPathTime = Time.time + 0.2f;
            _hasRequest = true;
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 0.8f, _agent.areaMask) &&
                Contains(hit.position, _agent.radius) && _agent.CalculatePath(hit.position, _path) &&
                _path.status == NavMeshPathStatus.PathComplete)
            {
                int count = _path.GetCornersNonAlloc(_corners);
                bool inside = count > 0 && count < _corners.Length;
                for (int i = 0; i < count; i++) inside &= Contains(_corners[i]);
                if (inside)
                {
                    _agent.isStopped = false;
                    return _agent.SetPath(_path);
                }
            }
            _agent.isStopped = true;
            _agent.ResetPath();
            return false;
        }

        public void ApplyDisplacement(Vector3 desired)
        {
            if (_agent == null || !_agent.enabled || !_agent.isOnNavMesh) return;
            desired = ClampDestination(desired);
            // NavMesh raycast clips the impulse at obstacles; no teleport through a wall.
            if (_agent.Raycast(desired, out NavMeshHit obstruction))
                desired = Vector3.MoveTowards(obstruction.position, transform.position, 0.05f);
            if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 0.25f, _agent.areaMask) && Contains(hit.position))
                _agent.Warp(hit.position);
        }

        private void LateUpdate()
        {
            if (_encounter == null || !_encounter.isActiveAndEnabled || _agent == null ||
                !_agent.enabled || !_agent.isOnNavMesh) return;
            if (Contains(transform.position))
            {
                _lastInside = transform.position;
                _hasInside = true;
            }
            else if (_hasInside)
            {
                // Agent avoidance can drift across a bound even when all path corners are inside.
                _agent.Warp(_lastInside);
                _agent.ResetPath();
            }
        }
    }
}
