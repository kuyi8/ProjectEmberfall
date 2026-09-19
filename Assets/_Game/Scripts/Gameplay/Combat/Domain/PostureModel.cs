using System;

namespace Emberfall.Gameplay.Combat.Domain
{
    /// <summary>Deterministic break resource shared by player defense and interruptible enemies.</summary>
    public sealed class PostureModel
    {
        private readonly float _regenPerSecond;
        private readonly float _regenDelay;
        private float _regenBlockedFor;

        public PostureModel(float maximum, float regenPerSecond, float regenDelay)
        {
            if (maximum <= 0f || regenPerSecond <= 0f || regenDelay < 0f ||
                float.IsNaN(maximum) || float.IsNaN(regenPerSecond) || float.IsNaN(regenDelay))
            {
                throw new ArgumentOutOfRangeException(nameof(maximum), "Posture tuning is invalid.");
            }

            Maximum = maximum;
            Current = maximum;
            _regenPerSecond = regenPerSecond;
            _regenDelay = regenDelay;
        }

        public float Current { get; private set; }
        public float Maximum { get; }
        public float Normalized => Current / Maximum;
        public bool IsBroken => Current <= 0f;

        public float ApplyDamage(float amount)
        {
            if (amount < 0f || float.IsNaN(amount) || float.IsInfinity(amount))
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            float applied = Math.Min(Current, amount);
            Current -= applied;
            if (amount > 0f) _regenBlockedFor = _regenDelay;
            return applied;
        }

        public void Tick(float deltaTime, bool canRegenerate)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            _regenBlockedFor = Math.Max(0f, _regenBlockedFor - deltaTime);
            if (canRegenerate && !IsBroken && _regenBlockedFor <= 0f)
            {
                Current = Math.Min(Maximum, Current + (_regenPerSecond * deltaTime));
            }
        }

        public void RestoreFull()
        {
            Current = Maximum;
            _regenBlockedFor = 0f;
        }
    }
}
