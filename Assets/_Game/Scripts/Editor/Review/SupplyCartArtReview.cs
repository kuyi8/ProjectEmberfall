using System;
using System.IO;
using Emberfall.Editor.Setup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Neutral-light model shortlist, not formal-camera/gameplay acceptance.</summary>
    public static class SupplyCartArtReview
    {
        public const string Source = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/07_Environment_Ruins/Medieval_Village_MegaKit/FBX/Prop_Wagon.fbx";
        public const string StallSource = "Assets/ThirdParty/Quaternius/EmberfallArtBaseline/FantasyProps/Exports/FBX/Stall_Cart_Empty.fbx";
        public static Bounds PlaceModel(GameObject model, Vector3 center, float yaw)
        {
            // FBX roots carry the authored axis conversion. Add world yaw, never replace it.
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0, yaw, 0) * model.transform.rotation);
            Bounds bounds = ModelBounds(model);
            model.transform.localScale *= 3f / Mathf.Max(bounds.size.x, bounds.size.z);
            bounds = ModelBounds(model);
            model.transform.position += center - new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            return ModelBounds(model);
        }
        public static Bounds ModelBounds(GameObject model)
        {
            var renderers = model.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) throw new InvalidOperationException("Model has no visible geometry.");
            Bounds result = renderers[0].bounds;
            foreach (Renderer renderer in renderers) result.Encapsulate(renderer.bounds);
            return result;
        }
        /// <summary>Same existing camp light/camera projection, equal distance, no authored scene mutation.</summary>
        public static string CompareInCamp()
        {
            var original = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || original.isDirty ||
                original.path != AshApproachEncounterSetup.ScenePath)
                throw new InvalidOperationException("Use the clean idle main scene.");
            var review = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            RenderTexture target = null; Texture2D image = null;
            RenderTexture previous = RenderTexture.active;
            try
            {
                var wagon = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Source));
                var stall = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(StallSource));
                PlaceModel(wagon, new Vector3(4.3f, .02f, 4), 0);
                PlaceModel(stall, new Vector3(6.7f, .02f, 4), 90);
                var camera = new GameObject("SupplyCart_ControlledComparisonCamera").AddComponent<Camera>();
                if (Camera.main != null) camera.CopyFrom(Camera.main);
                camera.enabled = false;
                camera.transform.position = new Vector3(5.5f, 3.2f, -2.8f);
                camera.transform.LookAt(new Vector3(5.5f, 1.05f, 4));
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = true;
                target = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
                target.Create(); camera.targetTexture = target; camera.Render();
                RenderTexture.active = target;
                image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
                string folder = Path.GetFullPath("Builds/ArtReview/SupplyCart/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
                Directory.CreateDirectory(folder);
                string path = folder + "/camp-comparison.png";
                File.WriteAllBytes(path, image.EncodeToPNG());
                File.WriteAllText(folder + "/scope.txt", "Left=Prop_Wagon; right=Stall_Cart_Empty. Same main-scene lighting, production Camera projection and equal-distance controlled view; no natural input/readability, collision or performance acceptance. Both longest XZ dimensions normalized to3m. Original scene/assets untouched.");
                return path;
            }
            finally
            {
                RenderTexture.active = previous;
                if (image != null) Object.DestroyImmediate(image);
                if (target != null) { target.Release(); Object.DestroyImmediate(target); }
                EditorSceneManager.CloseScene(review, true); SceneManager.SetActiveScene(original);
            }
        }
        public static string Capture(bool stall = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Preview needs idle Editor; user scene is preserved.");
            string source = stall ? StallSource : Source;
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(source);
            if (asset == null) throw new InvalidOperationException("Missing local licensed wagon.");
            string folder = Path.GetFullPath("Builds/ArtReview/SupplyCart/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            var preview = new PreviewRenderUtility();
            GameObject model = null; Texture2D image = null; RenderTexture target = null;
            RenderTexture previous = RenderTexture.active;
            bool opened = false;
            try
            {
                model = Object.Instantiate(asset); model.name = "Wagon_NeutralPreview";
                preview.AddSingleGO(model);
                Bounds bounds = model.GetComponentsInChildren<Renderer>()[0].bounds;
                foreach (Renderer renderer in model.GetComponentsInChildren<Renderer>()) bounds.Encapsulate(renderer.bounds);
                preview.camera.fieldOfView = 40;
                preview.camera.nearClipPlane = .03f; preview.camera.farClipPlane = 30;
                preview.camera.backgroundColor = new Color(.19f, .24f, .25f);
                preview.camera.clearFlags = CameraClearFlags.SolidColor;
                preview.camera.transform.position = bounds.center + new Vector3(5, 3.2f, 5);
                preview.camera.transform.LookAt(bounds.center);
                preview.lights[0].intensity = 1.1f;
                preview.lights[0].transform.rotation = Quaternion.Euler(35, 30, 0);
                preview.lights[1].intensity = .6f;
                preview.lights[1].transform.rotation = Quaternion.Euler(340, 200, 0);
                preview.ambientColor = Color.gray;
                preview.BeginPreview(new Rect(0, 0, 960, 640), GUIStyle.none); opened = true;
                preview.Render(true, false);
                Texture texture = preview.EndPreview(); opened = false;
                target = RenderTexture.GetTemporary(960, 640, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(texture, target); RenderTexture.active = target;
                image = new Texture2D(960, 640, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, 960, 640), 0, 0); image.Apply();
                string path = folder + "/wagon-neutral.png";
                File.WriteAllBytes(path, image.EncodeToPNG());
                File.WriteAllText(folder + "/scope.txt", "Neutral imported URP model at native metre scale; no runtime input, rig, collision, scene or performance acceptance. Source=" + source + "\nBounds=" + bounds);
                return path;
            }
            finally
            {
                if (opened) preview.EndPreview(); RenderTexture.active = previous;
                if (target != null) RenderTexture.ReleaseTemporary(target);
                if (image != null) Object.DestroyImmediate(image);
                preview.Cleanup();
            }
        }
    }
}
