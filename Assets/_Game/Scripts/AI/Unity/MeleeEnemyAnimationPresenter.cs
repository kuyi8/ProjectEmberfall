using Emberfall.AI.Domain;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.AI.Unity
{
    [DefaultExecutionOrder(100)]
    public sealed class MeleeEnemyAnimationPresenter : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private const float CrossFadeSeconds = 0.08f;

        [SerializeField] private Animator _animator;
        [SerializeField] private MeleeEnemyActor _actor;
        [SerializeField] private PlayerAnimationSet _animationSet;

        private PresentationState _presented = (PresentationState)(-1);
        private Vector3 _anchoredLocalPosition;
        private Quaternion _anchoredLocalRotation;
        private bool _hasAnchor;

        public bool IsConfigured =>
            _animator != null && _actor != null && _animationSet != null && _animationSet.Controller != null;

        public void Configure(Animator animator, MeleeEnemyActor actor, PlayerAnimationSet animationSet)
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
            float speed = _actor.State == MeleeEnemyState.Chase || _actor.State == MeleeEnemyState.Return
                ? _actor.HorizontalSpeed
                : 0f;
            _animator.SetFloat(SpeedId, speed, 0.08f, Time.deltaTime);
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
            if (state == PresentationState.Attack && _actor.Definition != null)
            {
                AnimationClip clip = _animationSet.GetClip(CombatState.LightAttack1);
                float duration = _actor.Definition.WindupDuration + _actor.Definition.AttackDuration;
                return GetPlaybackSpeed(clip, duration);
            }

            if (state == PresentationState.DelayedCombo && _actor.Definition != null)
            {
                AnimationClip clip = _animationSet.GetEnemyClip(EnemyAnimationAction.MeleeCombo);
                float duration = _actor.Brain.CurrentWindupDuration + _actor.Brain.CurrentAttackDuration;
                return GetPlaybackSpeed(clip, duration);
            }

            return 1f;
        }

        private static float GetPlaybackSpeed(AnimationClip clip, float duration)
        {
            if (clip == null || duration <= 0f)
            {
                return 1f;
            }

            // The authored attack is shorter than the authoritative sequence. Keep both timelines
            // aligned instead of letting a non-looping clip finish early and hold its final pose.
            return Mathf.Clamp(clip.length / duration, 0.25f, 3f);
        }

        private PresentationState GetPresentationState(MeleeEnemyState state)
        {
            switch (state)
            {
                case MeleeEnemyState.Windup:
                case MeleeEnemyState.Attack:
                    return _actor.Brain.CurrentAttack == MeleeAttackKind.DelayedCombo
                        ? PresentationState.DelayedCombo
                        : PresentationState.Attack;
                case MeleeEnemyState.HitReact:
                    return PresentationState.HitReact;
                case MeleeEnemyState.Dead:
                    return PresentationState.Dead;
                default:
                    return PresentationState.Locomotion;
            }
        }

        private static string GetStateName(PresentationState state)
        {
            switch (state)
            {
                case PresentationState.Attack:
                    return CombatState.LightAttack1.ToString();
                case PresentationState.DelayedCombo:
                    return "EnemyMeleeCombo";
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
            Attack,
            DelayedCombo,
            HitReact,
            Dead
        }
    }
}
