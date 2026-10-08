using System;
using System.Collections.Generic;
using Emberfall.AI.Domain;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;

namespace Emberfall.AI.Unity
{
    public sealed class RangedGroundRune : MonoBehaviour
    {
        private const int HitBufferSize = 12;
        private readonly Collider[] _hitBuffer = new Collider[HitBufferSize];
        private readonly HashSet<int> _resolvedTargets = new HashSet<int>();
        private DelayedAreaAttackModel _timer;
        private readonly List<Renderer> _segments = new List<Renderer>();
        private GroundRuneBoundaryMesh _boundaryMesh;
        private Renderer _boundaryRenderer;
        private Material _materialInstance;
        private int _attackerId;
        private int _attackSequence;
        private float _radius;
        private float _damage;
        private float _postureDamage;
        private bool _hasAuthority;
        private float _resolvedElapsed;
        private Action<DamageResult> _onResolved;
        private GameObject _impactVfxPrefab;

        public int WarningSegmentCount => _segments.Count;
        public bool ImpactVfxConfigured => _impactVfxPrefab != null;
        public float WarningRadius => _radius;
        public Renderer WarningBoundaryRenderer => _boundaryRenderer;
        public bool IsWarningBoundaryVisible => _boundaryRenderer != null && _boundaryRenderer.enabled &&
            _boundaryRenderer.gameObject.activeInHierarchy;

        public void Configure(
            int attackerId,
            int attackSequence,
            float triggerDelay,
            float radius,
            float damage,
            float postureDamage,
            Material warningMaterial,
            bool hasAuthority,
            Action<DamageResult> onResolved,
            GameObject impactVfxPrefab = null)
        {
            // Production callers create a fresh rune. Reject reuse before allocating any visual resources.
            if (_timer != null) throw new InvalidOperationException("A ground rune can only be configured once.");
            _attackerId = attackerId;
            _attackSequence = attackSequence;
            _timer = new DelayedAreaAttackModel(triggerDelay);
            _radius = radius;
            _damage = damage;
            _postureDamage = postureDamage;
            _hasAuthority = hasAuthority;
            _onResolved = onResolved;
            _impactVfxPrefab = impactVfxPrefab;
            if (_impactVfxPrefab != null)
                CombatBurstVfxPool.ForScene(gameObject.scene).Prewarm(CombatBurstKind.GroundBlast, _impactVfxPrefab);
            _materialInstance = warningMaterial != null ? new Material(warningMaterial) : null;
            CreateSegmentedWarning();
            CreateBoundaryWarning();
            SetWarningVisible(isActiveAndEnabled);
        }

        private void Update()
        {
            if (_timer == null)
            {
                Destroy(gameObject);
                return;
            }

            _timer.Tick(Time.deltaTime);
            UpdateSegmentedWarning(_timer.Normalized);
            if (_materialInstance != null)
            {
                Color warning = Color.Lerp(
                    new Color(0.72f, 0.16f, 0.42f),
                    new Color(1f, 0.22f, 0.035f),
                    _timer.Normalized);
                _materialInstance.color = warning;
            }

            if (_timer.Resolve())
            {
                // Close danger semantics on the existing local fuse; decorative blast lifetime is independent.
                SetWarningVisible(false);
                ResolveDamage();
            }

            if (_timer.IsResolved)
            {
                _resolvedElapsed += Time.deltaTime;
                if (_resolvedElapsed >= 0.22f)
                {
                    Destroy(gameObject);
                }
            }
        }

        private void CreateSegmentedWarning()
        {
            const int segmentCount = 10;
            for (int i = 0; i < segmentCount; i++)
            {
                GameObject segment = GameObject.CreatePrimitive(PrimitiveType.Cube);
                segment.name = $"HostileRuneSegment_{i:00}";
                segment.transform.SetParent(transform, false);
                Destroy(segment.GetComponent<Collider>());
                Renderer renderer = segment.GetComponent<Renderer>();
                renderer.sharedMaterial = _materialInstance;
                _segments.Add(renderer);
            }
            UpdateSegmentedWarning(0f);
        }

