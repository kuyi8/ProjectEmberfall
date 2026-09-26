using System;

namespace Emberfall.Gameplay.Combat.Domain
{
    /// <summary>Immutable runtime copy of authored combat tuning.</summary>
    public sealed class CombatTuning
    {
        private readonly float[] _lightDamage;
        private readonly float[] _lightDuration;
        private readonly float[] _lightDamageOpen;
        private readonly float[] _lightDamageClose;
        private readonly float[] _lightComboOpen;
        private readonly float[] _lightComboClose;
        private readonly float[] _lightDodgeCancelOpen;

        public CombatTuning(
            float maxHealth,
            float maxStamina,
            float staminaRegenPerSecond,
            float staminaRegenDelay,
            float inputBufferSeconds,
            float[] lightDamage,
            float lightStaminaCost,
            float[] lightDuration,
            float[] lightDamageOpen,
            float[] lightDamageClose,
            float[] lightComboOpen,
            float[] lightComboClose,
            float[] lightDodgeCancelOpen,
            float heavyDamage,
            float heavyStaminaCost,
            float heavyFullChargeSeconds,
            float heavyDuration,
            float heavyDamageOpen,
            float heavyDamageClose,
            float rangedDamage,
            float rangedPostureDamage,
            float rangedCooldown,
            float rangedDuration,
            float rangedReleaseTime,
            float rangedProjectileSpeed,
            float rangedMaximumDistance,
            float dodgeStaminaCost,
            float dodgeDuration,
            float dodgeInvulnerabilitySeconds,
            float dodgeDistance,
            float perfectDodgeWindow,
            float perfectDodgeStaminaRestore,
            float perfectDodgeDamageBonus,
            float perfectDodgePostureBonus,
            float hitReactDuration,
            float maxPosture,
            float postureRegenPerSecond,
            float postureRegenDelay,
            float guardDamageReduction,
            float perfectGuardWindow,
            float perfectGuardPostureMultiplier,
            float perfectGuardCounterPostureDamage,
            float guardBreakDuration,
            int healingFlaskCharges,
            float healDuration,
            float healResolveTime,
            float healHealthFraction,
            float sprintWarmupSeconds,
            float sprintStaminaPerSecond,
            float executionStaminaCost,
            float executionDuration,
            float executionResolveTime,
            float sweepDamage = 44f,
            float sweepStaminaCost = 30f,
            float sweepCooldown = 4.5f,
            float sweepDuration = 0.82f,
            float sweepDamageOpen = 0.255f,
            float sweepDamageClose = 0.495f,
            float sweepRadius = 2.7f,
            float sweepAngle = 240f)
        {
            ValidateTriplet(lightDamage, nameof(lightDamage));
            ValidateTriplet(lightDuration, nameof(lightDuration));
            ValidateTriplet(lightDamageOpen, nameof(lightDamageOpen));
            ValidateTriplet(lightDamageClose, nameof(lightDamageClose));
            ValidateTriplet(lightComboOpen, nameof(lightComboOpen));
            ValidateTriplet(lightComboClose, nameof(lightComboClose));
            ValidateTriplet(lightDodgeCancelOpen, nameof(lightDodgeCancelOpen));

            MaxHealth = Positive(maxHealth, nameof(maxHealth));
            MaxStamina = Positive(maxStamina, nameof(maxStamina));
            StaminaRegenPerSecond = Positive(staminaRegenPerSecond, nameof(staminaRegenPerSecond));
            StaminaRegenDelay = NonNegative(staminaRegenDelay, nameof(staminaRegenDelay));
            InputBufferSeconds = Positive(inputBufferSeconds, nameof(inputBufferSeconds));
            _lightDamage = (float[])lightDamage.Clone();
            LightStaminaCost = Positive(lightStaminaCost, nameof(lightStaminaCost));
            _lightDuration = (float[])lightDuration.Clone();
            _lightDamageOpen = (float[])lightDamageOpen.Clone();
            _lightDamageClose = (float[])lightDamageClose.Clone();
            _lightComboOpen = (float[])lightComboOpen.Clone();
            _lightComboClose = (float[])lightComboClose.Clone();
            _lightDodgeCancelOpen = (float[])lightDodgeCancelOpen.Clone();
            HeavyDamage = Positive(heavyDamage, nameof(heavyDamage));
            HeavyStaminaCost = Positive(heavyStaminaCost, nameof(heavyStaminaCost));
            HeavyFullChargeSeconds = Positive(heavyFullChargeSeconds, nameof(heavyFullChargeSeconds));
            HeavyDuration = Positive(heavyDuration, nameof(heavyDuration));
            HeavyDamageOpen = NonNegative(heavyDamageOpen, nameof(heavyDamageOpen));
            HeavyDamageClose = Positive(heavyDamageClose, nameof(heavyDamageClose));
            RangedDamage = Positive(rangedDamage, nameof(rangedDamage));
            RangedPostureDamage = Positive(rangedPostureDamage, nameof(rangedPostureDamage));
            RangedCooldown = Positive(rangedCooldown, nameof(rangedCooldown));
            RangedDuration = Positive(rangedDuration, nameof(rangedDuration));
            RangedReleaseTime = Positive(rangedReleaseTime, nameof(rangedReleaseTime));
            if (RangedReleaseTime > RangedDuration)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(rangedReleaseTime), "Ranged release cannot occur after the action ends.");
            }
            RangedProjectileSpeed = Positive(rangedProjectileSpeed, nameof(rangedProjectileSpeed));
            RangedMaximumDistance = Positive(rangedMaximumDistance, nameof(rangedMaximumDistance));
            DodgeStaminaCost = Positive(dodgeStaminaCost, nameof(dodgeStaminaCost));
            DodgeDuration = Positive(dodgeDuration, nameof(dodgeDuration));
            DodgeInvulnerabilitySeconds = Positive(dodgeInvulnerabilitySeconds, nameof(dodgeInvulnerabilitySeconds));
            if (DodgeInvulnerabilitySeconds > DodgeDuration)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(dodgeInvulnerabilitySeconds),
                    "Dodge invulnerability cannot outlast the dodge state.");
            }

            DodgeDistance = Positive(dodgeDistance, nameof(dodgeDistance));
            PerfectDodgeWindow = Positive(perfectDodgeWindow, nameof(perfectDodgeWindow));
            if (PerfectDodgeWindow > DodgeInvulnerabilitySeconds)
                throw new ArgumentOutOfRangeException(nameof(perfectDodgeWindow));
            PerfectDodgeStaminaRestore = Positive(perfectDodgeStaminaRestore, nameof(perfectDodgeStaminaRestore));
            PerfectDodgeDamageBonus = Positive(perfectDodgeDamageBonus, nameof(perfectDodgeDamageBonus));
            PerfectDodgePostureBonus = Positive(perfectDodgePostureBonus, nameof(perfectDodgePostureBonus));
            HitReactDuration = Positive(hitReactDuration, nameof(hitReactDuration));
            MaxPosture = Positive(maxPosture, nameof(maxPosture));
            PostureRegenPerSecond = Positive(postureRegenPerSecond, nameof(postureRegenPerSecond));
            PostureRegenDelay = NonNegative(postureRegenDelay, nameof(postureRegenDelay));
            GuardDamageReduction = UnitInterval(guardDamageReduction, nameof(guardDamageReduction));
            PerfectGuardWindow = Positive(perfectGuardWindow, nameof(perfectGuardWindow));
            PerfectGuardPostureMultiplier = UnitInterval(
                perfectGuardPostureMultiplier, nameof(perfectGuardPostureMultiplier));
            PerfectGuardCounterPostureDamage = Positive(
                perfectGuardCounterPostureDamage, nameof(perfectGuardCounterPostureDamage));
            GuardBreakDuration = Positive(guardBreakDuration, nameof(guardBreakDuration));
            if (healingFlaskCharges <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(healingFlaskCharges));
            }

            HealingFlaskCharges = healingFlaskCharges;
            HealDuration = Positive(healDuration, nameof(healDuration));
            HealResolveTime = Positive(healResolveTime, nameof(healResolveTime));
            if (HealResolveTime > HealDuration)
            {
                throw new ArgumentOutOfRangeException(nameof(healResolveTime), "Heal cannot resolve after its state ends.");
            }

            HealHealthFraction = UnitInterval(healHealthFraction, nameof(healHealthFraction));
            SprintWarmupSeconds = Positive(sprintWarmupSeconds, nameof(sprintWarmupSeconds));
            SprintStaminaPerSecond = Positive(sprintStaminaPerSecond, nameof(sprintStaminaPerSecond));
            ExecutionStaminaCost = Positive(executionStaminaCost, nameof(executionStaminaCost));
            ExecutionDuration = Positive(executionDuration, nameof(executionDuration));
            ExecutionResolveTime = Positive(executionResolveTime, nameof(executionResolveTime));
            if (ExecutionResolveTime > ExecutionDuration)
                throw new ArgumentOutOfRangeException(nameof(executionResolveTime));
            SweepDamage = Positive(sweepDamage, nameof(sweepDamage));
            SweepStaminaCost = Positive(sweepStaminaCost, nameof(sweepStaminaCost));
            SweepCooldown = Positive(sweepCooldown, nameof(sweepCooldown));
            SweepDuration = Positive(sweepDuration, nameof(sweepDuration));
            SweepDamageOpen = NonNegative(sweepDamageOpen, nameof(sweepDamageOpen));
            SweepDamageClose = Positive(sweepDamageClose, nameof(sweepDamageClose));
            if (SweepDamageClose > SweepDuration || SweepDamageOpen >= SweepDamageClose)
                throw new ArgumentOutOfRangeException(nameof(sweepDamageClose));
            SweepRadius = Positive(sweepRadius, nameof(sweepRadius));
            if (sweepAngle <= 0f || sweepAngle > 360f)
                throw new ArgumentOutOfRangeException(nameof(sweepAngle));
            SweepAngle = sweepAngle;
        }

        public float MaxHealth { get; }
        public float MaxStamina { get; }
        public float StaminaRegenPerSecond { get; }
        public float StaminaRegenDelay { get; }
        public float InputBufferSeconds { get; }
        public float LightStaminaCost { get; }
        public float HeavyDamage { get; }
        public float HeavyStaminaCost { get; }
        public float HeavyFullChargeSeconds { get; }
        public float HeavyDuration { get; }
        public float HeavyDamageOpen { get; }
        public float HeavyDamageClose { get; }
        public float RangedDamage { get; }
        public float RangedPostureDamage { get; }
        public float RangedCooldown { get; }
        public float RangedDuration { get; }
        public float RangedReleaseTime { get; }
        public float RangedProjectileSpeed { get; }
        public float RangedMaximumDistance { get; }
        public float DodgeStaminaCost { get; }
        public float DodgeDuration { get; }
        public float DodgeInvulnerabilitySeconds { get; }
        public float DodgeDistance { get; }
        public float PerfectDodgeWindow { get; }
        public float PerfectDodgeStaminaRestore { get; }
        public float PerfectDodgeDamageBonus { get; }
        public float PerfectDodgePostureBonus { get; }
        public float HitReactDuration { get; }
        public float MaxPosture { get; }
        public float PostureRegenPerSecond { get; }
        public float PostureRegenDelay { get; }
        public float GuardDamageReduction { get; }
        public float PerfectGuardWindow { get; }
        public float PerfectGuardPostureMultiplier { get; }
        public float PerfectGuardCounterPostureDamage { get; }
        public float GuardBreakDuration { get; }
        public int HealingFlaskCharges { get; }
        public float HealDuration { get; }
        public float HealResolveTime { get; }
        public float HealHealthFraction { get; }
        public float SprintWarmupSeconds { get; }
        public float SprintStaminaPerSecond { get; }
        public float ExecutionStaminaCost { get; }
        public float ExecutionDuration { get; }
        public float ExecutionResolveTime { get; }
        public float SweepDamage { get; }
        public float SweepStaminaCost { get; }
        public float SweepCooldown { get; }
        public float SweepDuration { get; }
        public float SweepDamageOpen { get; }
        public float SweepDamageClose { get; }
        public float SweepRadius { get; }
        public float SweepAngle { get; }

        public float GetLightDamage(int comboIndex) => _lightDamage[comboIndex];
        public float GetLightDuration(int comboIndex) => _lightDuration[comboIndex];
        public float GetLightDamageOpen(int comboIndex) => _lightDamageOpen[comboIndex];
        public float GetLightDamageClose(int comboIndex) => _lightDamageClose[comboIndex];
        public float GetLightComboOpen(int comboIndex) => _lightComboOpen[comboIndex];
        public float GetLightComboClose(int comboIndex) => _lightComboClose[comboIndex];
        public float GetLightDodgeCancelOpen(int comboIndex) => _lightDodgeCancelOpen[comboIndex];

        public static CombatTuning CreateDefault() => new CombatTuning(
            120f, 100f, 28f, 0.7f, 0.15f,
            new[] { 22f, 26f, 34f }, 10f,
            new[] { 0.58f, 0.62f, 0.72f },
            new[] { 0.15f, 0.17f, 0.21f },
            new[] { 0.29f, 0.32f, 0.40f },
            new[] { 0.28f, 0.30f, 0.34f },
            new[] { 0.50f, 0.54f, 0.62f },
            new[] { 0.36f, 0.39f, 0.46f },
            55f, 28f, 0.55f, 0.82f, 0.28f, 0.52f,
            30f, 14f, 3.5f, 0.56f, 0.22f, 19f, 16f,
            24f, 0.52f, 0.43f, 4.5f, 0.18f, 20f, 12f, 18f, 0.42f,
            100f, 34f, 1.2f, 0.72f, 0.20f, 0.15f, 60f, 0.78f,
            2, 1.05f, 0.78f, 0.45f,
            0.5f, 12f, 22f, 0.72f, 0.32f);

        private static void ValidateTriplet(float[] values, string name)
        {
            if (values == null || values.Length != 3)
            {
                throw new ArgumentException("Light combo tuning must contain exactly three values.", name);
            }

            for (int i = 0; i < values.Length; i++)
            {
                Positive(values[i], name);
            }
        }

        private static float Positive(float value, string name)
        {
            if (value <= 0f)
            {
                throw new ArgumentOutOfRangeException(name, "Value must be greater than zero.");
            }

            return value;
        }

        private static float NonNegative(float value, string name)
        {
            if (value < 0f)
            {
                throw new ArgumentOutOfRangeException(name, "Value cannot be negative.");
            }

            return value;
        }

        private static float UnitInterval(float value, string name)
        {
            if (value < 0f || value > 1f || float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new ArgumentOutOfRangeException(name, "Value must be between zero and one.");
            }

            return value;
        }
    }
}
