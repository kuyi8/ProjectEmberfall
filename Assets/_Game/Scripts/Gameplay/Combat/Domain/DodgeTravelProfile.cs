using System;

namespace Emberfall.Gameplay.Combat.Domain
{
    /// <summary>
    /// Deterministic normalized travel for a dodge. Movement finishes before the recovery pose ends,
    /// preventing residual sliding while keeping collision displacement independent from animation root motion.
    /// </summary>
    public static class DodgeTravelProfile
    {
        public const float TravelEndNormalized = 0.82f;

        public static float Evaluate(float stateNormalized)
        {
            float normalized = Math.Max(0f, Math.Min(1f, stateNormalized / TravelEndNormalized));
            return normalized * normalized * (3f - (2f * normalized));
        }
    }
}
