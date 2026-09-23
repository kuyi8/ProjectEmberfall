using System;
using Emberfall.Gameplay.Combat.Domain;

namespace Emberfall.AI.Domain
{
    /// <summary>Deterministic ranged enemy decisions. A future Server adapter owns simulation and projectile release.</summary>
    public sealed class RangedEnemyBrain
    {
        private readonly RangedEnemyDefinition _definition;
        private readonly HealthModel _health;
        private float _groundRuneCooldownRemaining;

        public RangedEnemyBrain(RangedEnemyDefinition definition)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _health = new HealthModel(definition.MaximumHealth);
            Posture = new PostureModel(
                definition.MaximumPosture,
                definition.PostureRegenPerSecond,
                definition.PostureRegenDelay);
        }

        public RangedEnemyState State { get; private set; } = RangedEnemyState.Idle;
        public float StateElapsed { get; private set; }
        public int AttackSequence { get; private set; }
        public RangedAttackKind CurrentAttack { get; private set; } = RangedAttackKind.Projectile;
        public HealthModel Health => _health;
        public PostureModel Posture { get; }
        public bool IsPostureExecutionWindow => State == RangedEnemyState.HitReact &&
            StateElapsed < ExecutionRules.PostureWindowSeconds && Posture.IsBroken;
        public bool WantsTargetMovement => State == RangedEnemyState.Approach;
        public bool WantsRetreatMovement => State == RangedEnemyState.Retreat;
        public bool WantsReturnMovement => State == RangedEnemyState.Return;
        public bool WantsFaceTarget =>
            State == RangedEnemyState.Approach || State == RangedEnemyState.Windup || State == RangedEnemyState.Release ||
            State == RangedEnemyState.Recovery;
        public bool IsProjectileReleaseOpen => State == RangedEnemyState.Release;
        public bool IsAttackReleaseOpen => State == RangedEnemyState.Release;
        public float CurrentWindupDuration => CurrentAttack == RangedAttackKind.GroundRune
            ? _definition.GroundRuneWindupDuration : _definition.WindupDuration;
        public float CurrentReleaseDuration => CurrentAttack == RangedAttackKind.GroundRune
            ? _definition.GroundRuneReleaseDuration : _definition.ReleaseDuration;
        public float CurrentRecoveryDuration => CurrentAttack == RangedAttackKind.GroundRune
            ? _definition.GroundRuneRecoveryDuration : _definition.RecoveryDuration;
        public bool IsResetReady =>
            State == RangedEnemyState.Dead && StateElapsed >= _definition.RespawnDelay;

