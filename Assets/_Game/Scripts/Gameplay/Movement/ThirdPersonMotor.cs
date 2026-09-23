using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Targeting;
using UnityEngine;

namespace Emberfall.Gameplay.Movement
{
    [DefaultExecutionOrder(-100)]
    [RequireComponent(typeof(CharacterController))]
    public sealed class ThirdPersonMotor : MonoBehaviour
    {
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private PlayerCombatActor _combat;
        [SerializeField] private LockOnTargeting _targeting;
        [SerializeField] private Transform _cameraTransform;
        [SerializeField] private CharacterController _controller;
        [SerializeField, Min(0.1f)] private float _moveSpeed = 5.4f;
        [SerializeField, Min(0.1f)] private float _sprintSpeed = 8.2f;
        [SerializeField, Min(0.1f)] private float _acceleration = 36f;
        [SerializeField, Min(0.1f)] private float _deceleration = 48f;
        [SerializeField, Min(1f)] private float _rotationSpeed = 900f;
        [SerializeField, Min(1f)] private float _reversalRotationSpeed = 2160f;
        [SerializeField, Min(1f)] private float _combatRotationSpeed = 1800f;
        [SerializeField] private float _gravity = -24f;

        private Vector3 _horizontalVelocity;
        private float _verticalVelocity;
        private CombatState _previousState;
        private Vector3 _dodgeDirection;
        private float _previousDodgeTravel;
        private bool _isReversalTurning;
        private float _sprintSessionSeconds;

        public float HorizontalSpeed => new Vector2(_horizontalVelocity.x, _horizontalVelocity.z).magnitude;
        public bool IsSprinting => _combat?.Model?.IsSprinting == true;

        public void Configure(
            PlayerInputReader input,
            PlayerCombatActor combat,
            LockOnTargeting targeting,
            Transform cameraTransform,
            CharacterController controller)
        {
            _input = input;
            _combat = combat;
            _targeting = targeting;
            _cameraTransform = cameraTransform;
            _controller = controller;
        }

        private void Awake()
        {
            _controller ??= GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (_input == null || _combat?.Model == null || _cameraTransform == null || _controller == null)
            {
                return;
            }

            CombatState state = _combat.Model.State;
            Vector3 desiredDirection = GetWorldInputDirection(_input.Move);
            if (state == CombatState.Dodge && _previousState != CombatState.Dodge)
            {
                Vector3 requestedDodge = GetWorldInputDirection(_combat.DodgeInput);
                _dodgeDirection = requestedDodge.sqrMagnitude > 0.01f ? requestedDodge : transform.forward;
                _previousDodgeTravel = 0f;
                _isReversalTurning = false;
            }

            Vector3 horizontalDisplacement;
            if (state == CombatState.Dodge)
            {
                float currentTravel = DodgeTravelProfile.Evaluate(_combat.Model.StateNormalized);
                float frameDistance = Mathf.Max(0f, currentTravel - _previousDodgeTravel) * _combat.Model.DodgeDistance;
                horizontalDisplacement = _dodgeDirection * frameDistance;
                _horizontalVelocity = Time.deltaTime > Mathf.Epsilon
                    ? horizontalDisplacement / Time.deltaTime
                    : Vector3.zero;
                _previousDodgeTravel = currentTravel;
            }
            else
            {
                if (_previousState == CombatState.Dodge)
                {
                    _horizontalVelocity = Vector3.zero;
                }

                float movementScale = GetMovementScale(state);
                UpdateReversalTurn(desiredDirection, state);
                FaceDesiredDirection(desiredDirection, state);

                float facingScale = GetFreeMovementFacingScale(desiredDirection, state);
                Vector3 responseDirection = movementScale > 0f ? desiredDirection : Vector3.zero;
                float authoredSpeed = state == CombatState.Locomotion && _combat.Model.IsSprinting
                    ? _sprintSpeed
                    : _moveSpeed;
                PlanarMovementStep movement = PlanarMovementResponse.Step(
                    _horizontalVelocity.x,
                    _horizontalVelocity.z,
                    responseDirection.x,
                    responseDirection.z,
                    authoredSpeed * movementScale * facingScale,
                    _acceleration,
                    _deceleration,
                    Time.deltaTime);
                _horizontalVelocity = new Vector3(movement.X, 0f, movement.Z);
                horizontalDisplacement = _horizontalVelocity * Time.deltaTime;
            }

            if (_controller.isGrounded && _verticalVelocity < 0f)
            {
                _verticalVelocity = -2f;
            }
            else
            {
                _verticalVelocity += _gravity * Time.deltaTime;
            }

            if (state == CombatState.Dodge)
            {
                FaceDesiredDirection(desiredDirection, state);
            }

            Vector3 verticalDisplacement = Vector3.up * (_verticalVelocity * Time.deltaTime);
            _controller.Move(horizontalDisplacement + verticalDisplacement);
            TrackSprintSession(_combat.Model.IsSprinting);
            _previousState = state;
        }

