using System;

namespace Emberfall.Gameplay.Combat.Domain
{
    /// <summary>
    /// Deterministic combat rules. Unity components translate input, time, physics and presentation into this model.
    /// </summary>
    public sealed class CombatStateMachine
    {
        private readonly CombatTuning _tuning;
        private CombatCommand? _bufferedCommand;
        private float _bufferRemaining;
        private float _heavyChargeSeconds;
        private bool _healResolved;
        private bool _rangedReleased;
        private bool _executionResolved;
        private bool _sprintRequested;
        private float _sprintHeldSeconds;

        public CombatStateMachine(CombatTuning tuning)
        {
            _tuning = tuning ?? throw new ArgumentNullException(nameof(tuning));
            Health = new HealthModel(tuning.MaxHealth);
            Stamina = new StaminaModel(tuning.MaxStamina, tuning.StaminaRegenPerSecond, tuning.StaminaRegenDelay);
            Posture = new PostureModel(
                tuning.MaxPosture, tuning.PostureRegenPerSecond, tuning.PostureRegenDelay);
            HealingFlasks = new HealingFlaskModel(tuning.HealingFlaskCharges);
            State = CombatState.Locomotion;
        }

        public CombatState State { get; private set; }
        public float StateElapsed { get; private set; }
        public int AttackSequence { get; private set; }
        public HealthModel Health { get; }
        public StaminaModel Stamina { get; }
        public PostureModel Posture { get; }
        public HealingFlaskModel HealingFlasks { get; }
        public bool IsDead => State == CombatState.Dead;
        public bool IsGuarding => State == CombatState.Guard;
        public bool IsPerfectGuardWindow => IsGuarding && StateElapsed <= _tuning.PerfectGuardWindow;
        public bool IsAttacking => IsLightAttack(State) || State == CombatState.HeavyAttack || State == CombatState.Sweep;
        public bool IsDamageWindowOpen => IsAttacking && StateElapsed >= DamageOpen && StateElapsed <= DamageClose;
        public bool IsInvulnerable =>
            (State == CombatState.Dodge && StateElapsed <= _tuning.DodgeInvulnerabilitySeconds) ||
            State == CombatState.Execution;
        public bool IsPerfectDodgeWindow =>
            State == CombatState.Dodge && StateElapsed <= _tuning.PerfectDodgeWindow;
        public float StateDuration => GetStateDuration(State);
        public float StateNormalized => StateDuration <= 0f ? 0f : Math.Min(1f, StateElapsed / StateDuration);
        public float DodgeSpeed => _tuning.DodgeDistance / _tuning.DodgeDuration;
        public float DodgeDistance => _tuning.DodgeDistance;
        public float LightRecoveryStart => IsLightAttack(State)
            ? _tuning.GetLightComboOpen(LightIndex)
            : 0f;
        public float CurrentAttackDamage { get; private set; }
        public AttackTag CurrentAttackTag { get; private set; }
        public bool IsHeavyFullyCharged { get; private set; }
        public int HealSequence { get; private set; }
        public float LastHealAmount { get; private set; }
        public int RangedReleaseSequence { get; private set; }
        public float RangedCooldownRemaining { get; private set; }
        public float RangedCooldownNormalized => Math.Min(1f, RangedCooldownRemaining / _tuning.RangedCooldown);
        public bool CanUseRangedAttack => State == CombatState.Locomotion && RangedCooldownRemaining <= 0f;
        public float SweepCooldownRemaining { get; private set; }
        public float SweepCooldownNormalized => Math.Min(1f, SweepCooldownRemaining / _tuning.SweepCooldown);
        public bool CanUseSweep => State == CombatState.Locomotion && SweepCooldownRemaining <= 0f &&
                                   Stamina.Current >= _tuning.SweepStaminaCost;
        public float SweepRadius => _tuning.SweepRadius;
        public float SweepAngle => _tuning.SweepAngle;
        public float RangedDamage => _tuning.RangedDamage;
        public float RangedPostureDamage => _tuning.RangedPostureDamage;
        public float RangedProjectileSpeed => _tuning.RangedProjectileSpeed;
        public float RangedMaximumDistance => _tuning.RangedMaximumDistance;
        public bool IsSprinting { get; private set; }
        public bool PerfectDodgeAttackReady { get; private set; }
        public bool CurrentAttackEmpowered { get; private set; }
        public float CurrentAttackPostureBonus { get; private set; }
        public int DodgeAttemptCount { get; private set; }
        public int PerfectDodgeCount { get; private set; }
        public int GuardAttemptCount { get; private set; }
        public int PerfectGuardCount { get; private set; }
        public int ExecutionSequence { get; private set; }
        public int ExecutionResolveSequence { get; private set; }

