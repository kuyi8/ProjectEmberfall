using System;
using System.Collections.Generic;
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
    public sealed class WardenActor : CombatTarget
    {
        private const int HitBufferSize = 16;

        [SerializeField] private TextAsset _definitionJson;
        [SerializeField] private string _wardenId = "boss:ember-warden";
        [SerializeField] private PlayerCombatActor _target;
        [SerializeField] private NavMeshAgent _agent;
        [SerializeField] private Transform _aimPoint;
        [SerializeField] private Transform _attackOrigin;
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField] private Renderer _shieldRenderer;
        [SerializeField] private Renderer _weaponRenderer;
        [SerializeField] private Renderer _telegraphRenderer;
        [SerializeField] private Light _phaseLight;
        [SerializeField] private Collider _bodyCollider;
        [SerializeField] private GameObject _delayedBlastVfxPrefab;
        [SerializeField, Min(0.1f)] private float _attackRadius = 1.15f;
        [SerializeField, Min(0.02f)] private float _perceptionInterval = 0.1f;

        private readonly Collider[] _hitBuffer = new Collider[HitBufferSize];
        private readonly HitRegistry _hitRegistry = new HitRegistry();
        private readonly List<RangedGroundRune> _activeBlasts = new List<RangedGroundRune>();
        private WardenDefinition _definition;
        private WardenBrain _brain;
        private MeleeEnemyPerception _perception;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Vector3 _committedChargeDirection;
        private Color _bodyBaseColor;
        private Color _shieldBaseColor;
        private Color _weaponBaseColor;
        private Material _phaseTelegraphMaterial;
        private GameObject _runeCleaveTelegraph;
        private GameObject _phaseRing;
        private GameObject _shieldVisualRoot;
        private float _chargeTravelRemaining;
        private float _perceptionRemaining;
        private float _flashRemaining;
        private bool _encounterEnabled;
        private int _releasedDelayedBlastSequence = -1;

        public override int CombatantId => GetInstanceID();
        public override Transform AimPoint => _aimPoint != null ? _aimPoint : transform;
        public override bool IsAvailable => _brain != null && _brain.State != WardenState.Dead;
        public override float HealthNormalized => _brain?.Health.Normalized ?? 0f;
        public override bool HasSecondaryResource => true;
        public override float SecondaryResourceNormalized => _brain?.GuardNormalized ?? 0f;
        public override bool IsThreatening => State == WardenState.Windup || State == WardenState.Attack;
        public WardenDefinition Definition => _definition;
        public WardenBrain Brain => _brain;
        public WardenState State => _brain?.State ?? WardenState.Dormant;
        public bool EncounterActive => _brain?.EncounterActive == true;
        public bool PhaseTwoThresholdReached => _brain?.PhaseTwoThresholdReached == true;
        public WardenPhase Phase => _brain?.Phase ?? WardenPhase.PhaseOne;
        public bool HasSimulationAuthority { get; private set; } = true;
        public float HorizontalSpeed => _agent != null
            ? Vector3.ProjectOnPlane(_agent.velocity, Vector3.up).magnitude
            : 0f;
        public string LastBossEvent { get; private set; } = "Dormant";
        public bool DelayedBlastVfxConfigured => _delayedBlastVfxPrefab != null;

        public event Action<WardenActor> EncounterStarted;
        public event Action<WardenActor> EncounterReset;
        public event Action<WardenActor> Died;
        public event Action DelayedBlastResolved;

        public void Configure(
            TextAsset definitionJson,
            string wardenId,
            PlayerCombatActor target,
            NavMeshAgent agent,
            Transform aimPoint,
            Transform attackOrigin,
            Renderer bodyRenderer,
            Renderer shieldRenderer,
            Renderer weaponRenderer,
            Renderer telegraphRenderer,
            Light phaseLight,
            Collider bodyCollider)
        {
            _definitionJson = definitionJson;
            _wardenId = wardenId;
            _target = target;
            _agent = agent;
            _aimPoint = aimPoint;
            _attackOrigin = attackOrigin;
            _bodyRenderer = bodyRenderer;
            _shieldRenderer = shieldRenderer;
            _weaponRenderer = weaponRenderer;
            _telegraphRenderer = telegraphRenderer;
            _phaseLight = phaseLight;
            _bodyCollider = bodyCollider;
        }

        public void SetEncounterEnabled(bool enabled)
        {
            _encounterEnabled = enabled;
            if (!enabled && _brain != null && _brain.State != WardenState.Dead) ResetEncounter();
        }

        public void SetSimulationAuthority(bool hasAuthority)
        {
            HasSimulationAuthority = hasAuthority;
            if (!hasAuthority) StopAgent();
        }

        public void ConfigureDelayedBlastVfx(GameObject delayedBlastVfxPrefab)
        {
            _delayedBlastVfxPrefab = delayedBlastVfxPrefab;
        }

        private void Awake()
        {
            if (_target == null || _agent == null || _attackOrigin == null ||
                _bodyCollider == null || !ContentId.TryCreate(_wardenId, out ContentId id))
            {
                Debug.LogError("WardenActor is not configured.", this);
                enabled = false;
                return;
            }

            _definition = WardenDefinitionJsonLoader.Load(LoadDefinitionJson()).GetRequired(id);
            _brain = new WardenBrain(_definition);
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _agent.speed = _definition.MoveSpeed;
            _agent.angularSpeed = _definition.RotationSpeed;
            _agent.stoppingDistance = Mathf.Max(0.3f, _definition.SwordCombo.MaximumRange * 0.88f);
            _agent.updateRotation = false;
            if (_bodyRenderer != null) _bodyBaseColor = _bodyRenderer.material.color;
            if (_shieldRenderer != null) _shieldBaseColor = _shieldRenderer.material.color;
            _shieldVisualRoot = FindShieldVisualRoot();
            if (_weaponRenderer != null) _weaponBaseColor = _weaponRenderer.material.color;
            if (_telegraphRenderer != null) _telegraphRenderer.gameObject.SetActive(false);
            CreatePhaseTelegraphs();
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
            throw new InvalidOperationException("Warden content requires an initialized runtime package.");
        }

        private void Start() => WarpToSpawn();

        private void Update()
        {
            if (!HasSimulationAuthority || _brain == null) return;

            _perceptionRemaining -= Time.deltaTime;
            if (_perceptionRemaining <= 0f)
            {
                RefreshPerception();
                _perceptionRemaining = _perceptionInterval;
            }

            WardenState previous = _brain.State;
            _brain.Tick(Time.deltaTime, _perception, _encounterEnabled);
            if (previous != _brain.State) HandleStateChanged(previous, _brain.State);
            _agent.speed = _definition.MoveSpeed * _brain.MoveSpeedMultiplier;
            DriveMovement();
            if (_brain.IsDamageWindowOpen) QueryAttackHits();
            UpdatePresentation();
        }

        public override DamageResult ReceiveDamage(DamageRequest request)
        {
            if (!HasSimulationAuthority || _brain == null) return DamageResult.Ignored;
            Vector3 toAttacker = _target != null
                ? Vector3.ProjectOnPlane(_target.transform.position - transform.position, Vector3.up)
                : Vector3.zero;
            bool frontal = toAttacker.sqrMagnitude > 0.001f &&
                Vector3.Angle(transform.forward, toAttacker) <= _definition.FrontalBlockAngle * 0.5f;
            WardenState previous = _brain.State;
            DamageResult result = _brain.ReceiveDamage(request, frontal);
            _flashRemaining = 0.14f;
            LastBossEvent = result.GuardBroken ? "Guard broken" : result.Blocked ? "Blocked" :
                result.Killed ? "Defeated" : result.Accepted ? "Damaged" : "Ignored";
            if (previous != _brain.State) HandleStateChanged(previous, _brain.State);
            return result;
        }

        public override float ApplyNeutralPostureDamage(float amount)
        {
            if (!HasSimulationAuthority || _brain == null || !IsAvailable) return 0f;
            float before = _brain.GuardCurrent;
            WardenState previous = _brain.State;
            bool broken = _brain.ApplyCounterPosture(amount);
            _flashRemaining = 0.18f;
            LastBossEvent = broken ? "Guard broken" : "Posture damaged";
            if (previous != _brain.State) HandleStateChanged(previous, _brain.State);
            return before - _brain.GuardCurrent;
        }

        public void ResetEncounter()
        {
            if (_brain == null) return;
            bool wasActive = _brain.EncounterActive;
            _brain.Reset();
            _releasedDelayedBlastSequence = -1;
            ClearActiveBlasts();
            RestoreBodyAtSpawn();
            LastBossEvent = "Reset";
            UpdatePresentation();
            if (wasActive) EncounterReset?.Invoke(this);
        }

        private void RefreshPerception()
        {
            bool targetAvailable = _target != null && _target.IsAvailable;
            float targetDistance = targetAvailable
                ? Vector3.ProjectOnPlane(_target.transform.position - transform.position, Vector3.up).magnitude
                : float.PositiveInfinity;
            _perception = new MeleeEnemyPerception(
                targetAvailable,
                targetAvailable && CanSeeTarget(),
                targetDistance,
                Vector3.Distance(transform.position, _spawnPosition));
        }

        private bool CanSeeTarget()
        {
            Vector3 direction = _target.AimPoint.position - AimPoint.position;
            if (!Physics.Raycast(AimPoint.position, direction.normalized, out RaycastHit hit,
                    direction.magnitude, ~0, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider.GetComponentInParent<PlayerCombatActor>() == _target;
        }

        private void DriveMovement()
        {
            if (State == WardenState.Dead || State == WardenState.GuardBreak ||
                State == WardenState.PhaseTransition || State == WardenState.Dormant)
            {
                StopAgent();
                return;
            }

            if (State == WardenState.Attack && _brain.CurrentAttack == WardenAttackKind.Charge)
            {
                StopAgent();
                float normalized = Mathf.Clamp01(_brain.StateElapsed / _brain.CurrentAttackDuration);
                float acceleration = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(normalized / 0.18f));
                float deceleration = Mathf.Lerp(
                    1f,
                    0.35f,
                    Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1f, normalized)));
                float distance = Mathf.Min(
                    _chargeTravelRemaining,
                    _definition.ChargeSpeed * acceleration * deceleration * Time.deltaTime);
                if (distance > 0f && _agent.enabled && _agent.isOnNavMesh)
                {
                    _agent.Move(_committedChargeDirection * distance);
                    _chargeTravelRemaining -= distance;
                }
                return;
            }

            if (_agent.isOnNavMesh && _brain.WantsTargetMovement && _target != null)
            {
                _agent.isStopped = false;
                _agent.SetDestination(_target.transform.position);
            }
            else if (_agent.isOnNavMesh && _brain.WantsReturnMovement)
            {
                _agent.isStopped = false;
                _agent.SetDestination(_spawnPosition);
            }
            else StopAgent();

            Vector3 facing = _brain.WantsFaceTarget && _target != null
                ? _target.transform.position - transform.position
                : _agent.velocity;
            facing = Vector3.ProjectOnPlane(facing, Vector3.up);
            if (facing.sqrMagnitude <= 0.001f) return;
            transform.rotation = Quaternion.RotateTowards(
                transform.rotation, Quaternion.LookRotation(facing.normalized, Vector3.up),
                _definition.RotationSpeed * Time.deltaTime);
        }

        private void QueryAttackHits()
        {
            if (_brain.CurrentAttack == WardenAttackKind.DelayedBlast)
            {
                TryReleaseDelayedBlast();
                return;
            }

            int hitKey = (_brain.AttackSequence * 10) + _brain.CurrentHitIndex;
            int count = Physics.OverlapSphereNonAlloc(
                _attackOrigin.position, _attackRadius * _brain.CurrentHitRadiusMultiplier,
                _hitBuffer, ~0, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
            {
                PlayerCombatActor target = _hitBuffer[i].GetComponentInParent<PlayerCombatActor>();
                if (target == null || !target.IsAvailable ||
                    (_brain.CurrentAttack == WardenAttackKind.RuneCleave && !CanHitWithRuneCleave(target)) ||
                    !_hitRegistry.TryRegister(hitKey, target.CombatantId))
                    continue;

                if (_brain.CurrentAttack == WardenAttackKind.Charge)
                    _chargeTravelRemaining = 0f;

                AttackTag tag = _brain.CurrentAttack == WardenAttackKind.SwordCombo ? AttackTag.Light : AttackTag.Heavy;
                DamageResult result = target.ReceiveDamage(new DamageRequest(
                    CombatantId, hitKey, _brain.CurrentAttackDamage, _brain.CurrentPostureDamage, tag,
                    true, DefenseArcUtility.IsThreatInFrontArc(target.transform, transform.position)));
                if (result.PerfectGuard)
                {
                    bool broken = _brain.ApplyCounterPosture(result.CounterPostureDamage);
                    LastBossEvent = broken ? "Perfect guard break" : "Perfect guard";
                }
                else LastBossEvent = result.Defended ? "Guarded" : result.Invulnerable ? "Evaded" :
                    result.Accepted ? "Hit player" : "Missed";
            }
        }

        private void HandleStateChanged(WardenState previous, WardenState current)
        {
            LastBossEvent = current == WardenState.PhaseTransition ? "Shield break · Phase transition" :
                current == WardenState.Windup ? $"Windup · {_brain.CurrentAttack}" : current.ToString();
            if (previous == WardenState.Dormant && current == WardenState.Chase) EncounterStarted?.Invoke(this);
            if (previous == WardenState.Return && current == WardenState.Dormant)
            {
                _brain.Reset();
                RestoreBodyAtSpawn();
                LastBossEvent = "Leash reset";
                EncounterReset?.Invoke(this);
            }

            if (current == WardenState.Attack && _brain.CurrentAttack == WardenAttackKind.Charge)
            {
                Vector3 direction = _target != null
                    ? Vector3.ProjectOnPlane(_target.transform.position - transform.position, Vector3.up)
                    : transform.forward;
                _committedChargeDirection = direction.sqrMagnitude > 0.001f ? direction.normalized : transform.forward;
                _chargeTravelRemaining = _definition.ChargeTravelDistance;
            }

            if (current == WardenState.Dead)
            {
                StopAgent();
                ClearActiveBlasts();
                _bodyCollider.enabled = false;
                Died?.Invoke(this);
            }

            if (previous == WardenState.PhaseTransition && _brain.Phase == WardenPhase.PhaseTwo)
                LastBossEvent = "Phase two · Rune sword ignited";
        }

        private bool CanHitWithRuneCleave(PlayerCombatActor target)
        {
            Vector3 direction = Vector3.ProjectOnPlane(target.transform.position - transform.position, Vector3.up);
            if (direction.sqrMagnitude <= 0.001f ||
                Vector3.Angle(transform.forward, direction) > _definition.RuneCleaveAngle * 0.5f)
                return false;

            Vector3 origin = _attackOrigin.position;
            Vector3 destination = target.AimPoint.position;
            if (!Physics.Linecast(origin, destination, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore))
                return true;
            return hit.collider.GetComponentInParent<PlayerCombatActor>() == target;
        }

        private void TryReleaseDelayedBlast()
        {
            if (_releasedDelayedBlastSequence == _brain.AttackSequence || _target == null) return;
            _releasedDelayedBlastSequence = _brain.AttackSequence;

            Vector3 targetPosition = _target.transform.position;
            if (NavMesh.SamplePosition(targetPosition, out NavMeshHit hit, 2.5f, NavMesh.AllAreas))
                targetPosition = hit.position;
            targetPosition.y += 0.04f;

            var blastObject = new GameObject(
                $"WardenDelayedBlast_{_brain.AttackSequence:000}", typeof(RangedGroundRune));
            blastObject.transform.position = targetPosition;
            RangedGroundRune blast = blastObject.GetComponent<RangedGroundRune>();
            Material warningMaterial = _phaseTelegraphMaterial != null
                ? _phaseTelegraphMaterial
                : _telegraphRenderer != null ? _telegraphRenderer.sharedMaterial : null;
            blast.Configure(
                CombatantId,
                _brain.AttackSequence,
                _definition.DelayedBlastFuse,
                _definition.DelayedBlastRadius,
                _brain.CurrentAttackDamage,
                _brain.CurrentPostureDamage,
                warningMaterial,
                HasSimulationAuthority,
                OnDelayedBlastResolved,
                _delayedBlastVfxPrefab);
            _activeBlasts.Add(blast);
            LastBossEvent = "Delayed blast armed";
        }

        private void OnDelayedBlastResolved(DamageResult result)
        {
            LastBossEvent = result.Invulnerable ? "Blast evaded" : result.Accepted ? "Blast hit player" : "Blast missed";
            DelayedBlastResolved?.Invoke();
        }

        private void ClearActiveBlasts()
        {
            for (int i = 0; i < _activeBlasts.Count; i++)
            {
                if (_activeBlasts[i] != null) Destroy(_activeBlasts[i].gameObject);
            }
            _activeBlasts.Clear();
        }

        private void CreatePhaseTelegraphs()
        {
            if (_telegraphRenderer == null) return;
            _phaseTelegraphMaterial = new Material(_telegraphRenderer.sharedMaterial)
            {
                color = new Color(0.78f, 0.12f, 1f, 0.78f)
            };

            _runeCleaveTelegraph = new GameObject("WardenRuneCleaveTelegraph");
            _runeCleaveTelegraph.transform.SetParent(transform, false);
            _runeCleaveTelegraph.transform.localPosition = new Vector3(0f, 0.055f, 0f);
            const int fanSegments = 9;
            float range = _definition.RuneCleave.MaximumRange;
            for (int i = 0; i < fanSegments; i++)
            {
                float angle = Mathf.Lerp(-_definition.RuneCleaveAngle * 0.5f,
                    _definition.RuneCleaveAngle * 0.5f, i / (fanSegments - 1f));
                GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"RuneCleaveRay_{i:00}";
                segment.transform.SetParent(_runeCleaveTelegraph.transform, false);
                segment.transform.localRotation = Quaternion.Euler(0f, angle, 0f);
                segment.transform.localPosition = segment.transform.localRotation *
                    (Vector3.forward * range * 0.48f);
                segment.transform.localScale = new Vector3(0.14f, 0.025f, range * 0.62f);
                segment.GetComponent<Renderer>().sharedMaterial = _phaseTelegraphMaterial;
                Destroy(segment.GetComponent<Collider>());
            }
            _runeCleaveTelegraph.SetActive(false);

            _phaseRing = new GameObject("WardenPhaseTransitionRing");
            _phaseRing.transform.SetParent(transform, false);
            _phaseRing.transform.localPosition = new Vector3(0f, 0.06f, 0f);
            const int ringSegments = 14;
            for (int i = 0; i < ringSegments; i++)
            {
                GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"PhaseRingSegment_{i:00}";
                segment.transform.SetParent(_phaseRing.transform, false);
                segment.GetComponent<Renderer>().sharedMaterial = _phaseTelegraphMaterial;
                Destroy(segment.GetComponent<Collider>());
            }
            _phaseRing.SetActive(false);
        }

        private void UpdatePhaseRing(float normalized)
        {
            if (_phaseRing == null) return;
            float radius = Mathf.Lerp(0.85f, 4.4f, normalized);
            int count = _phaseRing.transform.childCount;
            for (int i = 0; i < count; i++)
            {
                float degrees = (360f * i / count) - (Time.time * 42f);
                float radians = degrees * Mathf.Deg2Rad;
                Transform segment = _phaseRing.transform.GetChild(i);
                segment.localPosition = new Vector3(Mathf.Cos(radians) * radius, 0f, Mathf.Sin(radians) * radius);
                segment.localRotation = Quaternion.Euler(0f, -degrees, 0f);
                segment.localScale = new Vector3(0.58f, 0.025f, 0.13f);
            }
        }

        private GameObject FindShieldVisualRoot()
        {
            if (_shieldRenderer == null) return null;
            Transform candidate = _shieldRenderer.transform;
            while (candidate != null && candidate != transform)
            {
                if (candidate.name == "Shield_Warden_Equipped") return candidate.gameObject;
                candidate = candidate.parent;
            }
            return _shieldRenderer.gameObject;
        }

        private void RestoreBodyAtSpawn()
        {
            _bodyCollider.enabled = true;
            _hitRegistry.Clear();
            _chargeTravelRemaining = 0f;
            WarpToSpawn();
            transform.rotation = _spawnRotation;
            RefreshPerception();
        }

        private void WarpToSpawn()
        {
            if (_agent != null && _agent.enabled && NavMesh.SamplePosition(_spawnPosition, out NavMeshHit hit, 2f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
            else transform.position = _spawnPosition;
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
            _flashRemaining = Mathf.Max(0f, _flashRemaining - Time.deltaTime);
            if (_bodyRenderer != null)
            {
                Color target = State == WardenState.Dead ? new Color(0.07f, 0.07f, 0.07f) :
                    _flashRemaining > 0f ? Color.white : _bodyBaseColor;
                _bodyRenderer.material.color = Color.Lerp(_bodyRenderer.material.color, target, Time.deltaTime * 16f);
            }
            if (_shieldRenderer != null)
            {
                if (_shieldVisualRoot != null) _shieldVisualRoot.SetActive(Phase != WardenPhase.PhaseTwo &&
                    State != WardenState.Dead);
                Color target = State == WardenState.PhaseTransition ? new Color(1f, 0.32f, 0.04f) :
                    State == WardenState.GuardBreak ? new Color(1f, 0.2f, 0.04f) :
                    _flashRemaining > 0f ? Color.white : _shieldBaseColor;
                _shieldRenderer.material.color = Color.Lerp(_shieldRenderer.material.color, target, Time.deltaTime * 16f);
            }

            if (_weaponRenderer != null)
            {
                Color runeColor = new Color(1f, 0.16f, 0.035f);
                Color target = Phase == WardenPhase.PhaseOne ? _weaponBaseColor : runeColor;
                _weaponRenderer.material.color = Color.Lerp(_weaponRenderer.material.color, target,
                    Time.deltaTime * (State == WardenState.PhaseTransition ? 5f : 12f));
            }

            float transitionNormalized = State == WardenState.PhaseTransition
                ? Mathf.Clamp01(_brain.StateElapsed / _definition.PhaseTransitionDuration)
                : 0f;
            if (_phaseLight != null)
            {
                _phaseLight.color = Color.Lerp(new Color(1f, 0.25f, 0.04f), new Color(0.62f, 0.08f, 1f),
                    transitionNormalized);
                _phaseLight.intensity = State == WardenState.Dead ? 0f :
                    State == WardenState.PhaseTransition ? Mathf.Lerp(0.4f, 3.2f, transitionNormalized) :
                    Phase == WardenPhase.PhaseTwo ? 1.15f + Mathf.Sin(Time.time * 5f) * 0.2f : 0f;
            }

            bool phaseTransitionVisible = State == WardenState.PhaseTransition;
            if (_phaseRing != null)
            {
                _phaseRing.SetActive(phaseTransitionVisible);
                if (phaseTransitionVisible) UpdatePhaseRing(transitionNormalized);
            }

            bool runeCleaveVisible = State == WardenState.Windup &&
                _brain.CurrentAttack == WardenAttackKind.RuneCleave;
            if (_runeCleaveTelegraph != null)
            {
                _runeCleaveTelegraph.SetActive(runeCleaveVisible);
                if (runeCleaveVisible)
                {
                    float fanPulse = 0.96f + Mathf.Sin(Time.time * 16f) * 0.04f;
                    _runeCleaveTelegraph.transform.localScale = Vector3.one * fanPulse;
                }
            }

            if (_telegraphRenderer == null) return;
            bool visible = State == WardenState.Windup && !runeCleaveVisible;
            _telegraphRenderer.gameObject.SetActive(visible);
            if (!visible) return;
            Color color = _brain.CurrentAttack switch
            {
                WardenAttackKind.Charge => new Color(1f, 0.1f, 0.03f, 0.82f),
                WardenAttackKind.ShieldBash => new Color(0.08f, 0.72f, 1f, 0.82f),
                WardenAttackKind.DelayedBlast => new Color(0.72f, 0.08f, 1f, 0.82f),
                _ => new Color(1f, 0.48f, 0.06f, 0.82f)
            };
            float pulse = 0.82f + Mathf.Sin(Time.time * 14f) * 0.12f;
            _telegraphRenderer.material.color = color;
            _telegraphRenderer.transform.localScale = new Vector3(
                _brain.CurrentAttack == WardenAttackKind.Charge ? 1.1f : 2.35f,
                0.025f,
                (_brain.CurrentAttack == WardenAttackKind.Charge ? 5.2f : 2.35f) * pulse);
        }

        private void OnDestroy()
        {
            ClearActiveBlasts();
            if (_phaseTelegraphMaterial != null) Destroy(_phaseTelegraphMaterial);
        }

        private void OnDrawGizmosSelected()
        {
            if (_attackOrigin != null)
            {
                Gizmos.color = Color.red;
                Gizmos.DrawWireSphere(_attackOrigin.position, _attackRadius);
            }
            Gizmos.color = new Color(1f, 0.4f, 0.08f, 0.55f);
            Gizmos.DrawWireSphere(transform.position, _definition?.DetectionRange ?? 10f);
        }
    }
}
