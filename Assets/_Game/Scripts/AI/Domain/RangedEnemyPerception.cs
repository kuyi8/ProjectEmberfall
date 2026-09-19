using System;

namespace Emberfall.AI.Domain
{
    public readonly struct RangedEnemyPerception
    {
        public RangedEnemyPerception(
            bool targetAvailable,
            bool canSeeTarget,
            float distanceToTarget,
            float distanceToSpawn)
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
        }

        public bool TargetAvailable { get; }
        public bool CanSeeTarget { get; }
        public float DistanceToTarget { get; }
        public float DistanceToSpawn { get; }
    }
}