        public void SetSprintRequested(bool requested)
        {
            _sprintRequested = requested;
            if (!requested)
            {
                _sprintHeldSeconds = 0f;
                IsSprinting = false;
            }
        }

        public bool Submit(CombatCommand command)
        {
            if (IsDead)
            {
                return false;
            }

            if (TryExecute(command))
            {
                _bufferedCommand = null;
                return true;
            }

            if (command == CombatCommand.LightAttack || command == CombatCommand.Dodge)
            {
                _bufferedCommand = command;
                _bufferRemaining = _tuning.InputBufferSeconds;
            }

            return false;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            StateElapsed += deltaTime;
            RangedCooldownRemaining = Math.Max(0f, RangedCooldownRemaining - deltaTime);
            SweepCooldownRemaining = Math.Max(0f, SweepCooldownRemaining - deltaTime);
            if (State == CombatState.HeavyCharge)
            {
                _heavyChargeSeconds += deltaTime;
            }

            if (State == CombatState.Heal && !_healResolved && StateElapsed >= _tuning.HealResolveTime)
            {
                ResolveHeal();
            }

            if (State == CombatState.RangedAttack && !_rangedReleased &&
                StateElapsed >= _tuning.RangedReleaseTime)
            {
                _rangedReleased = true;
                RangedReleaseSequence++;
            }

            if (State == CombatState.Execution && !_executionResolved &&
                StateElapsed >= _tuning.ExecutionResolveTime)
            {
                _executionResolved = true;
                ExecutionResolveSequence++;
            }

            TickSprint(deltaTime);
            Stamina.Tick(deltaTime,
                (State == CombatState.Locomotion || State == CombatState.Guard) && !IsSprinting);
            Posture.Tick(deltaTime, State == CombatState.Locomotion);
            TickBufferedCommand(deltaTime);
            CompleteTimedState();
            TryConsumeBufferedCommand();
        }

        public DamageResult ReceiveDamage(DamageRequest request, float armor = 0f)
        {
            if (IsDead)
            {
                return DamageResult.Ignored;
            }

            if (IsInvulnerable)
            {
                bool perfectDodge = IsPerfectDodgeWindow;
                if (perfectDodge)
                {
                    PerfectDodgeCount++;
                    Stamina.Restore(_tuning.PerfectDodgeStaminaRestore);
                    PerfectDodgeAttackReady = true;
                }

                return new DamageResult(
                    false, true, 0f, false, perfectDodge: perfectDodge);
            }

            if (IsGuarding && request.IsDefendable && request.IsInDefenderFrontArc)
            {
                bool perfect = IsPerfectGuardWindow;
                if (perfect) PerfectGuardCount++;
                float postureDamage = request.PostureDamage *
                    (perfect ? _tuning.PerfectGuardPostureMultiplier : 1f);
                float appliedPosture = Posture.ApplyDamage(postureDamage);
                float guardedRawDamage = perfect
                    ? 0f
                    : request.RawDamage * (1f - _tuning.GuardDamageReduction);
                float appliedHealth = guardedRawDamage > 0f
                    ? Health.ApplyDamage(guardedRawDamage, armor)
                    : 0f;
                bool killed = Health.IsDead;
                bool broken = Posture.IsBroken && !killed;
                if (killed) EnterState(CombatState.Dead);
                else if (broken) EnterState(CombatState.GuardBreak);

                return new DamageResult(
                    appliedHealth > 0f,
                    false,
                    appliedHealth,
                    killed,
                    false,
                    broken,
                    true,
                    perfect,
                    false,
                    appliedPosture,
                    perfect ? _tuning.PerfectGuardCounterPostureDamage : 0f);
            }

            float applied = Health.ApplyDamage(request.RawDamage, armor);
            EnterState(Health.IsDead ? CombatState.Dead : CombatState.HitReact);
            return new DamageResult(true, false, applied, Health.IsDead);
        }

