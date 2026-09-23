using UnityEngine;
using UnityEngine.InputSystem;

namespace Emberfall.Gameplay.Input
{
    [DefaultExecutionOrder(-300)]
    public sealed class PlayerInputReader : MonoBehaviour
    {
        [SerializeField] private InputActionAsset _inputActions;
        [SerializeField] private bool _lockCursorOnEnable = true;

        private InputActionAsset _runtimeActions;
        private InputAction _move;
        private InputAction _look;
        private InputAction _switchTarget;
        private InputAction _light;
        private InputAction _heavy;
        private InputAction _ranged;
        private InputAction _sweep;
        private InputAction _dodge;
        private InputAction _sprint;
        private InputAction _guard;
        private InputAction _heal;
        private InputAction _lockOn;
        private InputAction _pause;
        private InputAction _guide;
        private InputAction _interact;
        private InputAction _inputOverlay;
        private bool _lightPressed;
        private bool _heavyPressed;
        private bool _heavyReleased;
        private bool _rangedPressed;
        private bool _sweepPressed;
        private bool _dodgePressed;
        private bool _guardPressed;
        private bool _guardReleased;
        private bool _healPressed;
        private bool _lockOnPressed;
        private bool _pausePressed;
        private bool _guidePressed;
        private bool _interactPressed;
        private bool _inputOverlayPressed;
        private bool _lookUsesPointer = true;

        public Vector2 Move => _move?.ReadValue<Vector2>() ?? Vector2.zero;
        public Vector2 Look => _look?.ReadValue<Vector2>() ?? Vector2.zero;
        public float SwitchTarget => _switchTarget?.ReadValue<float>() ?? 0f;
        public bool LookUsesPointer
        {
            get
            {
                if (_look?.activeControl != null)
                {
                    _lookUsesPointer = _look.activeControl.device is Pointer;
                }

                return _lookUsesPointer;
            }
        }
        public bool HeavyHeld { get; private set; }
        public bool InteractHeld => _interact?.IsPressed() ?? false;
        public bool SprintHeld => _sprint?.IsPressed() ?? false;
        public bool InteractPressedPending => _interactPressed;
        public bool HasInputOverlayAction => _inputOverlay != null;
        public bool HasGuardAction => _guard != null;
        public bool HasHealAction => _heal != null;
        public bool GuardHeld { get; private set; }

        public PlayerInputSnapshot CaptureSnapshot() => new PlayerInputSnapshot(
            Move,
            Look,
            LookUsesPointer,
            SprintHeld,
            _light?.IsPressed() ?? false,
            _heavy?.IsPressed() ?? false,
            _ranged?.IsPressed() ?? false,
            _sweep?.IsPressed() ?? false,
            _guard?.IsPressed() ?? false,
            _dodge?.IsPressed() ?? false,
            _heal?.IsPressed() ?? false,
            _lockOn?.IsPressed() ?? false,
            _interact?.IsPressed() ?? false,
            _guide?.IsPressed() ?? false,
            _pause?.IsPressed() ?? false);

        public void Configure(InputActionAsset inputActions)
        {
            _inputActions = inputActions;
        }

