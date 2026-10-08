using System;
using System.Collections.Generic;
using Emberfall.Gameplay.Combat.Domain;

namespace Emberfall.AI.Domain
{
    public enum SummonerAttackKind { Projectile, Summon }

    /// <summary>Deterministic decisions and ownership. Unity supplies visibility/navigation and acknowledges spawns.</summary>
    public sealed class SummonerEnemyBrain
    {
        private readonly SummonerEnemyDefinition _definition;
        private readonly HashSet<int> _living = new HashSet<int>();
        private float _summonCooldown;
        private float _postureWindow;
        private float _blockedRetreat;
        private readonly float _blockedRetreatLimit;
        private int _acknowledgedSequence;

        public SummonerEnemyBrain(SummonerEnemyDefinition definition, float blockedRetreatLimit = 2.5f)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            if (blockedRetreatLimit <= 0f || float.IsNaN(blockedRetreatLimit) || float.IsInfinity(blockedRetreatLimit))
                throw new ArgumentOutOfRangeException(nameof(blockedRetreatLimit));
            _blockedRetreatLimit = blockedRetreatLimit;
            Health = new HealthModel(definition.Combat.MaximumHealth);
            Posture = new PostureModel(definition.Combat.MaximumPosture,
                definition.Combat.PostureRegenPerSecond, definition.Combat.PostureRegenDelay);
            Reset();
        }

        public RangedEnemyState State { get; private set; }
        public float StateElapsed { get; private set; }
        public SummonerAttackKind CurrentAttack { get; private set; }
        public int AttackSequence { get; private set; }
        public int SummonCount { get; private set; }
        public int InterruptCount { get; private set; }
        public int LivingSummonCount => _living.Count;
        public float SummonCooldownRemaining => _summonCooldown;
        public float RetreatBlockedElapsed => _blockedRetreat;
        public HealthModel Health { get; }
        public PostureModel Posture { get; }
        public bool IsPostureExecutionWindow => !Health.IsDead && _postureWindow > 0f;
        public bool IsReleaseOpen => State == RangedEnemyState.Release;
        public bool IsSummoning => State == RangedEnemyState.Windup && CurrentAttack == SummonerAttackKind.Summon;
        public float CurrentWindupDuration => CurrentAttack == SummonerAttackKind.Summon
            ? _definition.SummonWindup : _definition.Combat.WindupDuration;

