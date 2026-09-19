using Emberfall.AI.Domain;
using UnityEngine;

namespace Emberfall.Networking
{
    /// <summary>Client-local visuals for Server-resolved Rune Priest attacks.</summary>
    public sealed class NetworkEnemyAttackPresentation : MonoBehaviour
    {
        private Vector3 _direction;
        private float _speed;
        private float _remaining;
        private float _lifetime;
        private bool _groundRune;
        private Vector3 _baseScale;

        public static void Spawn(
            RangedAttackKind kind,
            Vector3 origin,
            Vector3 releaseVector,
            float projectileSpeed,
            float projectileLifetime,
            float runeFuse,
            Material material)
        {
            GameObject visual = GameObject.CreatePrimitive(
                kind == RangedAttackKind.GroundRune ? PrimitiveType.Cylinder : PrimitiveType.Sphere);
            visual.name = kind == RangedAttackKind.GroundRune
                ? "NetworkRunePriest_GroundRune_Presentation"
                : "NetworkRunePriest_Projectile_Presentation";
            visual.transform.position = origin;
            Collider collider = visual.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            Renderer renderer = visual.GetComponent<Renderer>();
            if (renderer != null && material != null) renderer.sharedMaterial = material;

            NetworkEnemyAttackPresentation component = visual.AddComponent<NetworkEnemyAttackPresentation>();
            component._groundRune = kind == RangedAttackKind.GroundRune;
            if (component._groundRune)
            {
                float radius = Mathf.Max(0.2f, releaseVector.x);
                component._baseScale = new Vector3(radius * 2f, 0.025f, radius * 2f);
                visual.transform.localScale = component._baseScale * 0.25f;
                component._remaining = component._lifetime = Mathf.Max(0.1f, runeFuse);
            }
            else
            {
                component._direction = releaseVector.sqrMagnitude > 0.0001f
                    ? releaseVector.normalized
                    : Vector3.forward;
                component._speed = Mathf.Max(0.1f, projectileSpeed);
                component._remaining = component._lifetime = Mathf.Max(0.1f, projectileLifetime);
                visual.transform.localScale = Vector3.one * 0.22f;
                TrailRenderer trail = visual.AddComponent<TrailRenderer>();
                trail.sharedMaterial = material;
                trail.time = 0.22f;
                trail.startWidth = 0.16f;
                trail.endWidth = 0.01f;
            }
        }

        private void Update()
        {
            _remaining -= Time.deltaTime;
            if (_groundRune)
            {
                float normalized = 1f - Mathf.Clamp01(_remaining / _lifetime);
                transform.localScale = Vector3.Lerp(_baseScale * 0.25f, _baseScale, normalized);
                transform.Rotate(Vector3.up, 100f * Time.deltaTime, Space.World);
            }
            else
            {
                transform.position += _direction * (_speed * Time.deltaTime);
            }
            if (_remaining <= 0f) Destroy(gameObject);
        }
    }
}
