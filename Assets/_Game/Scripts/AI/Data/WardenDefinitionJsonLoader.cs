using System;
using System.Collections.Generic;
using Emberfall.AI.Domain;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using UnityEngine;

namespace Emberfall.AI.Data
{
    public static class WardenDefinitionJsonLoader
    {
        public const int CurrentSchemaVersion = 1;

        public static ContentRegistry<WardenDefinition> Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new ArgumentException("Warden definition JSON cannot be empty.", nameof(json));

            EnemyCatalogDto root;
            try { root = JsonUtility.FromJson<EnemyCatalogDto>(json); }
            catch (ArgumentException exception)
            {
                throw new FormatException("Warden definition JSON is malformed.", exception);
            }

            if (root == null || root.schemaVersion != CurrentSchemaVersion || root.wardens == null)
                throw new NotSupportedException("Warden definition schema is missing or unsupported.");

            var definitions = new List<WardenDefinition>(root.wardens.Length);
            foreach (WardenDefinitionDto dto in root.wardens)
            {
                if (dto == null || !ContentId.TryCreate(dto.id, out ContentId id) ||
                    !ContentId.TryCreate(dto.displayNameTextId, out ContentId textId))
                    throw new FormatException("Warden definition contains an invalid stable ID.");

                definitions.Add(new WardenDefinition(
                    id, textId, dto.maximumHealth, dto.armor, dto.detectionRange,
                    dto.loseTargetRange, dto.leashRange, dto.moveSpeed, dto.rotationSpeed,
                    dto.guardCapacity, dto.frontalBlockAngle, dto.guardBreakDuration,
                    dto.guardBreakDamageMultiplier, dto.recoveryDamageMultiplier,
                    dto.phaseTwoThreshold, dto.phaseTransitionDuration, dto.phaseTwoArmor,
                    dto.phaseTwoMoveSpeedMultiplier, dto.phaseTwoPostureCapacity,
                    dto.runeCleaveAngle, dto.delayedBlastFuse, dto.delayedBlastRadius,
                    dto.chargeSpeed, dto.chargeTravelDistance,
                    CreateAttack(WardenAttackKind.SwordCombo, dto.swordCombo),
                    CreateAttack(WardenAttackKind.ShieldBash, dto.shieldBash),
                    CreateAttack(WardenAttackKind.Charge, dto.charge),
                    CreateAttack(WardenAttackKind.RuneCleave, dto.runeCleave),
                    CreateAttack(WardenAttackKind.DelayedBlast, dto.delayedBlast)));
            }

            return new ContentRegistry<WardenDefinition>(definitions);
        }

        private static WardenAttackDefinition CreateAttack(WardenAttackKind kind, AttackDto dto)
        {
            if (dto == null) throw new FormatException($"Warden attack is missing: {kind}.");
            return new WardenAttackDefinition(
                kind, dto.minimumRange, dto.maximumRange, dto.windupDuration, dto.attackDuration,
                dto.recoveryDuration, dto.damage, dto.postureDamage, dto.hitRadiusMultiplier,
                dto.firstWindowStart, dto.firstWindowEnd, dto.secondWindowStart, dto.secondWindowEnd,
                dto.cooldown);
        }

        [Serializable]
        private sealed class EnemyCatalogDto
        {
            public int schemaVersion;
            public WardenDefinitionDto[] wardens;
        }

        [Serializable]
        private sealed class WardenDefinitionDto
        {
            public string id;
            public string displayNameTextId;
            public float maximumHealth;
            public float armor;
            public float detectionRange;
            public float loseTargetRange;
            public float leashRange;
            public float moveSpeed;
            public float rotationSpeed;
            public float guardCapacity;
            public float frontalBlockAngle;
            public float guardBreakDuration;
            public float guardBreakDamageMultiplier;
            public float recoveryDamageMultiplier;
            public float phaseTwoThreshold;
            public float phaseTransitionDuration;
            public float phaseTwoArmor;
            public float phaseTwoMoveSpeedMultiplier;
            public float phaseTwoPostureCapacity;
            public float runeCleaveAngle;
            public float delayedBlastFuse;
            public float delayedBlastRadius;
            public float chargeSpeed;
            public float chargeTravelDistance;
            public AttackDto swordCombo;
            public AttackDto shieldBash;
            public AttackDto charge;
            public AttackDto runeCleave;
            public AttackDto delayedBlast;
        }

        [Serializable]
        private sealed class AttackDto
        {
            public float minimumRange;
            public float maximumRange;
            public float windupDuration;
            public float attackDuration;
            public float recoveryDuration;
            public float damage;
            public float postureDamage;
            public float hitRadiusMultiplier;
            public float firstWindowStart;
            public float firstWindowEnd;
            public float secondWindowStart;
            public float secondWindowEnd;
            public float cooldown;
        }
    }
}