        /// <summary>
        /// Executes an authoritative environment death. This intentionally bypasses dodge
        /// invulnerability so leaving the playable world cannot be evaded.
        /// </summary>
        public bool ForceDeath()
        {
            if (IsDead)
            {
                return false;
            }

            Health.ApplyDamage(Health.Current, 0f);
            EnterState(CombatState.Dead);
            return true;
        }

        public float ApplyNeutralPostureDamage(float amount)
        {
            if (IsDead) return 0f;
            float applied = Posture.ApplyDamage(amount);
            if (Posture.IsBroken)
            {
                EnterState(CombatState.GuardBreak);
            }
            return applied;
        }

        public void Reset()
        {
            Health.RestoreFull();
            Stamina.RestoreFull();
            Posture.RestoreFull();
            _bufferedCommand = null;
            _bufferRemaining = 0f;
            _heavyChargeSeconds = 0f;
            CurrentAttackDamage = 0f;
            CurrentAttackPostureBonus = 0f;
            CurrentAttackEmpowered = false;
            PerfectDodgeAttackReady = false;
            IsHeavyFullyCharged = false;
            LastHealAmount = 0f;
            RangedCooldownRemaining = 0f;
            SweepCooldownRemaining = 0f;
            HealingFlasks.Refill();
            SetSprintRequested(false);
            EnterState(CombatState.Locomotion);
        }

        public bool Revive(float healthFraction)
        {
            if (healthFraction <= 0f || healthFraction > 1f)
                throw new ArgumentOutOfRangeException(nameof(healthFraction));
            if (!IsDead) return false;

            Reset();
            Health.ApplyDamage(Health.Maximum * (1f - healthFraction), 0f);
            return true;
        }

        private bool TryExecute(CombatCommand command)
        {
            switch (command)
            {
                case CombatCommand.LightAttack:
                    return TryLightAttack();
                case CombatCommand.HeavyPressed:
                    if (State != CombatState.Locomotion)
                    {
                        return false;
                    }

                    _heavyChargeSeconds = 0f;
                    EnterState(CombatState.HeavyCharge);
                    return true;
                case CombatCommand.HeavyReleased:
                    if (State != CombatState.HeavyCharge)
                    {
                        return false;
                    }

                    if (!Stamina.TrySpend(_tuning.HeavyStaminaCost))
                    {
                        EnterState(CombatState.Locomotion);
                        return false;
                    }

                    float charge = Math.Min(1f, _heavyChargeSeconds / _tuning.HeavyFullChargeSeconds);
                    CurrentAttackDamage = _tuning.HeavyDamage * (0.65f + (0.35f * charge));
                    CurrentAttackTag = AttackTag.Heavy;
                    ApplyPerfectDodgeAttackBonus();
                    IsHeavyFullyCharged = charge >= 0.999f;
                    AttackSequence++;
                    EnterState(CombatState.HeavyAttack);
                    return true;
                case CombatCommand.RangedAttack:
                    if (State != CombatState.Locomotion || RangedCooldownRemaining > 0f)
                    {
                        return false;
                    }

                    CurrentAttackDamage = _tuning.RangedDamage;
                    CurrentAttackTag = AttackTag.Projectile;
                    CurrentAttackEmpowered = false;
                    CurrentAttackPostureBonus = 0f;
                    IsHeavyFullyCharged = false;
                    AttackSequence++;
                    RangedCooldownRemaining = _tuning.RangedCooldown;
                    EnterState(CombatState.RangedAttack);
                    return true;
                case CombatCommand.Dodge:
                    return TryDodge();
                case CombatCommand.Heal:
                    if (State != CombatState.Locomotion ||
                        HealingFlasks.CurrentCharges <= 0 ||
                        Health.Current >= Health.Maximum)
                    {
                        return false;
                    }

                    EnterState(CombatState.Heal);
                    return true;
                case CombatCommand.GuardPressed:
                    if (State != CombatState.Locomotion)
                    {
                        return false;
                    }

                    EnterState(CombatState.Guard);
                    GuardAttemptCount++;
                    return true;
                case CombatCommand.GuardReleased:
                    if (State != CombatState.Guard)
                    {
                        return false;
                    }

                    EnterState(CombatState.Locomotion);
                    return true;
                case CombatCommand.Execution:
                    if (State != CombatState.Locomotion ||
                        !Stamina.TrySpend(_tuning.ExecutionStaminaCost))
                    {
                        return false;
                    }

                    CurrentAttackDamage = 0f;
                    CurrentAttackPostureBonus = 0f;
                    CurrentAttackEmpowered = false;
                    ExecutionSequence++;
                    EnterState(CombatState.Execution);
                    return true;
                case CombatCommand.Sweep:
                    if (State != CombatState.Locomotion || SweepCooldownRemaining > 0f ||
                        !Stamina.TrySpend(_tuning.SweepStaminaCost))
                    {
                        return false;
                    }

                    CurrentAttackDamage = _tuning.SweepDamage;
                    CurrentAttackTag = AttackTag.Sweep;
                    ApplyPerfectDodgeAttackBonus();
                    IsHeavyFullyCharged = false;
                    AttackSequence++;
                    SweepCooldownRemaining = _tuning.SweepCooldown;
                    EnterState(CombatState.Sweep);
                    return true;
                default:
                    throw new ArgumentOutOfRangeException(nameof(command), command, null);
            }
        }

