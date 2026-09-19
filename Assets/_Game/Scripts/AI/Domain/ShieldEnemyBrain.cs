using System;
using Emberfall.Gameplay.Combat.Domain;

namespace Emberfall.AI.Domain
{
    /// <summary>
    /// Deterministic shield-enemy authority. Spatial adapters submit whether a hit came from
    /// the guarded frontal arc; a future Server can calculate that fact from authoritative poses.
    /// </summary>
    public sealed class ShieldEnemyBrain
    {
        private readonly ShieldEnemyDefinition _definition;
        private float _bashCooldownRemaining;
        private float _scorchedBurstCooldownRemaining;
        private int _blockedHitsSinceBash;

        public ShieldEnemyBrain(ShieldEnemyDefinition definition)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            Health = new HealthModel(definition.MaximumHealth);
            GuardCurrent = definition.GuardCapacity;
        }

        public ShieldEnemyState State { get; private set; } = ShieldEnemyState.Idle;
        public float StateElapsed { get; private set; }
        public int AttackSequence { get; private set; }
        public ShieldAttackKind CurrentAttack { get; private set; } = ShieldAttackKind.HeavyStrike;
        public HealthModel Health { get; }
        public float GuardCurrent { get; private set; }
        public float GuardNormalized => GuardCurrent / _definition.GuardCapacity;
        public bool IsGuardBroken => State == ShieldEnemyState.GuardBreak;
        public bool WantsTargetMovement => State == ShieldEnemyState.Chase;
        public bool WantsReturnMovement => State == ShieldEnemyState.Return;
        public bool WantsFaceTarget => State == ShieldEnemyState.Chase || State == ShieldEnemyState.Windup;
        public bool IsDamageWindowOpen => CurrentAttack != ShieldAttackKind.ScorchedBurst &&
            State == ShieldEnemyState.Attack &&
            StateElapsed >= CurrentDamageWindowStart && StateElapsed <= CurrentDamageWindowEnd;
        public float CurrentWindupDuration => CurrentAttack switch
        {
            ShieldAttackKind.ShieldBash => _definition.BashWindupDuration,
            ShieldAttackKind.ScorchedBurst => _definition.ScorchedBurstWindupDuration,
            _ => _definition.WindupDuration
        };
        public float CurrentAttackDuration => CurrentAttack switch
        {
            ShieldAttackKind.ShieldBash => _definition.BashAttackDuration,
            ShieldAttackKind.ScorchedBurst => _definition.ScorchedBurstAttackDuration,
            _ => _definition.AttackDuration
        };
        public float CurrentRecoveryDuration => CurrentAttack switch
        {
            ShieldAttackKind.ShieldBash => _definition.BashRecoveryDuration,
            ShieldAttackKind.ScorchedBurst => _definition.ScorchedBurstRecoveryDuration,
            _ => _definition.RecoveryDuration
        };
        public float CurrentDamageWindowStart => CurrentAttack == ShieldAttackKind.ShieldBash
            ? _definition.BashDamageWindowStart : _definition.DamageWindowStart;
        public float CurrentDamageWindowEnd => CurrentAttack == ShieldAttackKind.ShieldBash
            ? _definition.BashDamageWindowEnd : _definition.DamageWindowEnd;
        public float CurrentAttackDamage => CurrentAttack == ShieldAttackKind.ShieldBash
            ? _definition.BashDamage : _definition.AttackDamage;
        public float CurrentPostureDamage => CurrentAttack == ShieldAttackKind.ShieldBash
            ? _definition.BashPostureDamage : _definition.PostureDamage;
        public float CurrentHitRadiusMultiplier => CurrentAttack == ShieldAttackKind.ShieldBash
            ? _definition.BashHitRadiusMultiplier : 1f;
        public bool IsResetReady =>
            State == ShieldEnemyState.Dead && StateElapsed >= _definition.RespawnDelay;

