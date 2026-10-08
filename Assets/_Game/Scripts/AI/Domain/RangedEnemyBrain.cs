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
        private float _postureWindowRemaining;
        private readonly float _retreatBlockedSeconds;
        private float _retreatBlockedElapsed;
        private bool _corneredWindup;

        public RangedEnemyBrain(RangedEnemyDefinition definition, float retreatBlockedSeconds = 2.5f)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (retreatBlockedSeconds <= 0f || float.IsNaN(retreatBlockedSeconds) || float.IsInfinity(retreatBlockedSeconds))
                throw new ArgumentOutOfRangeException(nameof(retreatBlockedSeconds));
            _retreatBlockedSeconds = retreatBlockedSeconds;
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
        public float RetreatBlockedElapsed => _retreatBlockedElapsed;
        public bool IsCorneredWindup => _corneredWindup && State == RangedEnemyState.Windup;
        public bool IsPostureExecutionWindow => !_health.IsDead && _postureWindowRemaining > 0f;
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

            bool abandonCorneredWindup = IsCorneredWindup && !CanAttemptCorneredAttack(perception);
            UpdateRetreatConstraint(deltaTime, perception);
            StateElapsed += deltaTime;
            if (_postureWindowRemaining > 0f)
            {
                _postureWindowRemaining = Math.Max(0f, _postureWindowRemaining - deltaTime);
                if (_postureWindowRemaining == 0f) Posture.RestoreFull();
            }
            _groundRuneCooldownRemaining = Math.Max(0f, _groundRuneCooldownRemaining - deltaTime);
            Posture.Tick(deltaTime,
                !IsPostureExecutionWindow && (State == RangedEnemyState.Idle || State == RangedEnemyState.Approach ||
                State == RangedEnemyState.Retreat || State == RangedEnemyState.Return));
            if (abandonCorneredWindup)
            {
                TransitionTo(ShouldDisengage(perception) ? RangedEnemyState.Return : RangedEnemyState.Approach);
                return;
            }
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
                    else if (perception.DistanceToTarget < _definition.PreferredMinimumRange * 0.65f && !IsCorneredWindup)
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
                    if (StateElapsed >= _definition.HitReactDuration)
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
            bool staggered = !killed && Posture.IsBroken && !IsPostureExecutionWindow;
            if (killed) TransitionTo(RangedEnemyState.Dead);
            else if (staggered) BeginPostureBreak();
            return new DamageResult(
                true, false, applied, killed,
                false, false, false, false, staggered, postureApplied);
        }

        public bool ApplyCounterPosture(float postureDamage)
        {
            if (State == RangedEnemyState.Dead || IsPostureExecutionWindow) return false;
            Posture.ApplyDamage(postureDamage);
            if (!Posture.IsBroken) return false;
            BeginPostureBreak();
            return true;
        }

        private void BeginPostureBreak()
        {
            _postureWindowRemaining = ExecutionRules.PostureWindowSeconds;
            TransitionTo(RangedEnemyState.HitReact);
        }

        public void Reset()
        {
            ResetRetreatConstraint();
            _health.RestoreFull();
            Posture.RestoreFull();
            State = RangedEnemyState.Idle;
            StateElapsed = 0f;
            AttackSequence = 0;
            CurrentAttack = RangedAttackKind.Projectile;
            _groundRuneCooldownRemaining = 0f;
            _postureWindowRemaining = 0f;
        }

        private void SelectCombatState(RangedEnemyPerception perception)
        {
            if (perception.DistanceToTarget < _definition.PreferredMinimumRange &&
                !(_retreatBlockedElapsed >= _retreatBlockedSeconds && CanAttemptCorneredAttack(perception)))
            {
                TransitionTo(RangedEnemyState.Retreat);
            }
            else if (!perception.CanSeeTarget || perception.DistanceToTarget > _definition.PreferredMaximumRange)
            {
                TransitionTo(RangedEnemyState.Approach);
            }
            else
            {
                _corneredWindup = perception.DistanceToTarget < _definition.PreferredMinimumRange;
                SelectAttack();
                TransitionTo(RangedEnemyState.Windup);
            }
        }

        /// <summary>Clear a movement constraint on authority loss / execution hold; never changes combat state.</summary>
        public void ResetRetreatConstraint()
        {
            _retreatBlockedElapsed = 0f;
            _corneredWindup = false;
        }

        private bool CanAttemptCorneredAttack(RangedEnemyPerception perception) =>
            perception.TargetAvailable && perception.CanSeeTarget &&
            perception.DistanceToTarget <= _definition.PreferredMaximumRange &&
            !ShouldDisengage(perception) && !IsPostureExecutionWindow;

        private void UpdateRetreatConstraint(float deltaTime, RangedEnemyPerception perception)
        {
            if (!CanAttemptCorneredAttack(perception) || !perception.RetreatUnavailable ||
                perception.DistanceToTarget >= _definition.PreferredMinimumRange ||
                (State != RangedEnemyState.Retreat && !IsCorneredWindup))
            {
                ResetRetreatConstraint();
                return;
            }
            if (State == RangedEnemyState.Retreat)
                _retreatBlockedElapsed = Math.Min(_retreatBlockedSeconds, _retreatBlockedElapsed + deltaTime);
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
            if (state != RangedEnemyState.Retreat && state != RangedEnemyState.Windup)
                ResetRetreatConstraint();
            if (state == RangedEnemyState.Dead) _postureWindowRemaining = 0f;
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
