using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.Gameplay.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>Imported presentation; only named scene wrappers may add camera queries, never physics or gameplay authority.</summary>
    public static class ImportedEnvironmentDressingSetup
    {
        public const string RootName = "ImportedEnvironmentDressing";
        public const string AssetRoot = "Assets/_Game/Art/ImportedEnvironmentDressing";
        public const string LayoutPath = AssetRoot + "/layout.json";
        public static readonly string[] Scenes = { "10_EmberValley", "20_Sanctum" };
        public static readonly string[] AllowedModels = { "wall_archedwindow_open", "wall_sloped",
            "floor_foundation_allsides", "banner_patternA_blue" };
        // Explicit scene-only exception: large upper walls/caps can obstruct the formal camera.
        // Banners and future unlisted entries stay pure and do not pull the camera inward.
        public static readonly string[] CameraOccluderEntries =
        {
            "10_EmberValley/CampArchiveWest", "10_EmberValley/CampArchiveEast",
            "10_EmberValley/CampSideWindow", "10_EmberValley/CampBrokenWest",
            "10_EmberValley/CampBrokenEast", "10_EmberValley/CampCapWest", "10_EmberValley/CampCapEast",
            "10_EmberValley/CourtArchiveWest", "10_EmberValley/CourtArchiveCenter",
            "10_EmberValley/CourtArchiveEast", "10_EmberValley/CourtBrokenEnd",
            "10_EmberValley/WardenArchiveEastA", "10_EmberValley/WardenArchiveEastB",
            "10_EmberValley/WardenArchiveEastC", "10_EmberValley/WardenBrokenNorthWest",
            "10_EmberValley/WardenBrokenNorthEast", "20_Sanctum/SanctumArchiveWest",
            "20_Sanctum/SanctumArchiveEast", "20_Sanctum/SanctumCapWest", "20_Sanctum/SanctumCapEast"
        };

        [Serializable] public sealed class Layout { public int format; public Entry[] entries; }
        [Serializable] public sealed class Entry
        {
            public string scene, name, model, anchor, palette;
            public Vector3 offset, scale = Vector3.one;
            public float yaw;
        }

        public static string[] SelectedSourcePaths => ReadLayout().entries.Select(e => Source(e.model))
            .Concat(new[] { M6EnvironmentSetup.KitRoot + "texture/dungeon_texture.png",
                M6EnvironmentSetup.KitRoot.Replace("addons/kaykit_dungeon_remastered/Assets/", "") + "LICENSE.txt" })
            .Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();

        public static Layout ReadLayout()
        {
            var text = AssetDatabase.LoadAssetAtPath<TextAsset>(LayoutPath);
            if (text == null) throw new InvalidDataException("Missing authored dressing layout: " + LayoutPath);
            var data = JsonUtility.FromJson<Layout>(text.text);
            if (data == null || data.format != 1 || data.entries == null || data.entries.Length == 0 || data.entries.Length > 64)
                throw new InvalidDataException("Invalid dressing layout.");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in data.entries)
            {
                if (e == null || !Scenes.Contains(e.scene) || string.IsNullOrEmpty(e.name) ||
                    !keys.Add(e.scene + "/" + e.name) || !AllowedModels.Contains(e.model) ||
                    string.IsNullOrEmpty(e.anchor) || !new[] { "Stone", "Camp", "Warden", "Banner" }.Contains(e.palette) ||
                    !Finite(e.offset) || !Finite(e.scale) || float.IsNaN(e.yaw) || float.IsInfinity(e.yaw) ||
                    e.scale.x <= 0 || e.scale.y <= 0 || e.scale.z <= 0 || e.scale.maxComponent() > 3)
                    throw new InvalidDataException("Invalid/duplicate dressing entry: " + (e == null ? "null" : e.name));
                // The existing M6 cleanup must never mistake this layer for a legacy wall.
                if (e.name.StartsWith("[Art]", StringComparison.Ordinal) || e.name.StartsWith("GateBlocker_", StringComparison.Ordinal))
                    throw new InvalidDataException("Reserved object name: " + e.name);
            }
            foreach (string key in CameraOccluderEntries)
            {
                var entry = data.entries.SingleOrDefault(e => e.scene + "/" + e.name == key);
                if (entry == null || entry.model == "banner_patternA_blue")
                    throw new InvalidDataException("Named structural camera entry is missing or has become a banner: " + key);
            }
            return data;
        }

        public static void ApplyToScene(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Idle Edit Mode required.");
            if (!Scenes.Contains(scene.name)) return;
            var data = ReadLayout().entries.Where(e => e.scene == scene.name).ToArray();
            if (data.Length == 0) throw new InvalidDataException("No dressing entries for " + scene.name);
            var all = scene.GetRootGameObjects().Where(r => r.name != RootName)
                .SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            // Validate dependencies and anchors before touching the scene.
            var anchors = data.ToDictionary(e => e.name, e => all.Single(t => t.name == e.anchor).GetComponentInChildren<Renderer>());
            if (anchors.Values.Any(r => r == null)) throw new InvalidDataException("Dressing requires visible existing wall anchors.");
            var root = scene.GetRootGameObjects().SingleOrDefault(r => r.name == RootName);
            // Reject unknown components/children before authoring assets or changing any live scene object.
            if (root != null) InspectSceneVisual(root, scene.name, data, false, false);
            var prefabs = data.ToDictionary(e => e.name, e => EnsureStaticPrefab(e.model, e.palette));
            if (root == null) { root = new GameObject(RootName); SceneManager.MoveGameObjectToScene(root, scene); }
            root.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); root.transform.localScale = Vector3.one;
            foreach (var e in data)
            {
                var wrapper = root.transform.Find(e.name);
                if (wrapper == null)
                {
                    wrapper = new GameObject(e.name).transform; wrapper.SetParent(root.transform, false);
                    var model = Object.Instantiate(prefabs[e.name], wrapper, false); model.name = "Model";
                }
                var anchor = anchors[e.name].bounds;
                wrapper.SetPositionAndRotation(new Vector3(anchor.center.x, anchor.max.y, anchor.center.z) + e.offset,
                    Quaternion.Euler(0, e.yaw, 0));
                wrapper.localScale = e.scale;
                var bounds = BoundsOf(wrapper.gameObject);
                // Actual imported geometry, not guessed FBX pivot. Anchor top is the mounting plane.
                wrapper.position += new Vector3(anchor.center.x + e.offset.x - bounds.center.x,
                    anchor.max.y + e.offset.y - bounds.min.y, anchor.center.z + e.offset.z - bounds.center.z);
                ValidatePureVisual(wrapper.Find("Model").gameObject);
                // Each architectural addition stays within the existing wall's horizontal envelope;
                // lower wall/physics are never hidden or replaced. Large overhangs are rejected.
                bounds = BoundsOf(wrapper.gameObject);
                if (bounds.min.x < anchor.min.x - .025f || bounds.max.x > anchor.max.x + .025f ||
                    bounds.min.z < anchor.min.z - .025f || bounds.max.z > anchor.max.z + .025f)
                    throw new InvalidDataException("Imported art extends beyond its existing solid wall envelope: " + e.name);
            }
            var expected = new HashSet<string>(data.Select(e => e.name), StringComparer.Ordinal);
            foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                if (!expected.Contains(child.name)) Object.DestroyImmediate(child.gameObject);
            RegisterCameraOccluders(scene);
            ValidateSceneVisual(root, scene.name);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Registers only exact authored structural wrappers. The returned count includes new
        /// components or repaired local caches; a correct second pass returns zero and stays clean.
        /// Does not save scenes/assets, invoke M6/Bake, or change an old wall anchor's references.
        /// </summary>
        public static int RegisterCameraOccluders(Scene scene)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                !scene.IsValid() || !scene.isLoaded || !Scenes.Contains(scene.name))
                throw new InvalidOperationException("Idle loaded dressing production scene required.");
            var data = ReadLayout().entries.Where(e => e.scene == scene.name).ToArray();
            var roots = scene.GetRootGameObjects().Where(r => r.name == RootName).ToArray();
            if (roots.Length != 1) throw new InvalidDataException("Exactly one dressing scene root required.");
            GameObject root = roots[0];
            // Full-tree preflight runs before the first AddComponent or serialized reference write.
            var targets = InspectSceneVisual(root, scene.name, data, true, false);
            int changed = 0;
            foreach (var target in targets)
            {
                var occluder = target.Key.GetComponent<CameraOccluder>();
                bool added = occluder == null;
                if (added) occluder = target.Key.gameObject.AddComponent<CameraOccluder>();
                var serialized = new SerializedObject(occluder);
                var array = serialized.FindProperty("_renderers");
                if (!RendererReferencesMatch(array, target.Value))
                {
                    array.arraySize = target.Value.Length;
                    for (int i = 0; i < target.Value.Length; i++)
                        array.GetArrayElementAtIndex(i).objectReferenceValue = target.Value[i];
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                    changed++;
                }
                else if (added) changed++;
            }
            ValidateSceneVisual(root, scene.name);
            if (changed > 0) EditorSceneManager.MarkSceneDirty(scene);
            return changed;
        }

        /// <summary>Scene wrappers alone have the exact camera-query exception; source prefabs remain strictly pure.</summary>
        public static void ValidateSceneVisual(GameObject root, string sceneName)
        {
            var data = ReadLayout().entries.Where(e => e.scene == sceneName).ToArray();
            InspectSceneVisual(root, sceneName, data, true, true);
        }

        static KeyValuePair<Transform, Renderer[]>[] InspectSceneVisual(GameObject root, string sceneName,
            Entry[] data, bool requireAllWrappers, bool requireRegistered)
        {
            if (root == null || root.name != RootName || root.transform.parent != null ||
                EditorUtility.IsPersistent(root) || !Scenes.Contains(sceneName) || data.Length == 0)
                throw new InvalidDataException("Expected an independent nonpersistent dressing root in an approved scene.");
            if (root.GetComponents<Component>().Any(c => !(c is Transform)) ||
                GameObjectUtility.GetStaticEditorFlags(root) != 0)
                throw new InvalidDataException("Dressing root itself must remain a nonstatic Transform only.");
            Transform[] children = root.transform.Cast<Transform>().ToArray();
            var expected = new HashSet<string>(data.Select(e => e.name), StringComparer.Ordinal);
            if (children.Select(t => t.name).Distinct(StringComparer.Ordinal).Count() != children.Length ||
                children.Any(t => !expected.Contains(t.name)) ||
                (requireAllWrappers && children.Length != data.Length))
                throw new InvalidDataException("Unknown, duplicate or missing dressing wrapper; do not rewrite a foreign hierarchy.");
            var targets = new List<KeyValuePair<Transform, Renderer[]>>();
            foreach (Transform wrapper in children)
            {
                bool cameraEntry = CameraOccluderEntries.Contains(sceneName + "/" + wrapper.name);
                CameraOccluder[] occluders = wrapper.GetComponents<CameraOccluder>();
                if (GameObjectUtility.GetStaticEditorFlags(wrapper.gameObject) != 0 ||
                    wrapper.GetComponents<Component>().Any(c => !(c is Transform) && !(cameraEntry && c is CameraOccluder)) ||
                    occluders.Length > 1 || (requireRegistered && cameraEntry && occluders.Length != 1))
                    throw new InvalidDataException("Only a named structural wrapper may own one camera query: " + wrapper.name);
                if (wrapper.childCount != 1 || wrapper.GetChild(0).name != "Model")
                    throw new InvalidDataException("Dressing wrapper must retain its single pure Model child: " + wrapper.name);
                ValidatePureVisual(wrapper.GetChild(0).gameObject);
                Renderer[] renderers = wrapper.GetComponentsInChildren<Renderer>(true)
                    .OrderBy(r => StableRendererPath(r.transform, wrapper), StringComparer.Ordinal).ToArray();
                if (renderers.Length == 0) throw new InvalidDataException("No drawn renderer for camera query: " + wrapper.name);
                if (occluders.Length == 1)
                {
                    if (!occluders[0].enabled) throw new InvalidDataException("Named camera query must be enabled: " + wrapper.name);
                    var array = new SerializedObject(occluders[0]).FindProperty("_renderers");
                    if (array == null || !array.isArray) throw new InvalidDataException("Camera renderer cache contract is missing.");
                    // Empty/partial local caches may be repaired; never overwrite foreign references.
                    for (int i = 0; i < array.arraySize; i++)
                    {
                        var renderer = array.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
                        if (renderer == null || !renderers.Contains(renderer))
                            throw new InvalidDataException("Camera cache contains null/foreign renderer: " + wrapper.name);
                    }
                    if (requireRegistered && !RendererReferencesMatch(array, renderers))
                        throw new InvalidDataException("Camera renderer coverage/order must be exact: " + wrapper.name);
                }
                if (cameraEntry) targets.Add(new KeyValuePair<Transform, Renderer[]>(wrapper, renderers));
            }
            return targets.ToArray();
        }

        static bool RendererReferencesMatch(SerializedProperty array, Renderer[] renderers)
        {
            if (array == null || !array.isArray || array.arraySize != renderers.Length) return false;
            for (int i = 0; i < renderers.Length; i++)
                if (array.GetArrayElementAtIndex(i).objectReferenceValue != renderers[i]) return false;
            return true;
        }

        static string StableRendererPath(Transform node, Transform wrapper)
        {
            var parts = new List<string>();
            while (node != wrapper)
            {
                parts.Add(node.GetSiblingIndex().ToString("D4") + ":" + node.name);
                node = node.parent;
            }
            parts.Reverse();
            return string.Join("/", parts);
        }

        public static void ValidatePureVisual(GameObject root)
        {
            if (!root.GetComponentsInChildren<MeshFilter>(true).Any(f => f.sharedMesh != null && f.sharedMesh.vertexCount > 0 &&
                f.GetComponent<MeshRenderer>() != null)) throw new InvalidDataException("Static art must contain real drawn mesh geometry.");
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0)
                    throw new InvalidDataException("Dressing may not become navigation/baked static: " + t.name);
                foreach (var component in t.GetComponents<Component>())
                    if (!(component is Transform) && !(component is MeshFilter) && !(component is MeshRenderer))
                        throw new InvalidDataException("Unexpected component on static art: " + t.name);
            }
        }

        static GameObject EnsureStaticPrefab(string model, string palette)
        {
            Folder(AssetRoot); Folder(AssetRoot + "/Prefabs"); Folder(AssetRoot + "/Materials");
            string path = AssetRoot + "/Prefabs/P_" + model + "_" + palette + ".prefab";
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (existing != null) { ValidatePureVisual(existing); return existing; }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source(model));
            if (source == null || source.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length != 0)
                throw new InvalidDataException("Missing/nonstatic selected source: " + model);
            var scene = EditorSceneManager.NewPreviewScene();
            GameObject wrapper = null;
            try
            {
                wrapper = new GameObject("P_" + model + "_" + palette); SceneManager.MoveGameObjectToScene(wrapper, scene);
                CloneMeshes(source.transform, wrapper.transform, Material(palette));
                ValidatePureVisual(wrapper); BoundsOf(wrapper);
                return PrefabUtility.SaveAsPrefabAsset(wrapper, path);
            }
            finally { if (wrapper != null) Object.DestroyImmediate(wrapper); EditorSceneManager.ClosePreviewScene(scene); }
        }

        static void CloneMeshes(Transform source, Transform parent, Material material)
        {
            var node = new GameObject(source.name).transform; node.SetParent(parent, false);
            node.localPosition = source.localPosition; node.localRotation = source.localRotation; node.localScale = source.localScale;
            var filter = source.GetComponent<MeshFilter>(); var renderer = source.GetComponent<MeshRenderer>();
            if (filter != null && renderer != null && filter.sharedMesh != null)
            {
                node.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
                var copy = node.gameObject.AddComponent<MeshRenderer>();
                copy.sharedMaterials = Enumerable.Repeat(material, filter.sharedMesh.subMeshCount).ToArray();
                copy.shadowCastingMode = ShadowCastingMode.On; copy.receiveShadows = true;
            }
            foreach (Transform child in source) CloneMeshes(child, node, material);
        }

        static Material Material(string palette)
        {
            string path = AssetRoot + "/Materials/M_Dressing_" + palette + ".mat";
            var result = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (result != null) return result;
            var original = AssetDatabase.LoadAssetAtPath<Material>(M6EnvironmentSetup.AssetRoot + "/M_KayKit_Courtyard.mat");
            if (original == null || original.shader.name != "Universal Render Pipeline/Lit")
                throw new InvalidDataException("Reviewed production URP material missing.");
            result = new Material(original) { name = "M_Dressing_" + palette };
            Color tint = palette == "Camp" ? new Color(.74f, .81f, .87f) : palette == "Warden" ? new Color(.65f, .69f, .72f) :
                palette == "Banner" ? new Color(.88f, .91f, .95f) : new Color(.86f, .83f, .75f);
            result.SetColor("_BaseColor", tint); result.SetColor("_Color", tint); result.DisableKeyword("_EMISSION");
            result.SetColor("_EmissionColor", Color.black);
            // Imported banner cloth has one-sided triangles. Both faces are decorative;
            // use a project-owned two-sided material, never mutate the source mesh/material.
            if (palette == "Banner") result.SetFloat("_Cull", (float)CullMode.Off);
            result.SetFloat("_Metallic", 0); result.SetFloat("_Smoothness", .10f);
            AssetDatabase.CreateAsset(result, path); AssetDatabase.SaveAssetIfDirty(result); return result;
        }

        public static Bounds BoundsOf(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<MeshRenderer>(true);
            if (renderers.Length == 0) throw new InvalidDataException("Empty imported art: " + root.name);
            var bounds = renderers[0].bounds;
            foreach (var r in renderers.Skip(1)) bounds.Encapsulate(r.bounds);
            if (bounds.size.sqrMagnitude < .000001f) throw new InvalidDataException("Empty imported bounds.");
            return bounds;
        }
        static string Source(string model) => M6EnvironmentSetup.KitRoot + "fbx/" + model + ".fbx";
        static bool Finite(Vector3 v) => !float.IsNaN(v.x) && !float.IsNaN(v.y) && !float.IsNaN(v.z) &&
            !float.IsInfinity(v.x) && !float.IsInfinity(v.y) && !float.IsInfinity(v.z);
        static float maxComponent(this Vector3 v) => Mathf.Max(v.x, Mathf.Max(v.y, v.z));
        static void Folder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            var parent = path.Substring(0, path.LastIndexOf('/')); Folder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
