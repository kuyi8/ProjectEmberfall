using Emberfall.Gameplay.Combat.Unity;
using Unity.Netcode;
using UnityEngine;

namespace Emberfall.Networking
{
    /// <summary>
    /// Client-local visual for a Server-resolved throwing knife. It has no collider,
    /// NetworkObject, hit callback, or damage authority.
    /// </summary>
    public sealed class NetworkGymProjectilePresentation : MonoBehaviour
    {
        private Vector3 _direction;
        private float _speed;
        private float _remainingDistance;
        private float _spinDegreesPerSecond;

        public static void Spawn(
            GameObject visualPrefab,
            Vector3 origin,
            Vector3 direction,
            float speed,
            float maximumDistance)
        {
            if (visualPrefab == null || speed <= 0f || maximumDistance <= 0f) return;
            Vector3 normalized = direction.sqrMagnitude > 0.0001f
                ? direction.normalized
                : Vector3.forward;
            GameObject instance = Instantiate(
                visualPrefab,
                origin,
                Quaternion.LookRotation(normalized, Vector3.up));
            instance.name = "NetworkThrowingKnife_Presentation";

            NetworkObject networkObject = instance.GetComponent<NetworkObject>();
            if (networkObject != null) Destroy(networkObject);
            PlayerThrowingKnifeProjectile gameplayProjectile =
                instance.GetComponent<PlayerThrowingKnifeProjectile>();
            if (gameplayProjectile != null) gameplayProjectile.enabled = false;
            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
            foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
                body.isKinematic = true;

            NetworkGymProjectilePresentation presentation =
                instance.GetComponent<NetworkGymProjectilePresentation>();
            if (presentation == null) presentation = instance.AddComponent<NetworkGymProjectilePresentation>();
            presentation.Initialize(normalized, speed, maximumDistance);
        }

        private void Initialize(Vector3 direction, float speed, float maximumDistance)
        {
            _direction = direction;
            _speed = speed;
            _remainingDistance = maximumDistance;
            _spinDegreesPerSecond = 900f;
            TrailRenderer trail = GetComponentInChildren<TrailRenderer>(true);
            if (trail != null) trail.Clear();
        }

        private void Update()
        {
            float distance = Mathf.Min(_remainingDistance, _speed * Time.deltaTime);
            transform.position += _direction * distance;
            transform.Rotate(Vector3.forward, _spinDegreesPerSecond * Time.deltaTime, Space.Self);
            _remainingDistance -= distance;
            if (_remainingDistance <= 0.001f) Destroy(gameObject);
        }
    }
}
