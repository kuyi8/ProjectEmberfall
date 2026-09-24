using Emberfall.Gameplay.Combat.Domain;

namespace Emberfall.Gameplay.Combat.Unity
{
    public enum HitFeedbackGrade { None, PerfectDefense, Light, Sweep, Heavy, GuardBreak, Execution }
    public enum ImpactSurface { Flesh, Metal }

    /// <summary>Read-only presentation policy. Never advances or pauses a combat model.</summary>
    public static class HitFeedbackRules
    {
        public static float Duration(HitFeedbackGrade grade) => grade switch
        {
            HitFeedbackGrade.PerfectDefense => 0.045f,
            HitFeedbackGrade.Light => 0.05f,
            HitFeedbackGrade.Sweep => 0.08f,
            HitFeedbackGrade.Heavy => 0.10f,
            HitFeedbackGrade.GuardBreak => 0.16f,
            HitFeedbackGrade.Execution => 0.22f,
            _ => 0f
        };

        public static HitFeedbackGrade Classify(DamageResult result, AttackTag tag, bool execution = false)
        {
            if (result.Invulnerable || !(result.Accepted || result.Blocked || result.Staggered || result.GuardBroken))
                return HitFeedbackGrade.None;
            if (execution) return HitFeedbackGrade.Execution;
            if (result.GuardBroken || result.Staggered) return HitFeedbackGrade.GuardBreak;
            return tag switch
            {
                AttackTag.Heavy => HitFeedbackGrade.Heavy,
                AttackTag.Sweep => HitFeedbackGrade.Sweep,
                _ => HitFeedbackGrade.Light
            };
        }
    }

    /// <summary>One stream per attacker. Reliable confirmed event ids, not client prediction ids.</summary>
    public sealed class HitFeedbackBatch
    {
        private ulong _lastSequence;
        private CombatImpactPresentationEvent _strongest;
        private bool _pending;

        public bool Offer(CombatImpactPresentationEvent impact)
        {
            if (impact.Sequence == 0 || impact.Sequence <= _lastSequence || impact.Grade == HitFeedbackGrade.None)
                return false;
            _lastSequence = impact.Sequence;
            if (!_pending || impact.Grade > _strongest.Grade) _strongest = impact;
            _pending = true;
            return true;
        }

        public bool Take(out CombatImpactPresentationEvent impact)
        {
            impact = _strongest;
            bool pending = _pending;
            _pending = false;
            return pending;
        }

        public void DiscardPending() => _pending = false;
    }
}
