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
        public const float PostureWindowSeconds = 2f;
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
                ExecutionTargetKind.Ordinary => guardBroken || healthNormalized <= OrdinaryHealthThreshold,
                ExecutionTargetKind.Elite => guardBroken || healthNormalized <= OrdinaryHealthThreshold,
                _ => false
            };
        }

        public static string ConditionTextId(ExecutionTargetKind kind, float health, bool broken, bool claimed)
        {
            if (kind == ExecutionTargetKind.Boss) return "text:execution.boss-excluded";
            if (claimed) return "text:execution.already-claimed";
            if (broken) return "text:execution.posture-ready";
            if (health <= OrdinaryHealthThreshold) return "text:execution.health-ready";
            return "text:execution.need-posture";
        }

        public static string FailureTextId(ExecutionTargetKind kind, float health, bool broken,
            bool claimed, float stamina, float cost)
        {
            if (kind == ExecutionTargetKind.Boss) return "text:execution.boss-excluded";
            if (claimed) return "text:execution.already-claimed";
            if (!IsEligible(kind, health, broken, false))
                return health <= 0.5f ? "text:execution.need-health" : "text:execution.need-posture";
            return stamina < cost ? "text:execution.need-stamina" : string.Empty;
        }
    }
}