        public void Tick(float deltaTime, MeleeEnemyPerception perception)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            StateElapsed += deltaTime;
            _bashCooldownRemaining = Math.Max(0f, _bashCooldownRemaining - deltaTime);
            _scorchedBurstCooldownRemaining = Math.Max(0f, _scorchedBurstCooldownRemaining - deltaTime);
            switch (State)
            {
                case ShieldEnemyState.Idle:
                    if (CanAcquire(perception)) TransitionTo(ShieldEnemyState.Chase);
                    break;
                case ShieldEnemyState.Chase:
                    if (ShouldDisengage(perception)) TransitionTo(ShieldEnemyState.Return);
                    else if (perception.AttackAllowed &&
                             perception.DistanceToTarget <= _definition.AttackRange)
                    {
                        SelectAttack(perception);
                        TransitionTo(ShieldEnemyState.Windup);
                    }
                    break;
                case ShieldEnemyState.Windup:
                    if (ShouldDisengage(perception)) TransitionTo(ShieldEnemyState.Return);
                    else if (StateElapsed >= CurrentWindupDuration) TransitionTo(ShieldEnemyState.Attack);
                    break;
                case ShieldEnemyState.Attack:
                    if (StateElapsed >= CurrentAttackDuration) TransitionTo(ShieldEnemyState.Recovery);
                    break;
                case ShieldEnemyState.Recovery:
                    if (StateElapsed >= CurrentRecoveryDuration)
                    {
                        TransitionTo(ShouldDisengage(perception) ? ShieldEnemyState.Return : ShieldEnemyState.Chase);
                    }
                    break;
                case ShieldEnemyState.GuardBreak:
                    if (StateElapsed >= _definition.GuardBreakDuration)
                    {
                        GuardCurrent = _definition.GuardCapacity;
                        TransitionTo(ShouldDisengage(perception) ? ShieldEnemyState.Return : ShieldEnemyState.Chase);
                    }
                    break;
                case ShieldEnemyState.HitReact:
                    if (StateElapsed >= _definition.HitReactDuration)
                    {
                        TransitionTo(ShouldDisengage(perception) ? ShieldEnemyState.Return : ShieldEnemyState.Chase);
                    }
                    break;
                case ShieldEnemyState.Return:
                    if (CanAcquire(perception) && perception.DistanceToSpawn <= _definition.LeashRange)
                    {
                        TransitionTo(ShieldEnemyState.Chase);
                    }
                    else if (perception.DistanceToSpawn <= 0.2f)
                    {
                        TransitionTo(ShieldEnemyState.Idle);
                    }
                    break;
            }
        }

        public DamageResult ReceiveDamage(DamageRequest request, bool isInFrontalBlockArc)
        {
            if (State == ShieldEnemyState.Dead)
            {
                return DamageResult.Ignored;
            }

            if (isInFrontalBlockArc && State != ShieldEnemyState.GuardBreak)
            {
                GuardCurrent = Math.Max(0f, GuardCurrent - request.PostureDamage);
                _blockedHitsSinceBash++;
                bool broken = GuardCurrent <= 0f;
                if (broken)
                {
                    TransitionTo(ShieldEnemyState.GuardBreak);
                }

                return new DamageResult(false, false, 0f, false, true, broken);
            }

            float multiplier = State == ShieldEnemyState.GuardBreak
                ? _definition.BrokenDamageMultiplier
                : 1f;
            float armor = State == ShieldEnemyState.GuardBreak ? 0f : _definition.Armor;
            float applied = Health.ApplyDamage(request.RawDamage * multiplier, armor);
            bool killed = Health.IsDead;
            if (killed)
            {
                TransitionTo(ShieldEnemyState.Dead);
            }
            else if (State != ShieldEnemyState.GuardBreak)
            {
                TransitionTo(ShieldEnemyState.HitReact);
            }

            return new DamageResult(true, false, applied, killed);
        }

        public bool ApplyCounterPosture(float postureDamage)
        {
            if (State == ShieldEnemyState.Dead || State == ShieldEnemyState.GuardBreak) return false;
            GuardCurrent = Math.Max(0f, GuardCurrent - postureDamage);
            if (GuardCurrent > 0f) return false;
            TransitionTo(ShieldEnemyState.GuardBreak);
            return true;
        }

        public void Reset()
        {
            Health.RestoreFull();
            GuardCurrent = _definition.GuardCapacity;
            State = ShieldEnemyState.Idle;
            StateElapsed = 0f;
            AttackSequence = 0;
            CurrentAttack = ShieldAttackKind.HeavyStrike;
            _bashCooldownRemaining = 0f;
            _scorchedBurstCooldownRemaining = 0f;
            _blockedHitsSinceBash = 0;
        }

        private void SelectAttack(MeleeEnemyPerception perception)
        {
            ShieldAttackKind previous = CurrentAttack;
            CurrentAttack = ShieldAttackKind.HeavyStrike;
            if (AttackSequence == 0)
            {
                return;
            }

            float heavyScore = 1f - _definition.BashWeight;
            float bashScore = float.MinValue;
            if (_bashCooldownRemaining <= 0f && perception.DistanceToTarget <= _definition.BashAttackRange)
            {
                bool retaliationReady = _blockedHitsSinceBash >= _definition.BlockedHitsForBash;
                bashScore = _definition.BashWeight + 0.2f + (retaliationReady ? 0.5f : 0f);
                if (previous == ShieldAttackKind.HeavyStrike) bashScore += 0.2f;
            }

            float burstScore = float.MinValue;
            if (_definition.ScorchedBurstEnabled && _scorchedBurstCooldownRemaining <= 0f)
                burstScore = _definition.ScorchedBurstWeight +
                    (previous == ShieldAttackKind.ScorchedBurst ? -0.35f : 0.25f);

            if (previous != ShieldAttackKind.HeavyStrike) heavyScore += 0.2f;
            if (burstScore > bashScore && burstScore > heavyScore)
                CurrentAttack = ShieldAttackKind.ScorchedBurst;
            else if (bashScore > heavyScore)
                CurrentAttack = ShieldAttackKind.ShieldBash;
        }

        private bool CanAcquire(MeleeEnemyPerception perception) =>
            perception.TargetAvailable && perception.CanSeeTarget &&
            perception.DistanceToTarget <= _definition.DetectionRange &&
            perception.DistanceToSpawn <= _definition.LeashRange;

        private bool ShouldDisengage(MeleeEnemyPerception perception) =>
            !perception.TargetAvailable ||
            perception.DistanceToTarget > _definition.LoseTargetRange ||
            perception.DistanceToSpawn > _definition.LeashRange;

        private void TransitionTo(ShieldEnemyState next)
        {
            if (State == next) return;
            State = next;
            StateElapsed = 0f;
            if (next == ShieldEnemyState.Attack)
            {
                AttackSequence++;
                if (CurrentAttack == ShieldAttackKind.ShieldBash)
                {
                    _bashCooldownRemaining = _definition.BashCooldown;
                    _blockedHitsSinceBash = 0;
                }
                else if (CurrentAttack == ShieldAttackKind.ScorchedBurst)
                {
                    _scorchedBurstCooldownRemaining = _definition.ScorchedBurstCooldown;
                }
            }
        }
    }
}
