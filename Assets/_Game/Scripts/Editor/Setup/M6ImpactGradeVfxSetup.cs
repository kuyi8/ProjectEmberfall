using System;
using System.Linq;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Editor.Setup
{
    /// <summary>Project-owned, presentation-only derivatives. Imported originals remain unchanged.</summary>
    public static class M6ImpactGradeVfxSetup
    {
        public const string Root = "Assets/_Game/Prefabs/VFX/M6Art/";
        public const string GuardBreakPath = Root + "P_M6_Impact_GuardBreak.prefab";
        public const string ExecutionPath = Root + "P_M6_Impact_Execution.prefab";
        public const string SweepPath = Root + "P_M6_Impact_Sweep.prefab";
        public const string ExecutionMeshPath = Root + "M_ExecutionCrescent.asset";
        public const string ExecutionMaterialPath = "Assets/_Game/Art/Materials/M6Art/M_ExecutionCrescent.mat";

        public static void EnsureAssets(bool rebuild = false)
        {
            Create(GuardBreakPath, root =>
            {
                Add(root, Root + "P_M6_Impact_Guard.prefab", .8f, Vector3.zero, new Color(1f, .65f, .18f));
                CreateContactBurst(root);
            }, rebuild);
            Create(ExecutionPath, CreateExecution, rebuild);
            Create(SweepPath, root =>
            {
                var particle = CreateAccent(root, "ConfirmedRangeArc", true, 1, .4f);
                var main = particle.main;
                main.startSize = 1; main.startSpeed = 0;
                var renderer = particle.GetComponent<ParticleSystemRenderer>();
                renderer.renderMode = ParticleSystemRenderMode.Mesh;
                renderer.alignment = ParticleSystemRenderSpace.Local;
                // The pool supplies a cached per-slot mesh from the actual confirmed query, never a constant prefab radius.
                renderer.mesh = null;
            }, rebuild);
        }

        public static void RebuildApprovedImpactAccents()
        {
            EnsureAssets(true);
            AssetDatabase.SaveAssets();
        }

        public static void RebuildExecutionAccent()
        {
            // Do not regenerate the already-reviewed Sweep/GuardBreak assets or their file IDs.
            Create(ExecutionPath, CreateExecution, true);
            AssetDatabase.SaveAssets();
        }

        private static void CreateExecution(GameObject root)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(ExecutionMaterialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Emberfall/M6ExecutionCrescent"));
                AssetDatabase.CreateAsset(material, ExecutionMaterialPath);
            }
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(ExecutionMeshPath);
            if (mesh == null)
            {
                // Baked presentation mesh, not an attack range and not a runtime allocation.
                const int segments = 40;
                var vertices = new Vector3[(segments + 1) * 2];
                var uv = new Vector2[vertices.Length];
                var triangles = new int[segments * 6];
                for (int i = 0; i <= segments; i++)
                {
                    float t = i / (float)segments;
                    float angle = Mathf.Lerp(-80, 80, t) * Mathf.Deg2Rad;
                    float width = .015f + .245f * Mathf.Sin(t * Mathf.PI);
                    var direction = new Vector3(Mathf.Sin(angle), Mathf.Cos(angle), 0);
                    vertices[i * 2] = direction * (.9f - width) - Vector3.up * .4f;
                    vertices[i * 2 + 1] = direction * .9f - Vector3.up * .4f;
                    uv[i * 2] = new Vector2(t, 0); uv[i * 2 + 1] = new Vector2(t, 1);
                    if (i == segments) continue;
                    int v = i * 2, index = i * 6;
                    triangles[index] = v; triangles[index + 1] = v + 1; triangles[index + 2] = v + 2;
                    triangles[index + 3] = v + 1; triangles[index + 4] = v + 3; triangles[index + 5] = v + 2;
                }
                mesh = new Mesh { name = "ExecutionCrescent", vertices = vertices, uv = uv, triangles = triangles };
                mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, ExecutionMeshPath);
            }
            var slash = CreateAccent(root, "ExecutionCrescent", true, 1, .32f);
            slash.transform.localPosition = new Vector3(0, .15f, -.34f);
            slash.transform.localRotation = Quaternion.Euler(0, 0, -35);
            var main = slash.main; main.startSize = 1; main.startSpeed = 0; main.startColor = Color.white;
            var renderer = slash.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.alignment = ParticleSystemRenderSpace.Local;
            renderer.mesh = mesh; renderer.sharedMaterial = material;
            Add(root, Root + "P_M6_Impact_Steel.prefab", .7f, Vector3.zero, new Color(1f, .65f, .35f));
        }

        private static void CreateContactBurst(GameObject root)
        {
            var shards = CreateAccent(root, "ContactFracture", false, 14, .26f);
            var main = shards.main;
            main.startSize = new ParticleSystem.MinMaxCurve(.14f, .28f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.1f, 2.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0, Mathf.PI * 2);
            var shape = shards.shape; shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere; shape.radius = .06f;
            var core = CreateAccent(root, "ContactFlash", false, 1, .11f);
            var flash = core.main; flash.startSize = .72f; flash.startSpeed = 0;
        }

        private static ParticleSystem CreateAccent(GameObject root, string name, bool arc, short count, float lifetime)
        {
            string materialPath = "Assets/_Game/Art/Materials/M6Art/M_ImpactAccent_" + (arc ? "Arc" : "Spark") + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Emberfall/M6ImpactAccent"));
                material.SetFloat("_Arc", arc ? 1 : 0);
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var effect = new GameObject(name); effect.transform.SetParent(root.transform, false);
            var particle = effect.AddComponent<ParticleSystem>();
            particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particle.main;
            main.loop = false; main.playOnAwake = false; main.duration = .5f;
            main.startLifetime = lifetime; main.startDelay = 0; main.simulationSpeed = 1;
            main.startColor = arc ? new Color(1f, .64f, .16f, .85f) : new Color(1f, .54f, .12f);
            main.maxParticles = count; main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            var emission = particle.emission; emission.rateOverTime = 0;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0, count) });
            var shape = particle.shape; shape.enabled = false;
            var color = particle.colorOverLifetime; color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(1, .2f), new GradientAlphaKey(0, 1) });
            color.color = gradient;
            var renderer = particle.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return particle;
        }

        public static void Bind(CombatImpactVfxPresenter presenter) => presenter.ConfigureGradeEffects(
            AssetDatabase.LoadAssetAtPath<GameObject>(GuardBreakPath),
            AssetDatabase.LoadAssetAtPath<GameObject>(ExecutionPath),
            AssetDatabase.LoadAssetAtPath<GameObject>(SweepPath));

        private static void Create(string path, Action<GameObject> configure, bool rebuild)
        {
            // Preserve reviewed/customized variants on subsequent scene/build setup runs.
            if (!rebuild && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null) return;
            var root = new GameObject(System.IO.Path.GetFileNameWithoutExtension(path));
            try { configure(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { UnityEngine.Object.DestroyImmediate(root); }
        }

        private static GameObject Add(GameObject root, string path, float scale, Vector3 rotation, Color tint)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (source == null) throw new InvalidOperationException("Missing reviewed VFX source: " + path);
            // Unconnected instance: every particle override belongs to our derivative, never the source.
            var effect = UnityEngine.Object.Instantiate(source, root.transform);
            effect.name = source.name;
            effect.transform.localPosition = Vector3.zero;
            effect.transform.localRotation = Quaternion.Euler(rotation);
            effect.transform.localScale = Vector3.one * scale;
            foreach (var script in effect.GetComponentsInChildren<MonoBehaviour>(true)) UnityEngine.Object.DestroyImmediate(script);
            foreach (var collider in effect.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
            foreach (var light in effect.GetComponentsInChildren<Light>(true)) UnityEngine.Object.DestroyImmediate(light);
            foreach (var particle in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                particle.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = particle.main;
                main.loop = false; main.playOnAwake = false; main.stopAction = ParticleSystemStopAction.None;
                main.scalingMode = ParticleSystemScalingMode.Hierarchy;
                main.startColor = tint; main.maxParticles = Mathf.Min(main.maxParticles, 32);
                main.simulationSpeed = Mathf.Max(1.7f, main.simulationSpeed);
                var collision = particle.collision; collision.enabled = false;
                var trigger = particle.trigger; trigger.enabled = false;
                var lights = particle.lights; lights.enabled = false;
            }
            foreach (var renderer in effect.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            return effect;
        }
    }
}