        private void OnEnable()
        {
            if (_inputActions == null)
            {
                Debug.LogError("PlayerInputReader requires an InputActionAsset.", this);
                enabled = false;
                return;
            }

            _runtimeActions = Instantiate(_inputActions);
            _move = _runtimeActions.FindAction("Player/Move", true);
            _look = _runtimeActions.FindAction("Player/Look", true);
            _switchTarget = _runtimeActions.FindAction("Player/SwitchTarget", true);
            _light = _runtimeActions.FindAction("Player/LightAttack", true);
            _heavy = _runtimeActions.FindAction("Player/HeavyAttack", true);
            _ranged = _runtimeActions.FindAction("Player/RangedAttack", true);
            _sweep = _runtimeActions.FindAction("Player/Sweep", true);
            _dodge = _runtimeActions.FindAction("Player/Dodge", true);
            _sprint = _runtimeActions.FindAction("Player/Sprint", true);
            _guard = _runtimeActions.FindAction("Player/Guard", true);
            _heal = _runtimeActions.FindAction("Player/Heal", true);
            _lockOn = _runtimeActions.FindAction("Player/LockOn", true);
            _pause = _runtimeActions.FindAction("Player/Pause", true);
            _guide = _runtimeActions.FindAction("Player/Guide", true);
            _interact = _runtimeActions.FindAction("Player/Interact", true);
            _inputOverlay = _runtimeActions.FindAction("Player/InputOverlay", false);

            _light.performed += OnLightPerformed;
            _heavy.started += OnHeavyStarted;
            _heavy.canceled += OnHeavyCanceled;
            _ranged.performed += OnRangedPerformed;
            _sweep.performed += OnSweepPerformed;
            _dodge.performed += OnDodgePerformed;
            _guard.started += OnGuardStarted;
            _guard.canceled += OnGuardCanceled;
            _heal.performed += OnHealPerformed;
            _lockOn.performed += OnLockOnPerformed;
            _pause.performed += OnPausePerformed;
            _guide.performed += OnGuidePerformed;
            _interact.performed += OnInteractPerformed;
            if (_inputOverlay != null)
            {
                _inputOverlay.performed += OnInputOverlayPerformed;
            }
            _runtimeActions.Enable();

            if (_lockCursorOnEnable)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        private void OnDisable()
        {
            if (_runtimeActions != null)
            {
                _runtimeActions.Disable();
                Destroy(_runtimeActions);
            }

            _runtimeActions = null;
            _move = null;
            _look = null;
            _switchTarget = null;
            _light = null;
            _heavy = null;
            _ranged = null;
            _sweep = null;
            _dodge = null;
            _sprint = null;
            _guard = null;
            _heal = null;
            _lockOn = null;
            _pause = null;
            _guide = null;
            _interact = null;
            _inputOverlay = null;
            HeavyHeld = false;
            GuardHeld = false;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        public bool ConsumeLightPressed() => Consume(ref _lightPressed);
        public bool ConsumeHeavyPressed() => Consume(ref _heavyPressed);
        public bool ConsumeHeavyReleased() => Consume(ref _heavyReleased);
        public bool ConsumeRangedPressed() => Consume(ref _rangedPressed);
        public bool ConsumeSweepPressed() => Consume(ref _sweepPressed);
        public bool ConsumeDodgePressed() => Consume(ref _dodgePressed);
        public bool ConsumeGuardPressed() => Consume(ref _guardPressed);
        public bool ConsumeGuardReleased() => Consume(ref _guardReleased);
        public bool ConsumeHealPressed() => Consume(ref _healPressed);
        public bool ConsumeLockOnPressed() => Consume(ref _lockOnPressed);
        public bool ConsumePausePressed() => Consume(ref _pausePressed);
        public bool ConsumeGuidePressed() => Consume(ref _guidePressed);
        public bool ConsumeInteractPressed() => Consume(ref _interactPressed);
        public bool ConsumeInputOverlayPressed() => Consume(ref _inputOverlayPressed);

        private void OnLightPerformed(InputAction.CallbackContext _) => _lightPressed = true;

        private void OnHeavyStarted(InputAction.CallbackContext _)
        {
            HeavyHeld = true;
            _heavyPressed = true;
        }

        private void OnHeavyCanceled(InputAction.CallbackContext _)
        {
            HeavyHeld = false;
            _heavyReleased = true;
        }

        private void OnDodgePerformed(InputAction.CallbackContext _) => _dodgePressed = true;
        private void OnRangedPerformed(InputAction.CallbackContext _) => _rangedPressed = true;
        private void OnSweepPerformed(InputAction.CallbackContext _) => _sweepPressed = true;
        private void OnGuardStarted(InputAction.CallbackContext _)
        {
            GuardHeld = true;
            _guardPressed = true;
        }

        private void OnGuardCanceled(InputAction.CallbackContext _)
        {
            GuardHeld = false;
            _guardReleased = true;
        }
        private void OnLockOnPerformed(InputAction.CallbackContext _) => _lockOnPressed = true;
        private void OnHealPerformed(InputAction.CallbackContext _) => _healPressed = true;
        private void OnPausePerformed(InputAction.CallbackContext _) => _pausePressed = true;
        private void OnGuidePerformed(InputAction.CallbackContext _) => _guidePressed = true;
        private void OnInteractPerformed(InputAction.CallbackContext _) => _interactPressed = true;
        private void OnInputOverlayPerformed(InputAction.CallbackContext _) => _inputOverlayPressed = true;

        private static bool Consume(ref bool value)
        {
            bool result = value;
            value = false;
            return result;
        }
    }
}
