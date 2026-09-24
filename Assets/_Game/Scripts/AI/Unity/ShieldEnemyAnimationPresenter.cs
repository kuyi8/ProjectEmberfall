using Emberfall.AI.Domain;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.AI.Unity
{
    [DefaultExecutionOrder(100)]
    public sealed class ShieldEnemyAnimationPresenter : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private const float CrossFadeSeconds = 0.08f;

        [SerializeField] private Animator _animator;
        [SerializeField] private ShieldEnemyActor _actor;
        [SerializeField] private PlayerAnimationSet _animationSet;

        private PresentationState _presented = (PresentationState)(-1);
        private Vector3 _anchoredLocalPosition;
        private Quaternion _anchoredLocalRotation;
        private bool _hasAnchor;

        public bool IsConfigured => _animator != null && _actor != null &&
            _animationSet != null && _animationSet.Controller != null;

        public void Configure(Animator animator, ShieldEnemyActor actor, PlayerAnimationSet animationSet)
        {
            _animator = animator;
            _actor = actor;
            _animationSet = animationSet;
            ApplyAnimatorSettings();
        }

        private void Awake() => ApplyAnimatorSettings();

        private void Update()
        {
            if (!IsConfigured || _actor.Brain == null) return;
            PresentationState desired = GetPresentationState(_actor.State);
            float speed = _actor.State == ShieldEnemyState.Chase || _actor.State == ShieldEnemyState.Return
                ? _actor.HorizontalSpeed : 0f;
            _animator.SetFloat(SpeedId, speed, 0.08f, Time.deltaTime);
            if (desired == _presented) return;

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
            if (_animator == null || _animationSet == null || _animationSet.Controller == null) return;
            _animator.runtimeAnimatorController = _animationSet.Controller;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            if (_hasAnchor) return;
            _anchoredLocalPosition = _animator.transform.localPosition;
            _anchoredLocalRotation = _animator.transform.localRotation;
            _hasAnchor = true;
        }

        private float GetPlaybackSpeed(PresentationState state)
        {
            if ((state != PresentationState.Attack && state != PresentationState.ShieldBash &&
                 state != PresentationState.ScorchedBurst) ||
                _actor.Definition == null) return 1f;
            AnimationClip clip = state switch
            {
                PresentationState.ShieldBash => _animationSet.GetEnemyClip(EnemyAnimationAction.ShieldBash),
                PresentationState.ScorchedBurst => _animationSet.GetEnemyClip(EnemyAnimationAction.RuneCast),
                _ => _animationSet.GetClip(CombatState.HeavyAttack)
            };
            float duration = _actor.Brain.CurrentWindupDuration + _actor.Brain.CurrentAttackDuration;
            return clip == null || duration <= 0f ? 1f : Mathf.Clamp(clip.length / duration, 0.25f, 3f);
        }

        private PresentationState GetPresentationState(ShieldEnemyState state)
        {
            switch (state)
            {
                case ShieldEnemyState.Windup:
                case ShieldEnemyState.Attack:
                    return _actor.Brain.CurrentAttack switch
                    {
                        ShieldAttackKind.ShieldBash => PresentationState.ShieldBash,
                        ShieldAttackKind.ScorchedBurst => PresentationState.ScorchedBurst,
                        _ => PresentationState.Attack
                    };
                case ShieldEnemyState.HitReact:
                case ShieldEnemyState.GuardBreak:
                    return PresentationState.HitReact;
                case ShieldEnemyState.Dead:
                    return PresentationState.Dead;
                case ShieldEnemyState.Idle:
                case ShieldEnemyState.Recovery:
                    return PresentationState.Guard;
                case ShieldEnemyState.Chase:
                    return _actor.HorizontalSpeed <= 0.12f
                        ? PresentationState.Guard
                        : PresentationState.Locomotion;
                default:
                    return PresentationState.Locomotion;
            }
        }

        private static string GetStateName(PresentationState state)
        {
            switch (state)
            {
                case PresentationState.Attack: return CombatState.HeavyAttack.ToString();
                case PresentationState.Guard: return "EnemyShieldGuard";
                case PresentationState.ShieldBash: return "EnemyShieldBash";
                case PresentationState.ScorchedBurst: return "EnemyRuneCast";
                case PresentationState.HitReact: return CombatState.HitReact.ToString();
                case PresentationState.Dead: return CombatState.Dead.ToString();
                default: return CombatState.Locomotion.ToString();
            }
        }

        private enum PresentationState { Locomotion, Guard, Attack, ShieldBash, ScorchedBurst, HitReact, Dead }
    }
}
