using System;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;

namespace Emberfall.AI.Domain
{
    public class MeleeEnemyDefinition : IContentDefinition
    {
        public MeleeEnemyDefinition(
            ContentId id,
            ContentId displayNameTextId,
            float maximumHealth,
            float armor,
            float detectionRange,
            float loseTargetRange,
            float fieldOfView,
            float leashRange,
            float attackRange,
            float moveSpeed,
            float rotationSpeed,
            float windupDuration,
            float attackDuration,
            float damageWindowStart,
            float damageWindowEnd,
            float recoveryDuration,
            float attackDamage,
            float postureDamage,
            float hitReactDuration,
            float respawnDelay,
            float maximumPosture = 55f,
            float postureRegenPerSecond = 22f,
            float postureRegenDelay = 1.25f,
            float comboAttackRange = 1.65f,
            float comboWeight = 0.42f,
            float comboCooldown = 3.2f,
            float comboWindupDuration = 0.82f,
            float comboAttackDuration = 0.86f,
            float comboDamageWindow1Start = 0.15f,
            float comboDamageWindow1End = 0.29f,
            float comboDamageWindow2Start = 0.57f,
            float comboDamageWindow2End = 0.73f,
            float comboRecoveryDuration = 0.84f,
            float comboHitDamage = 13f,
            float comboHitPostureDamage = 14f,
            float comboHitRadiusMultiplier = 1.08f)
        {
            Id = id;
            DisplayNameTextId = displayNameTextId;
            MaximumHealth = maximumHealth;
            Armor = armor;
            DetectionRange = detectionRange;
            LoseTargetRange = loseTargetRange;
            FieldOfView = fieldOfView;
            LeashRange = leashRange;
            AttackRange = attackRange;
            MoveSpeed = moveSpeed;
            RotationSpeed = rotationSpeed;
            WindupDuration = windupDuration;
            AttackDuration = attackDuration;
            DamageWindowStart = damageWindowStart;
            DamageWindowEnd = damageWindowEnd;
            RecoveryDuration = recoveryDuration;
            AttackDamage = attackDamage;
            PostureDamage = postureDamage;
            HitReactDuration = hitReactDuration;
            RespawnDelay = respawnDelay;
            MaximumPosture = maximumPosture;
            PostureRegenPerSecond = postureRegenPerSecond;
            PostureRegenDelay = postureRegenDelay;
            ComboAttackRange = comboAttackRange;
            ComboWeight = comboWeight;
            ComboCooldown = comboCooldown;
            ComboWindupDuration = comboWindupDuration;
            ComboAttackDuration = comboAttackDuration;
            ComboDamageWindow1Start = comboDamageWindow1Start;
            ComboDamageWindow1End = comboDamageWindow1End;
            ComboDamageWindow2Start = comboDamageWindow2Start;
            ComboDamageWindow2End = comboDamageWindow2End;
            ComboRecoveryDuration = comboRecoveryDuration;
            ComboHitDamage = comboHitDamage;
            ComboHitPostureDamage = comboHitPostureDamage;
            ComboHitRadiusMultiplier = comboHitRadiusMultiplier;
            Validate();
        }

        public ContentId Id { get; }
        public ContentId DisplayNameTextId { get; }
        public float MaximumHealth { get; }
        public float Armor { get; }
        public float DetectionRange { get; }
        public float LoseTargetRange { get; }
        public float FieldOfView { get; }
        public float LeashRange { get; }
        public float AttackRange { get; }
        public float MoveSpeed { get; }
        public float RotationSpeed { get; }
        public float WindupDuration { get; }
        public float AttackDuration { get; }
        public float DamageWindowStart { get; }
        public float DamageWindowEnd { get; }
        public float RecoveryDuration { get; }
        public float AttackDamage { get; }
        public float PostureDamage { get; }
        public float HitReactDuration { get; }
        public float RespawnDelay { get; }
        public float MaximumPosture { get; }
        public float PostureRegenPerSecond { get; }
        public float PostureRegenDelay { get; }
        public float ComboAttackRange { get; }
        public float ComboWeight { get; }
        public float ComboCooldown { get; }
        public float ComboWindupDuration { get; }
        public float ComboAttackDuration { get; }
        public float ComboDamageWindow1Start { get; }
        public float ComboDamageWindow1End { get; }
        public float ComboDamageWindow2Start { get; }
        public float ComboDamageWindow2End { get; }
        public float ComboRecoveryDuration { get; }
        public float ComboHitDamage { get; }
        public float ComboHitPostureDamage { get; }
        public float ComboHitRadiusMultiplier { get; }

        private void Validate()
        {
            if (Id.IsEmpty || !Id.Value.StartsWith("enemy:", StringComparison.Ordinal) ||
                DisplayNameTextId.IsEmpty || !DisplayNameTextId.Value.StartsWith("text:", StringComparison.Ordinal))
            {
                throw new FormatException("Enemy and display text IDs must use stable ID prefixes.");
            }

            if (!IsPositive(MaximumHealth) || !IsNonNegative(Armor) ||
                !IsPositive(DetectionRange) || !IsPositive(LoseTargetRange) || LoseTargetRange < DetectionRange ||
                !IsPositive(FieldOfView) || FieldOfView > 360f || !IsPositive(LeashRange) || LeashRange < LoseTargetRange ||
                !IsPositive(AttackRange) || AttackRange >= DetectionRange ||
                !IsPositive(MoveSpeed) || !IsPositive(RotationSpeed) ||
                !IsPositive(WindupDuration) || !IsPositive(AttackDuration) ||
                DamageWindowStart < 0f || DamageWindowEnd <= DamageWindowStart || DamageWindowEnd > AttackDuration ||
                !IsPositive(RecoveryDuration) || !IsPositive(AttackDamage) || !IsNonNegative(PostureDamage) ||
                !IsPositive(HitReactDuration) || !IsPositive(RespawnDelay) ||
                !IsPositive(MaximumPosture) || !IsPositive(PostureRegenPerSecond) ||
                !IsNonNegative(PostureRegenDelay) ||
                !IsPositive(ComboAttackRange) || ComboAttackRange > AttackRange ||
                !IsUnitInterval(ComboWeight) || !IsPositive(ComboCooldown) ||
                !IsPositive(ComboWindupDuration) || !IsPositive(ComboAttackDuration) ||
                ComboDamageWindow1Start < 0f || ComboDamageWindow1End <= ComboDamageWindow1Start ||
                ComboDamageWindow2Start <= ComboDamageWindow1End ||
                ComboDamageWindow2End <= ComboDamageWindow2Start ||
                ComboDamageWindow2End > ComboAttackDuration ||
                !IsPositive(ComboRecoveryDuration) || !IsPositive(ComboHitDamage) ||
                !IsNonNegative(ComboHitPostureDamage) || !IsPositive(ComboHitRadiusMultiplier))
            {
                throw new FormatException("Enemy definition contains invalid combat, movement, or timing values.");
            }
        }

        private static bool IsPositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsNonNegative(float value) =>
            value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsUnitInterval(float value) =>
            value >= 0f && value <= 1f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
