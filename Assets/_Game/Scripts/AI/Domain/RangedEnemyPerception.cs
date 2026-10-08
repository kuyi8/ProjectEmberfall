using System;

namespace Emberfall.AI.Domain
{
    public readonly struct RangedEnemyPerception
    {
        public RangedEnemyPerception(
            bool targetAvailable,
            bool canSeeTarget,
            float distanceToTarget,
            float distanceToSpawn,
            bool retreatUnavailable = false)
        {
            if (float.IsNaN(distanceToTarget) || distanceToTarget < 0f ||
                float.IsNaN(distanceToSpawn) || distanceToSpawn < 0f || float.IsInfinity(distanceToSpawn))
            {
                throw new ArgumentOutOfRangeException(nameof(distanceToTarget));
            }

            TargetAvailable = targetAvailable;
            CanSeeTarget = canSeeTarget;
            DistanceToTarget = distanceToTarget;
            DistanceToSpawn = distanceToSpawn;
            RetreatUnavailable = retreatUnavailable;
        }

        public bool TargetAvailable { get; }
        public bool CanSeeTarget { get; }
        public float DistanceToTarget { get; }
        public float DistanceToSpawn { get; }
        /// <summary>Adapter-supplied movement feasibility, NOT sight or a fabricated target distance.
        /// True when bounded, contained navigation cannot restore the firing band or makes no progress.
        /// Adapters that do not assess retreat retain the original behavior through the false default.</summary>
        public bool RetreatUnavailable { get; }
    }
}
