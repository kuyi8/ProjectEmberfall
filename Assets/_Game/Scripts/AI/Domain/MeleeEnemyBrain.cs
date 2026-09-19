using System;
using Emberfall.Gameplay.Combat.Domain;

namespace Emberfall.AI.Domain
{
    /// <summary>Deterministic melee enemy decisions. A future Server adapter owns calls to Tick and ReceiveDamage.</summary>
    public sealed class MeleeEnemyBrain
    {
        private readonly MeleeEnemyDefinition _definition;
        private readonly HealthModel _health;
        private float _comboCooldownRemaining;

        public MeleeEnemyBrain(MeleeEnemyDefinition definition)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _health = new HealthModel(definition.MaximumHealth);
            Posture = new PostureModel(
                definition.MaximumPosture,
                definition.PostureRegenPerSecond,
                definition.PostureRegenDelay);
        }

        public MeleeEnemyState State { get; private set; } = MeleeEnemyState.Idle;
        public float StateElapsed { get; private set; }
        public int AttackSequence { get; private set; }
        public MeleeAttackKind CurrentAttack { get; private set; } = MeleeAttackKind.QuickSlash;
        public HealthModel Health => _health;
        public PostureModel Posture { get; }
        public bool WantsTargetMovement => State == MeleeEnemyState.Chase;
        public bool WantsReturnMovement => State == MeleeEnemyState.Return;
        public bool WantsFaceTarget =>
            State == MeleeEnemyState.Chase || State == MeleeEnemyState.Windup;
        public int DamageWindowIndex
        {
            get
            {
                if (State != MeleeEnemyState.Attack) return 0;
                if (CurrentAttack == MeleeAttackKind.QuickSlash)
                {
                    return StateElapsed >= _definition.DamageWindowStart &&
                           StateElapsed <= _definition.DamageWindowEnd ? 1 : 0;
                }

                if (StateElapsed >= _definition.ComboDamageWindow1Start &&
                    StateElapsed <= _definition.ComboDamageWindow1End) return 1;
                return StateElapsed >= _definition.ComboDamageWindow2Start &&
                       StateElapsed <= _definition.ComboDamageWindow2End ? 2 : 0;
            }
        }
        public bool IsDamageWindowOpen => DamageWindowIndex > 0;
        public int CurrentHitSequence => (AttackSequence * 3) + DamageWindowIndex;
        public float CurrentAttackDamage => CurrentAttack == MeleeAttackKind.DelayedCombo
            ? _definition.ComboHitDamage : _definition.AttackDamage;
        public float CurrentPostureDamage => CurrentAttack == MeleeAttackKind.DelayedCombo
            ? _definition.ComboHitPostureDamage : _definition.PostureDamage;
        public float CurrentHitRadiusMultiplier => CurrentAttack == MeleeAttackKind.DelayedCombo
            ? _definition.ComboHitRadiusMultiplier : 1f;
        public float CurrentWindupDuration => CurrentAttack == MeleeAttackKind.DelayedCombo
            ? _definition.ComboWindupDuration : _definition.WindupDuration;
        public float CurrentAttackDuration => CurrentAttack == MeleeAttackKind.DelayedCombo
            ? _definition.ComboAttackDuration : _definition.AttackDuration;
        public float CurrentRecoveryDuration => CurrentAttack == MeleeAttackKind.DelayedCombo
            ? _definition.ComboRecoveryDuration : _definition.RecoveryDuration;
        public bool IsResetReady =>
            State == MeleeEnemyState.Dead && StateElapsed >= _definition.RespawnDelay;

