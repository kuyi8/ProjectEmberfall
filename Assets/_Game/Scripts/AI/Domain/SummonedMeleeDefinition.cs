using System;

namespace Emberfall.AI.Domain
{
    public static class SummonedMeleeDefinition
    {
        // Preserve every authored Fogwalker rule. Only health is the summoner JSON's override.
        public static MeleeEnemyDefinition From(MeleeEnemyDefinition source, float maximumHealth)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            return new MeleeEnemyDefinition(source.Id, source.DisplayNameTextId, maximumHealth, source.Armor,
                source.DetectionRange, source.LoseTargetRange, source.FieldOfView, source.LeashRange,
                source.AttackRange, source.MoveSpeed, source.RotationSpeed, source.WindupDuration,
                source.AttackDuration, source.DamageWindowStart, source.DamageWindowEnd, source.RecoveryDuration,
                source.AttackDamage, source.PostureDamage, source.HitReactDuration, source.RespawnDelay,
                source.MaximumPosture, source.PostureRegenPerSecond, source.PostureRegenDelay,
                source.ComboAttackRange, source.ComboWeight, source.ComboCooldown, source.ComboWindupDuration,
                source.ComboAttackDuration, source.ComboDamageWindow1Start, source.ComboDamageWindow1End,
                source.ComboDamageWindow2Start, source.ComboDamageWindow2End, source.ComboRecoveryDuration,
                source.ComboHitDamage, source.ComboHitPostureDamage, source.ComboHitRadiusMultiplier);
        }
    }
}
