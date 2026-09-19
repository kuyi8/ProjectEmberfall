using Emberfall.AI.Domain;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.AI.Unity
{
    [DefaultExecutionOrder(100)]
    public sealed class WardenAnimationPresenter : MonoBehaviour
    {
        private const float ChargeTravelStart = 0.24f;
        private const float ChargeRecoveryStart = 0.78f;
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        [SerializeField] private Animator _animator;
        [SerializeField] private WardenActor _actor;
        [SerializeField] private PlayerAnimationSet _animationSet;
        private PresentationState _presented = (PresentationState)(-1);
        private Vector3 _anchorPosition;
        private Quaternion _anchorRotation;
        private bool _hasAnchor;

        public bool IsConfigured => _animator != null && _actor != null &&
            _animationSet != null && _animationSet.Controller != null;

        public void Configure(Animator animator, WardenActor actor, PlayerAnimationSet animationSet)
        {
            _animator = animator;
            _actor = actor;
            _animationSet = animationSet;
            ApplySettings();
        }

        private void Awake() => ApplySettings();

        private void Update()
        {
            if (!IsConfigured || _actor.Brain == null) return;
            PresentationState desired = ResolveState();
            _animator.SetFloat(SpeedId, _actor.State == WardenState.Chase || _actor.State == WardenState.Return
                ? _actor.HorizontalSpeed : 0f, 0.08f, Time.deltaTime);
            if (desired == _presented) return;
            _presented = desired;
            _animator.speed = ResolvePlaybackSpeed(desired);
            _animator.CrossFadeInFixedTime(
                ResolveStateName(desired),
                desired == PresentationState.ChargeRecovery ? 0.12f : 0.06f,
                0,
                ResolveNormalizedOffset(desired));
        }

        private void LateUpdate()
        {
            if (!_hasAnchor || _animator == null) return;
            _animator.transform.localPosition = _anchorPosition;
            _animator.transform.localRotation = _anchorRotation;
        }

        private void ApplySettings()
        {
            if (_animator == null || _animationSet == null || _animationSet.Controller == null) return;
            _animator.runtimeAnimatorController = _animationSet.Controller;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (_hasAnchor) return;
            _anchorPosition = _animator.transform.localPosition;
            _anchorRotation = _animator.transform.localRotation;
            _hasAnchor = true;
        }

        private PresentationState ResolveState()
        {
            if (_actor.State == WardenState.PhaseTransition) return PresentationState.PhaseBreak;
            if (_actor.State == WardenState.GuardBreak) return PresentationState.HitReact;
            if (_actor.State == WardenState.Dead) return PresentationState.Dead;
            if (_actor.Phase == WardenPhase.PhaseOne &&
                (_actor.State == WardenState.Dormant ||
                 (_actor.State == WardenState.Chase && _actor.HorizontalSpeed <= 0.12f)))
                return PresentationState.Guard;
            if (_actor.Brain.CurrentAttack == WardenAttackKind.Charge)
            {
                if (_actor.State == WardenState.Windup) return PresentationState.ChargeWindup;
                if (_actor.State == WardenState.Attack) return PresentationState.Charge;
                if (_actor.State == WardenState.Recovery) return PresentationState.ChargeRecovery;
            }
            if (_actor.State != WardenState.Windup && _actor.State != WardenState.Attack)
                return PresentationState.Locomotion;
            return _actor.Brain.CurrentAttack switch
            {
                WardenAttackKind.ShieldBash => PresentationState.ShieldBash,
                WardenAttackKind.Charge => PresentationState.Charge,
                WardenAttackKind.RuneCleave => PresentationState.RuneCleave,
                WardenAttackKind.DelayedBlast => PresentationState.BlastCast,
                _ => PresentationState.Combo
            };
        }

        private float ResolvePlaybackSpeed(PresentationState state)
        {
            AnimationClip clip = state switch
            {
                PresentationState.ShieldBash => _animationSet.GetEnemyClip(EnemyAnimationAction.ShieldBash),
                PresentationState.ChargeWindup or PresentationState.Charge or PresentationState.ChargeRecovery =>
                    _animationSet.GetEnemyClip(EnemyAnimationAction.WardenCharge),
                PresentationState.PhaseBreak => _animationSet.GetEnemyClip(EnemyAnimationAction.WardenPhaseBreak),
                PresentationState.RuneCleave => _animationSet.GetEnemyClip(EnemyAnimationAction.WardenRuneCleave),
                PresentationState.BlastCast => _animationSet.GetEnemyClip(EnemyAnimationAction.RuneCast),
                PresentationState.Combo => _animationSet.GetEnemyClip(EnemyAnimationAction.MeleeCombo),
                _ => null
            };
            if (clip == null) return 1f;
            float duration;
            float clipFraction;
            switch (state)
            {
                case PresentationState.ChargeWindup:
                    duration = _actor.Brain.CurrentWindupDuration;
                    clipFraction = ChargeTravelStart;
                    break;
                case PresentationState.Charge:
                    duration = _actor.Brain.CurrentAttackDuration;
                    clipFraction = ChargeRecoveryStart - ChargeTravelStart;
                    break;
                case PresentationState.ChargeRecovery:
                    duration = _actor.Brain.CurrentRecoveryDuration;
                    clipFraction = 1f - ChargeRecoveryStart;
                    break;
                case PresentationState.PhaseBreak:
                    duration = _actor.Definition.PhaseTransitionDuration;
                    clipFraction = 1f;
                    break;
                default:
                    duration = _actor.Brain.CurrentWindupDuration + _actor.Brain.CurrentAttackDuration;
                    clipFraction = 1f;
                    break;
            }
            return duration <= 0f ? 1f : Mathf.Clamp((clip.length * clipFraction) / duration, 0.25f, 3f);
        }

        private static float ResolveNormalizedOffset(PresentationState state) => state switch
        {
            PresentationState.Charge => ChargeTravelStart,
            PresentationState.ChargeRecovery => ChargeRecoveryStart,
            _ => 0f
        };

        private static string ResolveStateName(PresentationState state) => state switch
        {
            PresentationState.Combo => "EnemyMeleeCombo",
            PresentationState.Guard => "EnemyShieldGuard",
            PresentationState.ShieldBash => "EnemyShieldBash",
            PresentationState.ChargeWindup => "WardenChargeWindup",
            PresentationState.Charge => "WardenCharge",
            PresentationState.ChargeRecovery => "WardenChargeRecovery",
            PresentationState.PhaseBreak => "WardenPhaseBreak",
            PresentationState.RuneCleave => "WardenRuneCleave",
            PresentationState.BlastCast => "EnemyRuneCast",
            PresentationState.HitReact => CombatState.HitReact.ToString(),
            PresentationState.Dead => CombatState.Dead.ToString(),
            _ => CombatState.Locomotion.ToString()
        };

        private enum PresentationState
        {
            Locomotion,
            Guard,
            Combo,
            ShieldBash,
            ChargeWindup,
            Charge,
            ChargeRecovery,
            PhaseBreak,
            RuneCleave,
            BlastCast,
            HitReact,
            Dead
        }
    }
}