        public void Tick(float deltaTime, MeleeEnemyPerception perception)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            StateElapsed += deltaTime;
            _comboCooldownRemaining = Math.Max(0f, _comboCooldownRemaining - deltaTime);
            Posture.Tick(deltaTime,
                State == MeleeEnemyState.Idle || State == MeleeEnemyState.Chase || State == MeleeEnemyState.Return);
            switch (State)
            {
                case MeleeEnemyState.Idle:
                    if (CanAcquire(perception))
                    {
                        TransitionTo(MeleeEnemyState.Chase);
                    }
                    break;
                case MeleeEnemyState.Chase:
                    if (ShouldDisengage(perception))
                    {
                        TransitionTo(MeleeEnemyState.Return);
                    }
                    else if (perception.AttackAllowed &&
                             perception.DistanceToTarget <= _definition.AttackRange)
                    {
                        SelectAttack(perception);
                        TransitionTo(MeleeEnemyState.Windup);
                    }
                    break;
                case MeleeEnemyState.Windup:
                    if (ShouldDisengage(perception))
                    {
                        TransitionTo(MeleeEnemyState.Return);
                    }
                    else if (StateElapsed >= CurrentWindupDuration)
                    {
                        TransitionTo(MeleeEnemyState.Attack);
                    }
                    break;
                case MeleeEnemyState.Attack:
                    if (StateElapsed >= CurrentAttackDuration)
                    {
                        TransitionTo(MeleeEnemyState.Recovery);
                    }
                    break;
                case MeleeEnemyState.Recovery:
                    if (StateElapsed >= CurrentRecoveryDuration)
                    {
                        TransitionTo(ShouldDisengage(perception)
                            ? MeleeEnemyState.Return
                            : MeleeEnemyState.Chase);
                    }
                    break;
                case MeleeEnemyState.HitReact:
                    if (StateElapsed >= _definition.HitReactDuration)
                    {
                        Posture.RestoreFull();
                        TransitionTo(ShouldDisengage(perception)
                            ? MeleeEnemyState.Return
                            : MeleeEnemyState.Chase);
                    }
                    break;
                case MeleeEnemyState.Return:
                    if (CanAcquire(perception) && perception.DistanceToSpawn <= _definition.LeashRange)
                    {
                        TransitionTo(MeleeEnemyState.Chase);
                    }
                    else if (perception.DistanceToSpawn <= 0.2f)
                    {
                        TransitionTo(MeleeEnemyState.Idle);
                    }
                    break;
                case MeleeEnemyState.Dead:
                    break;
            }
        }

        public DamageResult ReceiveDamage(DamageRequest request)
        {
            if (State == MeleeEnemyState.Dead)
            {
                return DamageResult.Ignored;
            }

            float applied = _health.ApplyDamage(request.RawDamage, _definition.Armor);
            bool killed = _health.IsDead;
            float postureApplied = killed ? 0f : Posture.ApplyDamage(request.PostureDamage);
            bool staggered = !killed && Posture.IsBroken;
            if (killed) TransitionTo(MeleeEnemyState.Dead);
            else if (staggered) TransitionTo(MeleeEnemyState.HitReact);
            return new DamageResult(
                true, false, applied, killed,
                false, false, false, false, staggered, postureApplied);
        }

        public bool ApplyCounterPosture(float postureDamage)
        {
            if (State == MeleeEnemyState.Dead) return false;
            Posture.ApplyDamage(postureDamage);
            if (!Posture.IsBroken) return false;
            TransitionTo(MeleeEnemyState.HitReact);
            return true;
        }

        public void Reset()
        {
            _health.RestoreFull();
            Posture.RestoreFull();
            State = MeleeEnemyState.Idle;
            StateElapsed = 0f;
            AttackSequence = 0;
            CurrentAttack = MeleeAttackKind.QuickSlash;
            _comboCooldownRemaining = 0f;
        }

        private void SelectAttack(MeleeEnemyPerception perception)
        {
            MeleeAttackKind previous = CurrentAttack;
            CurrentAttack = MeleeAttackKind.QuickSlash;
            if (AttackSequence == 0 || _comboCooldownRemaining > 0f ||
                perception.DistanceToTarget > _definition.ComboAttackRange)
            {
                return;
            }

            float comboScore = _definition.ComboWeight + 0.2f;
            float quickScore = 1f - _definition.ComboWeight;
            if (previous == MeleeAttackKind.QuickSlash) comboScore += 0.2f;
            else quickScore += 0.2f;
            if (comboScore > quickScore)
            {
                CurrentAttack = MeleeAttackKind.DelayedCombo;
            }
        }

        private bool CanAcquire(MeleeEnemyPerception perception) =>
            perception.TargetAvailable && perception.CanSeeTarget &&
            perception.DistanceToTarget <= _definition.DetectionRange &&
            perception.DistanceToSpawn <= _definition.LeashRange;

        private bool ShouldDisengage(MeleeEnemyPerception perception) =>
            !perception.TargetAvailable ||
            perception.DistanceToTarget > _definition.LoseTargetRange ||
            perception.DistanceToSpawn > _definition.LeashRange;

        private void TransitionTo(MeleeEnemyState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            StateElapsed = 0f;
            if (state == MeleeEnemyState.Attack)
            {
                AttackSequence++;
                if (CurrentAttack == MeleeAttackKind.DelayedCombo)
                {
                    _comboCooldownRemaining = _definition.ComboCooldown;
                }
            }
        }
    }
}
