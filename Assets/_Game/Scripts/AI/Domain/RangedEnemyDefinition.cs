using System;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;

namespace Emberfall.AI.Domain
{
    public sealed class RangedEnemyDefinition : IContentDefinition
    {
        public RangedEnemyDefinition(
            ContentId id,
            ContentId displayNameTextId,
            float maximumHealth,
            float armor,
            float detectionRange,
            float loseTargetRange,
            float fieldOfView,
            float leashRange,
            float preferredMinimumRange,
            float preferredMaximumRange,
            float moveSpeed,
            float rotationSpeed,
            float windupDuration,
            float releaseDuration,
            float recoveryDuration,
            float projectileSpeed,
            float projectileLifetime,
            float projectileRadius,
            float attackDamage,
            float postureDamage,
            float hitReactDuration,
            float respawnDelay,
            float maximumPosture = 38f,
            float postureRegenPerSecond = 18f,
            float postureRegenDelay = 1.4f,
            float groundRuneWeight = 0.44f,
            float groundRuneCooldown = 4.2f,
            float groundRuneWindupDuration = 0.9f,
            float groundRuneReleaseDuration = 0.24f,
            float groundRuneRecoveryDuration = 1.1f,
            float groundRuneTriggerDelay = 1.05f,
            float groundRuneRadius = 1.9f,
            float groundRuneDamage = 24f,
            float groundRunePostureDamage = 30f)
        {
            Id = id;
            DisplayNameTextId = displayNameTextId;
            MaximumHealth = maximumHealth;
            Armor = armor;
            DetectionRange = detectionRange;
            LoseTargetRange = loseTargetRange;
            FieldOfView = fieldOfView;
            LeashRange = leashRange;
            PreferredMinimumRange = preferredMinimumRange;
            PreferredMaximumRange = preferredMaximumRange;
            MoveSpeed = moveSpeed;
            RotationSpeed = rotationSpeed;
            WindupDuration = windupDuration;
            ReleaseDuration = releaseDuration;
            RecoveryDuration = recoveryDuration;
            ProjectileSpeed = projectileSpeed;
            ProjectileLifetime = projectileLifetime;
            ProjectileRadius = projectileRadius;
            AttackDamage = attackDamage;
            PostureDamage = postureDamage;
            HitReactDuration = hitReactDuration;
            RespawnDelay = respawnDelay;
            MaximumPosture = maximumPosture;
            PostureRegenPerSecond = postureRegenPerSecond;
            PostureRegenDelay = postureRegenDelay;
            GroundRuneWeight = groundRuneWeight;
            GroundRuneCooldown = groundRuneCooldown;
            GroundRuneWindupDuration = groundRuneWindupDuration;
            GroundRuneReleaseDuration = groundRuneReleaseDuration;
            GroundRuneRecoveryDuration = groundRuneRecoveryDuration;
            GroundRuneTriggerDelay = groundRuneTriggerDelay;
            GroundRuneRadius = groundRuneRadius;
            GroundRuneDamage = groundRuneDamage;
            GroundRunePostureDamage = groundRunePostureDamage;
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
        public float PreferredMinimumRange { get; }
        public float PreferredMaximumRange { get; }
        public float MoveSpeed { get; }
        public float RotationSpeed { get; }
        public float WindupDuration { get; }
        public float ReleaseDuration { get; }
        public float RecoveryDuration { get; }
        public float ProjectileSpeed { get; }
        public float ProjectileLifetime { get; }
        public float ProjectileRadius { get; }
        public float AttackDamage { get; }
        public float PostureDamage { get; }
        public float HitReactDuration { get; }
        public float RespawnDelay { get; }
        public float MaximumPosture { get; }
        public float PostureRegenPerSecond { get; }
        public float PostureRegenDelay { get; }
        public float GroundRuneWeight { get; }
        public float GroundRuneCooldown { get; }
        public float GroundRuneWindupDuration { get; }
        public float GroundRuneReleaseDuration { get; }
        public float GroundRuneRecoveryDuration { get; }
        public float GroundRuneTriggerDelay { get; }
        public float GroundRuneRadius { get; }
        public float GroundRuneDamage { get; }
        public float GroundRunePostureDamage { get; }

        private void Validate()
        {
            if (Id.IsEmpty || !Id.Value.StartsWith("enemy:", StringComparison.Ordinal) ||
                DisplayNameTextId.IsEmpty || !DisplayNameTextId.Value.StartsWith("text:", StringComparison.Ordinal))
            {
                throw new FormatException("Ranged enemy and display text IDs must use stable ID prefixes.");
            }

            if (!IsPositive(MaximumHealth) || !IsNonNegative(Armor) ||
                !IsPositive(DetectionRange) || !IsPositive(LoseTargetRange) || LoseTargetRange < DetectionRange ||
                !IsPositive(FieldOfView) || FieldOfView > 360f || !IsPositive(LeashRange) || LeashRange < LoseTargetRange ||
                !IsPositive(PreferredMinimumRange) || PreferredMinimumRange >= PreferredMaximumRange ||
                PreferredMaximumRange >= DetectionRange || !IsPositive(MoveSpeed) || !IsPositive(RotationSpeed) ||
                !IsPositive(WindupDuration) || !IsPositive(ReleaseDuration) || !IsPositive(RecoveryDuration) ||
                !IsPositive(ProjectileSpeed) || !IsPositive(ProjectileLifetime) || !IsPositive(ProjectileRadius) ||
                ProjectileRadius > 1f || !IsPositive(AttackDamage) || !IsNonNegative(PostureDamage) ||
                !IsPositive(HitReactDuration) || !IsPositive(RespawnDelay) ||
                !IsPositive(MaximumPosture) || !IsPositive(PostureRegenPerSecond) ||
                !IsNonNegative(PostureRegenDelay) ||
                !IsUnitInterval(GroundRuneWeight) || !IsPositive(GroundRuneCooldown) ||
                !IsPositive(GroundRuneWindupDuration) || !IsPositive(GroundRuneReleaseDuration) ||
                !IsPositive(GroundRuneRecoveryDuration) || !IsPositive(GroundRuneTriggerDelay) ||
                !IsPositive(GroundRuneRadius) || GroundRuneRadius > 5f ||
                !IsPositive(GroundRuneDamage) || !IsNonNegative(GroundRunePostureDamage))
            {
                throw new FormatException("Ranged enemy definition contains invalid combat, movement, or projectile values.");
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
