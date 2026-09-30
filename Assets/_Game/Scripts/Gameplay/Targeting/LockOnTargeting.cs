using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using UnityEngine;

namespace Emberfall.Gameplay.Targeting
{
    [DefaultExecutionOrder(-150)]
    public sealed class LockOnTargeting : MonoBehaviour
    {
        private const int CandidateBufferSize = 32;

        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private Transform _cameraTransform;
        [SerializeField, Min(1f)] private float _searchRadius = 18f;
        [SerializeField, Range(10f, 180f)] private float _maximumViewAngle = 75f;

        private readonly Collider[] _candidateBuffer = new Collider[CandidateBufferSize];
        private float _switchCooldown;
        private Camera _camera;

        public CombatTarget CurrentTarget { get; private set; }
        public bool IsLocked => CurrentTarget != null && CurrentTarget.IsAvailable;

        public void Configure(PlayerInputReader input, Transform cameraTransform)
        {
            _input = input;
            _cameraTransform = cameraTransform;
            _camera = cameraTransform != null ? cameraTransform.GetComponent<Camera>() : null;
        }

        private void Update()
        {
            _switchCooldown = Mathf.Max(0f, _switchCooldown - Time.deltaTime);
            if (CurrentTarget != null && !CurrentTarget.IsAvailable)
            {
                CurrentTarget = null;
            }

            if (_input == null || _cameraTransform == null)
            {
                return;
            }

            if (_input.ConsumeLockOnPressed())
            {
                CurrentTarget = IsLocked ? null : FindBestTarget(0);
            }

            float switchInput = _input.SwitchTarget;
            if (IsLocked && _switchCooldown <= 0f && Mathf.Abs(switchInput) > 0.5f)
            {
                CombatTarget switched = FindBestTarget(switchInput > 0f ? 1 : -1);
                if (switched != null)
                {
                    CurrentTarget = switched;
                }

                _switchCooldown = 0.35f;
            }
        }

        private CombatTarget FindBestTarget(int switchDirection)
        {
            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                _searchRadius,
                _candidateBuffer,
                ~0,
                QueryTriggerInteraction.Collide);
            Collider[] candidates = _candidateBuffer;
            if (count == _candidateBuffer.Length)
            {
                // Walls/triggers also occupy broadphase slots. Never silently drop enemies in dense areas.
                // This fallback allocates only on a lock/switch request, not on every Update.
                candidates = Physics.OverlapSphere(transform.position, _searchRadius, ~0, QueryTriggerInteraction.Collide);
                count = candidates.Length;
            }

            CombatTarget best = null;
            float bestScore = float.MaxValue;
            float bestDistance = float.MaxValue;
            if (_camera == null && _cameraTransform != null) _camera = _cameraTransform.GetComponent<Camera>();
            if (_camera == null) return null;
            Vector3 cameraForward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;
            Vector3 currentViewport = CurrentTarget != null
                ? _camera.WorldToViewportPoint(CurrentTarget.AimPoint.position)
                : new Vector3(.5f, .5f, 1f);

            for (int i = 0; i < count; i++)
            {
                CombatTarget candidate = candidates[i].GetComponentInParent<CombatTarget>();
                if (candidate == null || candidate.gameObject == gameObject || candidate == CurrentTarget ||
                    !candidate.isActiveAndEnabled || !candidate.IsAvailable || candidate.AimPoint == null)
                {
                    continue;
                }

                Vector3 toCandidate = candidate.AimPoint.position - transform.position;
                if (toCandidate.sqrMagnitude > _searchRadius * _searchRadius) continue;
                Vector3 viewport = _camera.WorldToViewportPoint(candidate.AimPoint.position);
                if (viewport.z <= _camera.nearClipPlane || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f)
                    continue;
                Vector3 flatDirection = Vector3.ProjectOnPlane(toCandidate, Vector3.up).normalized;
                float viewAngle = Vector3.Angle(cameraForward, flatDirection);
                if (viewAngle > _maximumViewAngle)
                {
                    continue;
                }

                float horizontalOffset = viewport.x - currentViewport.x;
                if (switchDirection != 0 && horizontalOffset * switchDirection <= .001f)
                {
                    continue;
                }

                // Rank by pixel-space distance to screen centre; world distance only breaks ties.
                // Switching instead ranks visible neighbours relative to the CURRENT target.
                Vector2 offset = switchDirection == 0
                    ? new Vector2((viewport.x - .5f) * _camera.aspect, viewport.y - .5f)
                    : new Vector2(horizontalOffset * _camera.aspect, viewport.y - currentViewport.y);
                float score = offset.sqrMagnitude;
                float distance = toCandidate.sqrMagnitude;
                if (score < bestScore - .000001f ||
                    (Mathf.Abs(score - bestScore) <= .000001f &&
                        (distance < bestDistance || (distance == bestDistance && best != null && candidate.CombatantId < best.CombatantId))))
                {
                    bestScore = score;
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, _searchRadius);
        }
    }
}
