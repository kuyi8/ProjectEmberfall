using System;
using System.Collections.Generic;
using Emberfall.AI.Domain;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using UnityEngine;

namespace Emberfall.AI.Data
{
    public static class ShieldEnemyDefinitionJsonLoader
    {
        public const int CurrentSchemaVersion = 1;

        public static ContentRegistry<ShieldEnemyDefinition> Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Shield enemy definition JSON cannot be empty.", nameof(json));
            }

            EnemyCatalogDto root;
            try
            {
                root = JsonUtility.FromJson<EnemyCatalogDto>(json);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("Shield enemy definition JSON is malformed.", exception);
            }

            if (root == null || root.schemaVersion != CurrentSchemaVersion || root.shieldEnemies == null)
            {
                throw new NotSupportedException("Shield enemy definition schema is missing or unsupported.");
            }

            var definitions = new List<ShieldEnemyDefinition>(root.shieldEnemies.Length);
            foreach (ShieldEnemyDefinitionDto dto in root.shieldEnemies)
            {
                if (dto == null || !ContentId.TryCreate(dto.id, out ContentId id) ||
                    !ContentId.TryCreate(dto.displayNameTextId, out ContentId displayNameTextId))
                {
                    throw new FormatException("Shield enemy definition contains an invalid stable ID.");
                }

                definitions.Add(new ShieldEnemyDefinition(
                    id, displayNameTextId, dto.maximumHealth, dto.armor,
                    dto.detectionRange, dto.loseTargetRange, dto.fieldOfView, dto.leashRange,
                    dto.attackRange, dto.moveSpeed, dto.rotationSpeed,
                    dto.windupDuration, dto.attackDuration, dto.damageWindowStart, dto.damageWindowEnd,
                    dto.recoveryDuration, dto.attackDamage, dto.postureDamage,
                    dto.hitReactDuration, dto.respawnDelay,
                    dto.guardCapacity, dto.frontalBlockAngle, dto.guardBreakDuration,
                    dto.brokenDamageMultiplier,
                    PositiveOr(dto.bashAttackRange, 1.5f),
                    UnitIntervalOr(dto.bashWeight, 0.43f),
                    PositiveOr(dto.bashCooldown, 3.5f),
                    PositiveOr(dto.bashWindupDuration, 0.4f),
                    PositiveOr(dto.bashAttackDuration, 0.34f),
                    NonNegativeOr(dto.bashDamageWindowStart, 0.1f),
                    PositiveOr(dto.bashDamageWindowEnd, 0.24f),
                    PositiveOr(dto.bashRecoveryDuration, 0.64f),
                    PositiveOr(dto.bashDamage, 12f),
                    NonNegativeOr(dto.bashPostureDamage, 38f),
                    PositiveOr(dto.bashHitRadiusMultiplier, 0.88f),
                    dto.blockedHitsForBash > 0 ? dto.blockedHitsForBash : 2,
                    dto.scorchedBurstEnabled,
                    UnitIntervalOr(dto.scorchedBurstWeight, 0.78f),
                    PositiveOr(dto.scorchedBurstCooldown, 6f),
                    PositiveOr(dto.scorchedBurstWindupDuration, 1.05f),
                    PositiveOr(dto.scorchedBurstAttackDuration, 0.2f),
                    PositiveOr(dto.scorchedBurstRecoveryDuration, 1f),
                    PositiveOr(dto.scorchedBurstTriggerDelay, 0.85f),
                    PositiveOr(dto.scorchedBurstRadius, 2.65f),
                    PositiveOr(dto.scorchedBurstDamage, 32f),
                    NonNegativeOr(dto.scorchedBurstPostureDamage, 42f)));
            }

            return new ContentRegistry<ShieldEnemyDefinition>(definitions);
        }

        private static float PositiveOr(float value, float fallback) => value > 0f ? value : fallback;
        private static float NonNegativeOr(float value, float fallback) => value >= 0f ? value : fallback;
        private static float UnitIntervalOr(float value, float fallback) =>
            value > 0f && value <= 1f ? value : fallback;

        [Serializable]
        private sealed class EnemyCatalogDto
        {
            public int schemaVersion;
            public ShieldEnemyDefinitionDto[] shieldEnemies;
        }

        [Serializable]
        private sealed class ShieldEnemyDefinitionDto
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
            public float guardCapacity;
            public float frontalBlockAngle;
            public float guardBreakDuration;
            public float brokenDamageMultiplier;
            public float bashAttackRange;
            public float bashWeight;
            public float bashCooldown;
            public float bashWindupDuration;
            public float bashAttackDuration;
            public float bashDamageWindowStart = -1f;
            public float bashDamageWindowEnd;
            public float bashRecoveryDuration;
            public float bashDamage;
            public float bashPostureDamage = -1f;
            public float bashHitRadiusMultiplier;
            public int blockedHitsForBash;
            public bool scorchedBurstEnabled;
            public float scorchedBurstWeight;
            public float scorchedBurstCooldown;
            public float scorchedBurstWindupDuration;
            public float scorchedBurstAttackDuration;
            public float scorchedBurstRecoveryDuration;
            public float scorchedBurstTriggerDelay;
            public float scorchedBurstRadius;
            public float scorchedBurstDamage;
            public float scorchedBurstPostureDamage = -1f;
        }
    }
}
