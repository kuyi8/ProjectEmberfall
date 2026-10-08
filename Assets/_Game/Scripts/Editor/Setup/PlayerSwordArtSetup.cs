using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>Owned offline visual only; sockets, combat, enemies and original resources stay unchanged.</summary>
    public static class PlayerSwordArtSetup
    {
        public const string SourcePath = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/03_Character_Kit/KayKit_Adventurers/addons/kaykit_character_pack_adventures/Assets/fbx/sword_1handed.fbx";
        public const string PrefabPath = "Assets/_Game/Prefabs/Weapons/Player/P_Player_KayKitSword.prefab";
        public const string MaterialPath = "Assets/_Game/Art/Materials/Player/M_Player_KayKitSword.mat";
        public const string PreviousPrefabPath = "Assets/_Game/Prefabs/Weapons/M6Art/P_M6_Skeleton_ShortSword.prefab";
        public const string EquippedName = "Sword_M6_Player_Equipped";
        // Measured from the candidate's actual brown-wrapped grip vertices/atlas,
        // not borrowed from the previous sword. Centre the grip, not the pommel.
        public const float OverallLength = .98f, PommelInset = .20191313f;
        public const float GripMinimumY = -.07562728f, GripMaximumY = .07562728f;
        static readonly string[] ScenePaths = { "Assets/_Game/Scenes/10_EmberValley.unity", "Assets/_Game/Scenes/90_CombatGym.unity" };

        public static GameObject EnsurePrefab()
        {
            RequireIdle();
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (existing != null) { CorrectInitialCandidateGrip(existing); ValidateCanonical(existing); return existing; }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePath);
            if (source == null) throw new FileNotFoundException("Imported licensed sword required.", SourcePath);
            Material[] materials = source.GetComponentsInChildren<Renderer>(true).SelectMany(r => r.sharedMaterials).Distinct().ToArray();
            if (materials.Length != 1 || materials[0] == null) throw new InvalidDataException("Expected one persisted atlas material.");
            EnsureFolder(Path.GetDirectoryName(PrefabPath).Replace('\\', '/'));
            EnsureFolder(Path.GetDirectoryName(MaterialPath).Replace('\\', '/'));
            Material material = EnsureMaterial(materials[0]);
            Scene preview = EditorSceneManager.NewPreviewScene();
            var root = new GameObject("P_Player_KayKitSword");
            SceneManager.MoveGameObjectToScene(root, preview);
            try
            {
                var model = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject;
                if (model == null) throw new InvalidOperationException("Sword instantiate failed.");
                model.name = "Model";
                // This source is actually Y-aligned; never transplant the old FBX's 90-degree fix.
                model.transform.localRotation = Quaternion.identity;
                model.transform.localPosition = Vector3.zero;
                model.transform.localScale = Vector3.one;
                if (model.GetComponentsInChildren<Collider>(true).Length != 0 || model.GetComponentsInChildren<MonoBehaviour>(true).Length != 0)
                    throw new InvalidDataException("Source sword must already be render-only.");
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) Object.DestroyImmediate(animator);
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterials = new[] { material };
                Bounds bounds = LocalGeometryBounds(root.transform);
                if (bounds.size.y < Mathf.Max(bounds.size.x, bounds.size.z)) throw new InvalidDataException("Unexpected source axis.");
                model.transform.localScale *= OverallLength / bounds.size.y;
                bounds = LocalGeometryBounds(root.transform);
                model.transform.localPosition += new Vector3(-bounds.center.x, -PommelInset - bounds.min.y, -bounds.center.z);
                ValidateCanonical(root);
                var saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (saved == null) throw new IOException("Owned sword prefab could not be saved.");
                return saved;
            }
            finally { Object.DestroyImmediate(root); EditorSceneManager.ClosePreviewScene(preview); }
        }

        public static Bounds LocalGeometryBounds(Transform root)
        {
            Vector3[] points = LocalVertices(root);
            if (points.Length == 0) throw new InvalidDataException("Sword requires actual static mesh vertices.");
            var bounds = new Bounds(points[0], Vector3.zero);
            foreach (Vector3 point in points) bounds.Encapsulate(point);
            return bounds;
        }

        static void CorrectInitialCandidateGrip(GameObject existing)
        {
            Bounds bounds = LocalGeometryBounds(existing.transform);
            if (Mathf.Abs(bounds.min.y + PommelInset) < .001f) return;
            // Only migrate this batch's unshipped first candidate. Unexpected authored
            // edits fail closed; do not repeatedly search offsets or alter its mesh/size.
            if (Mathf.Abs(bounds.size.y - OverallLength) > .001f || Mathf.Abs(bounds.min.y + .11f) > .001f)
                throw new InvalidDataException("Unknown candidate dimensions; preserve user work.");
            string backup = "Builds/ArtReview/player-sword-comparison/grip-20261007-a1";
            Directory.CreateDirectory(backup);
            if (!File.Exists(backup + "/P_Player_KayKitSword.prefab.before"))
                File.Copy(PrefabPath, backup + "/P_Player_KayKitSword.prefab.before", false);
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Transform model = contents.transform.Find("Model");
                if (model == null || AssetDatabase.GetAssetPath(PrefabUtility.GetCorrespondingObjectFromSource(model.gameObject)) != SourcePath ||
                    Quaternion.Angle(model.localRotation, Quaternion.identity) > .01f)
                    throw new InvalidDataException("Actual imported candidate source/axis required.");
                model.localPosition += Vector3.up * (-PommelInset - LocalGeometryBounds(contents.transform).min.y);
                ValidateCanonical(contents); PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
        }
        static Vector3[] LocalVertices(Transform root) => root.GetComponentsInChildren<MeshFilter>(true)
            .SelectMany(f => f.sharedMesh.vertices.Select(v => root.InverseTransformPoint(f.transform.TransformPoint(v)))).ToArray();

        public static Vector3 LocalBladeTip(Transform root)
        {
            Vector3[] points = LocalVertices(root);
            float top = points.Max(v => v.y);
            // A real blade-tip mesh vertex, not an arbitrary farthest AABB corner of the guard.
            return points.Where(v => Mathf.Abs(v.y - top) < .0001f).OrderBy(v => v.x * v.x + v.z * v.z).First();
        }

        public static void ValidateCanonical(GameObject root)
        {
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 || root.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 ||
                root.GetComponentsInChildren<Animator>(true).Length != 0 || root.GetComponentsInChildren<MeshRenderer>(true).Length != 1)
                throw new InvalidDataException("Player sword must be one static render-only mesh.");
            Bounds bounds = LocalGeometryBounds(root.transform);
            if (Mathf.Abs(bounds.size.y - OverallLength) > .001f || Mathf.Abs(bounds.min.y + PommelInset) > .001f ||
                Mathf.Abs(bounds.center.x) > .001f || Mathf.Abs(bounds.center.z) > .001f)
                throw new InvalidDataException("Canonical whole-sword length/grip-axis contract failed.");
            if (root.GetComponentsInChildren<MeshRenderer>(true).Any(r => r.sharedMaterial == null || AssetDatabase.GetAssetPath(r.sharedMaterial) != MaterialPath))
                throw new InvalidDataException("Only the project-owned URP material is allowed.");
        }

        public static int ApplyToScene(Scene scene, GameObject prefab)
        {
            RequireIdle();
            if (!ScenePaths.Contains(scene.path)) throw new InvalidOperationException("Only authored offline player scenes.");
            var player = scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<PlayerCombatActor>(true)).Single();
            var sword = player.GetComponentsInChildren<Transform>(true).Single(t => t.name == EquippedName);
            Transform socket = sword.parent;
            if (socket.name != "WeaponSocket_RightHand" || socket.parent != player.GetComponentInChildren<Animator>(true).GetBoneTransform(HumanBodyBones.RightHand))
                throw new InvalidDataException("Preserve the actual authored right-hand socket.");
            var source = PrefabUtility.GetCorrespondingObjectFromSource(sword.gameObject);
            string path = AssetDatabase.GetAssetPath(source);
            int changed = 0;
            if (path != PrefabPath)
            {
                if (path != PreviousPrefabPath || !PrefabUtility.IsAnyPrefabInstanceRoot(sword.gameObject))
                    throw new InvalidDataException("Unexpected equipped sword; do not overwrite another rig or user edit.");
                if (sword.localPosition.sqrMagnitude > .000001f || Quaternion.Angle(sword.localRotation, Quaternion.identity) > .01f || (sword.localScale - Vector3.one).sqrMagnitude > .000001f)
                    throw new InvalidDataException("Existing canonical attachment must not be reinterpreted.");
                AssertNoExternalReferences(scene, sword);
                Vector3 position = sword.localPosition, scale = sword.localScale; Quaternion rotation = sword.localRotation;
                var replacement = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, socket)).transform;
                replacement.name = EquippedName; replacement.localPosition = position; replacement.localRotation = rotation; replacement.localScale = scale;
                M1ProjectSetup.SetLayerRecursively(replacement.gameObject, player.gameObject.layer);
                ValidateCanonical(replacement.gameObject);
                Object.DestroyImmediate(sword.gameObject); sword = replacement; changed++;
            }
            ValidateCanonical(sword.gameObject);
            var trail = player.GetComponent<SwordTrailPresenter>();
            var data = new SerializedObject(trail);
            Transform root = data.FindProperty("_bladeRoot").objectReferenceValue as Transform;
            Transform tip = data.FindProperty("_bladeTip").objectReferenceValue as Transform;
            if (root == null || tip == null || root.parent != socket.parent || tip.parent != socket.parent)
                throw new InvalidDataException("Existing right-hand trail anchors required; no new runtime writer.");
            Vector3 rootLocal = root.parent.InverseTransformPoint(sword.position);
            Vector3 tipLocal = tip.parent.InverseTransformPoint(sword.TransformPoint(LocalBladeTip(sword)));
            if ((root.localPosition - rootLocal).sqrMagnitude > 1e-10f) { root.localPosition = rootLocal; changed++; }
            if ((tip.localPosition - tipLocal).sqrMagnitude > 1e-10f) { tip.localPosition = tipLocal; changed++; }
            if (changed != 0) EditorSceneManager.MarkSceneDirty(scene);
            return changed;
        }

        static void AssertNoExternalReferences(Scene scene, Transform sword)
        {
            var members = new HashSet<Object>(sword.GetComponentsInChildren<Transform>(true).SelectMany(t => t.GetComponents<Component>().Cast<Object>().Append(t.gameObject)));
            foreach (Component component in scene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Component>(true)))
            {
                if (component == null || members.Contains(component)) continue;
                var iterator = new SerializedObject(component).GetIterator();
                while (iterator.Next(true))
                {
                    // The socket's structural child link is intentionally replaced;
                    // gameplay/component references to old gear are never waived.
                    if (component == sword.parent && iterator.propertyPath.StartsWith("m_Children.Array.data[", StringComparison.Ordinal)) continue;
                    if (iterator.propertyType == SerializedPropertyType.ObjectReference && members.Contains(iterator.objectReferenceValue))
                        throw new InvalidDataException("External sword reference must be migrated explicitly: " + component.name + "/" + iterator.propertyPath);
                }
            }
        }

        [MenuItem("Emberfall/Presentation/Apply Owned Player Sword")]
        public static string ApplyProject()
        {
            RequireIdle();
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve unsaved scene work.");
            GameObject prefab = EnsurePrefab();
            var previous = EditorSceneManager.GetSceneManagerSetup(); var rows = new List<string>();
            try
            {
                foreach (string path in ScenePaths)
                {
                    Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    int changed = ApplyToScene(scene, prefab);
                    if (changed != 0) EditorSceneManager.SaveScene(scene);
                    rows.Add(path + " changed=" + changed);
                }
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
            return string.Join("\n", rows);
        }

        static Material EnsureMaterial(Material original)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;
            var persisted = new SerializedObject(original); Color color = Color.white; Texture texture = null; Vector2 scale = Vector2.one, offset = Vector2.zero;
            var colors = persisted.FindProperty("m_SavedProperties.m_Colors"); bool baseColor = false;
            for (int i = 0; i < colors.arraySize; i++)
            {
                var pair = colors.GetArrayElementAtIndex(i); string key = pair.FindPropertyRelative("first").stringValue;
                if (key == "_BaseColor" || (key == "_Color" && !baseColor))
                { color = pair.FindPropertyRelative("second").colorValue; if (key == "_BaseColor") baseColor = true; }
            }
            var textures = persisted.FindProperty("m_SavedProperties.m_TexEnvs");
            bool baseMap = false;
            for (int i = 0; i < textures.arraySize; i++)
            {
                var pair = textures.GetArrayElementAtIndex(i); string key = pair.FindPropertyRelative("first").stringValue;
                if (key != "_BaseMap" && (key != "_MainTex" || baseMap)) continue;
                var value = pair.FindPropertyRelative("second"); texture = value.FindPropertyRelative("m_Texture").objectReferenceValue as Texture;
                scale = value.FindPropertyRelative("m_Scale").vector2Value; offset = value.FindPropertyRelative("m_Offset").vector2Value;
                if (key == "_BaseMap") baseMap = true;
            }
            if (texture == null) throw new InvalidDataException("Persisted source atlas required; no source material getters.");
            var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "M_Player_KayKitSword" };
            material.SetColor("_BaseColor", color); material.SetTexture("_BaseMap", texture); material.SetTextureScale("_BaseMap", scale); material.SetTextureOffset("_BaseMap", offset);
            material.SetFloat("_Metallic", .25f); material.SetFloat("_Smoothness", .38f);
            Type validator = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitShader")).First(t => t != null);
            ((ShaderGUI)Activator.CreateInstance(validator)).ValidateMaterial(material);
            AssetDatabase.CreateAsset(material, MaterialPath); AssetDatabase.SaveAssetIfDirty(material); return material;
        }
        static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Idle Editor authoring only.");
        }
        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = path.Substring(0, path.LastIndexOf('/')); EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, path.Substring(path.LastIndexOf('/') + 1));
        }
    }
}
