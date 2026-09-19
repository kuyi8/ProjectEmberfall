using Emberfall.Gameplay.Combat.Domain;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    public sealed class TrainingDummy : CombatTarget
    {
        [SerializeField] private Transform _aimPoint;
        [SerializeField] private Renderer _renderer;
        [SerializeField] private Collider _collider;
        [SerializeField, Min(1f)] private float _maximumHealth = 140f;
        [SerializeField, Min(0f)] private float _armor = 3f;
        [SerializeField, Min(0.1f)] private float _respawnDelay = 2.5f;

        private HealthModel _health;
        private Color _baseColor;
        private float _flashRemaining;
        private float _deadElapsed;

        public override int CombatantId => GetInstanceID();
        public override Transform AimPoint => _aimPoint != null ? _aimPoint : transform;
        public override bool IsAvailable => _health != null && !_health.IsDead;
        public override float HealthNormalized => _health?.Normalized ?? 0f;

        public void Configure(Transform aimPoint, Renderer targetRenderer, Collider targetCollider)
        {
            _aimPoint = aimPoint;
            _renderer = targetRenderer;
            _collider = targetCollider;
        }

        private void Awake()
        {
            _health = new HealthModel(_maximumHealth);
            if (_renderer != null)
            {
                _baseColor = _renderer.material.color;
            }
        }

        private void Update()
        {
            if (_health.IsDead)
            {
                _deadElapsed += Time.deltaTime;
                if (_deadElapsed >= _respawnDelay)
                {
                    _health.RestoreFull();
                    _deadElapsed = 0f;
                    if (_collider != null)
                    {
                        _collider.enabled = true;
                    }
                }
            }

            _flashRemaining = Mathf.Max(0f, _flashRemaining - Time.deltaTime);
            if (_renderer != null)
            {
                Color target = _health.IsDead ? new Color(0.12f, 0.12f, 0.12f) :
                    _flashRemaining > 0f ? Color.white : _baseColor;
                _renderer.material.color = Color.Lerp(_renderer.material.color, target, Time.deltaTime * 18f);
                _renderer.transform.localScale = Vector3.Lerp(
                    _renderer.transform.localScale,
                    _health.IsDead ? new Vector3(1f, 0.18f, 1f) : Vector3.one,
                    Time.deltaTime * 10f);
            }
        }

        public override DamageResult ReceiveDamage(DamageRequest request)
        {
            if (_health.IsDead)
            {
                return DamageResult.Ignored;
            }

            float applied = _health.ApplyDamage(request.RawDamage, _armor);
            _flashRemaining = 0.12f;
            if (_health.IsDead && _collider != null)
            {
                _collider.enabled = false;
            }

            return new DamageResult(true, false, applied, _health.IsDead);
        }
    }
}
