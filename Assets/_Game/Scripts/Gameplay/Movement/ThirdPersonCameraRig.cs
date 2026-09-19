using Emberfall.Gameplay.Input;
using Emberfall.Gameplay.Targeting;
using UnityEngine;

namespace Emberfall.Gameplay.Movement
{
    [DefaultExecutionOrder(200)]
    public sealed class ThirdPersonCameraRig : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private PlayerInputReader _input;
        [SerializeField] private LockOnTargeting _targeting;
        [SerializeField, Min(0.1f)] private float _distance = 5.8f;
        [SerializeField, Min(0f)] private float _pivotHeight = 1.45f;
        [SerializeField, Min(0.01f)] private float _mouseSensitivity = 0.085f;
        [SerializeField, Min(1f)] private float _gamepadDegreesPerSecond = 165f;
        [SerializeField, Range(-85f, 0f)] private float _minimumPitch = -25f;
        [SerializeField, Range(0f, 85f)] private float _maximumPitch = 58f;
        [SerializeField, Min(0.01f)] private float _collisionRadius = 0.22f;
        [SerializeField] private LayerMask _collisionMask = ~0;

        private float _yaw;
        private float _pitch = 18f;
        private Vector3 _smoothedPosition;
        private CameraOccluder[] _artOccluders;
        private bool _lookInputBlocked;

        public bool IsLookInputBlocked => _lookInputBlocked;

        public void Configure(Transform target, PlayerInputReader input, LockOnTargeting targeting)
        {
            _target = target;
            _input = input;
            _targeting = targeting;
            if (_target != null)
            {
                _yaw = _target.eulerAngles.y;
                _smoothedPosition = transform.position;
            }
        }

        public void SetLookInputBlocked(bool blocked)
        {
            _lookInputBlocked = blocked;
        }

        private void Awake()
        {
            if (_target != null)
            {
                _yaw = _target.eulerAngles.y;
            }

            _smoothedPosition = transform.position;
        }

        private void Start()
        {
            _artOccluders = FindObjectsOfType<CameraOccluder>(true);
        }

        private void LateUpdate()
        {
            if (_target == null || _input == null)
            {
                return;
            }

            if (!_lookInputBlocked)
            {
                Vector2 look = _input.Look;
                if (_targeting != null && _targeting.IsLocked)
                {
                    Vector3 toTarget = _targeting.CurrentTarget.AimPoint.position - _target.position;
                    float desiredYaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(toTarget, Vector3.up)).eulerAngles.y;
                    _yaw = Mathf.LerpAngle(_yaw, desiredYaw, Time.deltaTime * 7f);
                    _pitch = Mathf.Lerp(_pitch, 15f, Time.deltaTime * 4f);
                }
                else if (_input.LookUsesPointer)
                {
                    _yaw += look.x * _mouseSensitivity;
                    _pitch -= look.y * _mouseSensitivity;
                }
                else
                {
                    _yaw += look.x * _gamepadDegreesPerSecond * Time.deltaTime;
                    _pitch -= look.y * _gamepadDegreesPerSecond * Time.deltaTime;
                }
            }

            _pitch = Mathf.Clamp(_pitch, _minimumPitch, _maximumPitch);
            Quaternion rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 pivot = _target.position + (Vector3.up * _pivotHeight);
            Vector3 backwards = -(rotation * Vector3.forward);
            float cameraDistance = _distance;
            if (Physics.SphereCast(
                    pivot,
                    _collisionRadius,
                    backwards,
                    out RaycastHit hit,
                    _distance,
                    _collisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                cameraDistance = Mathf.Max(0.35f, hit.distance - 0.08f);
            }

            Ray cameraRay = new Ray(pivot, backwards);
            if (_artOccluders != null)
            {
                for (int i = 0; i < _artOccluders.Length; i++)
                {
                    CameraOccluder occluder = _artOccluders[i];
                    if (occluder != null &&
                        occluder.TryGetDistance(cameraRay, _distance, _collisionRadius, out float artDistance))
                    {
                        cameraDistance = Mathf.Min(cameraDistance, Mathf.Max(0.35f, artDistance - 0.1f));
                    }
                }
            }

            Vector3 desiredPosition = pivot + (backwards * cameraDistance);
            bool occluded = cameraDistance < _distance - 0.01f;
            _smoothedPosition = occluded
                ? desiredPosition
                : Vector3.Lerp(_smoothedPosition, desiredPosition, 1f - Mathf.Exp(-18f * Time.deltaTime));
            transform.SetPositionAndRotation(_smoothedPosition, rotation);
        }
    }
}
