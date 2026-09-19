using System;
using Emberfall.Core.Identifiers;

namespace Emberfall.AI.Domain
{
    public sealed class ShieldEnemyDefinition : MeleeEnemyDefinition
    {
        public ShieldEnemyDefinition(
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
            float guardCapacity,
            float frontalBlockAngle,
            float guardBreakDuration,
            float brokenDamageMultiplier,
            float bashAttackRange = 1.5f,
            float bashWeight = 0.43f,
            float bashCooldown = 3.5f,
            float bashWindupDuration = 0.4f,
            float bashAttackDuration = 0.34f,
            float bashDamageWindowStart = 0.1f,
            float bashDamageWindowEnd = 0.24f,
            float bashRecoveryDuration = 0.64f,
            float bashDamage = 12f,
            float bashPostureDamage = 38f,
            float bashHitRadiusMultiplier = 0.88f,
            int blockedHitsForBash = 2,
            bool scorchedBurstEnabled = false,
            float scorchedBurstWeight = 0.78f,
            float scorchedBurstCooldown = 6f,
            float scorchedBurstWindupDuration = 1.05f,
            float scorchedBurstAttackDuration = 0.2f,
            float scorchedBurstRecoveryDuration = 1f,
            float scorchedBurstTriggerDelay = 0.85f,
            float scorchedBurstRadius = 2.65f,
            float scorchedBurstDamage = 32f,
            float scorchedBurstPostureDamage = 42f)
            : base(
                id,
                displayNameTextId,
                maximumHealth,
                armor,
                detectionRange,
                loseTargetRange,
                fieldOfView,
                leashRange,
                attackRange,
                moveSpeed,
                rotationSpeed,
                windupDuration,
                attackDuration,
                damageWindowStart,
                damageWindowEnd,
                recoveryDuration,
                attackDamage,
                postureDamage,
                hitReactDuration,
                respawnDelay)
        {
            GuardCapacity = guardCapacity;
            FrontalBlockAngle = frontalBlockAngle;
            GuardBreakDuration = guardBreakDuration;
            BrokenDamageMultiplier = brokenDamageMultiplier;
            BashAttackRange = bashAttackRange;
            BashWeight = bashWeight;
            BashCooldown = bashCooldown;
            BashWindupDuration = bashWindupDuration;
            BashAttackDuration = bashAttackDuration;
            BashDamageWindowStart = bashDamageWindowStart;
            BashDamageWindowEnd = bashDamageWindowEnd;
            BashRecoveryDuration = bashRecoveryDuration;
            BashDamage = bashDamage;
            BashPostureDamage = bashPostureDamage;
            BashHitRadiusMultiplier = bashHitRadiusMultiplier;
            BlockedHitsForBash = blockedHitsForBash;
            ScorchedBurstEnabled = scorchedBurstEnabled;
            ScorchedBurstWeight = scorchedBurstWeight;
            ScorchedBurstCooldown = scorchedBurstCooldown;
            ScorchedBurstWindupDuration = scorchedBurstWindupDuration;
            ScorchedBurstAttackDuration = scorchedBurstAttackDuration;
            ScorchedBurstRecoveryDuration = scorchedBurstRecoveryDuration;
            ScorchedBurstTriggerDelay = scorchedBurstTriggerDelay;
            ScorchedBurstRadius = scorchedBurstRadius;
            ScorchedBurstDamage = scorchedBurstDamage;
            ScorchedBurstPostureDamage = scorchedBurstPostureDamage;
            if (!IsPositive(guardCapacity) || !IsPositive(guardBreakDuration) ||
                !IsPositive(brokenDamageMultiplier) || brokenDamageMultiplier <= 1f ||
                !IsPositive(frontalBlockAngle) || frontalBlockAngle > 240f ||
                !IsPositive(BashAttackRange) || BashAttackRange > AttackRange ||
                !IsUnitInterval(BashWeight) || !IsPositive(BashCooldown) ||
                !IsPositive(BashWindupDuration) || !IsPositive(BashAttackDuration) ||
                BashDamageWindowStart < 0f || BashDamageWindowEnd <= BashDamageWindowStart ||
                BashDamageWindowEnd > BashAttackDuration || !IsPositive(BashRecoveryDuration) ||
                !IsPositive(BashDamage) || BashPostureDamage < 0f ||
                !IsPositive(BashHitRadiusMultiplier) || BlockedHitsForBash < 1)
            {
                throw new FormatException("Shield enemy guard tuning is invalid.");
            }

            if (ScorchedBurstEnabled &&
                (!IsUnitInterval(ScorchedBurstWeight) || !IsPositive(ScorchedBurstCooldown) ||
                 !IsPositive(ScorchedBurstWindupDuration) || !IsPositive(ScorchedBurstAttackDuration) ||
                 !IsPositive(ScorchedBurstRecoveryDuration) || !IsPositive(ScorchedBurstTriggerDelay) ||
                 !IsPositive(ScorchedBurstRadius) || !IsPositive(ScorchedBurstDamage) ||
                 ScorchedBurstPostureDamage < 0f))
            {
                throw new FormatException("Shield enemy scorched-burst tuning is invalid.");
            }
        }

        public float GuardCapacity { get; }
        public float FrontalBlockAngle { get; }
        public float GuardBreakDuration { get; }
        public float BrokenDamageMultiplier { get; }
        public float BashAttackRange { get; }
        public float BashWeight { get; }
        public float BashCooldown { get; }
        public float BashWindupDuration { get; }
        public float BashAttackDuration { get; }
        public float BashDamageWindowStart { get; }
        public float BashDamageWindowEnd { get; }
        public float BashRecoveryDuration { get; }
        public float BashDamage { get; }
        public float BashPostureDamage { get; }
        public float BashHitRadiusMultiplier { get; }
        public int BlockedHitsForBash { get; }
        public bool ScorchedBurstEnabled { get; }
        public float ScorchedBurstWeight { get; }
        public float ScorchedBurstCooldown { get; }
        public float ScorchedBurstWindupDuration { get; }
        public float ScorchedBurstAttackDuration { get; }
        public float ScorchedBurstRecoveryDuration { get; }
        public float ScorchedBurstTriggerDelay { get; }
        public float ScorchedBurstRadius { get; }
        public float ScorchedBurstDamage { get; }
        public float ScorchedBurstPostureDamage { get; }

        private static bool IsPositive(float value) =>
            value > 0f && !float.IsNaN(value) && !float.IsInfinity(value);

        private static bool IsUnitInterval(float value) =>
            value >= 0f && value <= 1f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
