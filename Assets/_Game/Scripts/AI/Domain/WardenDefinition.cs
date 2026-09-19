using System;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;

namespace Emberfall.AI.Domain
{
    public sealed class WardenDefinition : IContentDefinition
    {
        public WardenDefinition(
            ContentId id,
            ContentId displayNameTextId,
            float maximumHealth,
            float armor,
            float detectionRange,
            float loseTargetRange,
            float leashRange,
            float moveSpeed,
            float rotationSpeed,
            float guardCapacity,
            float frontalBlockAngle,
            float guardBreakDuration,
            float guardBreakDamageMultiplier,
            float recoveryDamageMultiplier,
            float phaseTwoThreshold,
            float phaseTransitionDuration,
            float phaseTwoArmor,
            float phaseTwoMoveSpeedMultiplier,
            float phaseTwoPostureCapacity,
            float runeCleaveAngle,
            float delayedBlastFuse,
            float delayedBlastRadius,
            float chargeSpeed,
            float chargeTravelDistance,
            WardenAttackDefinition swordCombo,
            WardenAttackDefinition shieldBash,
            WardenAttackDefinition charge,
            WardenAttackDefinition runeCleave,
            WardenAttackDefinition delayedBlast)
        {
            if (id.IsEmpty || displayNameTextId.IsEmpty || !Positive(maximumHealth) || !NonNegative(armor) ||
                !Positive(detectionRange) || !Positive(loseTargetRange) || loseTargetRange < detectionRange ||
                !Positive(leashRange) || !Positive(moveSpeed) || !Positive(rotationSpeed) ||
                !Positive(guardCapacity) || !Positive(frontalBlockAngle) || frontalBlockAngle > 240f ||
                !Positive(guardBreakDuration) || guardBreakDamageMultiplier <= 1f || recoveryDamageMultiplier <= 1f ||
                phaseTwoThreshold <= 0f || phaseTwoThreshold >= 1f || !Positive(phaseTransitionDuration) ||
                !NonNegative(phaseTwoArmor) || !Positive(phaseTwoMoveSpeedMultiplier) ||
                !Positive(phaseTwoPostureCapacity) || !Positive(runeCleaveAngle) || runeCleaveAngle > 180f ||
                !Positive(delayedBlastFuse) || !Positive(delayedBlastRadius) || !Positive(chargeSpeed) ||
                !Positive(chargeTravelDistance) || swordCombo?.Kind != WardenAttackKind.SwordCombo ||
                shieldBash?.Kind != WardenAttackKind.ShieldBash || charge?.Kind != WardenAttackKind.Charge ||
                runeCleave?.Kind != WardenAttackKind.RuneCleave ||
                delayedBlast?.Kind != WardenAttackKind.DelayedBlast)
            {
                throw new FormatException("Warden definition is invalid.");
            }

            Id = id;
            DisplayNameTextId = displayNameTextId;
            MaximumHealth = maximumHealth;
            Armor = armor;
            DetectionRange = detectionRange;
            LoseTargetRange = loseTargetRange;
            LeashRange = leashRange;
            MoveSpeed = moveSpeed;
            RotationSpeed = rotationSpeed;
            GuardCapacity = guardCapacity;
            FrontalBlockAngle = frontalBlockAngle;
            GuardBreakDuration = guardBreakDuration;
            GuardBreakDamageMultiplier = guardBreakDamageMultiplier;
            RecoveryDamageMultiplier = recoveryDamageMultiplier;
            PhaseTwoThreshold = phaseTwoThreshold;
            PhaseTransitionDuration = phaseTransitionDuration;
            PhaseTwoArmor = phaseTwoArmor;
            PhaseTwoMoveSpeedMultiplier = phaseTwoMoveSpeedMultiplier;
            PhaseTwoPostureCapacity = phaseTwoPostureCapacity;
            RuneCleaveAngle = runeCleaveAngle;
            DelayedBlastFuse = delayedBlastFuse;
            DelayedBlastRadius = delayedBlastRadius;
            ChargeSpeed = chargeSpeed;
            ChargeTravelDistance = chargeTravelDistance;
            SwordCombo = swordCombo;
            ShieldBash = shieldBash;
            Charge = charge;
            RuneCleave = runeCleave;
            DelayedBlast = delayedBlast;
        }

        public ContentId Id { get; }
        public ContentId DisplayNameTextId { get; }
        public float MaximumHealth { get; }
        public float Armor { get; }
        public float DetectionRange { get; }
        public float LoseTargetRange { get; }
        public float LeashRange { get; }
        public float MoveSpeed { get; }
        public float RotationSpeed { get; }
        public float GuardCapacity { get; }
        public float FrontalBlockAngle { get; }
        public float GuardBreakDuration { get; }
        public float GuardBreakDamageMultiplier { get; }
        public float RecoveryDamageMultiplier { get; }
        public float PhaseTwoThreshold { get; }
        public float PhaseTransitionDuration { get; }
        public float PhaseTwoArmor { get; }
        public float PhaseTwoMoveSpeedMultiplier { get; }
        public float PhaseTwoPostureCapacity { get; }
        public float RuneCleaveAngle { get; }
        public float DelayedBlastFuse { get; }
        public float DelayedBlastRadius { get; }
        public float ChargeSpeed { get; }
        public float ChargeTravelDistance { get; }
        public WardenAttackDefinition SwordCombo { get; }
        public WardenAttackDefinition ShieldBash { get; }
        public WardenAttackDefinition Charge { get; }
        public WardenAttackDefinition RuneCleave { get; }
        public WardenAttackDefinition DelayedBlast { get; }

        public WardenAttackDefinition GetAttack(WardenAttackKind kind) => kind switch
        {
            WardenAttackKind.ShieldBash => ShieldBash,
            WardenAttackKind.Charge => Charge,
            WardenAttackKind.RuneCleave => RuneCleave,
            WardenAttackKind.DelayedBlast => DelayedBlast,
            _ => SwordCombo
        };

        private static bool Positive(float value) => value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);
        private static bool NonNegative(float value) => value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
