using System;
using System.Globalization;
using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;

namespace Emberfall.Gameplay.Animation
{
    /// <summary>The only runtime writer of Animator.speed. Domain clocks are never consulted or changed.</summary>
    [DisallowMultipleComponent, RequireComponent(typeof(Animator)), DefaultExecutionOrder(10000)]
    public sealed class AnimatorSpeedCoordinator : MonoBehaviour
    {
        private Animator _animator;
        private float _baseSpeed = 1f;
        private double _startedAt;
        private double _endsAt;
        private bool _terminal;
        private ulong _sequence;
        public HitFeedbackGrade ActiveGrade { get; private set; }
        public float BaseSpeed => _baseSpeed;
        public double LastEffectiveMilliseconds { get; private set; }
        public event Action<HitFeedbackGrade, double> FreezeEnded;

        public static AnimatorSpeedCoordinator For(Animator animator)
        {
            if (animator == null) return null;
            var existing = animator.GetComponent<AnimatorSpeedCoordinator>();
            return existing != null ? existing : animator.gameObject.AddComponent<AnimatorSpeedCoordinator>();
        }

        public static void SetBase(Animator animator, float speed, bool terminal = false)
        {
            var coordinator = For(animator);
            if (coordinator == null) return;
            coordinator._baseSpeed = speed;
            coordinator._terminal = terminal;
            if (terminal) coordinator.Cancel("death");
            coordinator.Apply();
        }

        private void Awake()
        {
            _animator = GetComponent<Animator>();
            _baseSpeed = _animator.speed;
        }

        public bool Request(HitFeedbackGrade grade, ulong sequence = 0)
        {
            if (!isActiveAndEnabled || _terminal || HitFeedbackRules.Duration(grade) <= 0f) return false;
            // Same/lower grades never refresh an active freeze, including its last frame.
            if (ActiveGrade != HitFeedbackGrade.None && grade <= ActiveGrade) return false;
            if (ActiveGrade != HitFeedbackGrade.None) Cancel("upgraded");
            ActiveGrade = grade;
            _sequence = sequence;
            _startedAt = Time.realtimeSinceStartupAsDouble;
            _endsAt = _startedAt + HitFeedbackRules.Duration(grade);
            Apply();
            return true;
        }

        private void Update()
        {
            if (ActiveGrade != HitFeedbackGrade.None && Time.realtimeSinceStartupAsDouble >= _endsAt)
                Cancel("elapsed");
        }

        public void Cancel(string reason = "cancelled")
        {
            if (ActiveGrade == HitFeedbackGrade.None) { Apply(); return; }
            HitFeedbackGrade grade = ActiveGrade;
            LastEffectiveMilliseconds = (Time.realtimeSinceStartupAsDouble - _startedAt) * 1000d;
            ActiveGrade = HitFeedbackGrade.None;
            Apply();
            Debug.Log(string.Format(CultureInfo.InvariantCulture,
                "[M5C_FEEL] event=freeze-ended grade={0} sequence={1} requestedMs={2:0.###} effectiveMs={3:0.###} reason={4} animator={5}",
                grade, _sequence, HitFeedbackRules.Duration(grade) * 1000f, LastEffectiveMilliseconds, reason, GetInstanceID()));
            FreezeEnded?.Invoke(grade, LastEffectiveMilliseconds);
        }

        private void Apply()
        {
            if (_animator != null) _animator.speed = ActiveGrade == HitFeedbackGrade.None ? _baseSpeed : 0f;
        }

        private void OnDisable() => Cancel("disabled-or-scene-unload");
    }
}
