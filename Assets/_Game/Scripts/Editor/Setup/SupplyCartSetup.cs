using System;
using System.IO;
using System.Linq;
using Emberfall.Application.Flow;
using Emberfall.Editor.Content;
using Emberfall.Editor.Review;
using Emberfall.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>Additive optional camp reward; original geometry/NavMesh/prefabs stay immutable.</summary>
    public static class SupplyCartSetup
    {
        public const string RootName = "Content_AbandonedSupplyCart_v1";
        public const int PresentationRevision = 2;
        [MenuItem("Emberfall/Content/Add Abandoned Supply Cart")]
        public static string Apply()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || scene.isDirty ||
                scene.path != AshApproachEncounterSetup.ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Use the clean idle main route; unsaved user work is preserved.");
            var existing = scene.GetRootGameObjects().SingleOrDefault(g => g.name == RootName);
            if (existing != null)
            {
                if (existing.GetComponentsInChildren<RouteEnrichmentInteractable>().Single().Kind != RouteEnrichmentInteractionKind.SupplyCart ||
                    existing.GetComponentsInChildren<MeshCollider>().Length == 0)
                    throw new InvalidOperationException("Partial cart authoring preserved; inspect before repair.");
                return "Existing supply cart preserved; no regeneration.";
            }
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(SupplyCartArtReview.Source);
            var interactionSource = GameObject.Find("RouteChoice_Supply");
            var flow = Object.FindObjectOfType<M2RouteFlowController>();
            var net = Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true);
            if (source == null || interactionSource == null || flow == null || net == null)
                throw new InvalidOperationException("Missing established sources; no mutation.");
            string backup = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "/pre-supply-cart.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(scene.path, backup, false); File.Copy(scene.path + ".meta", backup + ".meta", false);
            var root = new GameObject(RootName);
            try
            {
                var model = Object.Instantiate(source, root.transform); model.name = "SupplyCart_VisualAndCollision";
                Bounds bounds = SupplyCartArtReview.PlaceModel(model, new Vector3(5.5f, .02f, 4), 0);
                if (bounds.max.x >= 7.7f || bounds.min.x <= 4.3f || bounds.max.z >= 7.5f || bounds.min.z <= 1.5f)
                    throw new InvalidOperationException("Cart leaves the approved camp side space: " + bounds);
                foreach (var filter in model.GetComponentsInChildren<MeshFilter>())
                {
                    if (filter.sharedMesh == null) continue;
                    var collider = filter.gameObject.AddComponent<MeshCollider>(); collider.sharedMesh = filter.sharedMesh;
                    collider.convex = false; collider.isTrigger = false;
                }
                var interaction = Object.Instantiate(interactionSource, root.transform);
                interaction.name = "SupplyCart_Interaction"; interaction.transform.position = new Vector3(3.85f, 0, 4);
                interaction.GetComponent<RouteEnrichmentInteractable>().Configure(flow, RouteEnrichmentInteractionKind.SupplyCart,
                    interaction.transform.Find("InteractionMarker").GetComponent<Renderer>(), interaction.GetComponentInChildren<M2QuestHighlightPresenter>());
                ConfigurePresentation(root);
                EnsureLightData(root);
                var serialized = new SerializedObject(net);
                var roots = serialized.FindProperty("_offlineActorRoots"); roots.InsertArrayElementAtIndex(roots.arraySize);
                roots.GetArrayElementAtIndex(roots.arraySize - 1).objectReferenceValue = root;
                var behaviours = serialized.FindProperty("_offlineBehaviours");
                foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true))
                {
                    behaviours.InsertArrayElementAtIndex(behaviours.arraySize);
                    behaviours.GetArrayElementAtIndex(behaviours.arraySize - 1).objectReferenceValue = behaviour;
                }
                serialized.ApplyModifiedPropertiesWithoutUndo();
                M4BuiltinContentPackager.BuildBuiltinPackage(); EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Cart save failed; preserve backup " + backup);
                return "Camp cart authored; exact mesh collision and accessible separate trigger; backup=" + backup;
            }
            catch { EditorSceneManager.MarkSceneDirty(scene); throw; }
        }

        public static string ApplyPresentation()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || scene.isDirty ||
                scene.path != AshApproachEncounterSetup.ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Use the clean idle main route.");
            var root = GameObject.Find(RootName);
            if (root == null) throw new InvalidOperationException("Author the cart first; no regeneration.");
            var interaction = root.transform.Find("SupplyCart_Interaction");
            if (!interaction.Find("InteractionMarker").GetComponent<Renderer>().enabled &&
                root.transform.Find("SupplyCart_VisualAndCollision").GetComponentInChildren<Renderer>().sharedMaterial.name == "M_SupplyCart_WornWood")
                return "Existing cart presentation preserved; no writes.";
            string backup = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "/pre-cart-presentation.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(scene.path, backup, false); File.Copy(scene.path + ".meta", backup + ".meta", false);
            ConfigurePresentation(root); EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Presentation save failed; preserve " + backup);
            return "Owned muted wood and cart-mounted rune; no model/collision/interaction-authority change; backup=" + backup;
        }
        public static string RepairNetworkIsolation()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || scene.isDirty ||
                scene.path != AshApproachEncounterSetup.ScenePath || SceneManager.sceneCount != 1)
                throw new InvalidOperationException("Use the clean idle main route; preserve unsaved work.");
            var root = GameObject.Find(RootName);
            var net = Object.FindObjectOfType<NetworkEmberValleyModeAdapter>(true);
            if (root == null || net == null) throw new InvalidOperationException("Existing cart/adapter required; no regeneration.");
            var serialized = new SerializedObject(net); var behaviours = serialized.FindProperty("_offlineBehaviours");
            var registered = Enumerable.Range(0, behaviours.arraySize).Select(i => behaviours.GetArrayElementAtIndex(i).objectReferenceValue).ToArray();
            if (root.GetComponentsInChildren<Light>(true).All(l => l.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>() != null) &&
                root.GetComponentsInChildren<Behaviour>(true).All(b => registered.Contains(b))) return "Cart isolation already complete; no writes.";
            string backup = Path.GetFullPath("Builds/SceneBackups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "/pre-cart-isolation.unity");
            Directory.CreateDirectory(Path.GetDirectoryName(backup)); File.Copy(scene.path, backup, false); File.Copy(scene.path + ".meta", backup + ".meta", false);
            EnsureLightData(root);
            foreach (var behaviour in root.GetComponentsInChildren<Behaviour>(true).Where(b => !registered.Contains(b)))
            {
                behaviours.InsertArrayElementAtIndex(behaviours.arraySize);
                behaviours.GetArrayElementAtIndex(behaviours.arraySize - 1).objectReferenceValue = behaviour;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo(); EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Isolation save failed; preserve " + backup);
            return "Missing actual cart behaviours appended; original prefix retained; backup=" + backup;
        }
        static void EnsureLightData(GameObject root)
        {
            foreach (var light in root.GetComponentsInChildren<Light>(true))
                if (light.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>() == null)
                    light.gameObject.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalLightData>();
        }
        private static void ConfigurePresentation(GameObject root)
        {
            const string folder = "Assets/_Game/Art/SupplyCart";
            if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder("Assets/_Game/Art", "SupplyCart");
            const string path = folder + "/M_SupplyCart_WornWood.mat";
            var wood = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (wood == null)
            {
                var original = AssetDatabase.LoadAssetAtPath<GameObject>(SupplyCartArtReview.Source).GetComponentInChildren<Renderer>().sharedMaterial;
                wood = new Material(original) { name = "M_SupplyCart_WornWood" };
                wood.SetColor("_BaseColor", new Color(.55f, .64f, .72f)); wood.SetColor("_Color", new Color(.55f, .64f, .72f));
                wood.SetFloat("_Smoothness", .1f); AssetDatabase.CreateAsset(wood, path);
            }
            foreach (var renderer in root.transform.Find("SupplyCart_VisualAndCollision").GetComponentsInChildren<Renderer>())
                renderer.sharedMaterials = renderer.sharedMaterials.Select(_ => wood).ToArray();
            var interaction = root.transform.Find("SupplyCart_Interaction");
            interaction.Find("InteractionMarker").GetComponent<Renderer>().enabled = false;
            var highlight = interaction.GetComponentInChildren<M2QuestHighlightPresenter>();
            var data = new SerializedObject(highlight);
            var core = (Renderer)data.FindProperty("_core").objectReferenceValue;
            var ring = (Renderer)data.FindProperty("_ring").objectReferenceValue;
            var light = (Light)data.FindProperty("_light").objectReferenceValue;
            core.transform.localPosition = new Vector3(1.65f, 1.54f, 0);
            ring.transform.localPosition = new Vector3(1.65f, .035f, 0);
            light.transform.localPosition = new Vector3(1.65f, 1.35f, 0);
            highlight.Configure(core, ring, light);
        }
    }
}
