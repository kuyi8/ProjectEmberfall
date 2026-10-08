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
        private float _locomotionYaw;
        public float AppliedOfflineLocomotionYaw => _locomotionYaw;
        private PlayerCombatActor _observedHitReactActor;
        private CombatStateMachine _observedHitReactModel;
        private Animator _observedHitReactAnimator;
        private PlayerAnimationSet _observedHitReactSet;
        private ulong _observedHitReactSequence;
        private bool _hitReactObservationEnabled;
        private bool _hasHitReactObservation;

        public CombatState PresentedState => _presentedState;
        public bool IsConfigured =>
            _animator != null && _combat != null && _motor != null &&
            _animationSet != null && _animationSet.OfflineController != null;

        // Request-time predicate, not a cached state window or a deferred freeze queue.
        // Exact references keep another actor/Animator and network-only adapters on the legacy path.
        public bool KeepsOfflineRangedAnimationMoving(PlayerCombatActor actor, Animator animator) =>
            actor != null && animator != null && actor == _combat && animator == _animator &&
            isActiveAndEnabled && actor.isActiveAndEnabled && animator.isActiveAndEnabled &&
            IsConfigured && _animationSet.KeepOfflineRangedAnimationMoving &&
            actor.Model != null && actor.Model.State == CombatState.RangedAttack;

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
            ResetHitReactObservation();
        }

        private void Awake()
        {
            ApplyAnimatorSettings();
            ResetHitReactObservation();
        }

        private void OnEnable() => ResetHitReactObservation();

        private void OnDisable()
        {
            ResetHitReactObservation();
            _locomotionYaw = 0f;
            if (_hasAnimatorAnchor && _animator != null)
                _animator.transform.localRotation = _anchoredLocalRotation;
        }

        private void Update()
        {
            // Drain even invalid/off observations: never replay a historical victim response.
            bool restartHitReact = ObserveHitReactReentry();
            if (!IsConfigured || _combat.Model == null)
            {
                return;
            }

            CombatState state = _combat.Model.State;
            // A hit from an already-flying knife can precede this transition. Cancel only
            // the player's visual freeze, never Motor/domain time or the struck target.
            if (!PlayerFreezePresentationPolicy.AllowOwnerFreeze(state) ||
                KeepsOfflineRangedAnimationMoving(_combat, _animator))
            {
                var speed = _animator.GetComponent<AnimatorSpeedCoordinator>();
                if (speed != null && speed.ActiveGrade != HitFeedbackGrade.None)
                    speed.Cancel(state == CombatState.RangedAttack ? "player-ranged-motion" : "player-evasion-motion");
            }
            _animator.SetFloat(SpeedId, _motor.HorizontalSpeed, 0.08f, Time.deltaTime);
            if (state == _presentedState && !restartHitReact)
            {
                TryBeginLightRecovery(state);
                return;
            }

            _presentedState = state;
            _isPlayingRecovery = false;
            float playbackSpeed = GetPlaybackSpeed(state);
            AnimatorSpeedCoordinator.SetBase(_animator, playbackSpeed, state == CombatState.Dead);
            float entryTime = restartHitReact ? 0f :
                _animationSet.GetOfflineEntryTimeOffset(state, _combat.Model.StateElapsed, playbackSpeed);
            if (entryTime > 0f && _animator.speed == 0f)
            {
                // The opt-in offline F contract has a measured Unity2022.3 edge case:
                // fixed-time entry loses its offset at speed0. Enter once in normalized
                // clip time instead; never cancel hit-stop or seek again on unfreeze.
                // Normalized blend duration belongs to the SOURCE state, not this clip.
                var clip = _animationSet.GetOfflineClip(state);
                float sourceLength = _animator.GetCurrentAnimatorStateInfo(0).length;
                if (clip != null && sourceLength > 0f && !float.IsInfinity(sourceLength))
                {
                    _animator.CrossFade(_animationSet.GetOfflineStateName(state),
                        CrossFadeSeconds / sourceLength, 0, entryTime / clip.length);
                    return;
                }
            }
            _animator.CrossFadeInFixedTime(_animationSet.GetOfflineStateName(state), CrossFadeSeconds, 0, entryTime);
        }

        private bool ObserveHitReactReentry()
        {
            CombatStateMachine model = _combat != null ? _combat.Model : null;
            ulong sequence = _combat != null ? _combat.HitReactPresentationSequence : 0;
            bool enabled = IsConfigured && model != null && isActiveAndEnabled &&
                _combat.isActiveAndEnabled && _animator.isActiveAndEnabled &&
                _animationSet.RestartOfflineHitReactOnAcceptedDamage;
            bool sameBinding = _observedHitReactActor == _combat && _observedHitReactModel == model &&
                _observedHitReactAnimator == _animator && _observedHitReactSet == _animationSet;
            bool changed = _hasHitReactObservation && sameBinding &&
                _hitReactObservationEnabled && enabled && sequence != _observedHitReactSequence;

            // A burst coalesces to its latest fact once; death/other states win, with no queue.
            _observedHitReactActor = _combat;
            _observedHitReactModel = model;
            _observedHitReactAnimator = _animator;
            _observedHitReactSet = _animationSet;
            _observedHitReactSequence = sequence;
            _hitReactObservationEnabled = enabled;
            _hasHitReactObservation = true;
            return changed && model.State == CombatState.HitReact && _presentedState == CombatState.HitReact;
        }

        private void ResetHitReactObservation()
        {
            _hasHitReactObservation = false;
            ObserveHitReactReentry();
        }

        private void LateUpdate()
        {
            if (!_hasAnimatorAnchor || _animator == null)
            {
                return;
            }

            // Root curves are presentation input only. CharacterController remains the position authority.
            _animator.transform.localPosition = _anchoredLocalPosition;
            // Animator Speed decays asymptotically after the motor stops. Do not retain a tiny
            // locomotion-facing bias forever while the actor is already standing still.
            float target = IsConfigured && _combat.Model != null && _motor.HorizontalSpeed > 0.01f
                ? _animationSet.GetOfflineLocomotionYaw(_combat.Model.State, _animator.GetFloat(SpeedId)) : 0f;
            // Both entering AND leaving movement are bounded; 27.3 degrees restore in < the 80ms action blend.
            // Use the untouched anchor every frame, never compound last frame's rotation.
            _locomotionYaw = Mathf.MoveTowards(_locomotionYaw, target, 360f * Time.deltaTime);
            _animator.transform.localRotation = _anchoredLocalRotation * Quaternion.AngleAxis(_locomotionYaw, Vector3.up);
        }

        private void ApplyAnimatorSettings()
        {
            if (_animator == null || _animationSet == null || _animationSet.OfflineController == null)
            {
                return;
            }

            _animator.runtimeAnimatorController = _animationSet.OfflineController;
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
            AnimationClip clip = _animationSet.GetOfflineClip(state);
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
