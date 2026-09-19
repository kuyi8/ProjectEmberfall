using System;
using System.Collections.Generic;
using Emberfall.AI.Domain;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using UnityEngine;

namespace Emberfall.AI.Data
{
    public static class RangedEnemyDefinitionJsonLoader
    {
        public const int CurrentSchemaVersion = 1;

        public static ContentRegistry<RangedEnemyDefinition> Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Enemy definition JSON cannot be empty.", nameof(json));
            }

            EnemyCatalogDto root;
            try
            {
                root = JsonUtility.FromJson<EnemyCatalogDto>(json);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("Enemy definition JSON is malformed.", exception);
            }

            if (root == null || root.schemaVersion != CurrentSchemaVersion || root.rangedEnemies == null)
            {
                throw new NotSupportedException("Ranged enemy definition schema is missing or unsupported.");
            }

            var definitions = new List<RangedEnemyDefinition>(root.rangedEnemies.Length);
            foreach (RangedEnemyDefinitionDto dto in root.rangedEnemies)
            {
                if (dto == null ||
                    !ContentId.TryCreate(dto.id, out ContentId id) ||
                    !ContentId.TryCreate(dto.displayNameTextId, out ContentId displayNameTextId))
                {
                    throw new FormatException("Ranged enemy definition contains an invalid stable ID.");
                }

                definitions.Add(new RangedEnemyDefinition(
                    id,
                    displayNameTextId,
                    dto.maximumHealth,
                    dto.armor,
                    dto.detectionRange,
                    dto.loseTargetRange,
                    dto.fieldOfView,
                    dto.leashRange,
                    dto.preferredMinimumRange,
                    dto.preferredMaximumRange,
                    dto.moveSpeed,
                    dto.rotationSpeed,
                    dto.windupDuration,
                    dto.releaseDuration,
                    dto.recoveryDuration,
                    dto.projectileSpeed,
                    dto.projectileLifetime,
                    dto.projectileRadius,
                    dto.attackDamage,
                    dto.postureDamage,
                    dto.hitReactDuration,
                    dto.respawnDelay,
                    PositiveOr(dto.maximumPosture, 38f),
                    PositiveOr(dto.postureRegenPerSecond, 18f),
                    NonNegativeOr(dto.postureRegenDelay, 1.4f),
                    UnitIntervalOr(dto.groundRuneWeight, 0.44f),
                    PositiveOr(dto.groundRuneCooldown, 4.2f),
                    PositiveOr(dto.groundRuneWindupDuration, 0.9f),
                    PositiveOr(dto.groundRuneReleaseDuration, 0.24f),
                    PositiveOr(dto.groundRuneRecoveryDuration, 1.1f),
                    PositiveOr(dto.groundRuneTriggerDelay, 1.05f),
                    PositiveOr(dto.groundRuneRadius, 1.9f),
                    PositiveOr(dto.groundRuneDamage, 24f),
                    NonNegativeOr(dto.groundRunePostureDamage, 30f)));
            }

            return new ContentRegistry<RangedEnemyDefinition>(definitions);
        }

        private static float PositiveOr(float value, float fallback) => value > 0f ? value : fallback;
        private static float NonNegativeOr(float value, float fallback) => value >= 0f ? value : fallback;
        private static float UnitIntervalOr(float value, float fallback) =>
            value > 0f && value <= 1f ? value : fallback;

        [Serializable]
        private sealed class EnemyCatalogDto
        {
            public int schemaVersion;
            public RangedEnemyDefinitionDto[] rangedEnemies;
        }

        [Serializable]
        private sealed class RangedEnemyDefinitionDto
        {
            public string id;
            public string displayNameTextId;
            public float maximumHealth;
            public float armor;
            public float detectionRange;
            public float loseTargetRange;
            public float fieldOfView;
            public float leashRange;
            public float preferredMinimumRange;
            public float preferredMaximumRange;
            public float moveSpeed;
            public float rotationSpeed;
            public float windupDuration;
            public float releaseDuration;
            public float recoveryDuration;
            public float projectileSpeed;
            public float projectileLifetime;
            public float projectileRadius;
            public float attackDamage;
            public float postureDamage;
            public float hitReactDuration;
            public float respawnDelay;
            public float maximumPosture;
            public float postureRegenPerSecond;
            public float postureRegenDelay = -1f;
            public float groundRuneWeight;
            public float groundRuneCooldown;
            public float groundRuneWindupDuration;
            public float groundRuneReleaseDuration;
            public float groundRuneRecoveryDuration;
            public float groundRuneTriggerDelay;
            public float groundRuneRadius;
            public float groundRuneDamage;
            public float groundRunePostureDamage = -1f;
        }
    }
}
