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

        public CombatTarget CurrentTarget { get; private set; }
        public bool IsLocked => CurrentTarget != null && CurrentTarget.IsAvailable;

        public void Configure(PlayerInputReader input, Transform cameraTransform)
        {
            _input = input;
            _cameraTransform = cameraTransform;
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

            CombatTarget best = null;
            float bestScore = float.MaxValue;
            Vector3 cameraForward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;

            for (int i = 0; i < count; i++)
            {
                CombatTarget candidate = _candidateBuffer[i].GetComponentInParent<CombatTarget>();
                if (candidate == null || candidate.gameObject == gameObject || candidate == CurrentTarget || !candidate.IsAvailable)
                {
                    continue;
                }

                Vector3 toCandidate = candidate.AimPoint.position - transform.position;
                Vector3 flatDirection = Vector3.ProjectOnPlane(toCandidate, Vector3.up).normalized;
                float viewAngle = Vector3.Angle(cameraForward, flatDirection);
                if (viewAngle > _maximumViewAngle)
                {
                    continue;
                }

                float signed = Vector3.SignedAngle(cameraForward, flatDirection, Vector3.up);
                if (switchDirection != 0 && Mathf.Sign(signed) != switchDirection)
                {
                    continue;
                }

                float score = switchDirection == 0
                    ? (viewAngle * 0.12f) + toCandidate.magnitude
                    : Mathf.Abs(signed);
                if (score < bestScore)
                {
                    bestScore = score;
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