        private void TrackSprintSession(bool sprinting)
        {
            if (sprinting)
            {
                _sprintSessionSeconds += Time.deltaTime;
                return;
            }

            if (_sprintSessionSeconds <= 0f) return;
            Debug.Log($"[M5C_FEEL] event=sprint-session value={_sprintSessionSeconds:0.###} sequence=0");
            _sprintSessionSeconds = 0f;
        }

        private Vector3 GetWorldInputDirection(Vector2 input)
        {
            Vector3 forward = Vector3.ProjectOnPlane(_cameraTransform.forward, Vector3.up).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3 direction = (forward * input.y) + (right * input.x);
            return Vector3.ClampMagnitude(direction, 1f);
        }

        private void UpdateReversalTurn(Vector3 moveDirection, CombatState state)
        {
            if (state != CombatState.Locomotion || moveDirection.sqrMagnitude <= 0.001f)
            {
                _isReversalTurning = false;
                return;
            }

            bool velocityReversed = PlanarMovementResponse.IsReversal(
                _horizontalVelocity.x,
                _horizontalVelocity.z,
                moveDirection.x,
                moveDirection.z);
            float facingAlignment = Vector3.Dot(transform.forward, moveDirection.normalized);
            if (velocityReversed || facingAlignment < -0.05f)
            {
                _isReversalTurning = true;
                _horizontalVelocity = Vector3.zero;
            }

            if (_isReversalTurning && facingAlignment >= 0.985f)
            {
                _isReversalTurning = false;
            }
        }

        private float GetFreeMovementFacingScale(Vector3 moveDirection, CombatState state)
        {
            if (state != CombatState.Locomotion || moveDirection.sqrMagnitude <= 0.001f)
            {
                return 1f;
            }

            return Mathf.Clamp01(Vector3.Dot(transform.forward, moveDirection.normalized));
        }

        private void FaceDesiredDirection(Vector3 moveDirection, CombatState state)
        {
            Vector3 facing = moveDirection;
            bool faceLockedTarget = _targeting != null &&
                                    _targeting.IsLocked &&
                                    LockOnFacingPolicy.ShouldFaceTarget(state);
            if (faceLockedTarget)
            {
                facing = Vector3.ProjectOnPlane(
                    _targeting.CurrentTarget.AimPoint.position - transform.position,
                    Vector3.up).normalized;
            }
            else if (state == CombatState.Dodge)
            {
                facing = _dodgeDirection;
            }

            if (facing.sqrMagnitude <= 0.001f)
            {
                return;
            }

            Quaternion desired = Quaternion.LookRotation(facing, Vector3.up);
            float turnSpeed = faceLockedTarget
                ? _combatRotationSpeed
                : (_isReversalTurning ? _reversalRotationSpeed : _rotationSpeed);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, desired, turnSpeed * Time.deltaTime);
        }

        private static float GetMovementScale(CombatState state)
        {
            switch (state)
            {
                case CombatState.Locomotion:
                    return 1f;
                case CombatState.HeavyCharge:
                case CombatState.Guard:
                    return 0.35f;
                case CombatState.LightAttack1:
                case CombatState.LightAttack2:
                case CombatState.LightAttack3:
                case CombatState.HeavyAttack:
                case CombatState.Sweep:
                case CombatState.RangedAttack:
                    return 0.12f;
                default:
                    return 0f;
            }
        }
    }
}
