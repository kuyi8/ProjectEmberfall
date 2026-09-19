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
            _attackerId = attackerId;
            _attackSequence = attackSequence;
            _timer = new DelayedAreaAttackModel(triggerDelay);
            _radius = radius;
            _damage = damage;
            _postureDamage = postureDamage;
            _hasAuthority = hasAuthority;
            _onResolved = onResolved;
            _impactVfxPrefab = impactVfxPrefab;
            _materialInstance = warningMaterial != null ? new Material(warningMaterial) : null;
            CreateSegmentedWarning();
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
            GameObject instance = Instantiate(_impactVfxPrefab, transform.position, Quaternion.identity);
            instance.name = $"WardenDelayedBlastImpactVfx_{_attackSequence:000}";
            Destroy(instance, 2.5f);
        }

        private void OnDestroy()
        {
            if (_materialInstance != null)
            {
                Destroy(_materialInstance);
            }
        }
    }
}
