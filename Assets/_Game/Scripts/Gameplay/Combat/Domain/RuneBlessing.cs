namespace Emberfall.Gameplay.Combat.Domain
{
    public enum RuneBlessing
    {
        None = 0,
        Ember = 1,
        Guard = 2
    }

    public static class RuneBlessingRules
    {
        public const float EmberHeavyBonusDamage = 16f;
        public const float EmberHeavyBonusPostureDamage = 24f;
        public const float GuardCounterBonusDamage = 12f;
        public const float GuardEvadeStaminaRestore = 18f;
        public const float GuardCounterStaminaRestore = 22f;

        public static bool ArmsGuardCounter(RuneBlessing blessing, bool perfectDefense) =>
            blessing == RuneBlessing.Guard && perfectDefense;

        public static float GetBonusDamage(
            RuneBlessing blessing,
            AttackTag attackTag,
            bool guardCounterReady,
            bool fullyChargedHeavy)
        {
            if (blessing == RuneBlessing.Ember && attackTag == AttackTag.Heavy && fullyChargedHeavy)
            {
                return EmberHeavyBonusDamage;
            }

            return blessing == RuneBlessing.Guard && attackTag == AttackTag.Light && guardCounterReady
                ? GuardCounterBonusDamage
                : 0f;
        }
    }
}
