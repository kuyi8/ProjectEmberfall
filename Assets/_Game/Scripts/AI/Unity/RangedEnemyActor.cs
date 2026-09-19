using System;
using Emberfall.AI.Data;
using Emberfall.AI.Domain;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;
using UnityEngine.AI;

namespace Emberfall.AI.Unity
{
    [DefaultExecutionOrder(-120)]
    public sealed class RangedEnemyActor : CombatTarget, IExecutionTarget
    {
        [SerializeField] private TextAsset _definitionJson;
        [SerializeField] private string _enemyId = "enemy:rune-priest";
        [SerializeField] private PlayerCombatActor _target;
        [SerializeField] private NavMeshAgent _agent;
        [SerializeField] private Transform _aimPoint;
        [SerializeField] private Transform _castOrigin;
        [SerializeField] private Transform _telegraphRoot;
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private Collider _bodyCollider;
        [SerializeField] private Material _projectileMaterial;
        [SerializeField] private bool _autoResetAfterDelay = true;
        [SerializeField, Min(0.02f)] private float _perceptionInterval = 0.14f;

        private RangedEnemyDefinition _definition;
        private RangedEnemyBrain _brain;
        private RangedEnemyPerception _perception;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Color _baseColor;
        private float _perceptionRemaining;
        private float _flashRemaining;
        private int _releasedAttackSequence;
        private bool _executionClaimed;
        private float _executionHoldRemaining;

        public override int CombatantId => GetInstanceID();
        public override Transform AimPoint => _aimPoint != null ? _aimPoint : transform;
        public override bool IsAvailable => _brain != null && _brain.State != RangedEnemyState.Dead;
        public override float HealthNormalized => _brain?.Health.Normalized ?? 0f;
        public override bool HasSecondaryResource => true;
        public override float SecondaryResourceNormalized => _brain?.Posture.Normalized ?? 0f;
        public override bool IsThreatening => State == RangedEnemyState.Windup || State == RangedEnemyState.Release;
        public RangedEnemyDefinition Definition => _definition;
        public RangedEnemyBrain Brain => _brain;
        public RangedEnemyState State => _brain?.State ?? RangedEnemyState.Idle;
        public float HorizontalSpeed => _agent != null ? Vector3.ProjectOnPlane(_agent.velocity, Vector3.up).magnitude : 0f;
        public bool HasSimulationAuthority { get; private set; } = true;
        public string LastAiEvent { get; private set; } = "Idle";
        public CombatTarget CombatTarget => this;
        public ExecutionTargetKind ExecutionKind => ExecutionTargetKind.Ordinary;
        public bool IsExecutionEligible => ExecutionRules.IsEligible(
            ExecutionKind, HealthNormalized, false, _executionClaimed);
        public float ExecutionDamage => ExecutionRules.OrdinaryDamage;

        public event Action<RangedEnemyActor> Died;

        public void Configure(
            TextAsset definitionJson,
            string enemyId,
            PlayerCombatActor target,
            NavMeshAgent agent,
            Transform aimPoint,
            Transform castOrigin,
            Transform telegraphRoot,
            Renderer bodyRenderer,
            Collider bodyCollider,
            Material projectileMaterial)
        {
            _definitionJson = definitionJson;
            _enemyId = enemyId;
            _target = target;
            _agent = agent;
            _aimPoint = aimPoint;
            _castOrigin = castOrigin;
            _telegraphRoot = telegraphRoot;
            _bodyRenderer = bodyRenderer;
            _bodyCollider = bodyCollider;
            _projectileMaterial = projectileMaterial;
        }

        public void SetSimulationAuthority(bool hasAuthority)
        {
            HasSimulationAuthority = hasAuthority;
            if (!hasAuthority)
            {
                StopAgent();
            }
        }

        public void SetAutoResetAfterDelay(bool enabled)
        {
            _autoResetAfterDelay = enabled;
        }

