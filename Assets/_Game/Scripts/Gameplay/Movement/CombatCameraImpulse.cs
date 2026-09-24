using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;

namespace Emberfall.Gameplay.Movement
{
    /// <summary>Render-only lens jitter; camera transform, aim basis and collision position stay untouched.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Camera)), DefaultExecutionOrder(300)]
    public sealed class CombatCameraImpulse : MonoBehaviour
    {
        private Camera _camera;
        private ThirdPersonCameraRig _rig;
        private double _startedAt;
        private HitFeedbackGrade _grade;
        private bool _applied;
        public bool ImpulsesEnabled { get; set; } = true;

        private void Awake() { _camera = GetComponent<Camera>(); _rig = GetComponent<ThirdPersonCameraRig>(); }

        public void Request(HitFeedbackGrade grade)
        {
            if (!ImpulsesEnabled || !isActiveAndEnabled || (_rig != null && _rig.IsLookInputBlocked)) return;
            if (_grade != HitFeedbackGrade.None && grade <= _grade) return;
            _grade = grade;
            _startedAt = Time.realtimeSinceStartupAsDouble;
        }

        private void LateUpdate()
        {
            if (_applied) { _camera.ResetProjectionMatrix(); _applied = false; }
            if (!ImpulsesEnabled || (_rig != null && _rig.IsLookInputBlocked)) { _grade = HitFeedbackGrade.None; return; }
            if (_grade == HitFeedbackGrade.None) return;
            float elapsed = (float)(Time.realtimeSinceStartupAsDouble - _startedAt);
            float duration = HitFeedbackRules.Duration(_grade) + 0.06f;
            if (elapsed >= duration) { _grade = HitFeedbackGrade.None; return; }
            float amplitude = (0.0015f + (int)_grade * 0.00065f) * (1f - elapsed / duration);
            Matrix4x4 projection = _camera.projectionMatrix;
            projection.m02 += Mathf.Sin(elapsed * 155f + 1f) * amplitude;
            projection.m12 += Mathf.Sin(elapsed * 113f + 2f) * amplitude;
            _camera.projectionMatrix = projection;
            _applied = true;
        }

        private void OnDisable()
        {
            if (_camera != null && _applied) _camera.ResetProjectionMatrix();
            _applied = false;
            _grade = HitFeedbackGrade.None;
        }
    }
}
