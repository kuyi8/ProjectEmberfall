using System;
using Emberfall.Gameplay.Combat.Domain;

namespace Emberfall.AI.Domain
{
    /// <summary>Bounded, source-specific stimulus. It never transitions a brain or declares line of sight.</summary>
    public sealed class EnemyHitAwareness
    {
        private int _source;
        public float Remaining { get; private set; }
        public bool IsAwareOf(int combatantId) => Remaining > 0f && _source == combatantId;

        public bool Record(DamageRequest request, DamageResult result, int targetId,
            bool targetAvailable, bool encounterAllowsTarget, float seconds)
        {
            if (targetId == 0 || !targetAvailable || !encounterAllowsTarget || request.SourceCombatantId != targetId ||
                result.Killed || result.Invulnerable || (!result.Accepted && !result.Blocked && !result.Defended)) return false;
            if (seconds <= 0f || float.IsNaN(seconds) || float.IsInfinity(seconds))
                throw new ArgumentOutOfRangeException(nameof(seconds));
            _source = targetId;
            Remaining = seconds;
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            Remaining = Math.Max(0f, Remaining - deltaTime);
            if (Remaining == 0f) _source = 0;
        }

        public void Clear() { _source = 0; Remaining = 0f; }
    }
}
