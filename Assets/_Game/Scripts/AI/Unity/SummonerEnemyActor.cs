using System;
using Emberfall.AI.Data;
using Emberfall.AI.Domain;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;
using UnityEngine.AI;

namespace Emberfall.AI.Unity
{
    [DefaultExecutionOrder(-120)]
    public sealed class SummonerEnemyActor : CombatTarget, IExecutionTarget, ISweepReactive
    {
        [SerializeField] private TextAsset _candidateJson;
        [SerializeField] private TextAsset _meleeJson;
        [SerializeField] private bool _isolatedCandidate;
        [SerializeField] private string _enemyId = "enemy:ash-caller";
        [SerializeField] private PlayerCombatActor _target;
        [SerializeField] private NavMeshAgent _agent;
        [SerializeField] private Collider _body;
        [SerializeField] private Transform _aimPoint, _castOrigin, _telegraph;
        [SerializeField] private SummonedMinionStyle _minionStyle;
        [SerializeField] private GameObject _minionWeapon;
        [SerializeField] private Material _projectileMaterial;
        [SerializeField] private Animator _animator;
        [SerializeField] private GameObject _identityCrown;
        [SerializeField] private PlayerAnimationSet _animationSet;
        [SerializeField] private string _encounterId = "encounter:ash-caller-candidate";
        private readonly MeleeEnemyActor[] _minions = new MeleeEnemyActor[SummonerEnemyDefinition.MaximumLivingSummons];
        private readonly int[] _minionIds = new int[SummonerEnemyDefinition.MaximumLivingSummons];
        private readonly EnemyHitAwareness _hitAwareness = new EnemyHitAwareness();
        private readonly RangedProjectile[] _projectiles = new RangedProjectile[8];
        private readonly SummonedMinionDissolve[] _dissolves = new SummonedMinionDissolve[SummonerEnemyDefinition.MaximumLivingSummons];
        private SummonerEnemyDefinition _definition;
        private MeleeEnemyDefinition _minionDefinition;
        private EncounterLeash _leash;
        private Vector3 _spawn, _visualAnchor, _retreatDestination, _retreatSamplePosition;
        private NavMeshPath _retreatPath;
        private readonly Vector3[] _retreatCorners = new Vector3[64];
        private bool _hasRetreatDestination, _retreatSampleReady, _retreatUnavailable;
        private Quaternion _spawnRotation, _visualRotation, _committedFacing;
        private RangedEnemyPerception _perception;
        private float _nextPerception, _executionHold;
        private int _releasedSequence;
        private string _presentedState;
        private bool _claimed, _firstSummonBeat;
        private Renderer _identityCrownRenderer;
        private float _summonInterruptedUntil = float.NegativeInfinity;

        // Read-only receipt from actual authority callbacks, not a second combat clock.
        public bool HasRecentSummonInterrupt => isActiveAndEnabled && HasSimulationAuthority &&
            IsAvailable && Time.time < _summonInterruptedUntil;