        private void Awake()
        {
            if (_target == null || _agent == null ||
                _castOrigin == null || _bodyCollider == null || _projectileMaterial == null ||
                !ContentId.TryCreate(_enemyId, out ContentId enemyId))
            {
                Debug.LogError("RangedEnemyActor is not configured.", this);
                enabled = false;
                return;
            }

            _definition = RangedEnemyDefinitionJsonLoader.Load(LoadDefinitionJson()).GetRequired(enemyId);
            _brain = new RangedEnemyBrain(_definition);
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _agent.speed = _definition.MoveSpeed;
            _agent.angularSpeed = _definition.RotationSpeed;
            _agent.stoppingDistance = 0.2f;
            _agent.updateRotation = false;
            if (_bodyRenderer != null)
            {
                _baseColor = _bodyRenderer.material.color;
            }

            SetTelegraphVisible(false);
            RefreshPerception();
        }

        private string LoadDefinitionJson()
        {
            if (ContentPackageRuntime.IsInitialized)
                return ContentPackageRuntime.GetRequiredText(RuntimeContentPaths.Enemies);
#if UNITY_EDITOR
            if (_definitionJson != null)
                return _definitionJson.text;
#endif
            throw new InvalidOperationException("Ranged enemy content requires an initialized runtime package.");
        }

        private void Start()
        {
            WarpToSpawn();
        }

        private void Update()
        {
            if (!HasSimulationAuthority || _brain == null)
            {
                return;
            }

            if (_executionHoldRemaining > 0f)
            {
                _executionHoldRemaining = Mathf.Max(0f, _executionHoldRemaining - Time.deltaTime);
                StopAgent();
                UpdatePresentation();
                return;
            }

            _perceptionRemaining -= Time.deltaTime;
            if (_perceptionRemaining <= 0f)
            {
                RefreshPerception();
                _perceptionRemaining = _perceptionInterval;
            }

            RangedEnemyState previousState = _brain.State;
            _brain.Tick(Time.deltaTime, _perception);
            if (_autoResetAfterDelay && _brain.IsResetReady)
            {
                ResetToSpawn();
                UpdatePresentation();
                return;
            }

            if (previousState != _brain.State)
            {
                HandleStateChanged(previousState, _brain.State);
            }

            DriveMovement();
            if (_brain.IsAttackReleaseOpen && _releasedAttackSequence != _brain.AttackSequence)
            {
                ReleaseAttack();
            }

            UpdatePresentation();
        }

        public override DamageResult ReceiveDamage(DamageRequest request)
        {
            if (!HasSimulationAuthority || _brain == null)
            {
                return DamageResult.Ignored;
            }

            RangedEnemyState previousState = _brain.State;
            DamageResult result = _brain.ReceiveDamage(request);
            _flashRemaining = 0.12f;
            if (previousState != _brain.State)
            {
                HandleStateChanged(previousState, _brain.State);
            }

            return result;
        }

        public override float ApplyNeutralPostureDamage(float amount)
        {
            if (!HasSimulationAuthority || _brain == null || !IsAvailable) return 0f;
            float before = _brain.Posture.Current;
            RangedEnemyState previous = _brain.State;
            bool staggered = _brain.ApplyCounterPosture(amount);
            _flashRemaining = 0.16f;
            LastAiEvent = staggered ? "Neutral posture break" : "Neutral posture hit";
            if (previous != _brain.State) HandleStateChanged(previous, _brain.State);
            return before - _brain.Posture.Current;
        }

        public void ResetToSpawn()
        {
            if (_brain == null)
            {
                return;
            }

            _brain.Reset();
            _executionClaimed = false;
            _executionHoldRemaining = 0f;
            _releasedAttackSequence = 0;
            RestoreBodyAtSpawn();
            LastAiEvent = "Reset";
        }

        public bool TryClaimExecution()
        {
            if (!IsExecutionEligible) return false;
            _executionClaimed = true;
            return true;
        }

        public void HoldForExecution(float seconds)
        {
            _executionHoldRemaining = Mathf.Max(_executionHoldRemaining, seconds);
            StopAgent();
        }

        private void RefreshPerception()
        {
            bool targetAvailable = _target != null && _target.IsAvailable;
            float distanceToTarget = targetAvailable
                ? Vector3.Distance(transform.position, _target.transform.position)
                : float.PositiveInfinity;
            float distanceToSpawn = Vector3.Distance(transform.position, _spawnPosition);
            _perception = new RangedEnemyPerception(
                targetAvailable,
                targetAvailable && CanSeeTarget(),
                distanceToTarget,
                distanceToSpawn);
        }

