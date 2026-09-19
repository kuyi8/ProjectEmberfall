using System;
using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>
    /// Unity collision adapter for a committed ranged attack. It receives an immutable
    /// release snapshot and never decides cooldown, input legality, or attack timing.
    /// </summary>
    public sealed class PlayerThrowingKnifeProjectile : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float _spinDegreesPerSecond = 900f;
        [SerializeField] private TrailRenderer _trail;
        [SerializeField] private BoxCollider _hitbox;

        private PlayerCombatActor _owner;
        private RangedAttackRelease _release;
        private Vector3 _direction;
        private float _travelled;
        private int _collisionMask;
        private Action<PlayerThrowingKnifeProjectile> _returnToPool;
        private bool _active;

        public bool IsInFlight => _active;

        public void Configure(TrailRenderer trail, BoxCollider hitbox)
        {
            _trail = trail;
            _hitbox = hitbox;
            if (_hitbox != null) _hitbox.enabled = false;
        }

        public void Launch(
            PlayerCombatActor owner,
            Vector3 position,
            Vector3 direction,
            RangedAttackRelease release,
            Action<PlayerThrowingKnifeProjectile> returnToPool)
        {
            _owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
            _release = release;
            _direction = direction.sqrMagnitude > 0.001f ? direction.normalized : owner.transform.forward;
            _travelled = 0f;
            _collisionMask = ~(1 << owner.gameObject.layer);
            _returnToPool = returnToPool;
            _active = true;
            transform.SetPositionAndRotation(position, Quaternion.LookRotation(_direction, Vector3.up));
            if (_hitbox != null) _hitbox.enabled = false;
            if (_trail != null) _trail.Clear();
        }

        private void Update()
        {
            if (!_active) return;

            float step = Mathf.Min(
                _release.ProjectileSpeed * Time.deltaTime,
                Mathf.Max(0f, _release.MaximumDistance - _travelled));
            if (step <= 0f)
            {
                Finish();
                return;
            }

            Vector3 origin = transform.position;
            Quaternion orientation = transform.rotation;
            Vector3 scale = transform.lossyScale;
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 centerOffset = _hitbox != null
                ? Vector3.Scale(_hitbox.center, scale)
                : Vector3.zero;
            Vector3 halfExtents = _hitbox != null
                ? Vector3.Scale(_hitbox.size * 0.5f, scale)
                : new Vector3(0.04f, 0.025f, 0.18f);
            Vector3 castCenter = origin + (orientation * centerOffset);
            if (Physics.BoxCast(
                    castCenter,
                    halfExtents,
                    _direction,
                    out RaycastHit hit,
                    orientation,
                    step,
                    _collisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                transform.position = origin + (_direction * hit.distance);
                CombatTarget target = hit.collider.GetComponentInParent<CombatTarget>();
                if (target != null && target != _owner && target.IsAvailable)
                {
                    DamageResult result = target.ReceiveDamage(new DamageRequest(
                        _owner.CombatantId,
                        _release.AttackSequence,
                        _release.Damage,
                        _release.PostureDamage,
                        AttackTag.Projectile,
                        true,
                        DefenseArcUtility.IsThreatInFrontArc(target.transform, origin)));
                    _owner.PresentRangedImpact(target, result, hit.point);
                }
                else
                {
                    _owner.PresentRangedImpact(null, DamageResult.Ignored, hit.point);
                }

                Finish();
                return;
            }

            transform.position = origin + (_direction * step);
            transform.Rotate(Vector3.forward, _spinDegreesPerSecond * Time.deltaTime, Space.Self);
            _travelled += step;
            if (_travelled >= _release.MaximumDistance - 0.001f) Finish();
        }

        private void Finish()
        {
            if (!_active) return;
            _active = false;
            _returnToPool?.Invoke(this);
        }
    }
}