        public SummonerEnemyBrain Brain { get; private set; }
        public SummonerEnemyDefinition Definition => _definition;
        public string EncounterId => _encounterId;
        public GameObject IdentityCrown => _identityCrown;
        public Vector3 IdentityTopPoint => _identityCrownRenderer != null
            ? new Vector3(_identityCrownRenderer.bounds.center.x, _identityCrownRenderer.bounds.max.y + .08f, _identityCrownRenderer.bounds.center.z)
            : AimPoint.position + Vector3.up * .4f;
        public void ConfigureIdentityCrown(GameObject crown)
        {
            if (crown == null || crown.transform.parent != transform)
                throw new ArgumentException("Identity crown belongs directly to its owner, outside the Animator hierarchy.", nameof(crown));
            _identityCrown = crown; _identityCrownRenderer = crown.GetComponent<Renderer>(); RefreshIdentityCrown();
        }
        private void RefreshIdentityCrown()
        {
            if (_identityCrown != null) _identityCrown.SetActive(Brain == null || !Brain.Health.IsDead);
        }
        public bool HasSimulationAuthority { get; private set; } = true;
        public int SpawnedEntityCount { get; private set; }
        public bool RetreatHasPreferredDestination { get; private set; }
        public bool RetreatUnavailable => _retreatUnavailable;
        public Vector3 RetreatDestination => _retreatDestination;
        public float RetreatBestReachableDistance { get; private set; }
        public int RetreatCandidatesExamined { get; private set; }
        public int RetreatNoNavCandidates { get; private set; }
        public int RetreatOutsideCandidates { get; private set; }
        public int RetreatIncompleteCandidates { get; private set; }
        public MeleeEnemyActor GetLivingEntity(int slot)
        {
            if (slot < 0 || slot >= _minions.Length) throw new ArgumentOutOfRangeException(nameof(slot));
            var entity = _minions[slot];
            return entity != null && entity.IsAvailable && entity.gameObject.activeInHierarchy ? entity : null;
        }
        public int LivingEntityCount { get { int count = 0; foreach (var m in _minions) if (m != null && m.IsAvailable && m.gameObject.activeInHierarchy) count++; return count; } }
        public override int CombatantId => GetInstanceID();
        public override Transform AimPoint => _aimPoint != null ? _aimPoint : transform;
        public override bool IsAvailable => Brain != null && !Brain.Health.IsDead;
        public override float HealthNormalized => Brain?.Health.Normalized ?? 0f;
        public override bool HasSecondaryResource => true;
        public override float SecondaryResourceNormalized => Brain?.Posture.Normalized ?? 0f;
        public override bool IsThreatening => Brain != null && (Brain.State == RangedEnemyState.Windup || Brain.IsReleaseOpen);
        public CombatTarget CombatTarget => this;
        public ExecutionTargetKind ExecutionKind => ExecutionTargetKind.Ordinary;
        public bool IsExecutionClaimed => _claimed;
        public bool IsPostureExecutionWindow => Brain?.IsPostureExecutionWindow == true;
        public bool IsExecutionEligible => IsAvailable && ExecutionRules.IsEligible(ExecutionKind, HealthNormalized, IsPostureExecutionWindow, _claimed);
        public float ExecutionDamage => ExecutionRules.OrdinaryDamage;
        public event Action<SummonerEnemyActor> Died;
        public event Action<SummonerEnemyActor> FirstSummonReleased;

        public void Configure(TextAsset candidateJson, TextAsset meleeJson, PlayerCombatActor target,
            NavMeshAgent agent, Collider body, Transform aim, Transform cast, Transform telegraph,
            SummonedMinionStyle minionStyle, Material projectileMaterial, Animator animator, PlayerAnimationSet animations,
            bool isolatedCandidate = false, string encounterId = "encounter:ash-caller-candidate", GameObject minionWeapon = null)
        {
            _candidateJson = candidateJson; _meleeJson = meleeJson; _target = target; _agent = agent;
            _body = body; _aimPoint = aim; _castOrigin = cast; _telegraph = telegraph;
            _minionStyle = minionStyle; _projectileMaterial = projectileMaterial;
            _animator = animator; _animationSet = animations; _isolatedCandidate = isolatedCandidate; _encounterId = encounterId;
            _minionWeapon = minionWeapon;
        }

