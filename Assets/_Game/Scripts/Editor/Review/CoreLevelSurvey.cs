using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.AI.Unity;
using Emberfall.Editor.Setup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Emberfall.Editor.Review
{
    /// <summary>Read-only authored scene survey. Never calls setup, saves assets, or bakes navigation.</summary>
    public static class CoreLevelSurvey
    {
        private const string ScenePath = "Assets/_Game/Scenes/10_EmberValley.unity";
        [Serializable] public sealed class Node
        {
            public string path, category, segment, face, prefab;
            public bool active, marker, solid, trigger, cameraOccluder;
            public Vector3 position, euler, scale, boxCenter, boxSize;
            public string[] components, meshes, materials;
            public Vector3 visualCenter, visualSize;
        }
        [Serializable] public sealed class View
        {
            public string name;
            public Vector3 position, lookAt;
            public float fov = 55;
        }
        [Serializable] public sealed class Report
        {
            public string createdUtc, scene, sceneSha256, physicsSha256;
            public string scope = "Authored Edit Mode baseline, not gameplay or R4 traversal. No scene save, no setup, no NavMesh bake. All nodes included for exception routing; no automatic change authorization.";
            public bool sceneBytesUnchanged, physicsUnchanged;
            public Node[] nodes;
            public View[] views;
        }
        public static string Hash(byte[] bytes)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "");
        }
        private static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        public static void Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Edit Mode with graphics required.");
            if (!UnityEngine.Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var previous = EditorSceneManager.GetSceneManagerSetup();
            var bytes = File.ReadAllBytes(ScenePath);
            string output = "Builds/ArtReview/0.9.6-core-survey-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(output);
            try
            {
                var scene = EditorSceneManager.OpenScene(ScenePath);
                string physics = M6BoundaryArtSetup.CapturePhysics(scene);
                var all = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Transform>(true)).ToArray();
                var nodes = all.Where(x => x.GetComponent<Collider>() != null || x.GetComponent<Renderer>() != null ||
                    x.GetComponent<EncounterBoundaryVisualMarker>() != null || x.GetComponent<CombatEncounterCoordinator>() != null)
                    .Select(Describe).OrderBy(x => x.path, StringComparer.Ordinal).ToArray();
                var views = new[] {
                    Shot("00-overview", new Vector3(52,65,-8), new Vector3(18,0,34), 55),
                    Shot("01-camp-entry", new Vector3(0,3,-6), new Vector3(0,1,15), 60),
                    Shot("02-forest-entry", new Vector3(0,3,23), new Vector3(0,1,37), 60),
                    Shot("03-courtyard", new Vector3(18,3,40), new Vector3(30,1,45), 60),
                    Shot("04-left-corridor", new Vector3(22,5,54), new Vector3(12,1,44), 60),
                    Shot("05-right-rear", new Vector3(27,4,43), new Vector3(39,1,31), 60),
                    Shot("06-warden-separation", new Vector3(37,4,33), new Vector3(33,1,20), 60),
                    Shot("07-courtyard-overview", new Vector3(22,23,25), new Vector3(29,0,44), 55)
                };
                foreach (var view in views) Render(view, output);
                var report = new Report { createdUtc=DateTime.UtcNow.ToString("O"), scene=ScenePath,
                    sceneSha256=Hash(bytes), physicsSha256=Hash(System.Text.Encoding.UTF8.GetBytes(physics)), nodes=nodes, views=views,
                    sceneBytesUnchanged=bytes.SequenceEqual(File.ReadAllBytes(ScenePath)),
                    physicsUnchanged=physics == M6BoundaryArtSetup.CapturePhysics(scene) };
                File.WriteAllText(output + "/physics-baseline.json", physics);
                File.WriteAllText(output + "/survey.json", JsonUtility.ToJson(report, true));
                if (!report.sceneBytesUnchanged || !report.physicsUnchanged) throw new InvalidOperationException("Survey mutated source state.");
                Debug.Log($"[CORE_SURVEY] output={output} nodes={nodes.Length} views={views.Length} sourceUnchanged=true");
            }
            finally
            {
                if (previous.Any(x => x.isLoaded && x.isActive)) EditorSceneManager.RestoreSceneManagerSetup(previous);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
        private static Node Describe(Transform t)
        {
            var marker = t.GetComponent<EncounterBoundaryVisualMarker>();
            var box = t.GetComponent<BoxCollider>();
            var collider = t.GetComponent<Collider>();
            var renderers = t.GetComponentsInChildren<Renderer>(true);
            var bounds = renderers.Length == 0 ? new Bounds(t.position, Vector3.zero) : renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return new Node { path=PathOf(t), active=t.gameObject.activeInHierarchy,
                category=marker != null ? "encounter-boundary" : t.name.StartsWith("GateBlocker_") ? "protected-gate" :
                    t.GetComponent<CombatEncounterCoordinator>() != null ? "encounter" : collider != null ? "collider-object" : "visual",
                marker=marker != null, segment=marker != null ? marker.Segment : "", face=marker != null ? marker.Face.ToString() : "",
                solid=collider != null && !collider.isTrigger, trigger=collider != null && collider.isTrigger,
                cameraOccluder=t.GetComponents<Component>().Any(x => x != null && x.GetType().Name.Contains("CameraOccluder")),
                position=t.position, euler=t.eulerAngles, scale=t.lossyScale,
                boxCenter=box != null ? box.center : Vector3.zero, boxSize=box != null ? box.size : Vector3.zero,
                components=t.GetComponents<Component>().Where(x => x != null).Select(x => x.GetType().Name).ToArray(),
                meshes=t.GetComponentsInChildren<MeshFilter>(true).Select(x => AssetDatabase.GetAssetPath(x.sharedMesh)).Distinct().ToArray(),
                materials=renderers.SelectMany(x => x.sharedMaterials).Where(x => x != null).Select(AssetDatabase.GetAssetPath).Distinct().ToArray(),
                prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(t.gameObject), visualCenter=bounds.center, visualSize=bounds.size };
        }
        private static View Shot(string name, Vector3 position, Vector3 lookAt, float fov) =>
            new View { name=name, position=position, lookAt=lookAt, fov=fov };
        private static void Render(View view, string output)
        {
            var cameraObject = new GameObject("Temporary core survey camera");
            var camera = cameraObject.AddComponent<Camera>();
            if (Camera.main != null) camera.CopyFrom(Camera.main);
            camera.enabled=false; camera.fieldOfView=view.fov; camera.nearClipPlane=.05f; camera.farClipPlane=200;
            camera.transform.position=view.position; camera.transform.LookAt(view.lookAt);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing=true;
            var target=new RenderTexture(1280,720,24,RenderTextureFormat.ARGB32);
            var image=new Texture2D(1280,720,TextureFormat.RGB24,false);
            var prior=RenderTexture.active;
            try
            {
                target.Create(); camera.targetTexture=target; camera.Render();
                RenderTexture.active=target; image.ReadPixels(new Rect(0,0,1280,720),0,0); image.Apply();
                File.WriteAllBytes(output+"/"+view.name+".png",image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture=null; RenderTexture.active=prior; target.Release();
                UnityEngine.Object.DestroyImmediate(image); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(cameraObject);
            }
        }
    }
}