        public void Tick(float deltaTime, RangedEnemyPerception perception)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            StateElapsed += deltaTime;
            _groundRuneCooldownRemaining = Math.Max(0f, _groundRuneCooldownRemaining - deltaTime);
            Posture.Tick(deltaTime,
                State == RangedEnemyState.Idle || State == RangedEnemyState.Approach ||
                State == RangedEnemyState.Retreat || State == RangedEnemyState.Return);
            switch (State)
            {
                case RangedEnemyState.Idle:
                    if (CanAcquire(perception))
                    {
                        SelectCombatState(perception);
                    }
                    break;
                case RangedEnemyState.Approach:
                case RangedEnemyState.Retreat:
                    if (ShouldDisengage(perception))
                    {
                        TransitionTo(RangedEnemyState.Return);
                    }
                    else
                    {
                        SelectCombatState(perception);
                    }
                    break;
                case RangedEnemyState.Windup:
                    if (ShouldDisengage(perception))
                    {
                        TransitionTo(RangedEnemyState.Return);
                    }
                    else if (!perception.CanSeeTarget)
                    {
                        TransitionTo(RangedEnemyState.Approach);
                    }
                    else if (perception.DistanceToTarget < _definition.PreferredMinimumRange * 0.65f)
                    {
                        TransitionTo(RangedEnemyState.Retreat);
                    }
                    else if (StateElapsed >= CurrentWindupDuration)
                    {
                        TransitionTo(RangedEnemyState.Release);
                    }
                    break;
                case RangedEnemyState.Release:
                    if (StateElapsed >= CurrentReleaseDuration)
                    {
                        TransitionTo(RangedEnemyState.Recovery);
                    }
                    break;
                case RangedEnemyState.Recovery:
                    if (StateElapsed >= CurrentRecoveryDuration)
                    {
                        if (ShouldDisengage(perception))
                        {
                            TransitionTo(RangedEnemyState.Return);
                        }
                        else
                        {
                            SelectCombatState(perception);
                        }
                    }
                    break;
                case RangedEnemyState.HitReact:
                    if (StateElapsed >= ExecutionRules.PostureWindowSeconds)
                    {
                        Posture.RestoreFull();
                        if (ShouldDisengage(perception))
                        {
                            TransitionTo(RangedEnemyState.Return);
                        }
                        else
                        {
                            SelectCombatState(perception);
                        }
                    }
                    break;
                case RangedEnemyState.Return:
                    if (CanAcquire(perception) && perception.DistanceToSpawn <= _definition.LeashRange)
                    {
                        SelectCombatState(perception);
                    }
                    else if (perception.DistanceToSpawn <= 0.2f)
                    {
                        TransitionTo(RangedEnemyState.Idle);
                    }
                    break;
                case RangedEnemyState.Dead:
                    break;
            }
        }

        public DamageResult ReceiveDamage(DamageRequest request)
        {
            if (State == RangedEnemyState.Dead)
            {
                return DamageResult.Ignored;
            }

            float applied = _health.ApplyDamage(request.RawDamage, _definition.Armor);
            bool killed = _health.IsDead;
            float postureApplied = killed ? 0f : Posture.ApplyDamage(request.PostureDamage);
            bool staggered = !killed && Posture.IsBroken;
            if (killed) TransitionTo(RangedEnemyState.Dead);
            else if (staggered) TransitionTo(RangedEnemyState.HitReact);
            return new DamageResult(
                true, false, applied, killed,
                false, false, false, false, staggered, postureApplied);
        }

        public bool ApplyCounterPosture(float postureDamage)
        {
            if (State == RangedEnemyState.Dead) return false;
            Posture.ApplyDamage(postureDamage);
            if (!Posture.IsBroken) return false;
            TransitionTo(RangedEnemyState.HitReact);
            return true;
        }

        public void Reset()
        {
            _health.RestoreFull();
            Posture.RestoreFull();
            State = RangedEnemyState.Idle;
            StateElapsed = 0f;
            AttackSequence = 0;
            CurrentAttack = RangedAttackKind.Projectile;
            _groundRuneCooldownRemaining = 0f;
        }

        private void SelectCombatState(RangedEnemyPerception perception)
        {
            if (perception.DistanceToTarget < _definition.PreferredMinimumRange)
            {
                TransitionTo(RangedEnemyState.Retreat);
            }
            else if (!perception.CanSeeTarget || perception.DistanceToTarget > _definition.PreferredMaximumRange)
            {
                TransitionTo(RangedEnemyState.Approach);
            }
            else
            {
                SelectAttack();
                TransitionTo(RangedEnemyState.Windup);
            }
        }

        private void SelectAttack()
        {
            RangedAttackKind previous = CurrentAttack;
            CurrentAttack = RangedAttackKind.Projectile;
            if (AttackSequence == 0 || _groundRuneCooldownRemaining > 0f)
            {
                return;
            }

            float runeScore = _definition.GroundRuneWeight + 0.2f;
            float projectileScore = 1f - _definition.GroundRuneWeight;
            if (previous == RangedAttackKind.Projectile) runeScore += 0.2f;
            else projectileScore += 0.2f;
            if (runeScore > projectileScore)
            {
                CurrentAttack = RangedAttackKind.GroundRune;
            }
        }

        private bool CanAcquire(RangedEnemyPerception perception) =>
            perception.TargetAvailable && perception.CanSeeTarget &&
            perception.DistanceToTarget <= _definition.DetectionRange &&
            perception.DistanceToSpawn <= _definition.LeashRange;

        private bool ShouldDisengage(RangedEnemyPerception perception) =>
            !perception.TargetAvailable ||
            perception.DistanceToTarget > _definition.LoseTargetRange ||
            perception.DistanceToSpawn > _definition.LeashRange;

        private void TransitionTo(RangedEnemyState state)
        {
            if (State == state)
            {
                return;
            }

            State = state;
            StateElapsed = 0f;
            if (state == RangedEnemyState.Release)
            {
                AttackSequence++;
                if (CurrentAttack == RangedAttackKind.GroundRune)
                {
                    _groundRuneCooldownRemaining = _definition.GroundRuneCooldown;
                }
            }
        }
    }
}
