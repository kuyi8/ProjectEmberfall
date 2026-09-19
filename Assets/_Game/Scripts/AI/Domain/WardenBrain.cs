using System;
using Emberfall.Gameplay.Combat.Domain;

namespace Emberfall.AI.Domain
{
    /// <summary>
    /// Deterministic two-phase Warden authority. Unity supplies perception and spatial delivery;
    /// attack choice, phase gates, damage windows, posture and cooldowns remain server-portable.
    /// </summary>
    public sealed class WardenBrain
    {
        private readonly WardenDefinition _definition;
        private float _bashCooldownRemaining;
        private float _chargeCooldownRemaining;
        private float _runeCleaveCooldownRemaining;
        private float _delayedBlastCooldownRemaining;
        private WardenAttackKind _lastAttack = WardenAttackKind.Charge;

        public WardenBrain(WardenDefinition definition, float maximumHealthMultiplier = 1f)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (maximumHealthMultiplier <= 0f || float.IsNaN(maximumHealthMultiplier) ||
                float.IsInfinity(maximumHealthMultiplier))
                throw new ArgumentOutOfRangeException(nameof(maximumHealthMultiplier));
            Health = new HealthModel(definition.MaximumHealth * maximumHealthMultiplier);
            GuardCurrent = definition.GuardCapacity;
        }

        public WardenState State { get; private set; } = WardenState.Dormant;
        public WardenPhase Phase { get; private set; } = WardenPhase.PhaseOne;
        public float StateElapsed { get; private set; }
        public int AttackSequence { get; private set; }
        public WardenAttackKind CurrentAttack { get; private set; } = WardenAttackKind.SwordCombo;
        public HealthModel Health { get; }
        public float GuardCurrent { get; private set; }
        public float ActivePostureCapacity => Phase == WardenPhase.PhaseTwo
            ? _definition.PhaseTwoPostureCapacity
            : _definition.GuardCapacity;
        public float GuardNormalized => Phase == WardenPhase.Transition
            ? 0f
            : GuardCurrent / ActivePostureCapacity;
        public bool EncounterActive => State != WardenState.Dormant && State != WardenState.Dead;
        public bool PhaseTwoThresholdReached => Phase != WardenPhase.PhaseOne ||
            Health.Normalized <= _definition.PhaseTwoThreshold;
        public bool WantsTargetMovement => State == WardenState.Chase;
        public bool WantsReturnMovement => State == WardenState.Return;
        public bool WantsFaceTarget => State == WardenState.Chase || State == WardenState.Windup;
        public bool IsRecoveryOpening => State == WardenState.Recovery || State == WardenState.GuardBreak;
        public float MoveSpeedMultiplier => Phase == WardenPhase.PhaseTwo
            ? _definition.PhaseTwoMoveSpeedMultiplier
            : 1f;
        public WardenAttackDefinition ActiveAttack => _definition.GetAttack(CurrentAttack);
        public float CurrentWindupDuration => ActiveAttack.WindupDuration;
        public float CurrentAttackDuration => ActiveAttack.AttackDuration;
        public float CurrentRecoveryDuration => ActiveAttack.RecoveryDuration;
        public float CurrentAttackDamage => ActiveAttack.Damage;
        public float CurrentPostureDamage => ActiveAttack.PostureDamage;
        public float CurrentHitRadiusMultiplier => ActiveAttack.HitRadiusMultiplier;
        public int CurrentHitIndex
        {
            get
            {
                if (State != WardenState.Attack) return 0;
                if (StateElapsed >= ActiveAttack.FirstWindowStart && StateElapsed <= ActiveAttack.FirstWindowEnd) return 1;
                if (ActiveAttack.HasSecondHit && StateElapsed >= ActiveAttack.SecondWindowStart && StateElapsed <= ActiveAttack.SecondWindowEnd) return 2;
                return 0;
            }
        }
        public bool IsDamageWindowOpen => CurrentHitIndex > 0;

