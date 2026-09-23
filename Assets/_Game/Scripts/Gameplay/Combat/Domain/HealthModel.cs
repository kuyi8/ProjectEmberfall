using System;

namespace Emberfall.Gameplay.Combat.Domain
{
    public sealed class HealthModel
    {
        public HealthModel(float maximum)
        {
            if (maximum <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum));
            }

            Maximum = maximum;
            Current = maximum;
        }

        public float Current { get; private set; }
        public float Maximum { get; }
        public float Normalized => Current / Maximum;
        public bool IsDead => Current <= 0f;

        public float ApplyDamage(float rawDamage, float armor)
        {
            if (rawDamage < 0f || armor < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(rawDamage));
            }

            float applied = Math.Min(Current, Math.Max(1f, rawDamage - armor));
            Current -= applied;
            return applied;
        }

        public void RestoreFull() => Current = Maximum;

        public float ApplyNonlethalPenalty(float maximumFraction)
        {
            if (float.IsNaN(maximumFraction) || maximumFraction < 0f || maximumFraction > 1f)
                throw new ArgumentOutOfRangeException(nameof(maximumFraction));
            if (IsDead) return 0f;
            float loss = Math.Min(Math.Max(0f, Current - 1f), Maximum * maximumFraction);
            Current -= loss;
            return loss;
        }

        public float Restore(float amount)
        {
            if (amount <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            float restored = Math.Min(amount, Maximum - Current);
            Current += restored;
            return restored;
        }
    }
}