        private bool TryLightAttack()
        {
            int comboIndex;
            if (State == CombatState.Locomotion)
            {
                comboIndex = 0;
            }
            else if (IsLightAttack(State))
            {
                int current = LightIndex;
                if (current >= 2 || StateElapsed < _tuning.GetLightComboOpen(current) || StateElapsed > _tuning.GetLightComboClose(current))
                {
                    return false;
                }

                comboIndex = current + 1;
            }
            else
            {
                return false;
            }

            if (!Stamina.TrySpend(_tuning.LightStaminaCost))
            {
                return false;
            }

            CurrentAttackDamage = _tuning.GetLightDamage(comboIndex);
            CurrentAttackTag = AttackTag.Light;
            ApplyPerfectDodgeAttackBonus();
            IsHeavyFullyCharged = false;
            AttackSequence++;
            EnterState((CombatState)((int)CombatState.LightAttack1 + comboIndex));
            return true;
        }

        private bool TryDodge()
        {
            bool canDodge = State == CombatState.Locomotion || State == CombatState.Guard;
            if (IsLightAttack(State))
            {
                canDodge = StateElapsed >= _tuning.GetLightDodgeCancelOpen(LightIndex);
            }

            if (!canDodge || !Stamina.TrySpend(_tuning.DodgeStaminaCost))
            {
                return false;
            }

            EnterState(CombatState.Dodge);
            DodgeAttemptCount++;
            return true;
        }

        private void CompleteTimedState()
        {
            switch (State)
            {
                case CombatState.LightAttack1:
                case CombatState.LightAttack2:
                case CombatState.LightAttack3:
                case CombatState.HeavyAttack:
                case CombatState.Sweep:
                case CombatState.RangedAttack:
                case CombatState.Dodge:
                case CombatState.HitReact:
                case CombatState.Heal:
                case CombatState.Execution:
                    if (StateElapsed >= StateDuration)
                    {
                        EnterState(CombatState.Locomotion);
                    }

                    break;
                case CombatState.GuardBreak:
                    if (StateElapsed >= StateDuration)
                    {
                        Posture.RestoreFull();
                        EnterState(CombatState.Locomotion);
                    }

                    break;
            }
        }

