using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using UnityEngine;

namespace Emberfall.Gameplay.Animation
{
    [DefaultExecutionOrder(100)]
    public sealed class PlayerAnimationPresenter : MonoBehaviour
    {
        private static readonly int SpeedId = Animator.StringToHash("Speed");
        private const float CrossFadeSeconds = 0.08f;

        [SerializeField] private Animator _animator;
        [SerializeField] private PlayerCombatActor _combat;
        [SerializeField] private ThirdPersonMotor _motor;
        [SerializeField] private PlayerAnimationSet _animationSet;

        private CombatState _presentedState = (CombatState)(-1);
        private bool _isPlayingRecovery;
        private Vector3 _anchoredLocalPosition;
        private Quaternion _anchoredLocalRotation;
        private bool _hasAnimatorAnchor;

        public CombatState PresentedState => _presentedState;
        public bool IsConfigured =>
            _animator != null && _combat != null && _motor != null &&
            _animationSet != null && _animationSet.Controller != null;

        public void Configure(
            Animator animator,
            PlayerCombatActor combat,
            ThirdPersonMotor motor,
            PlayerAnimationSet animationSet)
        {
            _animator = animator;
            _combat = combat;
            _motor = motor;
            _animationSet = animationSet;
            ApplyAnimatorSettings();
        }

        private void Awake()
        {
            ApplyAnimatorSettings();
        }

        private void Update()
        {
            if (!IsConfigured || _combat.Model == null)
            {
                return;
            }

            CombatState state = _combat.Model.State;
            _animator.SetFloat(SpeedId, _motor.HorizontalSpeed, 0.08f, Time.deltaTime);
            if (state == _presentedState)
            {
                TryBeginLightRecovery(state);
                return;
            }

            _presentedState = state;
            _isPlayingRecovery = false;
            AnimatorSpeedCoordinator.SetBase(_animator, GetPlaybackSpeed(state), state == CombatState.Dead);
            _animator.CrossFadeInFixedTime(state.ToString(), CrossFadeSeconds, 0, 0f);
        }

        private void LateUpdate()
        {
            if (!_hasAnimatorAnchor || _animator == null)
            {
                return;
            }

            // Root curves are presentation input only. CharacterController remains the position authority.
            _animator.transform.localPosition = _anchoredLocalPosition;
            _animator.transform.localRotation = _anchoredLocalRotation;
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
            if (!_hasAnimatorAnchor)
            {
                _anchoredLocalPosition = _animator.transform.localPosition;
                _anchoredLocalRotation = _animator.transform.localRotation;
                _hasAnimatorAnchor = true;
            }
        }

        private float GetPlaybackSpeed(CombatState state)
        {
            AnimationClip clip = _animationSet.GetClip(state);
            float stateDuration = state == CombatState.LightAttack1 || state == CombatState.LightAttack2
                ? _combat.Model.LightRecoveryStart
                : _combat.Model.StateDuration;
            if (clip == null || stateDuration <= 0f)
            {
                return 1f;
            }

            return Mathf.Clamp(clip.length / stateDuration, 0.35f, 3f);
        }

        private void TryBeginLightRecovery(CombatState state)
        {
            if (_isPlayingRecovery ||
                (state != CombatState.LightAttack1 && state != CombatState.LightAttack2) ||
                _combat.Model.StateElapsed < _combat.Model.LightRecoveryStart)
            {
                return;
            }

            AnimationClip recovery = _animationSet.GetRecoveryClip(state);
            if (recovery == null)
            {
                return;
            }

            float recoveryDuration = Mathf.Max(
                0.05f,
                _combat.Model.StateDuration - _combat.Model.LightRecoveryStart);
            _isPlayingRecovery = true;
            AnimatorSpeedCoordinator.SetBase(_animator, Mathf.Clamp(recovery.length / recoveryDuration, 0.35f, 4f));
            _animator.CrossFadeInFixedTime($"{state}Recovery", 0.04f, 0, 0f);
        }
    }
}
