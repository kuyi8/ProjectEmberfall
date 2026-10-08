using Emberfall.AI.Domain;
using Emberfall.AI.Unity;
using UnityEngine;
using UnityEngine.SceneManagement;

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
        private float _warningRadius;
        private GroundRuneBoundaryMesh _boundaryMesh;
        private GameObject _boundaryRoot;
        private Renderer _boundaryRenderer;
        private Renderer _groundRuneProgressRenderer;

        public float WarningRadius => _warningRadius;
        public Renderer WarningBoundaryRenderer => _boundaryRenderer;
        public bool IsWarningBoundaryVisible => _boundaryRenderer != null && _boundaryRenderer.enabled &&
            _boundaryRenderer.gameObject.activeInHierarchy;

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
                component._warningRadius = radius;
                component._groundRuneProgressRenderer = renderer;
                component.CreateBoundaryWarning(material);
                component.SetWarningVisible(true);
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
            if (_remaining <= 0f)
            {
                // This is the existing client-local fuse, not a newly synchronized Server resolve signal.
                if (_groundRune) SetWarningVisible(false);
                Destroy(gameObject);
            }
        }

        private void CreateBoundaryWarning(Material material)
        {
            // As in the offline adapter, invalid visual geometry must not introduce a new protocol exception.
            if (_warningRadius <= 0 || float.IsNaN(_warningRadius) || float.IsInfinity(_warningRadius)) return;
            _boundaryMesh = new GroundRuneBoundaryMesh();
            _boundaryMesh.SetRadius(_warningRadius);
            // A separate unit-scale root cannot inherit the legacy cylinder's expanding progress scale.
            _boundaryRoot = new GameObject("NetworkHostileRuneBoundary", typeof(MeshFilter), typeof(MeshRenderer));
            SceneManager.MoveGameObjectToScene(_boundaryRoot, gameObject.scene);
            _boundaryRoot.layer = gameObject.layer;
            _boundaryRoot.transform.position = transform.position + Vector3.up * .038f;
            _boundaryRoot.GetComponent<MeshFilter>().sharedMesh = _boundaryMesh.Mesh;
            _boundaryRenderer = _boundaryRoot.GetComponent<MeshRenderer>();
            _boundaryRenderer.sharedMaterial = material;
            _boundaryRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _boundaryRenderer.receiveShadows = false;
            _boundaryRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _boundaryRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private void SetWarningVisible(bool visible)
        {
            if (_boundaryRenderer != null) _boundaryRenderer.enabled = visible;
            if (_groundRuneProgressRenderer != null) _groundRuneProgressRenderer.enabled = visible;
        }

        private void OnEnable()
        {
            if (_groundRune) SetWarningVisible(_remaining > 0f);
        }

        private void OnDisable()
        {
            if (_groundRune) SetWarningVisible(false);
        }

        private void OnDestroy()
        {
            _boundaryMesh?.Dispose();
            if (_boundaryRoot != null) Destroy(_boundaryRoot);
        }
    }
}
