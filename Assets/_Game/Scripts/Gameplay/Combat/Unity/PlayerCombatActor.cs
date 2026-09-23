using System;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Input;
using Emberfall.Core.Identifiers;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    [DefaultExecutionOrder(-200)]
    public sealed class PlayerCombatActor : CombatTarget
    {
        private const int HitBufferSize = 24;

        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private CombatTuningAsset _tuning;
        [SerializeField] private Transform _attackOrigin;
        [SerializeField] private Transform _aimPoint;
        [SerializeField] private CharacterController _characterController;
        [SerializeField] private Renderer _bodyRenderer;
        [SerializeField, Min(0.1f)] private float _attackRadius = MeleeSectorRules.DefaultRadius;
        [SerializeField, Range(1f, 360f)] private float _attackAngle = MeleeSectorRules.DefaultFullAngleDegrees;
        [SerializeField, Min(0f)] private float _armor = 4f;
        [SerializeField, Min(0.1f)] private float _respawnDelay = 2.2f;

        private readonly Collider[] _hitBuffer = new Collider[HitBufferSize];
        private readonly HitRegistry _hitRegistry = new HitRegistry();
        private CombatStateMachine _model;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;
        private Color _baseColor;
        private ContentId _activeCheckpointId;
        private bool _guardCounterReady;
        private int _presentedHealSequence;
        private int _presentedRangedReleaseSequence;
        private int _presentedExecutionResolveSequence;
        private int _countedAttackSequence = -1;
        private int _currentAttackHitCount;
        private bool _attackHitMetricPending;
        private bool _countedAttackWasSweep;
        private IExecutionTarget _executionTarget;
        private IExecutionTarget _executionPromptTarget;
        private float _executionPromptRefreshRemaining;

        public override int CombatantId => GetInstanceID();
        public override Transform AimPoint => _aimPoint != null ? _aimPoint : transform;
        public override bool IsAvailable => _model != null && !_model.IsDead;
        public override float HealthNormalized => _model?.Health.Normalized ?? 0f;
        public override bool HasSecondaryResource => true;
        public override float SecondaryResourceNormalized => _model?.Posture.Normalized ?? 0f;
        public CombatStateMachine Model => _model;
        public Vector2 DodgeInput { get; private set; }
        public string LastCombatEvent { get; private set; } = "Ready";
        public ContentId ActiveCheckpointId => _activeCheckpointId;
        public Vector3 RespawnPosition => _spawnPosition;
        public Quaternion RespawnRotation => _spawnRotation;
        public RuneBlessing ActiveRuneBlessing { get; private set; }
        public bool IsGuardCounterReady => _guardCounterReady;

        public event Action<PlayerCombatActor> Died;
        public event Action<PlayerCombatActor> Respawned;
        public event Action<CombatImpactPresentationEvent> ImpactPresented;
        public event Action<RangedAttackRelease> RangedAttackReleased;
        public event Action<PlayerCombatActor, CombatProgressKind> CombatProgressed;
        public event Action<PerfectDefenseKind> PerfectDefensePresented;
        public CombatTarget ExecutionPromptTarget =>
            _model?.State == CombatState.Locomotion &&
            _executionPromptTarget?.IsExecutionEligible == true &&
            _executionPromptTarget.CombatTarget != null &&
            _executionPromptTarget.CombatTarget.IsAvailable
                ? _executionPromptTarget.CombatTarget
                : null;

        public void Configure(
            PlayerInputReader input,
            CombatTuningAsset tuning,
            Transform attackOrigin,
            Transform aimPoint,
            CharacterController characterController,
            Renderer bodyRenderer)
        {
            _input = input;
            _tuning = tuning;
            _attackOrigin = attackOrigin;
            _aimPoint = aimPoint;
            _characterController = characterController;
            _bodyRenderer = bodyRenderer;
        }

        private void Awake()
        {
            if (_tuning == null || _input == null || _attackOrigin == null)
            {
                Debug.LogError("PlayerCombatActor is not configured.", this);
                enabled = false;
                return;
            }

            _model = new CombatStateMachine(_tuning.CreateRuntimeCopy());
            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _activeCheckpointId = new ContentId("checkpoint:player.spawn");
            if (_bodyRenderer != null)
            {
                _baseColor = _bodyRenderer.material.color;
            }
        }

        private void Update()
        {
            if (_model == null)
            {
                return;
            }

            _executionPromptRefreshRemaining -= Time.deltaTime;
            if (_executionPromptRefreshRemaining <= 0f)
            {
                _executionPromptTarget = _model.State == CombatState.Locomotion
                    ? FindNearestExecutionTarget()
                    : null;
                _executionPromptRefreshRemaining = 0.08f;
            }

            if (_input.ConsumeLightPressed())
            {
                _model.Submit(CombatCommand.LightAttack);
            }

            if (_input.ConsumeHeavyPressed())
            {
                _model.Submit(CombatCommand.HeavyPressed);
            }

            if (_input.ConsumeHeavyReleased())
            {
                _model.Submit(CombatCommand.HeavyReleased);
            }

            if (_input.ConsumeRangedPressed())
            {
                if (!_model.Submit(CombatCommand.RangedAttack))
                {
                    LastCombatEvent = _model.RangedCooldownRemaining > 0f
                        ? $"Throwing knife cooldown {_model.RangedCooldownRemaining:0.0}s"
                        : "Cannot throw now";
                }
            }

            if (_input.ConsumeSweepPressed())
            {
                if (_model.Submit(CombatCommand.Sweep))
                {
                    LastCombatEvent = "Wide sweep";
                }
                else
                {
                    LastCombatEvent = _model.SweepCooldownRemaining > 0f
                        ? $"Wide sweep cooldown {_model.SweepCooldownRemaining:0.0}s"
                        : "Cannot sweep now";
                }
            }

            if (_input.ConsumeDodgePressed())
            {
                DodgeInput = _input.Move;
                if (_model.Submit(CombatCommand.Dodge)) LogFeel("dodge-attempt", _model.DodgeAttemptCount);
            }

            if (_input.ConsumeGuardPressed())
            {
                if (_model.Submit(CombatCommand.GuardPressed)) LogFeel("guard-attempt", _model.GuardAttemptCount);
            }

            if (_input.ConsumeGuardReleased())
            {
                _model.Submit(CombatCommand.GuardReleased);
            }

            if (_input.ConsumeHealPressed())
            {
                if (!_model.Submit(CombatCommand.Heal))
                {
                    LastCombatEvent = _model.HealingFlasks.CurrentCharges <= 0
                        ? "Healing flask empty"
                        : "Cannot heal now";
                }
            }

            if (_input.InteractPressedPending && TryBeginExecution())
            {
                _input.ConsumeInteractPressed();
            }

            bool sprintRequested = _input.SprintHeld && _input.Move.sqrMagnitude > 0.01f;
            _model.SetSprintRequested(sprintRequested);

            _model.Tick(Time.deltaTime);
            BeginAttackHitMetricIfNeeded();
            if (_model.ExecutionResolveSequence != _presentedExecutionResolveSequence)
            {
                _presentedExecutionResolveSequence = _model.ExecutionResolveSequence;
                ResolveExecution();
            }
            if (_model.RangedReleaseSequence != _presentedRangedReleaseSequence)
            {
                _presentedRangedReleaseSequence = _model.RangedReleaseSequence;
                LastCombatEvent = "Throwing knife released";
                RangedAttackReleased?.Invoke(new RangedAttackRelease(
                    _model.AttackSequence,
                    _model.RangedDamage,
                    _model.RangedPostureDamage,
                    _model.RangedProjectileSpeed,
                    _model.RangedMaximumDistance));
            }
            if (_model.HealSequence != _presentedHealSequence)
            {
                _presentedHealSequence = _model.HealSequence;
                LastCombatEvent = $"Flask restored {_model.LastHealAmount:0} health";
                SpawnRuneImpact(AimPoint.position, new Color(0.28f, 1f, 0.45f));
            }
            if (_model.IsDamageWindowOpen)
            {
                QueryAttackHits();
            }
            FlushAttackHitMetricIfComplete();

            UpdatePresentation();
            if (_model.IsDead && _model.StateElapsed >= _respawnDelay)
            {
                Respawn();
            }
        }

        public override DamageResult ReceiveDamage(DamageRequest request)
        {
            DamageResult result = _model?.ReceiveDamage(request, _armor) ?? DamageResult.Ignored;
            if (result.PerfectGuard)
            {
                if (RuneBlessingRules.ArmsGuardCounter(ActiveRuneBlessing, true))
                {
                    PrimeGuardCounter();
                }
                else
                {
                    LastCombatEvent = "Perfect guard";
                }
                SpawnRuneImpact(AimPoint.position, Color.white);
                PerfectDefensePresented?.Invoke(PerfectDefenseKind.Guard);
                LogFeel("perfect-guard", _model.PerfectGuardCount);
            }
            else if (result.Defended)
            {
                LastCombatEvent = result.GuardBroken
                    ? "Player guard broken"
                    : $"Guarded · posture {result.PostureDamageApplied:0}";
            }
            else if (result.Invulnerable)
            {
                if (result.PerfectDodge && RuneBlessingRules.ArmsGuardCounter(ActiveRuneBlessing, true))
                {
                    PrimeGuardCounter(false);
                }
                if (result.PerfectDodge)
                {
                    LastCombatEvent = "Perfect dodge · empowered attack ready";
                    SpawnRuneImpact(AimPoint.position, new Color(0.25f, 0.95f, 1f));
                    PerfectDefensePresented?.Invoke(PerfectDefenseKind.Dodge);
                    LogFeel("perfect-dodge", _model.PerfectDodgeCount);
                }
                else
                {
                    LastCombatEvent = "Evaded damage";
                }
            }
            else if (result.Accepted)
            {
                LastCombatEvent = result.Killed ? "Player downed" : $"Received {result.AppliedDamage:0} damage";
                NotifyCombatProgress(result, CombatProgressKind.DamageReceived);
                if (result.Killed)
                {
                    Died?.Invoke(this);
                }
            }

            return result;
        }

        public override float ApplyNeutralPostureDamage(float amount)
        {
            if (_model == null || _model.IsDead) return 0f;
            float applied = _model.ApplyNeutralPostureDamage(amount);
            if (applied > 0f)
            {
                CombatProgressed?.Invoke(this, CombatProgressKind.DamageReceived);
            }
            LastCombatEvent = _model.State == CombatState.GuardBreak
                ? "Neutral posture burst broke guard"
                : $"Neutral posture burst · {applied:0}";
            SpawnRuneImpact(AimPoint.position, new Color(0.85f, 0.28f, 1f));
            return applied;
        }

        public bool ExecuteVoidFall()
        {
            if (_model == null || !_model.ForceDeath())
            {
                return false;
            }

            LastCombatEvent = "Fell into the void";
            Died?.Invoke(this);
            return true;
        }

        public bool ActivateCheckpoint(ContentId checkpointId, Vector3 position, Quaternion rotation)
        {
            if (_model == null || _model.IsDead || checkpointId.IsEmpty ||
                !checkpointId.Value.StartsWith("checkpoint:", System.StringComparison.Ordinal))
            {
                return false;
            }

            _activeCheckpointId = checkpointId;
            _spawnPosition = position;
            _spawnRotation = rotation;
            _model.Reset();
            _guardCounterReady = false;
            LastCombatEvent = $"Checkpoint activated: {checkpointId}";
            return true;
        }

        public bool RestoreCheckpoint(ContentId checkpointId, Vector3 position, Quaternion rotation, bool teleport)
        {
            if (_model == null || checkpointId.IsEmpty ||
                !checkpointId.Value.StartsWith("checkpoint:", StringComparison.Ordinal))
            {
                return false;
            }

            _activeCheckpointId = checkpointId;
            _spawnPosition = position;
            _spawnRotation = rotation;
            _model.Reset();
            _guardCounterReady = false;
            if (teleport)
            {
                MoveToRespawnTransform();
            }

            LastCombatEvent = "Checkpoint restored";
            return true;
        }

        public void ApplyRuneBlessing(RuneBlessing blessing)
        {
            ActiveRuneBlessing = blessing;
            _guardCounterReady = false;
            LastCombatEvent = blessing switch
            {
                RuneBlessing.Ember => "Ember rune equipped",
                RuneBlessing.Guard => "Guard rune equipped",
                _ => "Rune blessing cleared"
            };
        }

        public bool TryUseSupply(float healthAmount, float staminaAmount)
        {
            if (_model == null || _model.IsDead || healthAmount <= 0f || staminaAmount <= 0f)
            {
                return false;
            }

            float health = _model.Health.Restore(healthAmount);
            float stamina = _model.Stamina.Restore(staminaAmount);
            LastCombatEvent = $"Supply restored {health:0} health / {stamina:0} stamina";
            return true;
        }

        public bool ApplyHealingFlaskCapacityBonus(int amount)
        {
            if (_model == null || amount <= 0)
            {
                return false;
            }

            _model.HealingFlasks.IncreaseMaximum(amount);
            LastCombatEvent = $"Healing flask capacity increased to {_model.HealingFlasks.MaximumCharges}";
            return true;
        }

        public void PresentRangedImpact(CombatTarget target, DamageResult result, Vector3 position)
        {
            if (target == null)
            {
                LastCombatEvent = "Throwing knife hit terrain";
                return;
            }

            if (result.Accepted || result.Blocked || result.Staggered)
            {
                ImpactPresented?.Invoke(new CombatImpactPresentationEvent(
                    position,
                    result.Blocked || result.Staggered ? CombatImpactStyle.Guard : CombatImpactStyle.Steel));
                NotifyCombatProgress(result, CombatProgressKind.DamageDealt);
            }

            LastCombatEvent = result.GuardBroken ? "Throwing knife broke guard" :
                result.Blocked ? "Throwing knife blocked" :
                result.Staggered ? "Throwing knife broke posture" :
                result.Accepted ? $"Throwing knife hit: {result.AppliedDamage:0}" :
                "Throwing knife missed";
        }

        private void QueryAttackHits()
        {
            bool sweep = _model.State == CombatState.Sweep;
            float radius = sweep ? _model.SweepRadius : _attackRadius;
            float angle = sweep ? _model.SweepAngle : _attackAngle;
            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                radius,
                _hitBuffer,
                ~0,
                QueryTriggerInteraction.Collide);

            for (int i = 0; i < count; i++)
            {
                CombatTarget target = _hitBuffer[i].GetComponentInParent<CombatTarget>();
                if (target == null || target == this || !target.IsAvailable ||
                    !MeleeSectorRules.Contains(
                        transform.position.x,
                        transform.position.z,
                        transform.forward.x,
                        transform.forward.z,
                        target.AimPoint.position.x,
                        target.AimPoint.position.z,
                        radius,
                        angle) ||
                    !_hitRegistry.TryRegister(_model.AttackSequence, target.CombatantId))
                {
                    continue;
                }

                bool emberBurst = ActiveRuneBlessing == RuneBlessing.Ember &&
                                  _model.CurrentAttackTag == AttackTag.Heavy &&
                                  _model.IsHeavyFullyCharged;
                bool guardCounter = ActiveRuneBlessing == RuneBlessing.Guard &&
                                    _guardCounterReady &&
                                    _model.CurrentAttackTag == AttackTag.Light;
                float bonusDamage = RuneBlessingRules.GetBonusDamage(
                    ActiveRuneBlessing,
                    _model.CurrentAttackTag,
                    _guardCounterReady,
                    _model.IsHeavyFullyCharged);
                var request = new DamageRequest(
                    CombatantId,
                    _model.AttackSequence,
                    _model.CurrentAttackDamage + bonusDamage,
                    ResolveCurrentPostureDamage(guardCounter),
                    _model.CurrentAttackTag);
                DamageResult result = target.ReceiveDamage(request);
                if (sweep && (result.Accepted || result.Staggered))
                {
                    ApplySweepReaction(target);
                }
                _currentAttackHitCount++;
                NotifyCombatProgress(result, CombatProgressKind.DamageDealt);
                CombatImpactStyle impactStyle = result.Blocked || result.Staggered
                    ? CombatImpactStyle.Guard
                    : emberBurst
                        ? CombatImpactStyle.Ember
                        : CombatImpactStyle.Steel;
                if (result.Accepted || result.Blocked || result.Staggered)
                {
                    ImpactPresented?.Invoke(new CombatImpactPresentationEvent(
                        target.AimPoint.position,
                        impactStyle));
                }
                if (result.Blocked)
                {
                    LastCombatEvent = result.GuardBroken ? "Enemy guard broken" : "Attack blocked by shield";
                }
                if (result.Staggered)
                {
                    LastCombatEvent = "Enemy posture broken";
                }
                else if (result.Accepted)
                {
                    if (emberBurst)
                    {
                        LastCombatEvent = $"Ember burst: {result.AppliedDamage:0}";
                    }
                    else if (guardCounter)
                    {
                        _guardCounterReady = false;
                        _model.Stamina.Restore(RuneBlessingRules.GuardCounterStaminaRestore);
                        LastCombatEvent = $"Guard counter: {result.AppliedDamage:0}";
                    }
                    else
                    {
                        LastCombatEvent = $"{_model.CurrentAttackTag} hit: {result.AppliedDamage:0}";
                    }
                }
            }
        }

        private void BeginAttackHitMetricIfNeeded()
        {
            if (!_model.IsAttacking || _model.CurrentAttackTag == AttackTag.Projectile ||
                _model.AttackSequence == _countedAttackSequence)
                return;

            FlushAttackHitMetric();
            _countedAttackSequence = _model.AttackSequence;
            _currentAttackHitCount = 0;
            _countedAttackWasSweep = _model.State == CombatState.Sweep;
            _attackHitMetricPending = true;
        }

        private void FlushAttackHitMetricIfComplete()
        {
            if (_attackHitMetricPending && !_model.IsAttacking) FlushAttackHitMetric();
        }

        private void FlushAttackHitMetric()
        {
            if (!_attackHitMetricPending) return;
            LogFeel("attack-hit-count", _currentAttackHitCount, _countedAttackSequence);
            if (_countedAttackWasSweep)
                LogFeel("sweep", _currentAttackHitCount, _countedAttackSequence);
            _attackHitMetricPending = false;
        }

        private void ApplySweepReaction(CombatTarget target)
        {
            MonoBehaviour[] behaviours = target.GetComponentsInParent<MonoBehaviour>();
            for (int i = 0; i < behaviours.Length; i++)
            {
                if (behaviours[i] is not ISweepReactive reactive) continue;
                reactive.ApplySweepImpulse(transform.position, 0.55f);
                return;
            }
        }

        private void NotifyCombatProgress(DamageResult result, CombatProgressKind kind)
        {
            if (!result.Accepted && result.AppliedDamage <= 0f && result.PostureDamageApplied <= 0f &&
                !result.Staggered && !result.GuardBroken)
            {
                return;
            }

            CombatProgressed?.Invoke(this, kind);
        }

        private float ResolveCurrentPostureDamage(bool guardCounter)
        {
            if (_model.CurrentAttackTag == AttackTag.Heavy)
            {
                return 60f + _model.CurrentAttackPostureBonus +
                    (ActiveRuneBlessing == RuneBlessing.Ember && _model.IsHeavyFullyCharged
                    ? RuneBlessingRules.EmberHeavyBonusPostureDamage
                    : 0f);
            }

            if (_model.CurrentAttackTag == AttackTag.Sweep)
            {
                return 34f + _model.CurrentAttackPostureBonus;
            }

            float postureDamage = _model.State switch
            {
                CombatState.LightAttack2 => 20f,
                CombatState.LightAttack3 => 38f,
                _ => 18f
            };
            postureDamage += _model.CurrentAttackPostureBonus;
            return guardCounter ? postureDamage + 18f : postureDamage;
        }

        private void PrimeGuardCounter(bool restoreStamina = true)
        {
            _guardCounterReady = true;
            if (restoreStamina)
                _model.Stamina.Restore(RuneBlessingRules.GuardEvadeStaminaRestore);
            LastCombatEvent = "Guard rune counter primed";
        }

        private bool TryBeginExecution()
        {
            if (_model == null || _model.State != CombatState.Locomotion) return false;
            IExecutionTarget best = FindNearestExecutionTarget();
            if (best == null || !_model.Submit(CombatCommand.Execution) || !best.TryClaimExecution())
                return false;

            _executionPromptTarget = null;
            _executionTarget = best;
            Vector3 facing = Vector3.ProjectOnPlane(
                best.CombatTarget.transform.position - transform.position,
                Vector3.up);
            if (facing.sqrMagnitude > 0.001f)
                transform.rotation = Quaternion.LookRotation(facing.normalized, Vector3.up);
            best.HoldForExecution(_model.StateDuration);
            LastCombatEvent = best.ExecutionKind == ExecutionTargetKind.Elite
                ? "Elite execution committed"
                : "Execution committed";
            return true;
        }

        private IExecutionTarget FindNearestExecutionTarget()
        {
            if (_model == null || _model.State != CombatState.Locomotion) return null;
            int count = Physics.OverlapSphereNonAlloc(
                transform.position,
                ExecutionRules.Range,
                _hitBuffer,
                ~0,
                QueryTriggerInteraction.Collide);
            IExecutionTarget best = null;
            float bestDistance = float.PositiveInfinity;
            int bestId = int.MaxValue;
            for (int i = 0; i < count; i++)
            {
                IExecutionTarget candidate = _hitBuffer[i].GetComponentInParent<IExecutionTarget>();
                if (candidate == null || !candidate.IsExecutionEligible ||
                    candidate.CombatTarget == null || !candidate.CombatTarget.IsAvailable)
                    continue;
                Vector3 offset = Vector3.ProjectOnPlane(
                    candidate.CombatTarget.transform.position - transform.position,
                    Vector3.up);
                float distance = offset.sqrMagnitude;
                int id = candidate.CombatTarget.CombatantId;
                if (distance < bestDistance - 0.0001f ||
                    (Mathf.Abs(distance - bestDistance) <= 0.0001f && id < bestId))
                {
                    best = candidate;
                    bestDistance = distance;
                    bestId = id;
                }
            }

            return best;
        }

        private void ResolveExecution()
        {
            if (_executionTarget == null || _executionTarget.CombatTarget == null ||
                !_executionTarget.CombatTarget.IsAvailable)
            {
                _executionTarget = null;
                return;
            }

            DamageResult result = _executionTarget.CombatTarget.ReceiveDamage(new DamageRequest(
                CombatantId,
                1000000 + _model.ExecutionResolveSequence,
                _executionTarget.ExecutionDamage,
                0f,
                AttackTag.Heavy,
                false,
                true));
            NotifyCombatProgress(result, CombatProgressKind.DamageDealt);
            ImpactPresented?.Invoke(new CombatImpactPresentationEvent(
                _executionTarget.CombatTarget.AimPoint.position,
                CombatImpactStyle.Ember));
            LastCombatEvent = result.Killed ? "Execution kill" : $"Execution: {result.AppliedDamage:0}";
            LogFeel("execution", 1, _executionTarget.CombatTarget.CombatantId);
            _executionTarget = null;
        }

        private static void LogFeel(string eventName, float value, int sequence = 0)
        {
            Debug.Log($"[M5C_FEEL] event={eventName} value={value:0.###} sequence={sequence}");
        }

        private static void SpawnRuneImpact(Vector3 position, Color color)
        {
            GameObject impact = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            impact.name = "RuneBlessingImpact";
            impact.layer = 2;
            impact.transform.position = position;
            impact.transform.localScale = Vector3.one * 0.22f;
            Destroy(impact.GetComponent<Collider>());
            Renderer renderer = impact.GetComponent<Renderer>();
            renderer.material.color = color;
            impact.AddComponent<RuneBlessingImpact>().Configure(renderer.material, color);
        }

        private void Respawn()
        {
            MoveToRespawnTransform();
            _model.Reset();
            _guardCounterReady = false;
            LastCombatEvent = "Respawned";
            Respawned?.Invoke(this);
        }

        private void MoveToRespawnTransform()
        {
            if (_characterController != null)
            {
                _characterController.enabled = false;
            }

            transform.SetPositionAndRotation(_spawnPosition, _spawnRotation);
            if (_characterController != null)
            {
                _characterController.enabled = true;
            }
        }

        private void UpdatePresentation()
        {
            if (_bodyRenderer == null)
            {
                return;
            }

            Color target = _baseColor;
            if (_model.State == CombatState.HitReact)
            {
                target = new Color(1f, 0.25f, 0.2f);
            }
            else if (_model.State == CombatState.Dodge)
            {
                target = _model.IsInvulnerable ? new Color(0.25f, 0.9f, 1f) : new Color(0.4f, 0.65f, 0.8f);
            }
            else if (_model.State == CombatState.Guard)
            {
                target = _model.IsPerfectGuardWindow
                    ? new Color(1f, 0.72f, 0.18f)
                    : new Color(0.3f, 0.62f, 0.92f);
            }
            else if (_model.State == CombatState.GuardBreak)
            {
                target = new Color(1f, 0.2f, 0.08f);
            }
            else if (_model.State == CombatState.Heal)
            {
                target = new Color(0.3f, 0.92f, 0.48f);
            }
            else if (_model.State == CombatState.Dead)
            {
                target = Color.gray;
            }

            _bodyRenderer.material.color = Color.Lerp(_bodyRenderer.material.color, target, Time.deltaTime * 14f);
        }

        private void OnDrawGizmosSelected()
        {
            if (_attackOrigin == null)
            {
                return;
            }

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, _attackRadius);
        }
    }

    public enum PerfectDefenseKind
    {
        Guard = 0,
        Dodge = 1
    }

    internal sealed class RuneBlessingImpact : MonoBehaviour
    {
        private Material _material;
        private Color _color;
        private float _elapsed;

        public void Configure(Material material, Color color)
        {
            _material = material;
            _color = color;
        }

        private void Update()
        {
            _elapsed += Time.deltaTime;
            transform.localScale = Vector3.one * Mathf.Lerp(0.22f, 1.15f, _elapsed / 0.42f);
            if (_material != null)
            {
                Color faded = _color;
                faded.a = Mathf.Clamp01(1f - (_elapsed / 0.42f));
                _material.color = faded;
            }

            if (_elapsed >= 0.42f)
            {
                Destroy(gameObject);
            }
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                Destroy(_material);
            }
        }
    }
}