        private void Awake()
        {
            if (_identityCrown != null) _identityCrownRenderer = _identityCrown.GetComponent<Renderer>();
            if (_target == null || _agent == null || _body == null || _castOrigin == null || _minionStyle == null || !_minionStyle.IsValid || _projectileMaterial == null)
                throw new InvalidOperationException("Summoner needs an inactive, fully configured root before activation.");
            // The explicit Editor candidate never replaces session content or changes the formal package.
            string json = ContentPackageRuntime.IsInitialized ? ContentPackageRuntime.GetRequiredText(RuntimeContentPaths.Enemies) : null;
#if UNITY_EDITOR
            if (_isolatedCandidate) json = _candidateJson != null ? _candidateJson.text : null;
            if (json == null && _candidateJson != null) json = _candidateJson.text;
#endif
            _definition = SummonerEnemyDefinitionJsonLoader.Load(json).GetRequired(new ContentId(_enemyId));
            string melee = ContentPackageRuntime.IsInitialized ? ContentPackageRuntime.GetRequiredText(RuntimeContentPaths.Enemies) : null;
#if UNITY_EDITOR
            if (melee == null && _meleeJson != null) melee = _meleeJson.text;
#endif
            _minionDefinition = SummonedMeleeDefinition.From(MeleeEnemyDefinitionJsonLoader.Load(melee).GetRequired(_definition.MinionId), _definition.MinionHealth);
            Brain = new SummonerEnemyBrain(_definition);
            _spawn = transform.position; _spawnRotation = transform.rotation;
            _leash = GetComponent<EncounterLeash>();
            _retreatPath = new NavMeshPath();
            _agent.speed = _definition.Combat.MoveSpeed; _agent.angularSpeed = _definition.Combat.RotationSpeed;
            _agent.updateRotation = false;
            if (_animator != null && _animationSet != null)
            {
                _animator.runtimeAnimatorController = _animationSet.Controller; _animator.applyRootMotion = false;
                _visualAnchor = _animator.transform.localPosition; _visualRotation = _animator.transform.localRotation;
            }
            ShowTelegraph(false);
            RefreshIdentityCrown();
        }

        private void Update()
        {
            if (!HasSimulationAuthority || Brain == null) return;
            _hitAwareness.Tick(Time.deltaTime);
            if (_target == null || !_target.IsAvailable || (_leash != null && !_leash.AllowsTarget(_target.transform.position))) _hitAwareness.Clear();
            PruneDeadMinions();
            if (_executionHold > 0f) { _executionHold = Mathf.Max(0f, _executionHold - Time.deltaTime); StopAgent(); Present(); return; }
            if (Time.time >= _nextPerception) { RefreshPerception(); _nextPerception = Time.time + .14f; }
            RangedEnemyState before = Brain.State;
            Brain.Tick(Time.deltaTime, _perception);
            if (Brain.State == RangedEnemyState.Windup && before != RangedEnemyState.Windup)
            {
                Vector3 facing = _target != null ? Vector3.ProjectOnPlane(_target.transform.position - transform.position, Vector3.up) : Vector3.zero;
                _committedFacing = facing.sqrMagnitude > .001f ? Quaternion.LookRotation(facing) : transform.rotation;
            }
            Move();
            if (Brain.IsReleaseOpen && _releasedSequence != Brain.AttackSequence)
            {
                _releasedSequence = Brain.AttackSequence;
                if (_target != null && _target.IsAvailable && (_leash == null || _leash.AllowsTarget(_target.transform.position)))
                { if (Brain.CurrentAttack == SummonerAttackKind.Summon) SpawnMinion(); else ReleaseProjectile(); }
            }
            Present();
        }