        private bool CanSeeTarget()
        {
            Vector3 origin = AimPoint.position;
            Vector3 direction = _target.AimPoint.position - origin;
            Vector3 flatDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flatDirection.sqrMagnitude > 0.001f &&
                Vector3.Angle(transform.forward, flatDirection) > _definition.FieldOfView * 0.5f)
            {
                return false;
            }

            if (!Physics.Raycast(origin, direction.normalized, out RaycastHit hit, direction.magnitude, ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return hit.collider.GetComponentInParent<PlayerCombatActor>() == _target;
        }

        private void DriveMovement()
        {
            if (_brain.State == RangedEnemyState.Dead || _brain.State == RangedEnemyState.HitReact)
            {
                StopAgent();
                return;
            }

            if (_agent.isOnNavMesh && _brain.WantsTargetMovement && _target != null)
            {
                _agent.stoppingDistance = _definition.PreferredMaximumRange * 0.9f;
                _agent.isStopped = false;
                _agent.SetDestination(_target.transform.position);
            }
            else if (_agent.isOnNavMesh && _brain.WantsRetreatMovement && _target != null)
            {
                Vector3 away = Vector3.ProjectOnPlane(transform.position - _target.transform.position, Vector3.up);
                Vector3 desired = transform.position + (away.sqrMagnitude > 0.001f ? away.normalized : -transform.forward) * 3f;
                _agent.stoppingDistance = 0.1f;
                if (NavMesh.SamplePosition(desired, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                {
                    _agent.isStopped = false;
                    _agent.SetDestination(hit.position);
                }
                else
                {
                    StopAgent();
                }
            }
            else if (_agent.isOnNavMesh && _brain.WantsReturnMovement)
            {
                _agent.stoppingDistance = 0.15f;
                _agent.isStopped = false;
                _agent.SetDestination(_spawnPosition);
            }
            else
            {
                StopAgent();
            }

            Vector3 facing = _brain.WantsFaceTarget && _target != null
                ? _target.transform.position - transform.position
                : _agent.velocity;
            facing = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (facing.sqrMagnitude > 0.001f)
            {
                Quaternion desired = Quaternion.LookRotation(facing.normalized, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    desired,
                    _definition.RotationSpeed * Time.deltaTime);
            }
        }

        private void ReleaseAttack()
        {
            _releasedAttackSequence = _brain.AttackSequence;
            if (_target == null || !_target.IsAvailable)
            {
                LastAiEvent = "Projectile cancelled";
                return;
            }

            if (_brain.CurrentAttack == RangedAttackKind.GroundRune)
            {
                ReleaseGroundRune();
                return;
            }

            Vector3 direction = (_target.AimPoint.position - _castOrigin.position).normalized;
            GameObject projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = $"RuneProjectile_{_brain.AttackSequence}";
            projectile.transform.position = _castOrigin.position;
            projectile.transform.localScale = Vector3.one * (_definition.ProjectileRadius * 1.08f);
            projectile.GetComponent<Renderer>().sharedMaterial = _projectileMaterial;
            Destroy(projectile.GetComponent<Collider>());
            TrailRenderer trail = projectile.AddComponent<TrailRenderer>();
            trail.sharedMaterial = _projectileMaterial;
            trail.time = 0.22f;
            trail.startWidth = _definition.ProjectileRadius * 0.82f;
            trail.endWidth = 0.015f;
            trail.minVertexDistance = 0.04f;
            trail.startColor = new Color(0.72f, 0.28f, 1f, 0.92f);
            trail.endColor = new Color(0.2f, 0.72f, 1f, 0f);
            RangedProjectile component = projectile.AddComponent<RangedProjectile>();
            component.Launch(
                transform,
                CombatantId,
                _brain.AttackSequence,
                direction,
                _definition.ProjectileSpeed,
                _definition.ProjectileRadius,
                _definition.ProjectileLifetime,
                _definition.AttackDamage,
                _definition.PostureDamage,
                HasSimulationAuthority,
                OnProjectileResolved);
            LastAiEvent = "Projectile released";
        }

        private void ReleaseGroundRune()
        {
            Vector3 center = FindGroundRuneCenter(_target.transform.position);

            GameObject rune = new GameObject();
            rune.name = $"DelayedGroundRune_{_brain.AttackSequence}";
            rune.transform.position = center;
            rune.AddComponent<RangedGroundRune>().Configure(
                CombatantId,
                _brain.AttackSequence,
                _definition.GroundRuneTriggerDelay,
                _definition.GroundRuneRadius,
                _definition.GroundRuneDamage,
                _definition.GroundRunePostureDamage,
                _projectileMaterial,
                HasSimulationAuthority,
                OnGroundRuneResolved);
            LastAiEvent = "Ground rune armed";
        }

        private Vector3 FindGroundRuneCenter(Vector3 targetPosition)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                targetPosition + Vector3.up * 3f,
                Vector3.down,
                8f,
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.GetComponentInParent<PlayerCombatActor>() != null ||
                    hit.collider.GetComponentInParent<CombatTarget>() != null)
                {
                    continue;
                }

                return hit.point + Vector3.up * 0.04f;
            }

            return targetPosition;
        }