        public void Tick(float deltaTime, RangedEnemyPerception p)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            StateElapsed += deltaTime;
            if (State == RangedEnemyState.Dead) return;
            _summonCooldown = Math.Max(0f, _summonCooldown - deltaTime);
            if (_postureWindow > 0f)
            {
                _postureWindow = Math.Max(0f, _postureWindow - deltaTime);
                if (_postureWindow == 0f) Posture.RestoreFull();
            }
            Posture.Tick(deltaTime, !IsPostureExecutionWindow &&
                (State == RangedEnemyState.Idle || State == RangedEnemyState.Approach || State == RangedEnemyState.Retreat));
            var c = _definition.Combat;
            bool lost = !p.TargetAvailable || p.DistanceToTarget > c.LoseTargetRange || p.DistanceToSpawn > c.LeashRange;
            _blockedRetreat = !lost && p.CanSeeTarget && p.RetreatUnavailable && State == RangedEnemyState.Retreat
                ? Math.Min(_blockedRetreatLimit, _blockedRetreat + deltaTime) : 0f;
            switch (State)
            {
                case RangedEnemyState.Idle:
                    if (!lost && p.CanSeeTarget && p.DistanceToTarget <= c.DetectionRange) Choose(p);
                    break;
                case RangedEnemyState.Approach:
                case RangedEnemyState.Retreat:
                    if (lost) Transition(RangedEnemyState.Return); else Choose(p);
                    break;
                case RangedEnemyState.Windup:
                    if (lost) Transition(RangedEnemyState.Return);
                    else if (!p.CanSeeTarget) Transition(RangedEnemyState.Approach);
                    else if (StateElapsed >= CurrentWindupDuration)
                    {
                        AttackSequence++;
                        if (CurrentAttack == SummonerAttackKind.Summon) _summonCooldown = _definition.SummonCooldown;
                        Transition(RangedEnemyState.Release);
                    }
                    break;
                case RangedEnemyState.Release:
                    if (StateElapsed >= c.ReleaseDuration) Transition(RangedEnemyState.Recovery);
                    break;
                case RangedEnemyState.Recovery:
                    if (StateElapsed >= (CurrentAttack == SummonerAttackKind.Summon ? _definition.SummonRecovery : c.RecoveryDuration))
                    { if (lost) Transition(RangedEnemyState.Return); else Choose(p); }
                    break;
                case RangedEnemyState.HitReact:
                    if (!IsPostureExecutionWindow && StateElapsed >= c.HitReactDuration)
                    { if (lost) Transition(RangedEnemyState.Return); else Choose(p); }
                    break;
                case RangedEnemyState.Return:
                    if (!lost && p.CanSeeTarget && p.DistanceToTarget <= c.DetectionRange) Choose(p);
                    else if (p.DistanceToSpawn <= .2f) Transition(RangedEnemyState.Idle);
                    break;
            }
        }

        public DamageResult ReceiveDamage(DamageRequest request)
        {
            if (Health.IsDead) return DamageResult.Ignored;
            float applied = Health.ApplyDamage(request.RawDamage, _definition.Combat.Armor);
            bool killed = Health.IsDead;
            float postureApplied = killed ? 0f : Posture.ApplyDamage(request.PostureDamage);
            bool broken = !killed && Posture.IsBroken && !IsPostureExecutionWindow;
            if (killed) { _living.Clear(); _postureWindow = 0f; Transition(RangedEnemyState.Dead); }
            else
            {
                if (IsSummoning && applied > 0f) InterruptSummon();
                if (broken) BreakPosture();
            }
            return new DamageResult(true, false, applied, killed, staggered: broken, postureDamageApplied: postureApplied);
        }

        public bool ApplyCounterPosture(float amount)
        {
            if (Health.IsDead || IsPostureExecutionWindow) return false;
            Posture.ApplyDamage(amount);
            if (!Posture.IsBroken) return false;
            if (IsSummoning) InterruptSummon();
            BreakPosture();
            return true;
        }

        // One successful minion per release. A stale/duplicate callback cannot fill the next cast's slot.
        public bool TryRegisterSummon(int combatantId, int releaseSequence)
        {
            if (combatantId == 0 || !IsReleaseOpen || CurrentAttack != SummonerAttackKind.Summon ||
                releaseSequence != AttackSequence || _acknowledgedSequence == releaseSequence ||
                _living.Count >= SummonerEnemyDefinition.MaximumLivingSummons || !_living.Add(combatantId)) return false;
            _acknowledgedSequence = releaseSequence;
            SummonCount++;
            return true;
        }

        public bool UnregisterSummon(int combatantId) => _living.Remove(combatantId);
        public void ClearSummons() => _living.Clear();
        public void ResetRetreatConstraint() => _blockedRetreat = 0f;

        public void Reset()
        {
            Health.RestoreFull(); Posture.RestoreFull(); _living.Clear();
            State = RangedEnemyState.Idle; StateElapsed = 0f; AttackSequence = 0;
            CurrentAttack = SummonerAttackKind.Projectile; _summonCooldown = _definition.InitialSummonDelay;
            _postureWindow = 0f; _blockedRetreat = 0f; _acknowledgedSequence = 0;
            SummonCount = 0; InterruptCount = 0;
        }

        private void Choose(RangedEnemyPerception p)
        {
            var c = _definition.Combat;
            if (p.DistanceToTarget < c.PreferredMinimumRange && _blockedRetreat < _blockedRetreatLimit)
                Transition(RangedEnemyState.Retreat);
            else if (!p.CanSeeTarget || p.DistanceToTarget > c.PreferredMaximumRange)
                Transition(RangedEnemyState.Approach);
            else
            {
                // Range is a positioning preference, not a permanent ban on casting in a narrow arena.
                // Confirmed blocked retreat still uses the SAME interruptible windup, cooldown and entity cap.
                CurrentAttack = _summonCooldown <= 0f && _living.Count < SummonerEnemyDefinition.MaximumLivingSummons &&
                    (p.DistanceToTarget >= c.PreferredMinimumRange || _blockedRetreat >= _blockedRetreatLimit)
                    ? SummonerAttackKind.Summon : SummonerAttackKind.Projectile;
                Transition(RangedEnemyState.Windup);
            }
        }

        private void InterruptSummon()
        {
            InterruptCount++; _summonCooldown = _definition.InterruptCooldown;
            Transition(RangedEnemyState.HitReact);
        }
        private void BreakPosture() { _postureWindow = ExecutionRules.PostureWindowSeconds; Transition(RangedEnemyState.HitReact); }
        private void Transition(RangedEnemyState state)
        {
            if (State == state) return;
            State = state; StateElapsed = 0f;
            if (state != RangedEnemyState.Retreat) _blockedRetreat = 0f;
        }
    }
}