        public override DamageResult ReceiveDamage(DamageRequest request)
        {
            if (!HasSimulationAuthority || Brain == null) return DamageResult.Ignored;
            int interrupts = Brain.InterruptCount;
            DamageResult result = Brain.ReceiveDamage(request);
            if (_hitAwareness.Record(request, result, _target == null ? 0 : _target.CombatantId,
                _target != null && _target.IsAvailable,
                _leash == null || (_target != null && _leash.AllowsTarget(_target.transform.position)),
                _target == null || _target.Model == null ? 0f : _target.Model.EnemyHitAwarenessSeconds)) _nextPerception = 0f;
            if (result.AppliedDamage > 0f) _nextPerception = 0f;
            RecordSummonInterrupt(interrupts);
            if (result.Killed) { _summonInterruptedUntil = float.NegativeInfinity; ClearOwnedEntities(true); StopAgent(); _body.enabled = false; Died?.Invoke(this); }
            Present();
            return result;
        }
        public override float ApplyNeutralPostureDamage(float amount)
        {
            if (!HasSimulationAuthority || !IsAvailable) return 0f;
            float before = Brain.Posture.Current;
            int interrupts = Brain.InterruptCount;
            Brain.ApplyCounterPosture(amount); RecordSummonInterrupt(interrupts); Present();
            return before - Brain.Posture.Current;
        }
        private void RecordSummonInterrupt(int previousCount)
        {
            if (Brain.InterruptCount <= previousCount) return;
            _summonInterruptedUntil = Time.time + 1.4f;
            Debug.Log($"[SUMMONER] interrupted encounter={_encounterId} owner={CombatantId}", this);
        }
        public bool TryClaimExecution() { if (!IsExecutionEligible) return false; _claimed = true; return true; }
        public void HoldForExecution(float seconds)
        { _executionHold = Mathf.Max(_executionHold, seconds); Brain?.ResetRetreatConstraint(); ResetRetreatNavigation(); _nextPerception = 0f; StopAgent(); }
        public void ApplySweepImpulse(Vector3 source, float distance)
        {
            if (!HasSimulationAuthority || !IsAvailable || !_agent.enabled || !_agent.isOnNavMesh) return;
            Vector3 desired = transform.position + Vector3.ProjectOnPlane(transform.position - source, Vector3.up).normalized * Mathf.Max(0f, distance);
            if (_leash != null) _leash.ApplyDisplacement(desired);
            else { if (_agent.Raycast(desired, out NavMeshHit obstruction)) desired = Vector3.MoveTowards(obstruction.position, transform.position, .05f); _agent.Warp(desired); }
        }
        public void SetSimulationAuthority(bool value)
        {
            HasSimulationAuthority = value;
            if (!value) { _summonInterruptedUntil = float.NegativeInfinity; _hitAwareness.Clear(); Brain?.ResetRetreatConstraint(); ResetRetreatNavigation(); _nextPerception = 0f; ClearOwnedEntities(); StopAgent(); ShowTelegraph(false); }
        }
        public void ResetToSpawn()
        {
            _summonInterruptedUntil = float.NegativeInfinity;
            _hitAwareness.Clear(); ClearOwnedEntities(); if (Brain == null) return;
            Brain.Reset(); _claimed = false; _executionHold = 0f; _releasedSequence = 0; _firstSummonBeat = false;
            ResetRetreatNavigation();
            SpawnedEntityCount = 0; _nextPerception = 0f; _body.enabled = true;
            StopAgent(); if (_agent.enabled && _agent.isOnNavMesh) _agent.Warp(_spawn); else transform.position = _spawn;
            transform.rotation = _spawnRotation; _committedFacing = _spawnRotation; _presentedState = null; Present();
        }