        private void OnGroundRuneResolved(DamageResult result)
        {
            LastAiEvent = result.Invulnerable ? "Ground rune evaded" :
                result.Accepted ? "Ground rune hit" : "Ground rune expired";
        }

        private void OnProjectileResolved(DamageResult result)
        {
            LastAiEvent = result.PerfectGuard ? "Projectile perfect guarded" :
                result.Defended ? "Projectile guarded" :
                result.Invulnerable ? "Projectile evaded" :
                result.Accepted ? "Projectile hit" : "Projectile blocked";
        }

        private void HandleStateChanged(RangedEnemyState previous, RangedEnemyState current)
        {
            LastAiEvent = current == RangedEnemyState.Windup
                ? $"Windup · {_brain.CurrentAttack}"
                : current.ToString();
            if (current == RangedEnemyState.Dead)
            {
                StopAgent();
                _bodyCollider.enabled = false;
                Died?.Invoke(this);
            }
            else if (previous == RangedEnemyState.Dead)
            {
                RestoreBodyAtSpawn();
            }
        }

        private void RestoreBodyAtSpawn()
        {
            _bodyCollider.enabled = true;
            WarpToSpawn();
            transform.rotation = _spawnRotation;
            RefreshPerception();
        }

        private void WarpToSpawn()
        {
            if (_agent != null && _agent.enabled &&
                NavMesh.SamplePosition(_spawnPosition, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                _agent.Warp(hit.position);
            }
            else
            {
                transform.position = _spawnPosition;
            }
        }

        private void StopAgent()
        {
            if (_agent != null && _agent.enabled && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }
        }

        private void UpdatePresentation()
        {
            bool telegraphVisible = _brain.State == RangedEnemyState.Windup ||
                                    _brain.State == RangedEnemyState.Release;
            SetTelegraphVisible(telegraphVisible);
            if (telegraphVisible && _telegraphRoot != null)
            {
                float normalized = _brain.State == RangedEnemyState.Windup
                    ? Mathf.Clamp01(_brain.StateElapsed / _brain.CurrentWindupDuration)
                    : 1f;
                float specialScale = _brain.CurrentAttack == RangedAttackKind.GroundRune ? 1.35f : 1f;
                _telegraphRoot.localScale = Vector3.one * Mathf.Lerp(0.35f, specialScale, normalized);
            }

            _flashRemaining = Mathf.Max(0f, _flashRemaining - Time.deltaTime);
            if (_bodyRenderer == null)
            {
                return;
            }

            Color target = _brain.State == RangedEnemyState.Dead
                ? new Color(0.08f, 0.06f, 0.1f)
                : _flashRemaining > 0f ? Color.white : _baseColor;
            _bodyRenderer.material.color = Color.Lerp(_bodyRenderer.material.color, target, Time.deltaTime * 16f);
        }

        private void SetTelegraphVisible(bool visible)
        {
            if (_telegraphRoot != null && _telegraphRoot.gameObject.activeSelf != visible)
            {
                _telegraphRoot.gameObject.SetActive(visible);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.65f, 0.25f, 1f, 0.6f);
            float minimum = _definition?.PreferredMinimumRange ?? 5f;
            float maximum = _definition?.PreferredMaximumRange ?? 9f;
            Gizmos.DrawWireSphere(transform.position, minimum);
            Gizmos.DrawWireSphere(transform.position, maximum);
        }
    }
}
