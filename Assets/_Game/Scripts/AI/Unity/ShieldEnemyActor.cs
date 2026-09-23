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
    public sealed class ShieldEnemyActor : CombatTarget, IExecutionTarget
    {
        private const int HitBufferSize = 12;

        [SerializeField] private TextAsset _definitionJson;
        [SerializeField] private string _enemyId = "enemy:ruin-guard";
        [SerializeField] private PlayerCombatActor _target;
        [SerializeField] private NavMeshAgent _agent;
        [SerializeField] private Transform _aimPoint;
        [SerializeField] private Transform _attackOrigin;
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private Renderer _shieldRenderer;
        [SerializeField] private Collider _bodyCollider;
        [SerializeField] private Material _scorchedWarningMaterial;
        [SerializeField] private GameObject _scorchedImpactVfxPrefab;
        [SerializeField] private bool _autoResetAfterDelay = true;
        [SerializeField, Min(0.1f)] private float _attackRadius = 1.05f;
        [SerializeField, Min(0.02f)] private float _perceptionInterval = 0.12f;

        private readonly Collider[] _hitBuffer = new Collider[HitBufferSize];
        private readonly HitRegistry _hitRegistry = new HitRegistry();
        private ShieldEnemyDefinition _definition;
        private ShieldEnemyBrain _brain;
        private EncounterLeash _leash;
        private MeleeEnemyPerception _perception;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Color _bodyBaseColor;
        private Color _shieldBaseColor;
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
        public override bool IsAvailable => _brain != null && _brain.State != ShieldEnemyState.Dead;
        public override float HealthNormalized => _brain?.Health.Normalized ?? 0f;
        public override bool HasSecondaryResource => true;
        public override float SecondaryResourceNormalized => _brain?.GuardNormalized ?? 0f;
        public override bool IsThreatening => State == ShieldEnemyState.Windup || State == ShieldEnemyState.Attack;
        public ShieldEnemyDefinition Definition => _definition;
        public ShieldEnemyBrain Brain => _brain;
        public ShieldEnemyState State => _brain?.State ?? ShieldEnemyState.Idle;
        public float HorizontalSpeed => _agent != null
            ? Vector3.ProjectOnPlane(_agent.velocity, Vector3.up).magnitude
            : 0f;
        public bool HasSimulationAuthority { get; private set; } = true;
        public bool IsAttackSlotCommitted => State == ShieldEnemyState.Windup ||
            State == ShieldEnemyState.Attack || State == ShieldEnemyState.Recovery;
        public bool IsAttackSlotEligible => IsAttackSlotCommitted ||
            (State == ShieldEnemyState.Chase && _perception.TargetAvailable && _perception.CanSeeTarget &&
             _perception.DistanceToTarget <= (_definition?.AttackRange ?? 0f) + 2.8f);
        public string LastAiEvent { get; private set; } = "Idle";
        public bool IsGroupAttackAllowed => _attackAllowed;
        public bool HasSupportDestination => _hasSupportDestination;
        public bool ScorchedPresentationConfigured =>
            _scorchedWarningMaterial != null && _scorchedImpactVfxPrefab != null;
        public Vector3 SupportDestination => _supportDestination;
        public CombatTarget CombatTarget => this;
        public ExecutionTargetKind ExecutionKind => ExecutionTargetKind.Elite;
        public bool IsExecutionClaimed => _executionClaimed;
        public bool IsPostureExecutionWindow => _brain?.State == ShieldEnemyState.GuardBreak;
        public bool IsExecutionEligible => ExecutionRules.IsEligible(
            ExecutionKind,
            HealthNormalized,
            State == ShieldEnemyState.GuardBreak,
            _executionClaimed);
        public float ExecutionDamage => ExecutionRules.EliteDamage;

        public event Action<ShieldEnemyActor> Died;
        public event Action<ShieldEnemyActor> GuardBroken;

        public void Configure(
            TextAsset definitionJson,
            string enemyId,
            PlayerCombatActor target,
            NavMeshAgent agent,
            Transform aimPoint,
            Transform attackOrigin,
            Renderer bodyRenderer,
            Renderer shieldRenderer,
            Collider bodyCollider)
        {
            _definitionJson = definitionJson;
            _enemyId = enemyId;
            _target = target;
            _agent = agent;
            _aimPoint = aimPoint;
            _attackOrigin = attackOrigin;
            _bodyRenderer = bodyRenderer;
            _shieldRenderer = shieldRenderer;
            _bodyCollider = bodyCollider;
        }

        public void SetSimulationAuthority(bool hasAuthority)
        {
            HasSimulationAuthority = hasAuthority;
            if (!hasAuthority) StopAgent();
        }

        public void SetAutoResetAfterDelay(bool enabled) => _autoResetAfterDelay = enabled;

        public void ConfigureScorchedPresentation(Material warningMaterial, GameObject impactVfxPrefab)
        {
            _scorchedWarningMaterial = warningMaterial;
            _scorchedImpactVfxPrefab = impactVfxPrefab;
        }

        private void Awake()
        {
            _leash = GetComponent<EncounterLeash>();
            if (_target == null || _agent == null ||
                _attackOrigin == null || _bodyCollider == null ||
                !ContentId.TryCreate(_enemyId, out ContentId enemyId))
            {
                Debug.LogError("ShieldEnemyActor is not configured.", this);
                enabled = false;
                return;
            }

            _definition = ShieldEnemyDefinitionJsonLoader.Load(LoadDefinitionJson()).GetRequired(enemyId);
            _brain = new ShieldEnemyBrain(_definition);
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _agent.speed = _definition.MoveSpeed;
            _agent.angularSpeed = _definition.RotationSpeed;
            _agent.stoppingDistance = Mathf.Max(0.2f, _definition.AttackRange * 0.94f);
            _agent.updateRotation = false;
            if (_bodyRenderer != null) _bodyBaseColor = _bodyRenderer.material.color;
            if (_shieldRenderer != null) _shieldBaseColor = _shieldRenderer.material.color;
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
            throw new InvalidOperationException("Shield enemy content requires an initialized runtime package.");
        }

        private void Start() => WarpToSpawn();

        private void Update()
        {
            if (!HasSimulationAuthority || _brain == null) return;

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

            ShieldEnemyState previousState = _brain.State;
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

            if (previousState != _brain.State) HandleStateChanged(previousState, _brain.State);
            DriveMovement();
            if (_brain.IsDamageWindowOpen) QueryAttackHits();
            UpdatePresentationTint();
        }

        public override DamageResult ReceiveDamage(DamageRequest request)
        {
            if (!HasSimulationAuthority || _brain == null) return DamageResult.Ignored;

            Vector3 toAttacker = _target != null
                ? Vector3.ProjectOnPlane(_target.transform.position - transform.position, Vector3.up)
                : Vector3.zero;
            bool isFrontal = toAttacker.sqrMagnitude > 0.001f &&
                Vector3.Angle(transform.forward, toAttacker) <= _definition.FrontalBlockAngle * 0.5f;
            ShieldEnemyState previousState = _brain.State;
            DamageResult result = _brain.ReceiveDamage(request, isFrontal);
            _flashRemaining = 0.14f;
            LastAiEvent = result.GuardBroken ? "Guard broken" :
                result.Blocked ? "Attack blocked" : result.Killed ? "Dead" : "Hit";
            if (previousState != _brain.State) HandleStateChanged(previousState, _brain.State);
            return result;
        }

        public override float ApplyNeutralPostureDamage(float amount)
        {
            if (!HasSimulationAuthority || _brain == null || !IsAvailable) return 0f;
            float before = _brain.GuardCurrent;
            ShieldEnemyState previous = _brain.State;
            bool broken = _brain.ApplyCounterPosture(amount);
            _flashRemaining = 0.18f;
            LastAiEvent = broken ? "Neutral guard break" : "Neutral posture hit";
            if (previous != _brain.State) HandleStateChanged(previous, _brain.State);
            return before - _brain.GuardCurrent;
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
                _agent.avoidancePriority = attackAllowed ? 22 : (supportSide < 0 ? 52 : 62);
            }
        }

        public void ResetToSpawn()
        {
            if (_brain == null) return;
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
            bool targetAvailable = _target != null && _target.IsAvailable &&
                (_leash == null || _leash.AllowsTarget(_target.transform.position));
            float distanceToTarget = targetAvailable
                ? Vector3.ProjectOnPlane(_target.transform.position - transform.position, Vector3.up).magnitude
                : float.PositiveInfinity;
            _perception = new MeleeEnemyPerception(
                targetAvailable,
                targetAvailable && CanSeeTarget(),
                distanceToTarget,
                Vector3.Distance(transform.position, _spawnPosition));
        }

        private bool CanSeeTarget()
        {
            Vector3 direction = _target.AimPoint.position - AimPoint.position;
            Vector3 flatDirection = Vector3.ProjectOnPlane(direction, Vector3.up);
            if (flatDirection.sqrMagnitude > 0.001f &&
                Vector3.Angle(transform.forward, flatDirection) > _definition.FieldOfView * 0.5f)
            {
                return false;
            }

            if (!Physics.Raycast(AimPoint.position, direction.normalized, out RaycastHit hit,
                    direction.magnitude, ~0, QueryTriggerInteraction.Ignore))
            {
                return true;
            }

            return hit.collider.GetComponentInParent<PlayerCombatActor>() == _target;
        }

        private void DriveMovement()
        {
            if (_brain.State == ShieldEnemyState.Dead || _brain.State == ShieldEnemyState.HitReact ||
                _brain.State == ShieldEnemyState.GuardBreak)
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
                SetDestination(_hasSupportDestination && !_attackAllowed
                    ? _supportDestination
                    : _target.transform.position);
            }
            else if (_agent.isOnNavMesh && _brain.WantsReturnMovement)
            {
                _agent.isStopped = false;
                SetDestination(_spawnPosition);
            }
            else
            {
                StopAgent();
            }

            Vector3 facing = _brain.WantsFaceTarget && _target != null
                ? _target.transform.position - transform.position
                : _agent.velocity;
            facing = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (facing.sqrMagnitude <= 0.001f) return;
            Quaternion desired = Quaternion.LookRotation(facing.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, desired, _definition.RotationSpeed * Time.deltaTime);
        }

        private void QueryAttackHits()
        {
            if (_leash != null && _target != null && !_leash.AllowsTarget(_target.transform.position)) return;
            int count = Physics.OverlapSphereNonAlloc(
                _attackOrigin.position, _attackRadius * _brain.CurrentHitRadiusMultiplier,
                _hitBuffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                PlayerCombatActor target = _hitBuffer[i].GetComponentInParent<PlayerCombatActor>();
                if (target == null || !target.IsAvailable ||
                    !_hitRegistry.TryRegister(_brain.AttackSequence, target.CombatantId))
                {
                    continue;
                }

                DamageResult result = target.ReceiveDamage(new DamageRequest(
                    CombatantId, _brain.AttackSequence, _brain.CurrentAttackDamage,
                    _brain.CurrentPostureDamage, AttackTag.Heavy,
                    true,
                    DefenseArcUtility.IsThreatInFrontArc(target.transform, transform.position)));
                if (result.PerfectGuard)
                {
                    bool broken = _brain.ApplyCounterPosture(result.CounterPostureDamage);
                    LastAiEvent = broken ? "Perfect guard broke guard" : "Perfect guard deflected";
                }
                else
                {
                    LastAiEvent = result.Defended ? "Attack guarded" :
                        result.Invulnerable ? "Attack evaded" :
                        result.Accepted ? "Attack hit" : "Attack missed";
                }
            }
        }

        private void HandleStateChanged(ShieldEnemyState previous, ShieldEnemyState current)
        {
            if (current == ShieldEnemyState.GuardBreak)
            {
                LastAiEvent = "Guard broken";
                if (previous != ShieldEnemyState.GuardBreak)
                {
                    GuardBroken?.Invoke(this);
                }
            }
            else if (current == ShieldEnemyState.Windup) LastAiEvent = $"Windup · {_brain.CurrentAttack}";
            else LastAiEvent = current.ToString();

            if (current == ShieldEnemyState.Attack &&
                _brain.CurrentAttack == ShieldAttackKind.ScorchedBurst)
            {
                ReleaseScorchedBurst();
            }

            if (current == ShieldEnemyState.Dead)
            {
                StopAgent();
                _bodyCollider.enabled = false;
                Died?.Invoke(this);
            }
            else if (previous == ShieldEnemyState.Dead)
            {
                RestoreBodyAtSpawn();
            }
        }

        private void ReleaseScorchedBurst()
        {
            if (_leash != null && _target != null && !_leash.AllowsTarget(_target.transform.position)) return;
            Vector3 center = ProjectToGround(transform.position);
            var runeObject = new GameObject($"ScorchedBurst_{_brain.AttackSequence:000}");
            runeObject.transform.position = center;
            RangedGroundRune rune = runeObject.AddComponent<RangedGroundRune>();
            rune.Configure(
                CombatantId,
                _brain.AttackSequence,
                _definition.ScorchedBurstTriggerDelay,
                _definition.ScorchedBurstRadius,
                _definition.ScorchedBurstDamage,
                _definition.ScorchedBurstPostureDamage,
                _scorchedWarningMaterial,
                HasSimulationAuthority,
                result => LastAiEvent = result.Invulnerable ? "Scorched burst evaded" :
                    result.Accepted ? "Scorched burst hit" : "Scorched burst resolved",
                _scorchedImpactVfxPrefab);
            LastAiEvent = "Scorched burst released";
        }

        private Vector3 ProjectToGround(Vector3 position)
        {
            RaycastHit[] hits = Physics.RaycastAll(
                position + Vector3.up * 4f,
                Vector3.down,
                10f,
                ~0,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            for (int i = 0; i < hits.Length; i++)
            {
                Collider collider = hits[i].collider;
                if (collider == null || collider.transform.IsChildOf(transform) ||
                    collider.GetComponentInParent<PlayerCombatActor>() != null ||
                    collider.GetComponentInParent<ShieldEnemyActor>() != null) continue;
                return hits[i].point;
            }
            return position;
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

        private void SetDestination(Vector3 position)
        {
            if (_leash != null) _leash.SetDestination(position);
            else _agent.SetDestination(position);
        }

        private void UpdatePresentationTint()
        {
            _flashRemaining = Mathf.Max(0f, _flashRemaining - Time.deltaTime);
            Color bodyTarget = _brain.State == ShieldEnemyState.Dead
                ? new Color(0.08f, 0.09f, 0.08f)
                : _flashRemaining > 0f ? Color.white : _bodyBaseColor;
            Color shieldTarget = _brain.State == ShieldEnemyState.GuardBreak
                ? new Color(1f, 0.18f, 0.04f)
                : _flashRemaining > 0f ? Color.white : _shieldBaseColor;
            if (_bodyRenderer != null)
                _bodyRenderer.material.color = Color.Lerp(_bodyRenderer.material.color, bodyTarget, Time.deltaTime * 16f);
            if (_shieldRenderer != null)
                _shieldRenderer.material.color = Color.Lerp(_shieldRenderer.material.color, shieldTarget, Time.deltaTime * 18f);
        }

        private void OnDrawGizmosSelected()
        {
            if (_attackOrigin != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(_attackOrigin.position, _attackRadius);
            }
            Gizmos.color = new Color(0.1f, 0.75f, 0.95f, 0.55f);
            Gizmos.DrawWireSphere(transform.position, _definition?.DetectionRange ?? 10f);
        }
    }
}
