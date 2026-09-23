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
        public const float OrdinaryHealthThreshold = 0.1f;
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
                ExecutionTargetKind.Elite => guardBroken,
                _ => false
            };
        }

        public static string ConditionTextId(ExecutionTargetKind kind, float health, bool broken, bool claimed)
        {
            if (kind == ExecutionTargetKind.Boss) return string.Empty;
            if (claimed) return "text:execution.already-claimed";
            if (broken) return "text:execution.posture-ready";
            if (kind == ExecutionTargetKind.Ordinary && health <= OrdinaryHealthThreshold)
                return "text:execution.health-ready";
            if (kind == ExecutionTargetKind.Elite) return "text:execution.need-guard-break";
            return "text:execution.need-posture";
        }

        public static string FailureTextId(ExecutionTargetKind kind, float health, bool broken,
            bool claimed, float stamina, float cost)
        {
            if (kind == ExecutionTargetKind.Boss) return string.Empty;
            if (claimed) return "text:execution.already-claimed";
            if (!IsEligible(kind, health, broken, false))
                return kind == ExecutionTargetKind.Elite ? "text:execution.need-guard-break" :
                    health <= 0.2f ? "text:execution.need-health" : "text:execution.need-posture";
            return stamina < cost ? "text:execution.need-stamina" : string.Empty;
        }

        public static bool IsNearEligible(ExecutionTargetKind kind, float health, float postureRemaining) =>
            kind != ExecutionTargetKind.Boss &&
            (postureRemaining <= 0.2f || (kind == ExecutionTargetKind.Ordinary && health <= 0.2f));

        // Read-only presentation selection: deliberately independent of lock-on.
        public static string MarkerTextId(ExecutionTargetKind kind, float health, bool broken,
            bool claimed, float postureRemaining)
        {
            if (kind == ExecutionTargetKind.Boss || health <= 0f) return string.Empty;
            if (claimed) return kind == ExecutionTargetKind.Elite ? "text:execution.already-claimed" : string.Empty;
            if (broken) return "text:execution.marker-posture";
            if (IsEligible(kind, health, false, false)) return "text:execution.marker-health";
            return postureRemaining <= 0.2f ? "text:execution.near-break" : string.Empty;
        }
    }
}