        public void Tick(float deltaTime, MeleeEnemyPerception perception, bool encounterEnabled)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));

            StateElapsed += deltaTime;
            _bashCooldownRemaining = Math.Max(0f, _bashCooldownRemaining - deltaTime);
            _chargeCooldownRemaining = Math.Max(0f, _chargeCooldownRemaining - deltaTime);
            _runeCleaveCooldownRemaining = Math.Max(0f, _runeCleaveCooldownRemaining - deltaTime);
            _delayedBlastCooldownRemaining = Math.Max(0f, _delayedBlastCooldownRemaining - deltaTime);

            if (!encounterEnabled && State != WardenState.Dead)
            {
                TransitionTo(WardenState.Dormant);
                return;
            }

            switch (State)
            {
                case WardenState.Dormant:
                    if (CanAcquire(perception)) TransitionTo(WardenState.Chase);
                    break;
                case WardenState.Chase:
                    if (ShouldDisengage(perception)) TransitionTo(WardenState.Return);
                    else if (TrySelectAttack(perception.DistanceToTarget)) TransitionTo(WardenState.Windup);
                    break;
                case WardenState.Windup:
                    if (ShouldDisengage(perception)) TransitionTo(WardenState.Return);
                    else if (StateElapsed >= CurrentWindupDuration) TransitionTo(WardenState.Attack);
                    break;
                case WardenState.Attack:
                    if (StateElapsed >= CurrentAttackDuration) TransitionTo(WardenState.Recovery);
                    break;
                case WardenState.Recovery:
                    if (StateElapsed >= CurrentRecoveryDuration)
                        TransitionTo(ShouldDisengage(perception) ? WardenState.Return : WardenState.Chase);
                    break;
                case WardenState.GuardBreak:
                    if (StateElapsed >= _definition.GuardBreakDuration)
                    {
                        GuardCurrent = ActivePostureCapacity;
                        TransitionTo(ShouldDisengage(perception) ? WardenState.Return : WardenState.Chase);
                    }
                    break;
                case WardenState.PhaseTransition:
                    if (StateElapsed >= _definition.PhaseTransitionDuration)
                    {
                        Phase = WardenPhase.PhaseTwo;
                        GuardCurrent = _definition.PhaseTwoPostureCapacity;
                        TransitionTo(ShouldDisengage(perception) ? WardenState.Return : WardenState.Chase);
                    }
                    break;
                case WardenState.Return:
                    if (CanAcquire(perception)) TransitionTo(WardenState.Chase);
                    else if (perception.DistanceToSpawn <= 0.2f) TransitionTo(WardenState.Dormant);
                    break;
            }
        }

        public DamageResult ReceiveDamage(DamageRequest request, bool isInFrontalBlockArc)
        {
            if (State == WardenState.Dead || State == WardenState.Dormant ||
                State == WardenState.PhaseTransition) return DamageResult.Ignored;

            if (Phase == WardenPhase.PhaseOne && isInFrontalBlockArc && !IsRecoveryOpening)
            {
                float blockedPostureApplied = Math.Min(GuardCurrent, request.PostureDamage);
                GuardCurrent = Math.Max(0f, GuardCurrent - request.PostureDamage);
                bool broken = GuardCurrent <= 0f;
                if (broken) TransitionTo(WardenState.GuardBreak);
                return new DamageResult(false, false, 0f, false, true, broken,
                    postureDamageApplied: blockedPostureApplied);
            }

            float multiplier = State == WardenState.GuardBreak
                ? _definition.GuardBreakDamageMultiplier
                : State == WardenState.Recovery ? _definition.RecoveryDamageMultiplier : 1f;
            float armor = IsRecoveryOpening ? 0f :
                Phase == WardenPhase.PhaseTwo ? _definition.PhaseTwoArmor : _definition.Armor;
            float applied = ApplyHealthDamageWithPhaseGate(request.RawDamage * multiplier, armor);
            bool killed = Health.IsDead;
            bool postureBroken = false;
            float postureApplied = 0f;

            if (!killed && Phase == WardenPhase.PhaseTwo && !IsRecoveryOpening && request.PostureDamage > 0f)
            {
                postureApplied = Math.Min(GuardCurrent, request.PostureDamage);
                GuardCurrent = Math.Max(0f, GuardCurrent - request.PostureDamage);
                postureBroken = GuardCurrent <= 0f;
                if (postureBroken) TransitionTo(WardenState.GuardBreak);
            }

            if (killed) TransitionTo(WardenState.Dead);
            return new DamageResult(true, false, applied, killed, guardBroken: postureBroken,
                staggered: postureBroken, postureDamageApplied: postureApplied);
        }

        public bool ApplyCounterPosture(float amount)
        {
            if (State == WardenState.Dead || State == WardenState.Dormant ||
                State == WardenState.PhaseTransition || IsRecoveryOpening) return false;
            GuardCurrent = Math.Max(0f, GuardCurrent - amount);
            if (GuardCurrent > 0f) return false;
            TransitionTo(WardenState.GuardBreak);
            return true;
        }

        public void Reset()
        {
            Health.RestoreFull();
            GuardCurrent = _definition.GuardCapacity;
            Phase = WardenPhase.PhaseOne;
            State = WardenState.Dormant;
            StateElapsed = 0f;
            AttackSequence = 0;
            CurrentAttack = WardenAttackKind.SwordCombo;
            _lastAttack = WardenAttackKind.Charge;
            _bashCooldownRemaining = 0f;
            _chargeCooldownRemaining = 0f;
            _runeCleaveCooldownRemaining = 0f;
            _delayedBlastCooldownRemaining = 0f;
        }

        private float ApplyHealthDamageWithPhaseGate(float rawDamage, float armor)
        {
            if (Phase != WardenPhase.PhaseOne)
            {
                return Health.ApplyDamage(rawDamage, armor);
            }

            float thresholdHealth = Health.Maximum * _definition.PhaseTwoThreshold;
            float effectiveDamage = Math.Max(1f, rawDamage - armor);
            float damageUntilGate = Health.Current - thresholdHealth;
            if (effectiveDamage < damageUntilGate)
            {
                return Health.ApplyDamage(rawDamage, armor);
            }

            float applied = damageUntilGate > 0f ? Health.ApplyDamage(damageUntilGate, 0f) : 0f;
            Phase = WardenPhase.Transition;
            GuardCurrent = 0f;
            TransitionTo(WardenState.PhaseTransition);
            return applied;
        }

        private bool TrySelectAttack(float distance)
        {
            if (Phase == WardenPhase.PhaseTwo)
            {
                if (_runeCleaveCooldownRemaining <= 0f && _lastAttack != WardenAttackKind.RuneCleave &&
                    InRange(_definition.RuneCleave, distance))
                {
                    CurrentAttack = WardenAttackKind.RuneCleave;
                    return true;
                }

                if (_delayedBlastCooldownRemaining <= 0f && _lastAttack != WardenAttackKind.DelayedBlast &&
                    InRange(_definition.DelayedBlast, distance))
                {
                    CurrentAttack = WardenAttackKind.DelayedBlast;
                    return true;
                }
            }

            if (_chargeCooldownRemaining <= 0f && InRange(_definition.Charge, distance))
            {
                CurrentAttack = WardenAttackKind.Charge;
                return true;
            }

            if (Phase == WardenPhase.PhaseOne && _bashCooldownRemaining <= 0f &&
                _lastAttack != WardenAttackKind.ShieldBash && InRange(_definition.ShieldBash, distance))
            {
                CurrentAttack = WardenAttackKind.ShieldBash;
                return true;
            }

            if (InRange(_definition.SwordCombo, distance))
            {
                CurrentAttack = WardenAttackKind.SwordCombo;
                return true;
            }

            return false;
        }

        private static bool InRange(WardenAttackDefinition attack, float distance) =>
            distance >= attack.MinimumRange && distance <= attack.MaximumRange;

        private bool CanAcquire(MeleeEnemyPerception perception) =>
            perception.TargetAvailable && perception.CanSeeTarget &&
            perception.DistanceToTarget <= _definition.DetectionRange &&
            perception.DistanceToSpawn <= _definition.LeashRange;

        private bool ShouldDisengage(MeleeEnemyPerception perception) =>
            !perception.TargetAvailable || perception.DistanceToTarget > _definition.LoseTargetRange ||
            perception.DistanceToSpawn > _definition.LeashRange;

        private void TransitionTo(WardenState next)
        {
            if (State == next) return;
            State = next;
            StateElapsed = 0f;
            if (next != WardenState.Attack) return;
            AttackSequence++;
            _lastAttack = CurrentAttack;
            switch (CurrentAttack)
            {
                case WardenAttackKind.ShieldBash:
                    _bashCooldownRemaining = ActiveAttack.Cooldown;
                    break;
                case WardenAttackKind.Charge:
                    _chargeCooldownRemaining = ActiveAttack.Cooldown;
                    break;
                case WardenAttackKind.RuneCleave:
                    _runeCleaveCooldownRemaining = ActiveAttack.Cooldown;
                    break;
                case WardenAttackKind.DelayedBlast:
                    _delayedBlastCooldownRemaining = ActiveAttack.Cooldown;
                    break;
            }
        }
    }
}
