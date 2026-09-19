using System.Collections.Generic;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Targeting;
using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>Targets and pools the visual/physics adapter for player throwing knives.</summary>
    public sealed class PlayerThrowingKnifeLauncher : MonoBehaviour
    {
        [SerializeField] private PlayerCombatActor _actor;
        [SerializeField] private LockOnTargeting _targeting;
        [SerializeField] private Transform _launchOrigin;
        [SerializeField] private GameObject _projectilePrefab;
        [SerializeField, Range(1, 8)] private int _prewarmCount = 4;
        [SerializeField, Range(0f, 0.25f)] private float _unlockedAimAssistRadius = 0.1f;

        private readonly Queue<PlayerThrowingKnifeProjectile> _available =
            new Queue<PlayerThrowingKnifeProjectile>();
        private Transform _poolRoot;

        public bool IsConfigured =>
            _actor != null && _targeting != null && _launchOrigin != null && _projectilePrefab != null;
        public int ActiveProjectileCount { get; private set; }

        public void Configure(
            PlayerCombatActor actor,
            LockOnTargeting targeting,
            Transform launchOrigin,
            GameObject projectilePrefab)
        {
            if (_actor != null) _actor.RangedAttackReleased -= HandleRelease;
            _actor = actor;
            _targeting = targeting;
            _launchOrigin = launchOrigin;
            _projectilePrefab = projectilePrefab;
            if (isActiveAndEnabled && _actor != null) _actor.RangedAttackReleased += HandleRelease;
        }

        private void Awake()
        {
            _poolRoot = new GameObject("ThrowingKnifePool").transform;
            _poolRoot.SetParent(transform, false);
            if (!IsConfigured)
            {
                Debug.LogError("PlayerThrowingKnifeLauncher is not configured.", this);
                enabled = false;
                return;
            }

            for (int i = 0; i < _prewarmCount; i++) _available.Enqueue(CreateProjectile());
        }

        private void OnEnable()
        {
            if (_actor != null) _actor.RangedAttackReleased += HandleRelease;
        }

        private void OnDisable()
        {
            if (_actor != null) _actor.RangedAttackReleased -= HandleRelease;
        }

        private void HandleRelease(RangedAttackRelease release)
        {
            PlayerThrowingKnifeProjectile projectile = _available.Count > 0
                ? _available.Dequeue()
                : CreateProjectile();
            Vector3 origin = _launchOrigin.position;
            Vector3 direction = _targeting.IsLocked
                ? _targeting.CurrentTarget.AimPoint.position - origin
                : ResolveUnlockedDirection(origin, release.MaximumDistance);
            projectile.transform.SetParent(null, true);
            projectile.gameObject.SetActive(true);
            ActiveProjectileCount++;
            projectile.Launch(_actor, origin, direction, release, ReturnToPool);
        }

        private Vector3 ResolveUnlockedDirection(Vector3 launchOrigin, float maximumDistance)
        {
            Vector3 centerOrigin = _actor.AimPoint.position;
            Vector3 forward = _actor.transform.forward;
            int mask = ~(1 << _actor.gameObject.layer);
            if (Physics.SphereCast(
                    centerOrigin,
                    _unlockedAimAssistRadius,
                    forward,
                    out RaycastHit hit,
                    maximumDistance,
                    mask,
                    QueryTriggerInteraction.Ignore))
            {
                Vector3 converged = hit.point - launchOrigin;
                if (converged.sqrMagnitude > 0.001f) return converged.normalized;
            }
            return forward;
        }

        private PlayerThrowingKnifeProjectile CreateProjectile()
        {
            GameObject instance = Instantiate(_projectilePrefab, _poolRoot);
            instance.name = "PlayerThrowingKnife_Pooled";
            PlayerThrowingKnifeProjectile projectile = instance.GetComponent<PlayerThrowingKnifeProjectile>();
            if (projectile == null)
                throw new MissingComponentException("Throwing-knife prefab has no projectile adapter.");
            instance.SetActive(false);
            return projectile;
        }

        private void ReturnToPool(PlayerThrowingKnifeProjectile projectile)
        {
            if (projectile == null) return;
            ActiveProjectileCount = Mathf.Max(0, ActiveProjectileCount - 1);
            projectile.gameObject.SetActive(false);
            projectile.transform.SetParent(_poolRoot, false);
            _available.Enqueue(projectile);
        }
    }
}