        private void ResolveHeal()
        {
            _healResolved = true;
            if (!HealingFlasks.TryConsume())
            {
                return;
            }

            LastHealAmount = Health.Restore(Health.Maximum * _tuning.HealHealthFraction);
            HealSequence++;
        }

        private void TickBufferedCommand(float deltaTime)
        {
            if (!_bufferedCommand.HasValue)
            {
                return;
            }

            _bufferRemaining -= deltaTime;
            if (_bufferRemaining <= 0f)
            {
                _bufferedCommand = null;
            }
        }

        private void TryConsumeBufferedCommand()
        {
            if (!_bufferedCommand.HasValue)
            {
                return;
            }

            CombatCommand command = _bufferedCommand.Value;
            if (TryExecute(command))
            {
                _bufferedCommand = null;
            }
        }

        private void EnterState(CombatState next)
        {
            State = next;
            StateElapsed = 0f;
            if (next == CombatState.Heal)
            {
                _healResolved = false;
                LastHealAmount = 0f;
            }
            if (next == CombatState.RangedAttack)
            {
                _rangedReleased = false;
            }
            if (next == CombatState.Execution)
            {
                _executionResolved = false;
                SetSprintRequested(false);
            }
            else if (next != CombatState.Locomotion)
            {
                SetSprintRequested(false);
            }
        }

        private int LightIndex => (int)State - (int)CombatState.LightAttack1;
        private float DamageOpen => IsLightAttack(State) ? _tuning.GetLightDamageOpen(LightIndex) :
            State == CombatState.Sweep ? _tuning.SweepDamageOpen : _tuning.HeavyDamageOpen;
        private float DamageClose => IsLightAttack(State) ? _tuning.GetLightDamageClose(LightIndex) :
            State == CombatState.Sweep ? _tuning.SweepDamageClose : _tuning.HeavyDamageClose;

        private float GetStateDuration(CombatState state)
        {
            if (IsLightAttack(state))
            {
                return _tuning.GetLightDuration((int)state - (int)CombatState.LightAttack1);
            }

            switch (state)
            {
                case CombatState.HeavyAttack:
                    return _tuning.HeavyDuration;
                case CombatState.Sweep:
                    return _tuning.SweepDuration;
                case CombatState.RangedAttack:
                    return _tuning.RangedDuration;
                case CombatState.Dodge:
                    return _tuning.DodgeDuration;
                case CombatState.HitReact:
                    return _tuning.HitReactDuration;
                case CombatState.GuardBreak:
                    return _tuning.GuardBreakDuration;
                case CombatState.Heal:
                    return _tuning.HealDuration;
                case CombatState.Execution:
                    return _tuning.ExecutionDuration;
                default:
                    return 0f;
            }
        }

        private static bool IsLightAttack(CombatState state) =>
            state >= CombatState.LightAttack1 && state <= CombatState.LightAttack3;

        private void ApplyPerfectDodgeAttackBonus()
        {
            CurrentAttackEmpowered = PerfectDodgeAttackReady;
            CurrentAttackPostureBonus = CurrentAttackEmpowered
                ? _tuning.PerfectDodgePostureBonus
                : 0f;
            if (!CurrentAttackEmpowered) return;
            CurrentAttackDamage += _tuning.PerfectDodgeDamageBonus;
            PerfectDodgeAttackReady = false;
        }

        private void TickSprint(float deltaTime)
        {
            if (!_sprintRequested || State != CombatState.Locomotion)
            {
                _sprintHeldSeconds = 0f;
                IsSprinting = false;
                return;
            }

            _sprintHeldSeconds += deltaTime;
            if (_sprintHeldSeconds < _tuning.SprintWarmupSeconds)
            {
                IsSprinting = false;
                return;
            }

            float requestedDrain = _tuning.SprintStaminaPerSecond * deltaTime;
            float drained = Stamina.Drain(requestedDrain);
            IsSprinting = drained + 0.0001f >= requestedDrain && Stamina.Current > 0.0001f;
            if (!IsSprinting)
            {
                _sprintRequested = false;
                _sprintHeldSeconds = 0f;
            }
        }
    }
}
