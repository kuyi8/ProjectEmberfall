using System;
using System.Collections.Generic;
using Emberfall.AI.Domain;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using UnityEngine;

namespace Emberfall.AI.Data
{
    public static class MeleeEnemyDefinitionJsonLoader
    {
        public const int CurrentSchemaVersion = 1;

        public static ContentRegistry<MeleeEnemyDefinition> Load(string json)
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

            if (root == null || root.schemaVersion != CurrentSchemaVersion || root.enemies == null)
            {
                throw new NotSupportedException("Enemy definition schema is missing or unsupported.");
            }

            var definitions = new List<MeleeEnemyDefinition>(root.enemies.Length);
            foreach (EnemyDefinitionDto dto in root.enemies)
            {
                if (dto == null ||
                    !ContentId.TryCreate(dto.id, out ContentId id) ||
                    !ContentId.TryCreate(dto.displayNameTextId, out ContentId displayNameTextId))
                {
                    throw new FormatException("Enemy definition contains an invalid stable ID.");
                }

                definitions.Add(new MeleeEnemyDefinition(
                    id,
                    displayNameTextId,
                    dto.maximumHealth,
                    dto.armor,
                    dto.detectionRange,
                    dto.loseTargetRange,
                    dto.fieldOfView,
                    dto.leashRange,
                    dto.attackRange,
                    dto.moveSpeed,
                    dto.rotationSpeed,
                    dto.windupDuration,
                    dto.attackDuration,
                    dto.damageWindowStart,
                    dto.damageWindowEnd,
                    dto.recoveryDuration,
                    dto.attackDamage,
                    dto.postureDamage,
                    dto.hitReactDuration,
                    dto.respawnDelay,
                    PositiveOr(dto.maximumPosture, 55f),
                    PositiveOr(dto.postureRegenPerSecond, 22f),
                    NonNegativeOr(dto.postureRegenDelay, 1.25f),
                    PositiveOr(dto.comboAttackRange, Math.Min(1.65f, dto.attackRange)),
                    UnitIntervalOr(dto.comboWeight, 0.42f),
                    PositiveOr(dto.comboCooldown, 3.2f),
                    PositiveOr(dto.comboWindupDuration, 0.82f),
                    PositiveOr(dto.comboAttackDuration, 0.86f),
                    NonNegativeOr(dto.comboDamageWindow1Start, 0.15f),
                    PositiveOr(dto.comboDamageWindow1End, 0.29f),
                    PositiveOr(dto.comboDamageWindow2Start, 0.57f),
                    PositiveOr(dto.comboDamageWindow2End, 0.73f),
                    PositiveOr(dto.comboRecoveryDuration, 0.84f),
                    PositiveOr(dto.comboHitDamage, 13f),
                    NonNegativeOr(dto.comboHitPostureDamage, 14f),
                    PositiveOr(dto.comboHitRadiusMultiplier, 1.08f)));
            }

            return new ContentRegistry<MeleeEnemyDefinition>(definitions);
        }

        private static float PositiveOr(float value, float fallback) => value > 0f ? value : fallback;
        private static float NonNegativeOr(float value, float fallback) => value >= 0f ? value : fallback;
        private static float UnitIntervalOr(float value, float fallback) =>
            value > 0f && value <= 1f ? value : fallback;

        [Serializable]
        private sealed class EnemyCatalogDto
        {
            public int schemaVersion;
            public EnemyDefinitionDto[] enemies;
        }

        [Serializable]
        private sealed class EnemyDefinitionDto
        {
            public string id;
            public string displayNameTextId;
            public float maximumHealth;
            public float armor;
            public float detectionRange;
            public float loseTargetRange;
            public float fieldOfView;
            public float leashRange;
            public float attackRange;
            public float moveSpeed;
            public float rotationSpeed;
            public float windupDuration;
            public float attackDuration;
            public float damageWindowStart;
            public float damageWindowEnd;
            public float recoveryDuration;
            public float attackDamage;
            public float postureDamage;
            public float hitReactDuration;
            public float respawnDelay;
            public float maximumPosture;
            public float postureRegenPerSecond;
            public float postureRegenDelay = -1f;
            public float comboAttackRange;
            public float comboWeight;
            public float comboCooldown;
            public float comboWindupDuration;
            public float comboAttackDuration;
            public float comboDamageWindow1Start = -1f;
            public float comboDamageWindow1End;
            public float comboDamageWindow2Start;
            public float comboDamageWindow2End;
            public float comboRecoveryDuration;
            public float comboHitDamage;
            public float comboHitPostureDamage = -1f;
            public float comboHitRadiusMultiplier;
        }
    }
}
