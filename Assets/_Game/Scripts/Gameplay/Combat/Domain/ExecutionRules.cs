namespace Emberfall.Gameplay.Combat.Domain
{
    public enum ExecutionTargetKind
    {
        Ordinary = 0,
        Elite = 1,
        Boss = 2
    }

    public static class ExecutionRules
    {
        public const float OrdinaryHealthThreshold = 0.25f;
        public const float Range = 2.1f;
        public const float OrdinaryDamage = 10000f;
        public const float EliteDamage = 95f;

        public static bool IsEligible(
            ExecutionTargetKind kind,
            float healthNormalized,
            bool guardBroken,
            bool alreadyClaimed)
        {
            if (alreadyClaimed || healthNormalized <= 0f || healthNormalized > 1f) return false;
            return kind switch
            {
                ExecutionTargetKind.Ordinary => healthNormalized <= OrdinaryHealthThreshold,
                ExecutionTargetKind.Elite => guardBroken,
                _ => false
            };
        }
    }
}
