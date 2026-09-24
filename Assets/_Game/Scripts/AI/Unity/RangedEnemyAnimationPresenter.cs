using Emberfall.AI.Domain;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.AI.Unity
{
    [DefaultExecutionOrder(100)]
    public sealed class RangedEnemyAnimationPresenter : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private const float CrossFadeSeconds = 0.08f;

        [SerializeField] private Animator _animator;
        [SerializeField] private RangedEnemyActor _actor;
        [SerializeField] private PlayerAnimationSet _animationSet;

        private PresentationState _presented = (PresentationState)(-1);
        private Vector3 _anchoredLocalPosition;
        private Quaternion _anchoredLocalRotation;
        private bool _hasAnchor;

        public bool IsConfigured =>
            _animator != null && _actor != null && _animationSet != null && _animationSet.Controller != null;

        public void Configure(Animator animator, RangedEnemyActor actor, PlayerAnimationSet animationSet)
        {
            _animator = animator;
            _actor = actor;
            _animationSet = animationSet;
            ApplyAnimatorSettings();
        }

        private void Awake()
        {
            ApplyAnimatorSettings();
        }

        private void Update()
        {
            if (!IsConfigured || _actor.Brain == null)
            {
                return;
            }

            PresentationState desired = GetPresentationState(_actor.State);
            _animator.SetFloat(SpeedId, _actor.HorizontalSpeed, 0.08f, Time.deltaTime);
            if (desired == _presented)
            {
                return;
            }

            _presented = desired;
            AnimatorSpeedCoordinator.SetBase(_animator, GetPlaybackSpeed(desired), desired == PresentationState.Dead);
            _animator.CrossFadeInFixedTime(GetStateName(desired), CrossFadeSeconds, 0, 0f);
        }

        private void LateUpdate()
        {
            if (_hasAnchor && _animator != null)
            {
                _animator.transform.localPosition = _anchoredLocalPosition;
                _animator.transform.localRotation = _anchoredLocalRotation;
            }
        }

        private void ApplyAnimatorSettings()
        {
            if (_animator == null || _animationSet == null || _animationSet.Controller == null)
            {
                return;
            }

            _animator.runtimeAnimatorController = _animationSet.Controller;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (!_hasAnchor)
            {
                _anchoredLocalPosition = _animator.transform.localPosition;
                _anchoredLocalRotation = _animator.transform.localRotation;
                _hasAnchor = true;
            }
        }

        private float GetPlaybackSpeed(PresentationState state)
        {
            if (_actor.Definition == null)
            {
                return 1f;
            }

            if (state == PresentationState.Release)
            {
                AnimationClip clip = _animationSet.GetClip(CombatState.HeavyAttack);
                return clip != null
                    ? Mathf.Clamp(clip.length / _actor.Definition.ReleaseDuration, 0.35f, 3f)
                    : 1f;
            }

            if (state == PresentationState.GroundRune)
            {
                AnimationClip clip = _animationSet.GetEnemyClip(EnemyAnimationAction.RuneCast);
                float duration = _actor.Brain.CurrentWindupDuration + _actor.Brain.CurrentReleaseDuration;
                return clip != null
                    ? Mathf.Clamp(clip.length / duration, 0.35f, 3f)
                    : 1f;
            }

            return 1f;
        }

        private PresentationState GetPresentationState(RangedEnemyState state)
        {
            if ((state == RangedEnemyState.Windup || state == RangedEnemyState.Release) &&
                _actor.Brain.CurrentAttack == RangedAttackKind.GroundRune)
            {
                return PresentationState.GroundRune;
            }

            switch (state)
            {
                case RangedEnemyState.Windup:
                    return PresentationState.Windup;
                case RangedEnemyState.Release:
                    return PresentationState.Release;
                case RangedEnemyState.HitReact:
                    return PresentationState.HitReact;
                case RangedEnemyState.Dead:
                    return PresentationState.Dead;
                default:
                    return PresentationState.Locomotion;
            }
        }

        private static string GetStateName(PresentationState state)
        {
            switch (state)
            {
                case PresentationState.Windup:
                    return CombatState.HeavyCharge.ToString();
                case PresentationState.Release:
                    return CombatState.HeavyAttack.ToString();
                case PresentationState.GroundRune:
                    return "EnemyRuneCast";
                case PresentationState.HitReact:
                    return CombatState.HitReact.ToString();
                case PresentationState.Dead:
                    return CombatState.Dead.ToString();
                default:
                    return CombatState.Locomotion.ToString();
            }
        }

        private enum PresentationState
        {
            Locomotion,
            Windup,
            Release,
            GroundRune,
            HitReact,
            Dead
        }
    }
}
