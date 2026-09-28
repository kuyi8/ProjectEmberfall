using System;
using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;
using UnityEngine.Rendering;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>Offline render leases only. Projectile movement, collision and lifetime remain authoritative.</summary>
    [DisallowMultipleComponent, DefaultExecutionOrder(20000)]
    public sealed class PlayerKnifePresentation : MonoBehaviour
    {
        public const float BridgeDuration = .06f;
        public const float GripToCenter = .13f;
        private sealed class Slot
        {
            public PlayerThrowingKnifeProjectile Projectile;
            public Transform Visual;
            public TrailRenderer Trail, OriginalTrail;
            public Renderer[] Original;
            public bool[] Hidden;
            public bool Leased, OriginalEmitting;
            public Vector3 Offset;
            public Quaternion Rotation;
            public double Birth;
        }

        private PlayerCombatActor _actor;
        private PlayerThrowingKnifeLauncher _launcher;
        private Transform _grip, _held;
        private Renderer[] _sword;
        private bool[] _swordHidden;
        private Renderer _swordTrail;
        private bool _swordTrailHidden, _swordLeased, _pendingRelease, _released;
        private int _blockedSequence = -1;
        private Slot[] _slots;
        private KnifeGripPose _pose;
        private Transform[] _handJoints;
        private Quaternion[] _animatedRotations;
        private bool _poseApplied;
        private float _releaseTime;

        public void ConfigureGrip(KnifeGripPose pose)
        {
            RestoreGrip();
            _pose = pose;
            if (pose == null) return;
            var animator = _actor.GetComponentInChildren<Animator>();
            _handJoints = new Transform[pose.joints.Length];
            _animatedRotations = new Quaternion[pose.joints.Length];
            for (int i = 0; i < pose.joints.Length; i++)
                _handJoints[i] = animator.GetBoneTransform(pose.joints[i].bone);
        }

        // Remove last frame's override before Animator evaluates; never feed it back as base pose.
        private void Update() => RestoreGrip();
        private void RestoreGrip()
        {
            if (!_poseApplied) return;
            for (int i = 0; i < _handJoints.Length; i++)
                if (_handJoints[i] != null) _handJoints[i].localRotation = _animatedRotations[i];
            _poseApplied = false;
        }

        private void ApplyGrip(bool throwing)
        {
            RestoreGrip();
            if (!throwing || _pose == null) return;
            float weight = _released ? 1f - Mathf.Clamp01((Time.time - _releaseTime) / .08f)
                : Mathf.Clamp01(_actor.Model.StateElapsed / .06f);
            for (int i = 0; i < _handJoints.Length; i++)
            {
                _animatedRotations[i] = _handJoints[i].localRotation;
                _handJoints[i].localRotation = Quaternion.Slerp(_animatedRotations[i], _pose.joints[i].rotation, weight);
            }
            _poseApplied = true;
        }

        public bool IsConfigured => _slots != null;
        public bool HeldVisible => _held != null && _held.gameObject.activeSelf;
        public bool FlightVisible => ActiveVisualCount > 0;
        public int ActiveVisualCount { get; private set; }
        public int VisualCapacity => _slots == null ? 0 : _slots.Length;
        public float BridgeAge { get; private set; } = -1;
        public Vector3 VisualPosition { get; private set; }
        public Vector3 GripPosition => _pose != null ? _handJoints[0].TransformPoint(_pose.gripLocalPosition)
            : _grip != null ? _grip.position : Vector3.zero;
        public bool SwordVisible
        {
            get { if (_sword != null) foreach (var r in _sword) if (r != null && r.enabled && !r.forceRenderingOff) return true; return false; }
        }
        public bool SwordTrailVisible => _swordTrail != null && _swordTrail.enabled && !_swordTrail.forceRenderingOff;
        public int TrailPointCount
        {
            get { int count = 0; if (_slots != null) foreach (var s in _slots) count += s.Trail.positionCount; return count; }
        }

        public void Initialize(PlayerCombatActor actor, PlayerThrowingKnifeLauncher launcher,
            Transform grip, PlayerThrowingKnifeProjectile[] pool)
        {
            if (IsConfigured) throw new InvalidOperationException("Knife presentation is initialized once.");
            _actor = actor; _launcher = launcher; _grip = grip;
            _sword = grip.GetComponentsInChildren<Renderer>(true);
            _swordHidden = new bool[_sword.Length];
            var trailTransform = actor.transform.Find("SwordTrail_Runtime");
            _swordTrail = trailTransform != null ? trailTransform.GetComponent<Renderer>() : null;
            _slots = new Slot[pool.Length];
            for (int i = 0; i < pool.Length; i++)
            {
                var projectile = pool[i];
                var source = projectile.transform.Find("Model");
                var visual = CopyModel(source, "KnifeFlightVisual_" + i);
                var originalTrail = projectile.GetComponent<TrailRenderer>();
                var trail = visual.gameObject.AddComponent<TrailRenderer>();
                trail.sharedMaterial = originalTrail.sharedMaterial;
                trail.time = originalTrail.time;
                trail.widthCurve = originalTrail.widthCurve;
                trail.widthMultiplier = originalTrail.widthMultiplier;
                trail.colorGradient = originalTrail.colorGradient;
                trail.minVertexDistance = originalTrail.minVertexDistance;
                trail.alignment = originalTrail.alignment;
                trail.textureMode = originalTrail.textureMode;
                trail.shadowCastingMode = ShadowCastingMode.Off;
                trail.receiveShadows = false;
                trail.emitting = false;
                trail.autodestruct = false;
                var renderers = projectile.GetComponentsInChildren<Renderer>(true);
                _slots[i] = new Slot { Projectile = projectile, Visual = visual, Trail = trail,
                    OriginalTrail = originalTrail, Original = renderers, Hidden = new bool[renderers.Length] };
                if (i == 0) _held = CopyModel(source, "KnifeHeldVisual");
            }
            if (isActiveAndEnabled) _actor.RangedAttackReleased += OnRelease;
        }

        private Transform CopyModel(Transform source, string label)
        {
            // Actor root has unit authored scale; never parent metre-authored gear to FBX hand bones.
            var root = new GameObject(label).transform;
            root.SetParent(transform, false);
            var model = Instantiate(source.gameObject, root).transform;
            model.localPosition = source.localPosition;
            model.localRotation = source.localRotation;
            model.localScale = source.localScale;
            if (root.GetComponentInChildren<Collider>(true) != null || root.GetComponentInChildren<MonoBehaviour>(true) != null)
                throw new InvalidOperationException("Knife Model must be render-only.");
            root.gameObject.SetActive(false);
            return root;
        }

        private void OnEnable()
        {
            if (_actor == null) return;
            _blockedSequence = _actor.Model.AttackSequence;
            _actor.RangedAttackReleased += OnRelease;
        }

        private void OnRelease(RangedAttackRelease release)
        {
            // Consume the authoritative fact now, sample the animated hand later in LateUpdate.
            if (!_actor.isActiveAndEnabled || !_launcher.isActiveAndEnabled ||
                _actor.Model.AttackSequence == _blockedSequence) return;
            _pendingRelease = true;
            _released = true;
            _releaseTime = Time.time;
        }

        private void LateUpdate()
        {
            if (!IsConfigured) return;
            if (!_actor.isActiveAndEnabled || !_launcher.isActiveAndEnabled)
            {
                _blockedSequence = _actor.Model.AttackSequence;
                ReleaseAll();
                return;
            }
            bool throwing = _actor.Model.State == CombatState.RangedAttack &&
                _actor.Model.AttackSequence != _blockedSequence;
            SetSwordHidden(throwing);
            ApplyGrip(throwing);
            Vector3 position = _grip.position + _grip.up * GripToCenter;
            Quaternion rotation = Quaternion.LookRotation(_grip.up, _grip.forward);
            if (_pose != null)
            {
                Transform hand = _handJoints[0];
                rotation = hand.rotation * _pose.gripLocalRotation;
                position = hand.TransformPoint(_pose.gripLocalPosition) + rotation * Vector3.forward * GripToCenter;
            }
            _held.SetPositionAndRotation(position, rotation);
            _held.gameObject.SetActive(throwing && !_released);
            if (!throwing) _released = false;
            ActiveVisualCount = 0;
            BridgeAge = -1;
            VisualPosition = HeldVisible ? position : Vector3.zero;
            foreach (var s in _slots)
            {
                bool alive = s.Projectile != null && s.Projectile.isActiveAndEnabled && s.Projectile.IsInFlight;
                if (_pendingRelease && alive && !s.Leased)
                {
                    for (int i = 0; i < s.Original.Length; i++)
                    { s.Hidden[i] = s.Original[i].forceRenderingOff; s.Original[i].forceRenderingOff = true; }
                    s.OriginalEmitting = s.OriginalTrail.emitting;
                    s.OriginalTrail.emitting = false;
                    s.OriginalTrail.Clear();
                    s.Offset = position - s.Projectile.transform.position;
                    s.Rotation = rotation;
                    s.Birth = Time.timeAsDouble;
                    s.Leased = true;
                    s.Visual.SetPositionAndRotation(position, rotation);
                    s.Visual.gameObject.SetActive(true);
                    s.Trail.Clear();
                    s.Trail.emitting = true;
                }
                if (!s.Leased) continue;
                if (!alive) { Release(s); continue; }
                float age = (float)(Time.timeAsDouble - s.Birth);
                float t = Mathf.SmoothStep(0, 1, Mathf.Clamp01(age / BridgeDuration));
                // Only the separate render root is written. Never move a hitbox or projectile.
                s.Visual.SetPositionAndRotation(s.Projectile.transform.position + s.Offset * (1-t),
                    Quaternion.Slerp(s.Rotation, s.Projectile.transform.rotation, t));
                ActiveVisualCount++;
                BridgeAge = age;
                VisualPosition = s.Visual.position;
            }
            _pendingRelease = false;
        }

        private void SetSwordHidden(bool hidden)
        {
            if (hidden && !_swordLeased)
            {
                for (int i = 0; i < _sword.Length; i++) _swordHidden[i] = _sword[i].forceRenderingOff;
                if (_swordTrail != null) _swordTrailHidden = _swordTrail.forceRenderingOff;
                _swordLeased = true;
            }
            if (!_swordLeased) return;
            for (int i = 0; i < _sword.Length; i++) if (_sword[i] != null) _sword[i].forceRenderingOff = hidden || _swordHidden[i];
            if (_swordTrail != null) _swordTrail.forceRenderingOff = hidden || _swordTrailHidden;
            if (!hidden) _swordLeased = false;
        }

        private static void Release(Slot s)
        {
            if (s.Trail != null) { s.Trail.emitting = false; s.Trail.Clear(); }
            if (s.Visual != null) s.Visual.gameObject.SetActive(false);
            if (!s.Leased) return;
            for (int i = 0; i < s.Original.Length; i++) if (s.Original[i] != null) s.Original[i].forceRenderingOff = s.Hidden[i];
            if (s.OriginalTrail != null) { s.OriginalTrail.Clear(); s.OriginalTrail.emitting = s.OriginalEmitting; }
            s.Leased = false;
        }

        private void ReleaseAll()
        {
            RestoreGrip();
            SetSwordHidden(false);
            if (_held != null) _held.gameObject.SetActive(false);
            if (_slots != null) foreach (var s in _slots) if (s != null) Release(s);
            _pendingRelease = _released = false;
            ActiveVisualCount = 0;
            BridgeAge = -1;
            VisualPosition = Vector3.zero;
        }

        private void OnDisable()
        {
            if (_actor != null) _actor.RangedAttackReleased -= OnRelease;
            ReleaseAll();
        }

        private void OnDestroy()
        {
            ReleaseAll();
            if (_held != null) Destroy(_held.gameObject);
            if (_slots != null) foreach (var s in _slots) if (s != null && s.Visual != null) Destroy(s.Visual.gameObject);
        }
    }
}
