using System;
using System.IO;
using System.Linq;
using Emberfall.Application.Flow;
using Emberfall.Networking;
using Emberfall.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>Owned additive migration. Wall/physics/nav/actor assets remain untouched.</summary>
    public static class AshEncounterBannerSetup
    {
        public const string RootName = "[Art] Ash Clear Banners";
        public const string PrefabPath = "Assets/_Game/Art/ImportedEnvironmentDressing/Prefabs/P_banner_patternA_blue_Banner.prefab";

        [MenuItem("Emberfall/Presentation/Add Ash Clear Banners")]
        public static string Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                scene.isDirty || SceneManager.sceneCount != 1 || scene.path != AshApproachEncounterSetup.ScenePath)
                throw new InvalidOperationException("Clean idle Valley required; preserve unsaved work.");
            if (scene.GetRootGameObjects().Any(g => g.name == RootName))
            { Validate(scene); return "Existing complete banners preserved; no writes."; }
            string folder = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-pre-ash-banners");
            Directory.CreateDirectory(folder);
            File.Copy(scene.path, folder + "/10_EmberValley.unity", false);
            File.Copy(scene.path + ".meta", folder + "/10_EmberValley.unity.meta", false);
            var net = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<NetworkEmberValleyModeAdapter>(true)).Single();
            var old = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true)).Where(c => c != net)
                .ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
            var netData = new SerializedObject(net);
            Object[] oldRoots = References(netData.FindProperty("_offlineActorRoots"));
            Object[] oldBehaviours = References(netData.FindProperty("_offlineBehaviours"));
            string navPath = AssetDatabase.GetAssetPath(Resources.FindObjectsOfTypeAll<UnityEngine.AI.NavMeshData>().FirstOrDefault(AssetDatabase.Contains));
            string navHash = string.IsNullOrEmpty(navPath) ? "" : Hash(navPath);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            var renderer = prefab.GetComponentInChildren<Renderer>(true);
            string meshPath = AssetDatabase.GetAssetPath(renderer.GetComponent<MeshFilter>().sharedMesh);
            string materialPath = AssetDatabase.GetAssetPath(renderer.sharedMaterial);
            string texturePath = AssetDatabase.GetAssetPath(renderer.sharedMaterial.GetTexture("_BaseMap"));
            var protectedPaths = new[] { PrefabPath, meshPath, materialPath, texturePath }
                .SelectMany(p => new[] { p, p + ".meta" }).Concat(new[] {
                    "Assets/_Game/Art/DownloadResources/UnityFreeAssets/07_Environment_Ruins/KayKit_Dungeon_Remastered/LICENSE.txt" });
            var protectedArt = protectedPaths.ToDictionary(p => p, Hash);
            if (ApplyToScene(scene) != 3) throw new InvalidOperationException("All three formal content roots required; backup=" + folder);
            int changed = old.Count(pair => pair.Key == null || pair.Value != EditorJsonUtility.ToJson(pair.Key));
            netData.Update();
            bool prefixes = References(netData.FindProperty("_offlineActorRoots")).Take(oldRoots.Length).SequenceEqual(oldRoots) &&
                References(netData.FindProperty("_offlineBehaviours")).Take(oldBehaviours.Length).SequenceEqual(oldBehaviours);
            bool navSame = string.IsNullOrEmpty(navPath) || navHash == Hash(navPath);
            bool artSame = protectedArt.All(p => Hash(p.Key) == p.Value);
            File.WriteAllText(folder + "/protection.txt", "oldComponents=" + old.Count + " changed=" + changed +
                " rootsPrefix=" + oldRoots.Length + " behavioursPrefix=" + oldBehaviours.Length + " prefixesUnchanged=" + prefixes +
                " navPath=" + navPath + " navUnchanged=" + navSame + " artFiles=" + protectedArt.Count + " artUnchanged=" + artSame +
                "\nScope: all original serialized component JSON except the intentionally appended network isolation component; original isolation array prefixes, loaded existing NavMeshData asset bytes and exact reused art/metadata/license files. No claim of a whole workspace snapshot.");
            if (changed != 0 || !prefixes || !navSame || !artSame) throw new InvalidOperationException("Protection mismatch; preserve unsaved scene and backup=" + folder);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Preserve scene and backup=" + folder);
            return "Three passive wall banners authored; old physics/NavMesh unchanged; backup=" + folder;
        }

        private static Object[] References(SerializedProperty array)
            => Enumerable.Range(0, array.arraySize).Select(i => array.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
        private static string Hash(string path)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "");
        }

        public static int ApplyToScene(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle Edit mode required.");
            if (scene.name != "10_EmberValley") return 0;
            var roots = scene.GetRootGameObjects();
            if (roots.Any(g => g.name == RootName)) { Validate(scene); return 0; }
            // A generator without the new formal encounters must not invent three clear claims.
            if (!new[] { AshApproachEncounterSetup.RootName, AshApproachEncounterSetup.GuardRootName, AshApproachEncounterSetup.ReturnRootName }
                .All(n => roots.Any(g => g.name == n))) return 0;
            var flow = roots.SelectMany(g => g.GetComponentsInChildren<M2RouteFlowController>(true)).Single();
            var net = roots.SelectMany(g => g.GetComponentsInChildren<NetworkEmberValleyModeAdapter>(true)).Single();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null || prefab.GetComponentsInChildren<Component>(true).Any(c => !(c is Transform || c is MeshFilter || c is MeshRenderer)))
                throw new InvalidOperationException("Require the existing pure licensed banner derivative.");
            var root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene);
            foreach (var pair in Mounts(scene))
            {
                var wrapper = new GameObject("ClearBanner_" + pair.Item1); wrapper.transform.SetParent(root.transform, false);
                var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                model.name = "Model"; model.transform.SetParent(wrapper.transform, false);
                model.transform.localScale = Vector3.one * pair.Item3;
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                Place(wrapper.transform, renderers, pair.Item2.bounds);
                foreach (var r in renderers)
                { r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = true; r.enabled = false; }
                wrapper.AddComponent<M2EncounterBannerPresenter>().Configure(flow, pair.Item1, renderers);
            }
            var data = new SerializedObject(net);
            Append(data.FindProperty("_offlineActorRoots"), root);
            foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true)) Append(data.FindProperty("_offlineBehaviours"), behaviour);
            data.ApplyModifiedPropertiesWithoutUndo();
            Validate(scene); EditorSceneManager.MarkSceneDirty(scene); return 3;
        }

        private static (AshEncounterBanner, Collider, float)[] Mounts(Scene scene)
        {
            var colliders = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Collider>(true)).ToArray();
            // Mount on the far wall seen while entering the encounter, not under the foreground HUD.
            Collider a = colliders.Single(c => !c.isTrigger && Vector3.Distance(c.bounds.center, new Vector3(-2.9f, .4f, 17f)) < .001f);
            Collider b = colliders.Single(c => !c.isTrigger && c.transform.parent != null && c.transform.parent.name == "Forest_SE_0");
            Collider c = colliders.Single(x => x.transform.parent != null && x.transform.parent.name == "Return_N_2");
            return new[] { (AshEncounterBanner.Approach, a, .22f), (AshEncounterBanner.GuardPass, b, .22f), (AshEncounterBanner.Return, c, .35f) };
        }

        private static void Place(Transform wrapper, Renderer[] renderers, Bounds wall)
        {
            Bounds bounds = renderers[0].bounds;
            foreach (var r in renderers) bounds.Encapsulate(r.bounds);
            Vector3 center = new Vector3(wall.center.x, wall.center.y, wall.min.z - bounds.extents.z - .06f);
            wrapper.position += center - bounds.center;
        }

        [MenuItem("Emberfall/Presentation/Reposition Owned Ash Clear Banners")]
        public static string RepositionOwnedBanners()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                scene.isDirty || SceneManager.sceneCount != 1 || scene.path != AshApproachEncounterSetup.ScenePath)
                throw new InvalidOperationException("Clean idle Valley required; preserve unsaved work.");
            Validate(scene);
            var root = scene.GetRootGameObjects().Single(g => g.name == RootName);
            var mounts = Mounts(scene);
            string folder = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-pre-banner-placement");
            Directory.CreateDirectory(folder);
            File.Copy(scene.path, folder + "/10_EmberValley.unity", false);
            File.Copy(scene.path + ".meta", folder + "/10_EmberValley.unity.meta", false);
            var outside = scene.GetRootGameObjects().Where(g => g != root).SelectMany(g => g.GetComponentsInChildren<Component>(true))
                .ToDictionary(c => c, c => EditorJsonUtility.ToJson(c));
            string navPath = AssetDatabase.GetAssetPath(Resources.FindObjectsOfTypeAll<UnityEngine.AI.NavMeshData>().FirstOrDefault(AssetDatabase.Contains));
            string navHash = string.IsNullOrEmpty(navPath) ? "" : Hash(navPath);
            foreach (var mount in mounts)
            {
                var presenter = root.GetComponentsInChildren<M2EncounterBannerPresenter>(true).Single(p => p.Encounter == mount.Item1);
                Place(presenter.transform, presenter.GetComponentsInChildren<Renderer>(true), mount.Item2.bounds);
            }
            Validate(scene);
            int changed = outside.Count(p => p.Key == null || p.Value != EditorJsonUtility.ToJson(p.Key));
            bool navSame = string.IsNullOrEmpty(navPath) || navHash == Hash(navPath);
            File.WriteAllText(folder + "/protection.txt", "outsideComponents=" + outside.Count + " changed=" + changed + " navUnchanged=" + navSame +
                "\nScope: all scene component JSON outside the owned banner root, INCLUDING the network isolation adapter; existing loaded NavMesh asset bytes. Owned flag transforms intentionally moved.");
            if (changed != 0 || !navSame) throw new InvalidOperationException("Protection mismatch; preserve unsaved scene and backup=" + folder);
            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Preserve scene and backup=" + folder);
            return "Owned banners moved onto existing far walls; outside components unchanged; backup=" + folder;
        }

        private static void Append(SerializedProperty array, Object value)
        {
            if (array == null || !array.isArray) throw new InvalidOperationException("Existing isolation array required.");
            if (Enumerable.Range(0, array.arraySize).Any(i => array.GetArrayElementAtIndex(i).objectReferenceValue == value)) return;
            array.InsertArrayElementAtIndex(array.arraySize); array.GetArrayElementAtIndex(array.arraySize - 1).objectReferenceValue = value;
        }

        public static void Validate(Scene scene)
        {
            var root = scene.GetRootGameObjects().Single(g => g.name == RootName);
            var presenters = root.GetComponentsInChildren<M2EncounterBannerPresenter>(true);
            if (root.transform.childCount != 3 || presenters.Length != 3 || presenters.Select(p => p.Encounter).Distinct().Count() != 3 ||
                root.GetComponentsInChildren<Component>(true).Any(c => !(c is Transform || c is MeshFilter || c is MeshRenderer || c is M2EncounterBannerPresenter)))
                throw new InvalidOperationException("Unknown/partial/nonvisual banner hierarchy preserved; inspect it.");
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                var material = renderer.sharedMaterial;
                if (material == null || material.shader.name != "Universal Render Pipeline/Lit" || material.renderQueue != 2000 ||
                    material.GetFloat("_Cull") != 0 || material.GetFloat("_Surface") != 0 || material.GetFloat("_ZWrite") != 1)
                    throw new InvalidOperationException("Existing opaque double-sided banner material required.");
                if (renderer.bounds.size.y > 1.41f) throw new InvalidOperationException("Decorative flag must fit existing low masonry, not float above it.");
            }
        }
    }
}
