using System;

namespace Emberfall.AI.Domain
{
    public readonly struct MeleeEnemyPerception
    {
        public MeleeEnemyPerception(
            bool targetAvailable,
            bool canSeeTarget,
            float distanceToTarget,
            float distanceToSpawn,
            bool attackAllowed = true)
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
            AttackAllowed = attackAllowed;
        }

        public bool TargetAvailable { get; }
        public bool CanSeeTarget { get; }
        public float DistanceToTarget { get; }
        public float DistanceToSpawn { get; }
        public bool AttackAllowed { get; }
    }
}
