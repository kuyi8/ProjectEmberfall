using System;

namespace Emberfall.Gameplay.Combat.Domain
{
    public sealed class StaminaModel
    {
        private readonly float _regenPerSecond;
        private readonly float _regenDelay;
        private float _regenBlockedFor;

        public StaminaModel(float maximum, float regenPerSecond, float regenDelay)
        {
            if (maximum <= 0f || regenPerSecond <= 0f || regenDelay < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(maximum), "Stamina tuning is invalid.");
            }

            Maximum = maximum;
            Current = maximum;
            _regenPerSecond = regenPerSecond;
            _regenDelay = regenDelay;
        }

        public float Current { get; private set; }
        public float Maximum { get; }
        public float Normalized => Current / Maximum;

        public bool TrySpend(float amount)
        {
            if (amount <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            if (Current + 0.0001f < amount)
            {
                return false;
            }

            Current -= amount;
            _regenBlockedFor = _regenDelay;
            return true;
        }

        public void Tick(float deltaTime, bool canRegenerate)
        {
            if (deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            _regenBlockedFor = Math.Max(0f, _regenBlockedFor - deltaTime);
            if (canRegenerate && _regenBlockedFor <= 0f)
            {
                Current = Math.Min(Maximum, Current + (_regenPerSecond * deltaTime));
            }
        }

        public void RestoreFull()
        {
            Current = Maximum;
            _regenBlockedFor = 0f;
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

        public float Drain(float amount)
        {
            if (amount <= 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            float drained = Math.Min(amount, Current);
            Current -= drained;
            if (drained > 0f)
            {
                _regenBlockedFor = _regenDelay;
            }
            return drained;
        }
    }
}
