using System;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;

namespace Emberfall.AI.Unity
{
    public sealed class RangedProjectile : MonoBehaviour
    {
        private const int HitBufferSize = 12;

        private readonly RaycastHit[] _hitBuffer = new RaycastHit[HitBufferSize];
        private Transform _sourceRoot;
        private int _sourceId;
        private int _attackSequence;
        private Vector3 _velocity;
        private float _radius;
        private float _remainingLifetime;
        private float _damage;
        private float _postureDamage;
        private Action<DamageResult> _onResolved;

        public bool HasSimulationAuthority { get; private set; } = true;

        public void Launch(
            Transform sourceRoot,
            int sourceId,
            int attackSequence,
            Vector3 direction,
            float speed,
            float radius,
            float lifetime,
            float damage,
            float postureDamage,
            bool hasSimulationAuthority,
            Action<DamageResult> onResolved)
        {
            _sourceRoot = sourceRoot;
            _sourceId = sourceId;
            _attackSequence = attackSequence;
            _velocity = direction.normalized * speed;
            _radius = radius;
            _remainingLifetime = lifetime;
            _damage = damage;
            _postureDamage = postureDamage;
            HasSimulationAuthority = hasSimulationAuthority;
            _onResolved = onResolved;
        }

        private void Update()
        {
            if (!HasSimulationAuthority)
            {
                return;
            }

            float distance = _velocity.magnitude * Time.deltaTime;
            if (distance > 0f && TryFindBlockingHit(distance, out RaycastHit hit))
            {
                PlayerCombatActor player = hit.collider.GetComponentInParent<PlayerCombatActor>();
                DamageResult result = player != null && player.IsAvailable
                    ? player.ReceiveDamage(new DamageRequest(
                        _sourceId,
                        _attackSequence,
                        _damage,
                        _postureDamage,
                        AttackTag.Projectile,
                        true,
                        DefenseArcUtility.IsThreatInFrontArc(player.transform, transform.position)))
                    : DamageResult.Ignored;
                _onResolved?.Invoke(result);
                Destroy(gameObject);
                return;
            }

            transform.position += _velocity * Time.deltaTime;
            _remainingLifetime -= Time.deltaTime;
            if (_remainingLifetime <= 0f)
            {
                _onResolved?.Invoke(DamageResult.Ignored);
                Destroy(gameObject);
            }
        }

        private bool TryFindBlockingHit(float distance, out RaycastHit nearest)
        {
            int count = Physics.SphereCastNonAlloc(
                transform.position,
                _radius,
                _velocity.normalized,
                _hitBuffer,
                distance,
                ~0,
                QueryTriggerInteraction.Ignore);

            nearest = default;
            float nearestDistance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                Collider candidate = _hitBuffer[i].collider;
                if (candidate == null || candidate.transform == transform ||
                    (_sourceRoot != null && candidate.transform.IsChildOf(_sourceRoot)))
                {
                    continue;
                }

                if (_hitBuffer[i].distance < nearestDistance)
                {
                    nearestDistance = _hitBuffer[i].distance;
                    nearest = _hitBuffer[i];
                }
            }

            return nearest.collider != null;
        }
    }
}
