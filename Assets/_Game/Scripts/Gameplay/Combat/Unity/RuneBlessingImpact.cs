using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>A world-space pulse only. No combat, movement, animator or network authority.</summary>
    public sealed class RuneBlessingImpact : MonoBehaviour
    {
        public const float LifetimeSeconds = .42f;
        public const float InitialScale = .22f;
        public const float FinalScale = 1.15f;
        public const string ShaderName = "Emberfall/M6ExecutionCrescent";
        const int RingSegments = 32;
        const int SparkCount = 6;
        const float Opacity = .65f;
        static readonly int EdgeColor = Shader.PropertyToID("_EdgeColor");
        static readonly int CoreColor = Shader.PropertyToID("_CoreColor");

        Mesh _mesh;
        Material _material;
        Color32[] _colors;
        float _sourceAlpha;
        float _elapsed;
        bool _configured;

        public float ElapsedSeconds => _elapsed;
        public float CurrentAlpha => _colors != null && _colors.Length > 0 ? _colors[0].a / 255f : 0f;
        public Color SourceColor { get; private set; }
        public Vector3 BirthPosition { get; private set; }
        public Quaternion BirthRotation { get; private set; }
        public int BirthCameraId { get; private set; }

        public static RuneBlessingImpact Create(Vector3 position, Color color, Scene ownerScene, Camera birthCamera)
        {
            // Fail closed before allocating a visual root; never restore an opaque primitive fallback.
            Shader shader = Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported)
            {
                Debug.LogError("RuneBlessingImpact requires the supported " + ShaderName + " shader.");
                return null;
            }
            if (!ownerScene.IsValid() || !ownerScene.isLoaded)
            {
                Debug.LogError("RuneBlessingImpact requires its owner's loaded scene.");
                return null;
            }
            GameObject root = null;
            try
            {
                root = new GameObject("RuneBlessingImpact") { layer = 2 };
                SceneManager.MoveGameObjectToScene(root, ownerScene);
                root.transform.SetPositionAndRotation(position, birthCamera != null ? birthCamera.transform.rotation : Quaternion.identity);
                var impact = root.AddComponent<RuneBlessingImpact>();
                impact.Configure(shader, color, birthCamera);
                return impact;
            }
            catch (Exception exception)
            {
                if (root != null) ReleaseObject(root);
                Debug.LogException(exception);
                return null;
            }
        }

        void Configure(Shader shader, Color color, Camera birthCamera)
        {
            if (_configured) throw new InvalidOperationException("A rune pulse cannot be reconfigured.");
            try
            {
                if (shader == null) throw new ArgumentNullException(nameof(shader));
                SourceColor = color;
                _sourceAlpha = Mathf.Clamp01(color.a);
                BirthPosition = transform.position;
                BirthRotation = transform.rotation;
                BirthCameraId = birthCamera != null ? birthCamera.GetInstanceID() : 0;
                _mesh = BuildMesh();
                _colors = new Color32[_mesh.vertexCount];
                _material = new Material(shader) { name = "M_Runtime_RuneBlessingImpact", hideFlags = HideFlags.DontSave,
                    renderQueue = (int)RenderQueue.Transparent, enableInstancing = false };
                _material.SetOverrideTag("RenderType", "Transparent");
                // This custom shader has fixed alpha blending, not URP Lit surface keywords.
                Color rgb = new Color(color.r, color.g, color.b, 1f);
                _material.SetColor(EdgeColor, rgb);
                _material.SetColor(CoreColor, rgb);
                gameObject.AddComponent<MeshFilter>().sharedMesh = _mesh;
                var renderer = gameObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = _material;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                renderer.lightProbeUsage = LightProbeUsage.Off;
                renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                // Fixed colour layers avoid distance-driven swaps between coincident unlike pulses.
                // Same-colour pulses commute under alpha compositing; no global resource/serial cache.
                renderer.sortingOrder = ColorLayer(color);
                _configured = true;
                ApplyVisual();
            }
            catch
            {
                ReleaseOwned();
                throw;
            }
        }

        static int ColorLayer(Color color)
        {
            if (color.r > .7f && color.g < .6f && color.b > .7f) return 3; // Neutral posture: purple.
            if (color.r < .5f && color.b > .9f) return 2; // Perfect dodge: cyan.
            if (color.r < .5f && color.g > .9f) return 1; // Healing: green.
            return 0; // Perfect guard: white.
        }

        static Mesh BuildMesh()
        {
            const int quads = RingSegments + SparkCount;
            var vertices = new Vector3[quads * 4];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[quads * 6];
            for (int i = 0; i < RingSegments; i++)
            {
                Vector3 a = Radial(i * Mathf.PI * 2f / RingSegments);
                Vector3 b = Radial((i + 1) * Mathf.PI * 2f / RingSegments);
                PutQuad(i, a * .43f, a * .485f, b * .43f, b * .485f, vertices, uv, triangles);
            }
            for (int i = 0; i < SparkCount; i++)
            {
                Vector3 radial = Radial((i + .5f) * Mathf.PI * 2f / SparkCount);
                Vector3 side = new Vector3(-radial.y, radial.x, 0f) * .008f;
                PutQuad(RingSegments + i, radial * .45f - side, radial * .45f + side,
                    radial * .495f - side, radial * .495f + side, vertices, uv, triangles);
            }
            var mesh = new Mesh { name = "Mesh_Runtime_RuneBlessingImpact", hideFlags = HideFlags.DontSave };
            try
            {
                mesh.MarkDynamic(); mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles;
                mesh.RecalculateBounds();
                return mesh;
            }
            catch { ReleaseObject(mesh); throw; }
        }

        static Vector3 Radial(float angle) => new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);

        static void PutQuad(int quad, Vector3 a, Vector3 b, Vector3 c, Vector3 d,
            Vector3[] vertices, Vector2[] uv, int[] triangles)
        {
            int v = quad * 4, t = quad * 6;
            vertices[v] = a; vertices[v + 1] = b; vertices[v + 2] = c; vertices[v + 3] = d;
            uv[v] = new Vector2(0, 0); uv[v + 1] = new Vector2(0, 1);
            uv[v + 2] = new Vector2(1, 0); uv[v + 3] = new Vector2(1, 1);
            triangles[t] = v; triangles[t + 1] = v + 1; triangles[t + 2] = v + 2;
            triangles[t + 3] = v + 2; triangles[t + 4] = v + 1; triangles[t + 5] = v + 3;
        }

        void Update()
        {
            if (!_configured) return;
            // Preserve scaled world time: pause stops it, local Animator hit-stop does not.
            _elapsed += Time.deltaTime;
            ApplyVisual();
            if (_elapsed >= LifetimeSeconds) Destroy(gameObject);
        }

        void ApplyVisual()
        {
            float progress = Mathf.Clamp01(_elapsed / LifetimeSeconds);
            transform.localScale = Vector3.one * Mathf.Lerp(InitialScale, FinalScale, progress);
            byte alpha = (byte)Mathf.RoundToInt(_sourceAlpha * Opacity * (1f - progress) * 255f);
            for (int i = 0; i < _colors.Length; i++) _colors[i] = new Color32(255, 255, 255, alpha);
            _mesh.colors32 = _colors;
        }

        void ReleaseOwned()
        {
            var filter = GetComponent<MeshFilter>(); if (filter != null) filter.sharedMesh = null;
            var renderer = GetComponent<MeshRenderer>(); if (renderer != null) renderer.sharedMaterial = null;
            Mesh mesh = _mesh; Material material = _material;
            _mesh = null; _material = null; _colors = null; _configured = false;
            if (mesh != null) ReleaseObject(mesh);
            if (material != null) ReleaseObject(material);
        }

        static void ReleaseObject(UnityEngine.Object value)
        {
            if (UnityEngine.Application.isPlaying) Destroy(value); else DestroyImmediate(value);
        }

        void OnDestroy() => ReleaseOwned();
    }
}
