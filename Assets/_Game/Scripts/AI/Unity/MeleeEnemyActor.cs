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
    public sealed class MeleeEnemyActor : CombatTarget, IExecutionTarget
    {
        private const int HitBufferSize = 12;

        [SerializeField] private TextAsset _definitionJson;
        [SerializeField] private string _enemyId = "enemy:fogwalker";
        [SerializeField] private PlayerCombatActor _target;
        [SerializeField] private NavMeshAgent _agent;
        [SerializeField] private Transform _aimPoint;
        [SerializeField] private Transform _attackOrigin;
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private Collider _bodyCollider;
        [SerializeField] private bool _autoResetAfterDelay = true;
        [SerializeField, Min(0.1f)] private float _attackRadius = 1.05f;
        [SerializeField, Min(0.02f)] private float _perceptionInterval = 0.12f;

        private readonly Collider[] _hitBuffer = new Collider[HitBufferSize];
        private readonly HitRegistry _hitRegistry = new HitRegistry();
        private MeleeEnemyDefinition _definition;
        private MeleeEnemyBrain _brain;
        private MeleeEnemyPerception _perception;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Color _baseColor;
        private float _perceptionRemaining;
        private float _flashRemaining;
        private bool _attackAllowed = true;
        private bool _hasSupportDestination;
        private Vector3 _supportDestination;
        private float _supportArrivalDistance = 0.65f;
        private bool _executionClaimed;
        private float _executionHoldRemaining;

        public override int CombatantId => GetInstanceID();
        public override Transform AimPoint => _aimPoint != null ? _aimPoint : transform;
        public override bool IsAvailable => _brain != null && _brain.State != MeleeEnemyState.Dead;
        public override float HealthNormalized => _brain?.Health.Normalized ?? 0f;
        public override bool HasSecondaryResource => true;
        public override float SecondaryResourceNormalized => _brain?.Posture.Normalized ?? 0f;
        public override bool IsThreatening => State == MeleeEnemyState.Windup || State == MeleeEnemyState.Attack;
        public MeleeEnemyDefinition Definition => _definition;
        public MeleeEnemyBrain Brain => _brain;
        public MeleeEnemyState State => _brain?.State ?? MeleeEnemyState.Idle;
        public float HorizontalSpeed => _agent != null ? Vector3.ProjectOnPlane(_agent.velocity, Vector3.up).magnitude : 0f;
        public bool HasSimulationAuthority { get; private set; } = true;
        public bool IsAttackSlotCommitted => State == MeleeEnemyState.Windup ||
            State == MeleeEnemyState.Attack || State == MeleeEnemyState.Recovery;
        public bool IsAttackSlotEligible => IsAttackSlotCommitted ||
            (State == MeleeEnemyState.Chase && _perception.TargetAvailable && _perception.CanSeeTarget &&
             _perception.DistanceToTarget <= (_definition?.AttackRange ?? 0f) + 2.8f);
        public string LastAiEvent { get; private set; } = "Idle";
        public bool IsGroupAttackAllowed => _attackAllowed;
        public bool HasSupportDestination => _hasSupportDestination;
        public Vector3 SupportDestination => _supportDestination;
        public CombatTarget CombatTarget => this;
        public ExecutionTargetKind ExecutionKind => ExecutionTargetKind.Ordinary;
        public bool IsExecutionEligible => ExecutionRules.IsEligible(
            ExecutionKind, HealthNormalized, false, _executionClaimed);
        public float ExecutionDamage => ExecutionRules.OrdinaryDamage;

        public event Action<MeleeEnemyActor> Died;

        public void Configure(
            TextAsset definitionJson,
            string enemyId,
            PlayerCombatActor target,
            NavMeshAgent agent,
            Transform aimPoint,
            Transform attackOrigin,
            Renderer bodyRenderer,
            Collider bodyCollider)
        {
            _definitionJson = definitionJson;
            _enemyId = enemyId;
            _target = target;
            _agent = agent;
            _aimPoint = aimPoint;
            _attackOrigin = attackOrigin;
            _bodyRenderer = bodyRenderer;
            _bodyCollider = bodyCollider;
        }

        public void SetSimulationAuthority(bool hasAuthority)
        {
            HasSimulationAuthority = hasAuthority;
            if (!hasAuthority)
            {
                StopAgent();
            }
        }

        private void Awake()
        {
            if (_target == null || _agent == null ||
                _attackOrigin == null || _bodyCollider == null ||
                !ContentId.TryCreate(_enemyId, out ContentId enemyId))
            {
                Debug.LogError("MeleeEnemyActor is not configured.", this);
                enabled = false;
                return;
            }

            _definition = MeleeEnemyDefinitionJsonLoader.Load(LoadDefinitionJson()).GetRequired(enemyId);
            _brain = new MeleeEnemyBrain(_definition);
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _agent.speed = _definition.MoveSpeed;
            _agent.angularSpeed = _definition.RotationSpeed;
            // Keep the navigation body outside the player's capsule so the two
            // silhouettes remain readable while the authored weapon still reaches.
            _agent.stoppingDistance = Mathf.Max(0.2f, _definition.AttackRange * 0.94f);
            _agent.updateRotation = false;
            if (_bodyRenderer != null)
            {
                _baseColor = _bodyRenderer.material.color;
            }

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
            throw new InvalidOperationException("Melee enemy content requires an initialized runtime package.");
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
                UpdatePresentationTint();
                return;
            }

            _perceptionRemaining -= Time.deltaTime;
            if (_perceptionRemaining <= 0f)
            {
                RefreshPerception();
                _perceptionRemaining = _perceptionInterval;
            }

            MeleeEnemyState previousState = _brain.State;
            var coordinatedPerception = new MeleeEnemyPerception(
                _perception.TargetAvailable,
                _perception.CanSeeTarget,
                _perception.DistanceToTarget,
                _perception.DistanceToSpawn,
                _attackAllowed);
            _brain.Tick(Time.deltaTime, coordinatedPerception);
            if (_autoResetAfterDelay && _brain.IsResetReady)
            {
                ResetToSpawn();
                UpdatePresentationTint();
                return;
            }

            if (previousState != _brain.State)
            {
                HandleStateChanged(previousState, _brain.State);
            }

            DriveMovement();
            if (_brain.IsDamageWindowOpen)
            {
                QueryAttackHits();
            }

            UpdatePresentationTint();
        }

        public override DamageResult ReceiveDamage(DamageRequest request)
        {
            if (!HasSimulationAuthority || _brain == null)
            {
                return DamageResult.Ignored;
            }

            MeleeEnemyState previousState = _brain.State;
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
            MeleeEnemyState previous = _brain.State;
            bool staggered = _brain.ApplyCounterPosture(amount);
            _flashRemaining = 0.16f;
            LastAiEvent = staggered ? "Neutral posture break" : "Neutral posture hit";
            if (previous != _brain.State) HandleStateChanged(previous, _brain.State);
            return before - _brain.Posture.Current;
        }

        public void SetGroupDirective(
            bool attackAllowed,
            bool hasSupportDestination,
            Vector3 supportDestination,
            int supportSide = 0,
            float supportArrivalDistance = 0.65f)
        {
            _attackAllowed = attackAllowed;
            _hasSupportDestination = hasSupportDestination;
            _supportDestination = supportDestination;
            _supportArrivalDistance = Mathf.Max(0.2f, supportArrivalDistance);
            if (_agent != null)
            {
                _agent.avoidancePriority = attackAllowed ? 24 : (supportSide < 0 ? 54 : 64);
            }
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
                ? Vector3.ProjectOnPlane(
                    _target.transform.position - transform.position,
                    Vector3.up).magnitude
                : float.PositiveInfinity;
            float distanceToSpawn = Vector3.Distance(transform.position, _spawnPosition);
            _perception = new MeleeEnemyPerception(
                targetAvailable,
                targetAvailable && CanSeeTarget(),
                distanceToTarget,
                distanceToSpawn);
        }

        private bool CanSeeTarget()
        {
            Vector3 origin = AimPoint.position;
            Vector3 targetPosition = _target.AimPoint.position;
            Vector3 direction = targetPosition - origin;
            Vector3 flatDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flatDirection.sqrMagnitude > 0.001f &&
                Vector3.Angle(transform.forward, flatDirection) > _definition.FieldOfView * 0.5f)
            {
                return false;
            }

            if (!Physics.Raycast(
                    origin,
                    direction.normalized,
                    out RaycastHit hit,
                    direction.magnitude,
                    ~0,
                    QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return hit.collider.GetComponentInParent<PlayerCombatActor>() == _target;
        }

        private void DriveMovement()
        {
            if (_brain.State == MeleeEnemyState.Dead || _brain.State == MeleeEnemyState.HitReact)
            {
                StopAgent();
                return;
            }

            if (_agent.isOnNavMesh && _brain.WantsTargetMovement && _target != null)
            {
                _agent.stoppingDistance = _hasSupportDestination && !_attackAllowed
                    ? _supportArrivalDistance
                    : Mathf.Max(0.2f, _definition.AttackRange * 0.94f);
                _agent.isStopped = false;
                _agent.SetDestination(_hasSupportDestination && !_attackAllowed
                    ? _supportDestination
                    : _target.transform.position);
            }
            else if (_agent.isOnNavMesh && _brain.WantsReturnMovement)
            {
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

        private void QueryAttackHits()
        {
            int count = Physics.OverlapSphereNonAlloc(
                _attackOrigin.position,
                _attackRadius * _brain.CurrentHitRadiusMultiplier,
                _hitBuffer,
                ~0,
                QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                PlayerCombatActor target = _hitBuffer[i].GetComponentInParent<PlayerCombatActor>();
                if (target == null || !target.IsAvailable ||
                    !_hitRegistry.TryRegister(_brain.CurrentHitSequence, target.CombatantId))
                {
                    continue;
                }

                DamageResult result = target.ReceiveDamage(new DamageRequest(
                    CombatantId,
                    _brain.CurrentHitSequence,
                    _brain.CurrentAttackDamage,
                    _brain.CurrentPostureDamage,
                    AttackTag.Light,
                    true,
                    DefenseArcUtility.IsThreatInFrontArc(target.transform, transform.position)));
                if (result.PerfectGuard)
                {
                    bool staggered = _brain.ApplyCounterPosture(result.CounterPostureDamage);
                    LastAiEvent = staggered ? "Perfect guard stagger" : "Perfect guard deflected";
                }
                else
                {
                    LastAiEvent = result.Defended ? "Attack guarded" :
                        result.Invulnerable ? "Attack evaded" :
                        result.Accepted ? "Attack hit" : "Attack missed";
                }
            }
        }

        private void HandleStateChanged(MeleeEnemyState previous, MeleeEnemyState current)
        {
            LastAiEvent = current == MeleeEnemyState.Windup
                ? $"Windup · {_brain.CurrentAttack}"
                : current.ToString();
            if (current == MeleeEnemyState.Dead)
            {
                StopAgent();
                _bodyCollider.enabled = false;
                Died?.Invoke(this);
            }
            else if (previous == MeleeEnemyState.Dead)
            {
                RestoreBodyAtSpawn();
            }
        }

        public void SetAutoResetAfterDelay(bool enabled)
        {
            _autoResetAfterDelay = enabled;
        }

        private void RestoreBodyAtSpawn()
        {
            _bodyCollider.enabled = true;
            _hitRegistry.Clear();
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

        private void UpdatePresentationTint()
        {
            _flashRemaining = Mathf.Max(0f, _flashRemaining - Time.deltaTime);
            if (_bodyRenderer == null)
            {
                return;
            }

            Color target = _brain.State == MeleeEnemyState.Dead
                ? new Color(0.08f, 0.09f, 0.08f)
                : _flashRemaining > 0f ? Color.white : _baseColor;
            _bodyRenderer.material.color = Color.Lerp(
                _bodyRenderer.material.color,
                target,
                Time.deltaTime * 16f);
        }

        private void OnDrawGizmosSelected()
        {
            if (_attackOrigin != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(_attackOrigin.position, _attackRadius);
            }

            Gizmos.color = new Color(0.9f, 0.7f, 0.1f, 0.5f);
            float detection = _definition?.DetectionRange ?? 9f;
            Gizmos.DrawWireSphere(transform.position, detection);
        }
    }
}
