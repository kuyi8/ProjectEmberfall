using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    [CreateAssetMenu(menuName = "Emberfall/Combat/Combat Tuning", fileName = "CombatTuning")]
    public sealed class CombatTuningAsset : ScriptableObject
    {
        [Header("Resources")]
        [SerializeField, Min(1f)] private float _maxHealth = 120f;
        [SerializeField, Min(1f)] private float _maxStamina = 100f;
        [SerializeField, Min(0.1f)] private float _staminaRegenPerSecond = 28f;
        [SerializeField, Min(0f)] private float _staminaRegenDelay = 0.7f;
        [SerializeField, Min(0.01f)] private float _inputBufferSeconds = 0.15f;

        [Header("Three-hit light combo")]
        [SerializeField] private float[] _lightDamage = { 22f, 26f, 34f };
        [SerializeField, Min(0.1f)] private float _lightStaminaCost = 10f;
        [SerializeField] private float[] _lightDuration = { 0.58f, 0.62f, 0.72f };
        [SerializeField] private float[] _lightDamageOpen = { 0.15f, 0.17f, 0.21f };
        [SerializeField] private float[] _lightDamageClose = { 0.29f, 0.32f, 0.40f };
        [SerializeField] private float[] _lightComboOpen = { 0.28f, 0.30f, 0.34f };
        [SerializeField] private float[] _lightComboClose = { 0.50f, 0.54f, 0.62f };
        [SerializeField] private float[] _lightDodgeCancelOpen = { 0.36f, 0.39f, 0.46f };

        [Header("Heavy attack")]
        [SerializeField, Min(1f)] private float _heavyDamage = 55f;
        [SerializeField, Min(0.1f)] private float _heavyStaminaCost = 28f;
        [SerializeField, Min(0.1f)] private float _heavyFullChargeSeconds = 0.55f;
        [SerializeField, Min(0.1f)] private float _heavyDuration = 0.82f;
        [SerializeField, Min(0f)] private float _heavyDamageOpen = 0.28f;
        [SerializeField, Min(0.01f)] private float _heavyDamageClose = 0.52f;

        [Header("Throwing knife")]
        [SerializeField, Min(1f)] private float _rangedDamage = 30f;
        [SerializeField, Min(0.1f)] private float _rangedPostureDamage = 14f;
        [SerializeField, Min(0.1f)] private float _rangedCooldown = 3.5f;
        [SerializeField, Min(0.1f)] private float _rangedDuration = 0.56f;
        [SerializeField, Min(0.01f)] private float _rangedReleaseTime = 0.22f;
        [SerializeField, Min(0.1f)] private float _rangedProjectileSpeed = 19f;
        [SerializeField, Min(1f)] private float _rangedMaximumDistance = 16f;

        [Header("Dodge and reaction")]
        [SerializeField, Min(0.1f)] private float _dodgeStaminaCost = 24f;
        [SerializeField, Min(0.1f)] private float _dodgeDuration = 0.52f;
        [SerializeField, Min(0.01f)] private float _dodgeInvulnerabilitySeconds = 0.43f;
        [SerializeField, Min(0.1f)] private float _dodgeDistance = 4.5f;
        [SerializeField, Min(0.01f)] private float _perfectDodgeWindow = 0.18f;
        [SerializeField, Min(0.1f)] private float _perfectDodgeStaminaRestore = 20f;
        [SerializeField, Min(0.1f)] private float _perfectDodgeDamageBonus = 12f;
        [SerializeField, Min(0.1f)] private float _perfectDodgePostureBonus = 18f;
        [SerializeField, Min(0.1f)] private float _hitReactDuration = 0.42f;

        [Header("Guard and posture")]
        [SerializeField, Min(1f)] private float _maxPosture = 100f;
        [SerializeField, Min(0.1f)] private float _postureRegenPerSecond = 34f;
        [SerializeField, Min(0f)] private float _postureRegenDelay = 1.2f;
        [SerializeField, Range(0f, 1f)] private float _guardDamageReduction = 0.72f;
        [SerializeField, Min(0.01f)] private float _perfectGuardWindow = 0.20f;
        [SerializeField, Range(0f, 1f)] private float _perfectGuardPostureMultiplier = 0.15f;
        [SerializeField, Min(0.1f)] private float _perfectGuardCounterPostureDamage = 60f;
        [SerializeField, Min(0.1f)] private float _guardBreakDuration = 0.78f;

        [Header("Healing flask")]
        [SerializeField, Min(1)] private int _healingFlaskCharges = 2;
        [SerializeField, Min(0.1f)] private float _healDuration = 1.05f;
        [SerializeField, Min(0.05f)] private float _healResolveTime = 0.78f;
        [SerializeField, Range(0.01f, 1f)] private float _healHealthFraction = 0.45f;

        [Header("Sprint and execution")]
        [SerializeField, Min(0.01f)] private float _sprintWarmupSeconds = 0.5f;
        [SerializeField, Min(0.1f)] private float _sprintStaminaPerSecond = 12f;
        [SerializeField, Min(0.1f)] private float _executionStaminaCost = 22f;
        [SerializeField, Min(0.1f)] private float _executionDuration = 0.72f;
        [SerializeField, Min(0.01f)] private float _executionResolveTime = 0.32f;

        [Header("Wide sweep (offline)")]
        [SerializeField, Min(1f)] private float _sweepDamage = 44f;
        [SerializeField, Min(0.1f)] private float _sweepStaminaCost = 30f;
        [SerializeField, Min(0.1f)] private float _sweepCooldown = 4.5f;
        [SerializeField, Min(0.1f)] private float _sweepDuration = 0.82f;
        [SerializeField, Min(0f)] private float _sweepDamageOpen = 0.24f;
        [SerializeField, Min(0.01f)] private float _sweepDamageClose = 0.48f;
        [SerializeField, Min(0.1f)] private float _sweepRadius = 2.7f;
        [SerializeField, Range(1f, 360f)] private float _sweepAngle = 240f;

        public CombatTuning CreateRuntimeCopy() => new CombatTuning(
            _maxHealth, _maxStamina, _staminaRegenPerSecond, _staminaRegenDelay, _inputBufferSeconds,
            _lightDamage, _lightStaminaCost, _lightDuration, _lightDamageOpen, _lightDamageClose,
            _lightComboOpen, _lightComboClose, _lightDodgeCancelOpen,
            _heavyDamage, _heavyStaminaCost, _heavyFullChargeSeconds, _heavyDuration,
            _heavyDamageOpen, _heavyDamageClose,
            _rangedDamage, _rangedPostureDamage, _rangedCooldown, _rangedDuration,
            _rangedReleaseTime, _rangedProjectileSpeed, _rangedMaximumDistance,
            _dodgeStaminaCost, _dodgeDuration, _dodgeInvulnerabilitySeconds, _dodgeDistance,
            _perfectDodgeWindow, _perfectDodgeStaminaRestore, _perfectDodgeDamageBonus,
            _perfectDodgePostureBonus, _hitReactDuration,
            _maxPosture, _postureRegenPerSecond, _postureRegenDelay, _guardDamageReduction,
            _perfectGuardWindow, _perfectGuardPostureMultiplier, _perfectGuardCounterPostureDamage,
            _guardBreakDuration, _healingFlaskCharges, _healDuration, _healResolveTime,
            _healHealthFraction, _sprintWarmupSeconds, _sprintStaminaPerSecond,
            _executionStaminaCost, _executionDuration, _executionResolveTime,
            _sweepDamage, _sweepStaminaCost, _sweepCooldown, _sweepDuration,
            _sweepDamageOpen, _sweepDamageClose, _sweepRadius, _sweepAngle);
    }
}
