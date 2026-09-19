using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.Gameplay.Animation
{
    [CreateAssetMenu(menuName = "Emberfall/Animation/Player Animation Set", fileName = "PlayerAnimationSet")]
    public sealed class PlayerAnimationSet : ScriptableObject
    {
        [SerializeField] private RuntimeAnimatorController _controller;
        [SerializeField] private AnimationClip _lightAttack1;
        [SerializeField] private AnimationClip _lightAttack1Recovery;
        [SerializeField] private AnimationClip _lightAttack2;
        [SerializeField] private AnimationClip _lightAttack2Recovery;
        [SerializeField] private AnimationClip _lightAttack3;
        [SerializeField] private AnimationClip _heavyCharge;
        [SerializeField] private AnimationClip _heavyAttack;
        [SerializeField] private AnimationClip _rangedAttack;
        [SerializeField] private AnimationClip _dodge;
        [SerializeField] private AnimationClip _guard;
        [SerializeField] private AnimationClip _guardBreak;
        [SerializeField] private AnimationClip _hitReact;
        [SerializeField] private AnimationClip _heal;
        [SerializeField] private AnimationClip _dead;
        [SerializeField] private AnimationClip _enemyMeleeCombo;
        [SerializeField] private AnimationClip _enemyRuneCast;
        [SerializeField] private AnimationClip _enemyShieldGuard;
        [SerializeField] private AnimationClip _enemyShieldBash;
        [SerializeField] private AnimationClip _wardenCharge;
        [SerializeField] private AnimationClip _wardenPhaseBreak;
        [SerializeField] private AnimationClip _wardenRuneCleave;

        public RuntimeAnimatorController Controller => _controller;

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
                _ => null
            };
        }

#if UNITY_EDITOR
        public void Configure(
            RuntimeAnimatorController controller,
            AnimationClip lightAttack1,
            AnimationClip lightAttack1Recovery,
            AnimationClip lightAttack2,
            AnimationClip lightAttack2Recovery,
            AnimationClip lightAttack3,
            AnimationClip heavyCharge,
            AnimationClip heavyAttack,
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
