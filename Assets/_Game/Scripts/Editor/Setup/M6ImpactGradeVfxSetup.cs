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
        private const string Shock = "Assets/_Game/Art/DownloadResources/Vefects/Easy Shockwaves VFX URP/VFX/Shockwaves/Particles/VFX_Shockwave_01_White_Big_1s.prefab";
        private const string Slash = "Assets/_Game/Art/DownloadResources/Matthew Guz/Slash Effects FREE/Prefab/Multiple Slash 2 .prefab";

        public static void EnsureAssets(bool rebuild = false)
        {
            Create(GuardBreakPath, root =>
            {
                Add(root, Root + "P_M6_Impact_Guard.prefab", .8f, Vector3.zero, new Color(1f, .65f, .18f));
                Add(root, Shock, 1.4f, Vector3.zero, new Color(1f, .7f, .25f));
            }, rebuild);
            Create(ExecutionPath, root =>
            {
                Add(root, Slash, .42f, new Vector3(0, 0, 30), new Color(1f, .4f, .18f));
                Add(root, Root + "P_M6_Impact_Steel.prefab", .7f, Vector3.zero, new Color(1f, .65f, .35f));
            }, rebuild);
            Create(SweepPath, root =>
            {
                var effect = Add(root, Slash, .55f, Vector3.zero, new Color(1f, .85f, .55f));
                // One physical mesh crescent, without the source's second slash and sparkle halo.
                foreach (var child in effect.GetComponentsInChildren<Transform>(true).Reverse().ToArray())
                    if (child != effect.transform)
                        UnityEngine.Object.DestroyImmediate(child.gameObject);
            }, rebuild);
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