        private void SpawnMinion()
        {
            int slot = -1;
            for (int i = 0; i < _minions.Length; i++) if (_minions[i] == null) { slot = i; break; }
            if (slot < 0) return;
            Vector3 desired = transform.position + transform.right * (slot == 0 ? -1.3f : 1.3f) + transform.forward * 1.2f;
            if (_leash != null) desired = _leash.ClampDestination(desired);
            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, .8f, _agent.areaMask) || (_leash != null && !_leash.Contains(hit.position, .42f))) return;
            var root = new GameObject("Summoned_Fogwalker"); root.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, gameObject.scene);
            root.transform.position = hit.position;
            MeleeEnemyActor actor = null; bool committed = false;
            try
            {
            // All creation is synchronous and inactive until the domain accepts this exact release token.
            var agent = root.AddComponent<NavMeshAgent>(); agent.radius = .42f; agent.height = 2f; agent.acceleration = 12f;
            var body = root.AddComponent<CapsuleCollider>(); body.center = Vector3.up * 1.05f; body.height = 2f; body.radius = .42f;
            GameObject visual = Instantiate(_minionStyle.VisualPrefab, root.transform); visual.transform.localPosition = Vector3.up * 1.05f;
            var aim = new GameObject("AimPoint").transform; aim.SetParent(root.transform, false); aim.localPosition = Vector3.up * 1.7f;
            var attack = new GameObject("AttackOrigin").transform; attack.SetParent(root.transform, false); attack.localPosition = new Vector3(0, 1.35f, 1.02f);
            actor = root.AddComponent<MeleeEnemyActor>();
            actor.Configure(_meleeJson, _minionDefinition.Id.Value, _target, agent, aim, attack, visual.GetComponentInChildren<Renderer>(), body);
            actor.ConfigureRuntimeSpawn(_minionDefinition);
            if (_leash != null) root.AddComponent<EncounterLeash>().Configure(_leash.Encounter);
            Animator animator = visual.GetComponentInChildren<Animator>();
            if (_minionWeapon != null) AttachMinionWeapon(animator);
            root.AddComponent<SummonedMinionPresentation>().Configure(_minionStyle);
            if (animator != null && _animationSet != null) root.AddComponent<MeleeEnemyAnimationPresenter>().Configure(animator, actor, _animationSet);
            if (!Brain.TryRegisterSummon(actor.CombatantId, Brain.AttackSequence)) return;
            _minions[slot] = actor; _minionIds[slot] = actor.CombatantId;
            actor.Died += OnMinionDied; root.SetActive(true);
            if (!actor.IsAvailable) throw new InvalidOperationException("Activated summon did not initialize its combat model.");
            committed = true; SpawnedEntityCount++;
            Debug.Log($"[SUMMONER] summoner:summoned encounter={_encounterId} owner={CombatantId} alive={LivingEntityCount} total={SpawnedEntityCount}", this);
            if (!_firstSummonBeat)
            {
                _firstSummonBeat = true;
                // This receipt follows a real accepted entity. The route collector deduplicates it per encounter/session.
                Debug.Log($"[SUMMONER] first-summon encounter={_encounterId}", this);
                FirstSummonReleased?.Invoke(this);
            }
            }
            finally
            {
                if (!committed)
                {
                    if (actor != null) { Brain.UnregisterSummon(actor.CombatantId); actor.Died -= OnMinionDied; }
                    _minions[slot] = null; _minionIds[slot] = 0;
                    root.SetActive(false); Destroy(root);
                }
            }
        }
        private void AttachMinionWeapon(Animator animator)
        {
            if (animator == null || !animator.isHuman) throw new InvalidOperationException("Armed minion requires its configured Human rig.");
            Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand == null) throw new InvalidOperationException("Armed minion is missing RightHand.");
            var socket = new GameObject("SummonedWeaponSocket_RightHand").transform; socket.SetParent(hand, false);
            Vector3 scale = socket.lossyScale;
            if (Mathf.Abs(scale.x) < .0001f || Mathf.Abs(scale.y) < .0001f || Mathf.Abs(scale.z) < .0001f)
                throw new InvalidOperationException("Armed minion has a zero-scale hand.");
            socket.localScale = new Vector3(1f / scale.x, 1f / scale.y, 1f / scale.z);
            // Existing Fogwalker metre-space grip, not the imported FBX's compensated bone units.
            socket.position = hand.position + hand.rotation * new Vector3(0f, .015f, 0f);
            Instantiate(_minionWeapon, socket, false);
        }
        private void OnMinionDied(MeleeEnemyActor actor)
        {
            Brain.UnregisterSummon(actor.CombatantId);
            for (int i = 0; i < _minions.Length; i++) if (_minions[i] == actor) { _minions[i] = null; _minionIds[i] = 0; break; }
            actor.Died -= OnMinionDied; actor.gameObject.SetActive(false); Destroy(actor.gameObject);
        }
        private void PruneDeadMinions()
        {
            for (int i = 0; i < _minions.Length; i++)
            {
                var m = _minions[i];
                if (!ReferenceEquals(m, null) && (m == null || !m.gameObject.activeInHierarchy || !m.IsAvailable))
                {
                    Brain.UnregisterSummon(_minionIds[i]);
                    if (m != null) { m.Died -= OnMinionDied; m.DespawnRuntimeEntity(); m.gameObject.SetActive(false); Destroy(m.gameObject); }
                    _minions[i] = null; _minionIds[i] = 0;
                }
            }
        }
        private void ClearOwnedEntities(bool ownerDeath = false)
        {
            ClearDissolves();
            for (int i = 0; i < _minions.Length; i++)
            {
                var m = _minions[i]; _minions[i] = null; _minionIds[i] = 0;
                if (m == null) continue;
                Vector3 position = m.transform.position;
                m.Died -= OnMinionDied; m.DespawnRuntimeEntity(); m.gameObject.SetActive(false); Destroy(m.gameObject);
                if (ownerDeath) _dissolves[i] = SummonedMinionDissolve.Create(_minionStyle, transform, position);
            }
            Brain?.ClearSummons();
            for (int i = 0; i < _projectiles.Length; i++)
            { var p = _projectiles[i]; _projectiles[i] = null; if (p != null) { p.gameObject.SetActive(false); Destroy(p.gameObject); } }
        }
        private void ClearDissolves()
        {
            for (int i = 0; i < _dissolves.Length; i++)
            {
                var effect = _dissolves[i]; _dissolves[i] = null;
                if (effect != null) { effect.gameObject.SetActive(false); Destroy(effect.gameObject); }
            }
        }
        private void ReleaseProjectile()
        {
            int slot = Array.FindIndex(_projectiles, p => p == null); if (slot < 0) return;
            var root = GameObject.CreatePrimitive(PrimitiveType.Sphere); root.name = "AshCallerProjectile";
            root.transform.position = _castOrigin.position; root.transform.localScale = Vector3.one * _definition.Combat.ProjectileRadius;
            var collider = root.GetComponent<Collider>(); collider.enabled = false; Destroy(collider);
            root.GetComponent<Renderer>().sharedMaterial = _projectileMaterial;
            var p = root.AddComponent<RangedProjectile>(); _projectiles[slot] = p; var c = _definition.Combat;
            p.Launch(transform, CombatantId, Brain.AttackSequence, _target.AimPoint.position - _castOrigin.position,
                c.ProjectileSpeed, c.ProjectileRadius, c.ProjectileLifetime, c.AttackDamage, c.PostureDamage, HasSimulationAuthority, null);
        }
        private void RefreshPerception()
        {
            bool available = _target != null && _target.IsAvailable && (_leash == null || _leash.AllowsTarget(_target.transform.position));
            bool visible = available;
            if (available)
            {
                Vector3 direction = _target.AimPoint.position - AimPoint.position;
                visible = _hitAwareness.IsAwareOf(_target.CombatantId) || Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(direction, Vector3.up)) <= _definition.Combat.FieldOfView * .5f;
                if (visible && Physics.Raycast(AimPoint.position, direction.normalized, out RaycastHit hit, direction.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    visible = hit.collider.GetComponentInParent<PlayerCombatActor>() == _target;
            }
            if (available && Brain.State == RangedEnemyState.Retreat) AssessRetreatNavigation();
            else ResetRetreatNavigation();
            _perception = new RangedEnemyPerception(available, visible, available ? Vector3.Distance(transform.position, _target.transform.position) : float.PositiveInfinity,
                Vector3.Distance(transform.position, _spawn), _retreatUnavailable);
        }
        private void ResetRetreatNavigation()
        {
            _hasRetreatDestination = _retreatSampleReady = _retreatUnavailable = false;
            RetreatHasPreferredDestination = false;
        }
        private void AssessRetreatNavigation()
        {
            bool previouslyPreferred = _hasRetreatDestination && RetreatHasPreferredDestination;
            bool progressing = Vector3.ProjectOnPlane(transform.position - _retreatSamplePosition, Vector3.up).sqrMagnitude > .0001f;
            _hasRetreatDestination = _agent.enabled && _agent.isOnNavMesh && TryPlanRetreat(out _retreatDestination);
            // Small avoidance motion is not evidence that the preferred range can be restored.
            // A complete, contained preferred path gets one perception interval to start moving.
            _retreatUnavailable = !_hasRetreatDestination || !RetreatHasPreferredDestination ||
                (_retreatSampleReady && previouslyPreferred && !progressing && !_agent.pathPending);
            _retreatSampleReady = true; _retreatSamplePosition = transform.position;
        }
        private bool TryPlanRetreat(out Vector3 destination)
        {
            destination = transform.position; RetreatHasPreferredDestination = false;
            RetreatCandidatesExamined = RetreatNoNavCandidates = RetreatOutsideCandidates = RetreatIncompleteCandidates = 0;
            Vector3 away = Vector3.ProjectOnPlane(transform.position - _target.transform.position, Vector3.up);
            away = away.sqrMagnitude > .001f ? away.normalized : -transform.forward;
            float bestDistance = Vector3.Distance(transform.position, _target.transform.position);
            RetreatBestReachableDistance = bestDistance;
            if (TryRetreatCandidate(transform.position + away * 3f, out Vector3 straight))
            {
                float distance = Vector3.Distance(straight, _target.transform.position);
                RetreatBestReachableDistance = Mathf.Max(RetreatBestReachableDistance, distance);
                if (distance >= _definition.Combat.PreferredMinimumRange)
                { RetreatHasPreferredDestination = true; destination = straight; return true; }
                if (distance > bestDistance + .05f) { bestDistance = distance; destination = straight; }
            }
            var encounter = _leash == null ? null : _leash.Encounter;
            float bestPreferredTravel = float.PositiveInfinity;
            // Nine bounded candidates in total; no trial-writing agent destinations or per-frame buffers.
            for (int i = 0; i < 8; i++)
            {
                Vector3 desired;
                if (encounter != null)
                {
                    Vector2 half = encounter.ArenaHalfExtents;
                    int x = i < 3 ? -1 : i < 5 ? 0 : 1;
                    int z = i < 3 ? i - 1 : i < 5 ? (i == 3 ? -1 : 1) : i - 6;
                    desired = encounter.ArenaCenter + new Vector3(x * half.x, 0, z * half.y);
                    desired.y = transform.position.y;
                }
                else desired = transform.position + Quaternion.Euler(0, i * 45f, 0) * away * 3f;
                if (!TryRetreatCandidate(desired, out Vector3 candidate)) continue;
                float distance = Vector3.Distance(candidate, _target.transform.position);
                RetreatBestReachableDistance = Mathf.Max(RetreatBestReachableDistance, distance);
                if (distance >= _definition.Combat.PreferredMinimumRange)
                {
                    float travel = (candidate - transform.position).sqrMagnitude;
                    if (travel >= bestPreferredTravel) continue;
                    bestPreferredTravel = travel; RetreatHasPreferredDestination = true; destination = candidate;
                }
                else if (!RetreatHasPreferredDestination && distance > bestDistance + .05f)
                { bestDistance = distance; destination = candidate; }
            }
            return (destination - transform.position).sqrMagnitude > .01f;
        }
        private bool TryRetreatCandidate(Vector3 desired, out Vector3 candidate)
        {
            RetreatCandidatesExamined++; candidate = transform.position;
            if (_leash != null) desired = _leash.ClampDestination(desired);
            if (!NavMesh.SamplePosition(desired, out NavMeshHit hit, .8f, _agent.areaMask))
            { RetreatNoNavCandidates++; return false; }
            if (_leash != null && !_leash.Contains(hit.position, _agent.radius))
            { RetreatOutsideCandidates++; return false; }
            if (!_agent.CalculatePath(hit.position, _retreatPath) || _retreatPath.status != NavMeshPathStatus.PathComplete)
            { RetreatIncompleteCandidates++; return false; }
            int count = _retreatPath.GetCornersNonAlloc(_retreatCorners);
            if (count == 0 || count >= _retreatCorners.Length) { RetreatIncompleteCandidates++; return false; }
            for (int i = 0; i < count; i++)
                if (_leash != null && !_leash.Contains(_retreatCorners[i])) { RetreatOutsideCandidates++; return false; }
            candidate = hit.position; return true;
        }
        private void Move()
        {
            if (!_agent.enabled || !_agent.isOnNavMesh) return;
            Vector3 destination = transform.position; bool move = true;
            switch (Brain.State)
            {
                case RangedEnemyState.Approach: destination = _target.NavigationFootPosition; _agent.stoppingDistance = _definition.Combat.PreferredMaximumRange * .9f; break;
                case RangedEnemyState.Retreat: destination = _retreatDestination; move = _hasRetreatDestination; _agent.stoppingDistance = .1f; break;
                case RangedEnemyState.Return: destination = _spawn; _agent.stoppingDistance = .15f; break;
                default: move = false; break;
            }
            if (move)
            {
                bool accepted;
                if (_leash != null) accepted = _leash.SetDestination(destination);
                else { _agent.isStopped = false; accepted = _agent.SetDestination(destination); }
                if (!accepted && Brain.State == RangedEnemyState.Retreat) _retreatUnavailable = true;
            }
            else StopAgent();
            if (_target != null && Brain.State != RangedEnemyState.Idle && Brain.State != RangedEnemyState.Dead && Brain.State != RangedEnemyState.HitReact)
            {
                Vector3 facing = Vector3.ProjectOnPlane(_target.transform.position - transform.position, Vector3.up);
                bool committed = Brain.State == RangedEnemyState.Windup || Brain.State == RangedEnemyState.Release || Brain.State == RangedEnemyState.Recovery;
                if (facing.sqrMagnitude > .001f) transform.rotation = Quaternion.RotateTowards(transform.rotation,
                    committed ? _committedFacing : Quaternion.LookRotation(facing), _definition.Combat.RotationSpeed * Time.deltaTime);
            }
        }
        private void StopAgent() { if (_agent != null && _agent.enabled && _agent.isOnNavMesh) { _agent.isStopped = true; _agent.ResetPath(); } }
        private void ShowTelegraph(bool value) { if (_telegraph != null && _telegraph.gameObject.activeSelf != value) _telegraph.gameObject.SetActive(value); }
        private void Present()
        {
            RefreshIdentityCrown();
            if (Brain == null) return;
            ShowTelegraph(HasSimulationAuthority && Brain.State == RangedEnemyState.Windup);
            if (_telegraph != null && Brain.State == RangedEnemyState.Windup)
                _telegraph.localScale = Vector3.one * Mathf.Lerp(.2f, Brain.CurrentAttack == SummonerAttackKind.Summon ? 1.15f : .55f, Mathf.Clamp01(Brain.StateElapsed / Brain.CurrentWindupDuration));
            if (_animator == null || _animationSet == null) return;
            _animator.SetFloat("Speed", _agent.enabled && _agent.isOnNavMesh ? _agent.velocity.magnitude : 0f);
            string state = Brain.State == RangedEnemyState.Dead ? "Dead" : Brain.State == RangedEnemyState.HitReact ? "HitReact" :
                (Brain.State == RangedEnemyState.Windup || Brain.IsReleaseOpen) && Brain.CurrentAttack == SummonerAttackKind.Summon ? "EnemyRuneCast" :
                Brain.State == RangedEnemyState.Windup ? "PriestProjectileWindup" : Brain.IsReleaseOpen ? "PriestProjectileRelease" : "Locomotion";
            if (state == _presentedState) return; _presentedState = state;
            AnimationClip clip = state == "EnemyRuneCast" ? _animationSet.GetEnemyClip(EnemyAnimationAction.RuneCast) :
                state == "PriestProjectileWindup" ? _animationSet.GetEnemyClip(EnemyAnimationAction.PriestProjectileWindup) :
                state == "PriestProjectileRelease" ? _animationSet.GetEnemyClip(EnemyAnimationAction.PriestProjectileRelease) :
                state == "HitReact" ? _animationSet.GetClip(CombatState.HitReact) : null;
            float duration = state == "EnemyRuneCast" ? _definition.SummonWindup + _definition.Combat.ReleaseDuration :
                state == "PriestProjectileWindup" ? _definition.Combat.WindupDuration :
                state == "HitReact" ? _definition.Combat.HitReactDuration : _definition.Combat.ReleaseDuration;
            AnimatorSpeedCoordinator.SetBase(_animator, clip != null ? Mathf.Clamp(clip.length / duration, .25f, 3f) : 1f, state == "Dead");
            // Stop the abandoned chant promptly while keeping a non-zero blend. Only the
            // existing visual clip is fitted; the brain still owns the original .35s window.
            _animator.CrossFadeInFixedTime(state, state == "HitReact" ? .04f : .08f, 0, 0f);
        }
        private void LateUpdate() { if (_animator != null) { _animator.transform.localPosition = _visualAnchor; _animator.transform.localRotation = _visualRotation; } }
        private void OnEnable() => RefreshIdentityCrown();
        private void OnDisable() { _summonInterruptedUntil = float.NegativeInfinity; Brain?.ResetRetreatConstraint(); ResetRetreatNavigation(); _nextPerception = 0f; ClearOwnedEntities(); StopAgent(); ShowTelegraph(false); if (_identityCrown != null) _identityCrown.SetActive(false); }
    }
}