        private void UpdateSegmentedWarning(float normalized)
        {
            float distance = _radius * Mathf.Lerp(0.3f, 1f, normalized);
            float rotationOffset = -Time.time * 34f;
            for (int i = 0; i < _segments.Count; i++)
            {
                float degrees = rotationOffset + (360f * i / _segments.Count);
                float radians = degrees * Mathf.Deg2Rad;
                Transform segment = _segments[i].transform;
                segment.localPosition = new Vector3(
                    Mathf.Cos(radians) * distance,
                    0.035f,
                    Mathf.Sin(radians) * distance);
                segment.localRotation = Quaternion.Euler(0f, -degrees, 0f);
                segment.localScale = new Vector3(0.5f, 0.025f, 0.14f);
            }
        }

        private void CreateBoundaryWarning()
        {
            // Invalid geometry must not add a new exception path to the existing authority adapter.
            if (_radius <= 0 || float.IsNaN(_radius) || float.IsInfinity(_radius)) return;
            _boundaryMesh = new GroundRuneBoundaryMesh();
            _boundaryMesh.SetRadius(_radius);
            var boundary = new GameObject("HostileRuneBoundary", typeof(MeshFilter), typeof(MeshRenderer));
            boundary.layer = gameObject.layer;
            boundary.transform.SetParent(transform, false);
            boundary.transform.localPosition = new Vector3(0, .038f, 0);
            // Query radius is in world metres; normal callers use unit scale, but presentation must not inherit expansion.
            var scale = transform.lossyScale;
            boundary.transform.localScale = new Vector3(
                Mathf.Abs(scale.x) > .00001f ? 1f / scale.x : 1f,
                Mathf.Abs(scale.y) > .00001f ? 1f / scale.y : 1f,
                Mathf.Abs(scale.z) > .00001f ? 1f / scale.z : 1f);
            boundary.GetComponent<MeshFilter>().sharedMesh = _boundaryMesh.Mesh;
            _boundaryRenderer = boundary.GetComponent<MeshRenderer>();
            _boundaryRenderer.sharedMaterial = _materialInstance;
            _boundaryRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _boundaryRenderer.receiveShadows = false;
            _boundaryRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _boundaryRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private void SetWarningVisible(bool visible)
        {
            if (_boundaryRenderer != null) _boundaryRenderer.enabled = visible;
            foreach (var segment in _segments) if (segment != null) segment.enabled = visible;
        }

        private void OnEnable()
        {
            if (_timer != null) SetWarningVisible(!_timer.IsResolved);
        }

        private void OnDisable() => SetWarningVisible(false);

        private void ResolveDamage()
        {
            SpawnImpactVfx();
            if (!_hasAuthority)
            {
                _onResolved?.Invoke(DamageResult.Ignored);
                return;
            }

            int count = Physics.OverlapSphereNonAlloc(
                transform.position, _radius, _hitBuffer, ~0, QueryTriggerInteraction.Collide);
            bool resolvedAny = false;
            for (int i = 0; i < count; i++)
            {
                PlayerCombatActor target = _hitBuffer[i].GetComponentInParent<PlayerCombatActor>();
                if (target == null || !target.IsAvailable || !_resolvedTargets.Add(target.CombatantId))
                {
                    continue;
                }

                resolvedAny = true;
                DamageResult result = target.ReceiveDamage(new DamageRequest(
                    _attackerId,
                    (_attackSequence * 10) + 9,
                    _damage,
                    _postureDamage,
                    AttackTag.Hazard,
                    false,
                    false));
                _onResolved?.Invoke(result);
            }

            if (!resolvedAny)
            {
                _onResolved?.Invoke(DamageResult.Ignored);
            }
        }

        private void SpawnImpactVfx()
        {
            if (_impactVfxPrefab == null) return;
            CombatBurstVfxPool.ForScene(gameObject.scene).TrySpawn(CombatBurstKind.GroundBlast,
                _impactVfxPrefab, transform.position, Quaternion.identity, 2.5f);
        }

        private void OnDestroy()
        {
            _boundaryMesh?.Dispose();
            if (_materialInstance != null)
            {
                Destroy(_materialInstance);
            }
        }
    }
}
