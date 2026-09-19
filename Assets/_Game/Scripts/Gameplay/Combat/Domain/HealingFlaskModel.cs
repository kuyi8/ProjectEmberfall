using System;

namespace Emberfall.Gameplay.Combat.Domain
{
    /// <summary>Runtime-only healing charge state. Authored values stay in combat tuning.</summary>
    public sealed class HealingFlaskModel
    {
        public HealingFlaskModel(int maximumCharges)
        {
            if (maximumCharges <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumCharges));
            }

            MaximumCharges = maximumCharges;
            CurrentCharges = maximumCharges;
        }

        public int MaximumCharges { get; private set; }
        public int CurrentCharges { get; private set; }

        public bool TryConsume()
        {
            if (CurrentCharges <= 0)
            {
                return false;
            }

            CurrentCharges--;
            return true;
        }

        public void Refill() => CurrentCharges = MaximumCharges;

        public void IncreaseMaximum(int amount, bool grantNewCharges = true)
        {
            if (amount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(amount));
            }

            MaximumCharges += amount;
            if (grantNewCharges)
            {
                CurrentCharges = Math.Min(MaximumCharges, CurrentCharges + amount);
            }
        }
    }
}
