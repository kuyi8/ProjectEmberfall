using System;

namespace Emberfall.AI.Domain
{
    public sealed class WardenAttackDefinition
    {
        public WardenAttackDefinition(
            WardenAttackKind kind,
            float minimumRange,
            float maximumRange,
            float windupDuration,
            float attackDuration,
            float recoveryDuration,
            float damage,
            float postureDamage,
            float hitRadiusMultiplier,
            float firstWindowStart,
            float firstWindowEnd,
            float secondWindowStart = 0f,
            float secondWindowEnd = 0f,
            float cooldown = 0f)
        {
            if (!FiniteNonNegative(minimumRange) || !FinitePositive(maximumRange) || minimumRange > maximumRange ||
                !FinitePositive(windupDuration) || !FinitePositive(attackDuration) || !FinitePositive(recoveryDuration) ||
                !FinitePositive(damage) || !FiniteNonNegative(postureDamage) || !FinitePositive(hitRadiusMultiplier) ||
                !ValidWindow(firstWindowStart, firstWindowEnd, attackDuration) || !FiniteNonNegative(cooldown) ||
                ((secondWindowStart > 0f || secondWindowEnd > 0f) &&
                 (!ValidWindow(secondWindowStart, secondWindowEnd, attackDuration) || secondWindowStart <= firstWindowEnd)))
            {
                throw new FormatException($"Warden attack tuning is invalid: {kind}.");
            }

            Kind = kind;
            MinimumRange = minimumRange;
            MaximumRange = maximumRange;
            WindupDuration = windupDuration;
            AttackDuration = attackDuration;
            RecoveryDuration = recoveryDuration;
            Damage = damage;
            PostureDamage = postureDamage;
            HitRadiusMultiplier = hitRadiusMultiplier;
            FirstWindowStart = firstWindowStart;
            FirstWindowEnd = firstWindowEnd;
            SecondWindowStart = secondWindowStart;
            SecondWindowEnd = secondWindowEnd;
            Cooldown = cooldown;
        }

        public WardenAttackKind Kind { get; }
        public float MinimumRange { get; }
        public float MaximumRange { get; }
        public float WindupDuration { get; }
        public float AttackDuration { get; }
        public float RecoveryDuration { get; }
        public float Damage { get; }
        public float PostureDamage { get; }
        public float HitRadiusMultiplier { get; }
        public float FirstWindowStart { get; }
        public float FirstWindowEnd { get; }
        public float SecondWindowStart { get; }
        public float SecondWindowEnd { get; }
        public float Cooldown { get; }
        public bool HasSecondHit => SecondWindowEnd > SecondWindowStart;

        private static bool ValidWindow(float start, float end, float duration) =>
            FiniteNonNegative(start) && FinitePositive(end) && end > start && end <= duration;

        private static bool FinitePositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool FiniteNonNegative(float value) =>
            value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
