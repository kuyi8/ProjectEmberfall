using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.Gameplay.Animation
{
    [CreateAssetMenu(menuName = "Emberfall/Animation/Player Animation Set", fileName = "PlayerAnimationSet")]
    public sealed class PlayerAnimationSet : ScriptableObject
    {
        [SerializeField] private RuntimeAnimatorController _controller;
        [SerializeField] private RuntimeAnimatorController _offlineController;
        [SerializeField, HideInInspector] private string _offlineControllerSourceHash;
        [Header("Offline locomotion visual facing (degrees, not motor heading)")]
        [SerializeField, Range(-45f, 45f)] private float _offlineWalkYaw;
        [SerializeField, Range(-45f, 45f)] private float _offlineJogYaw;
        [SerializeField, Range(-45f, 45f)] private float _offlineSprintYaw;
        [SerializeField] private AnimationClip _lightAttack1;
        [SerializeField] private AnimationClip _lightAttack1Recovery;
        [SerializeField] private AnimationClip _lightAttack2;
        [SerializeField] private AnimationClip _lightAttack2Recovery;
        [SerializeField] private AnimationClip _lightAttack3;
        [SerializeField] private AnimationClip _heavyCharge;
        [SerializeField] private AnimationClip _heavyAttack;
        [SerializeField] private AnimationClip _offlineHeavyAttack;
        [SerializeField] private AnimationClip _offlineExecution;
        [SerializeField] private AnimationClip _offlineRangedAttack;
        // Opt-in for owned offline clips authored on the domain's timeline. Legacy sets keep zero entry offset.
        [SerializeField] private bool _offlineRangedDomainEntryTime;
        // Independent presentation opt-in: never implicitly enabled by entry clock alignment.
        [SerializeField] private bool _offlineRangedMotionContinuity;
        [SerializeField] private AnimationClip _sweep;
        [SerializeField] private AnimationClip _rangedAttack;
        [SerializeField] private AnimationClip _dodge;
        [SerializeField] private AnimationClip _guard;
        [SerializeField] private AnimationClip _guardBreak;
        [SerializeField] private AnimationClip _hitReact;
        // Independent offline candidate; authored sets retain the legacy same-state behaviour by default.
        [SerializeField] private bool _offlineHitReactReentry;
        [SerializeField] private AnimationClip _heal;
        [SerializeField] private AnimationClip _dead;
        [SerializeField] private AnimationClip _enemyMeleeCombo;
        [SerializeField] private AnimationClip _enemyRuneCast;
        [SerializeField] private AnimationClip _enemyShieldGuard;
        [SerializeField] private AnimationClip _enemyShieldBash;
        [SerializeField] private AnimationClip _wardenCharge;
        [SerializeField] private AnimationClip _wardenPhaseBreak;
        [SerializeField] private AnimationClip _wardenRuneCleave;
        [SerializeField] private AnimationClip _priestProjectileWindup;
        [SerializeField] private AnimationClip _priestProjectileRelease;

        public RuntimeAnimatorController Controller => _controller;
        // Optional offline presentation only. Enemy and network adapters retain Controller.
        public RuntimeAnimatorController OfflineController => _offlineController != null ? _offlineController : _controller;
        public bool KeepOfflineRangedAnimationMoving => _offlineRangedMotionContinuity && _offlineRangedAttack != null;
        public bool RestartOfflineHitReactOnAcceptedDamage => _offlineHitReactReentry && _hitReact != null;

        // Same linear weights and damped Speed parameter as the owned 0/2.2/5.4/8.2 tree.
        // Shared enemy/network adapters do not call this offline presentation policy.
        public float GetOfflineLocomotionYaw(CombatState state, float speed)
        {
            if (_offlineController == null || state != CombatState.Locomotion ||
                float.IsNaN(speed) || float.IsInfinity(speed) || speed <= 0f) return 0f;
            if (speed <= 2.2f) return Mathf.Lerp(0f, _offlineWalkYaw, speed / 2.2f);
            if (speed <= 5.4f) return Mathf.Lerp(_offlineWalkYaw, _offlineJogYaw, (speed - 2.2f) / 3.2f);
            return Mathf.Lerp(_offlineJogYaw, _offlineSprintYaw, (speed - 5.4f) / 2.8f);
        }

        public float GetOfflineEntryTimeOffset(CombatState state, float stateElapsed, float playbackSpeed)
        {
            if (!_offlineRangedDomainEntryTime || state != CombatState.RangedAttack ||
                _offlineRangedAttack == null || float.IsNaN(stateElapsed) || float.IsInfinity(stateElapsed) ||
                float.IsNaN(playbackSpeed) || float.IsInfinity(playbackSpeed) || playbackSpeed <= 0f)
            {
                return 0f;
            }

            // CrossFadeInFixedTime takes state seconds, not normalized phase. No per-frame seeking/HitStop override.
            return Mathf.Clamp(stateElapsed * playbackSpeed, 0f, _offlineRangedAttack.length);
        }

        // Existing GetClip/state names remain the shared enemy/network contract.
        public AnimationClip GetOfflineClip(CombatState state) => state switch
        {
            CombatState.HeavyAttack when _offlineHeavyAttack != null => _offlineHeavyAttack,
            CombatState.Execution when _offlineExecution != null => _offlineExecution,
            CombatState.RangedAttack when _offlineRangedAttack != null => _offlineRangedAttack,
            _ => GetClip(state)
        };

        public string GetOfflineStateName(CombatState state) => state switch
        {
            CombatState.HeavyAttack when _offlineHeavyAttack != null => "PlayerHeavyAttack",
            CombatState.Execution when _offlineExecution != null => "PlayerExecution",
            CombatState.RangedAttack when _offlineRangedAttack != null => "PlayerRangedAttack",
            _ => state.ToString()
        };

        public AnimationClip GetClip(CombatState state)
        {
            switch (state)
            {
                case CombatState.LightAttack1:
                    return _lightAttack1;
                case CombatState.LightAttack2:
                    return _lightAttack2;
                case CombatState.LightAttack3:
                    return _lightAttack3;
                case CombatState.HeavyCharge:
                    return _heavyCharge;
                case CombatState.HeavyAttack:
                    return _heavyAttack;
                case CombatState.Sweep:
                    return _sweep;
                case CombatState.RangedAttack:
                    return _rangedAttack;
                case CombatState.Dodge:
                    return _dodge;
                case CombatState.Guard:
                    return _guard;
                case CombatState.GuardBreak:
                    return _guardBreak;
                case CombatState.HitReact:
                    return _hitReact;
                case CombatState.Heal:
                    return _heal;
                case CombatState.Execution:
                    return _heavyAttack;
                case CombatState.Dead:
                    return _dead;
                default:
                    return null;
            }
        }

        public AnimationClip GetRecoveryClip(CombatState state)
        {
            switch (state)
            {
                case CombatState.LightAttack1:
                    return _lightAttack1Recovery;
                case CombatState.LightAttack2:
                    return _lightAttack2Recovery;
                default:
                    return null;
            }
        }

        public AnimationClip GetEnemyClip(EnemyAnimationAction action)
        {
            return action switch
            {
                EnemyAnimationAction.MeleeCombo => _enemyMeleeCombo,
                EnemyAnimationAction.RuneCast => _enemyRuneCast,
                EnemyAnimationAction.ShieldGuard => _enemyShieldGuard,
                EnemyAnimationAction.ShieldBash => _enemyShieldBash,
                EnemyAnimationAction.WardenCharge => _wardenCharge,
                EnemyAnimationAction.WardenPhaseBreak => _wardenPhaseBreak,
                EnemyAnimationAction.WardenRuneCleave => _wardenRuneCleave,
                EnemyAnimationAction.PriestProjectileWindup => _priestProjectileWindup,
                EnemyAnimationAction.PriestProjectileRelease => _priestProjectileRelease,
                _ => null
            };
        }

#if UNITY_EDITOR
        public void ConfigureOfflineHitReactReentry(bool enabled) => _offlineHitReactReentry = enabled;

        public void ConfigureOfflineRangedAttack(AnimationClip clip) => _offlineRangedAttack = clip;

        public void ConfigureOfflineRangedEntryTime(bool enabled) => _offlineRangedDomainEntryTime = enabled;

        public void ConfigureOfflineRangedMotionContinuity(bool enabled) => _offlineRangedMotionContinuity = enabled;

        public void ConfigureOfflineStrikes(AnimationClip heavy, AnimationClip execution)
        {
            _offlineHeavyAttack = heavy;
            _offlineExecution = execution;
        }

        public void ConfigureSweep(AnimationClip clip) => _sweep = clip;

        public void ConfigurePriestProjectile(AnimationClip windup, AnimationClip release)
        {
            _priestProjectileWindup = windup;
            _priestProjectileRelease = release;
        }

        public string OfflineControllerSourceHash => _offlineControllerSourceHash;
        public void ConfigureOfflineController(RuntimeAnimatorController controller, string sourceHash)
        {
            _offlineController = controller;
            _offlineControllerSourceHash = sourceHash;
        }

        public void Configure(
            RuntimeAnimatorController controller,
            AnimationClip lightAttack1,
            AnimationClip lightAttack1Recovery,
            AnimationClip lightAttack2,
            AnimationClip lightAttack2Recovery,
            AnimationClip lightAttack3,
            AnimationClip heavyCharge,
            AnimationClip heavyAttack,
            AnimationClip sweep,
            AnimationClip rangedAttack,
            AnimationClip dodge,
            AnimationClip guard,
            AnimationClip guardBreak,
            AnimationClip hitReact,
            AnimationClip heal,
            AnimationClip dead,
            AnimationClip enemyMeleeCombo,
            AnimationClip enemyRuneCast,
            AnimationClip enemyShieldGuard,
            AnimationClip enemyShieldBash,
            AnimationClip wardenCharge,
            AnimationClip wardenPhaseBreak,
            AnimationClip wardenRuneCleave)
        {
            _controller = controller;
            _lightAttack1 = lightAttack1;
            _lightAttack1Recovery = lightAttack1Recovery;
            _lightAttack2 = lightAttack2;
            _lightAttack2Recovery = lightAttack2Recovery;
            _lightAttack3 = lightAttack3;
            _heavyCharge = heavyCharge;
            _heavyAttack = heavyAttack;
            _sweep = sweep;
            _rangedAttack = rangedAttack;
            _dodge = dodge;
            _guard = guard;
            _guardBreak = guardBreak;
            _hitReact = hitReact;
            _heal = heal;
            _dead = dead;
            _enemyMeleeCombo = enemyMeleeCombo;
            _enemyRuneCast = enemyRuneCast;
            _enemyShieldGuard = enemyShieldGuard;
            _enemyShieldBash = enemyShieldBash;
            _wardenCharge = wardenCharge;
            _wardenPhaseBreak = wardenPhaseBreak;
            _wardenRuneCleave = wardenRuneCleave;
        }
#endif
    }
}
