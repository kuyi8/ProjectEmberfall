using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>New Quaternius native-clip route, not a retry of the stopped KayKit/Tiny adaptation.</summary>
    [InitializeOnLoad]
    public static class KnightNativeEarlyScreening
    {
        private const string PendingKey = "Emberfall.KnightNativeScreening.Pending";
        private const string Source = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/01_Warden_Boss/Knight Character by @Quaternius/FBX/KnightCharacter.fbx";
        [Serializable] private sealed class Row
        {
            public string clip, image;
            public float time, floorRelativeMeshMinimumY, floorRelativeMeshMaximumY;
            public Vector3 leftFoot, rightFoot, hips;
        }
        [Serializable] private sealed class Result
        {
            public string status, error, output;
            public string scope = "Controlled native Roll entry-calibrated scale and CPU-baked static-pose renders, followed by same-calibration Idle; BakeMesh(true), drawn triangle vertices and original renderer matrix. NOT live GPU animation, neutral-rest size, natural input, toe contact, gameplay .52s Dodge, equipment/Throw/Heal or production replacement acceptance.";
            public string sourceBefore, sourceAfter, metaBefore, metaAfter;
            public bool originalsUnchanged;
            public float sourceRestHeight, uniformScale;
            public string[] nativeClipNames;
            public Row[] frames;
        }

        static KnightNativeEarlyScreening()
        {
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, ""))) EditorApplication.update += RunPending;
        }
        private static void RunPending()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
            string output = SessionState.GetString(PendingKey, "");
            if (string.IsNullOrEmpty(output)) { EditorApplication.update -= RunPending; return; }
            if (SceneManager.GetActiveScene().isDirty) return;
            SessionState.EraseString(PendingKey); EditorApplication.update -= RunPending;
            Capture(output);
        }

        public static string Request(string label)
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(label ?? "", "^[a-zA-Z0-9._-]+$")) throw new ArgumentException("Safe label required.");
            string output = "Builds/ArtReview/player-early-screening/" + label;
            string status = output + "/result.json";
            if (File.Exists(status)) return Path.GetFullPath(status);
            if (!string.IsNullOrEmpty(SessionState.GetString(PendingKey, "")))
                throw new InvalidOperationException("An early screening is already queued; preserve its job.");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Idle, saved Editor required.");
            Directory.CreateDirectory(output);
            File.WriteAllText(status, JsonUtility.ToJson(new Result { status = "queued", output = Path.GetFullPath(output) }, true));
            SessionState.SetString(PendingKey, output);
            EditorApplication.update -= RunPending; EditorApplication.update += RunPending;
            return Path.GetFullPath(status);
        }

        private static void Capture(string output)
        {
            Directory.CreateDirectory(output);
            var result = new Result { status = "running", output = Path.GetFullPath(output), sourceBefore = Hash(Source), metaBefore = Hash(Source + ".meta") };
            string status = output + "/result.json";
            File.WriteAllText(status, JsonUtility.ToJson(result, true));
            Scene original = SceneManager.GetActiveScene(); Scene review = default;
            PlayableGraph graph = default; Material material = null, floorMaterial = null;
            var rows = new List<Row>();
            try
            {
                review = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                SceneManager.SetActiveScene(review);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
                RenderSettings.ambientLight = new Color(.55f, .6f, .65f); RenderSettings.fog = false;
                var obj = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Source));
                var wrapper = new GameObject("NativeKnightReviewRoot");
                obj.transform.SetParent(wrapper.transform, false);
                var animator = obj.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                    throw new InvalidOperationException("Native candidate is not a valid Human rig.");
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind(); animator.Update(0);
                var clips = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
                result.nativeClipNames = clips.Select(c => c.name).ToArray();
                var roll = clips.Single(c => c.name.EndsWith("Roll_sword", StringComparison.Ordinal));
                graph = PlayableGraph.Create("Quaternius Knight early screening"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var idlePlayable = AnimationClipPlayable.Create(graph, roll); idlePlayable.SetApplyFootIK(false); idlePlayable.SetApplyPlayableIK(false);
                var animation = AnimationPlayableOutput.Create(graph, "Native", animator); animation.SetSourcePlayable(idlePlayable); graph.Play();
                idlePlayable.SetTime(0); graph.Evaluate(0);
                // Explicit standing Roll-entry calibration, then ALL poses retain this scale. Idle is also rendered
                // at that same calibration so a cross-clip mismatch cannot be hidden by independently resizing it.
                Bounds rest = BoundsOf(obj); result.sourceRestHeight = rest.size.y;
                float importedEnvelopeHeight = obj.GetComponentsInChildren<SkinnedMeshRenderer>().Max(s => s.bounds.size.y);
                if (rest.size.y <= .1f || float.IsNaN(rest.size.y) || rest.size.y > importedEnvelopeHeight * 1.5f)
                    throw new InvalidOperationException("Native entry geometry disagrees with its imported renderer envelope.");
                // Scale the independent parent, never the Animator's animated/imported root.
                result.uniformScale = 1.8f / rest.size.y; wrapper.transform.localScale *= result.uniformScale;
                // A single scale/rest calibration is recorded. Never readjust height for a sampled pose.
                rest = BoundsOf(obj); wrapper.transform.position += Vector3.up * -rest.min.y;
                Vector3 root = wrapper.transform.position;
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")); material.color = new Color(.35f, .43f, .51f);
                foreach (Renderer renderer in obj.GetComponentsInChildren<Renderer>()) renderer.sharedMaterials = Enumerable.Repeat(material, renderer.sharedMaterials.Length).ToArray();
                var floor = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(floor.GetComponent<Collider>());
                floor.transform.position = new Vector3(0, -.05f, 0); floor.transform.localScale = new Vector3(6, .1f, 6);
                floorMaterial = new Material(material); floorMaterial.color = new Color(.32f, .35f, .38f); floor.GetComponent<Renderer>().sharedMaterial = floorMaterial;
                var lamp = new GameObject("Screening Light").AddComponent<Light>(); lamp.type = LightType.Directional; lamp.intensity = 1f; lamp.transform.rotation = Quaternion.Euler(45, -35, 0);
                var camera = new GameObject("Screening Camera").AddComponent<Camera>(); camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.24f, .29f, .34f);
                camera.orthographic = true; camera.orthographicSize = 1.4f; camera.nearClipPlane = .03f; camera.farClipPlane = 30f;
                camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(review);
                camera.transform.position = new Vector3(3, 2.2f, 5); camera.transform.LookAt(new Vector3(0, .85f, 0));
                var playable = AnimationClipPlayable.Create(graph, roll); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                animation.SetSourcePlayable(playable);
                Vector3 anchor = animator.transform.localPosition; Quaternion rotation = animator.transform.localRotation;
                float[] times = { 0f, .15f, .30f, .45f, .65f, .85f, 1.05f, 1.24f };
                for (int i = 0; i < times.Length; i++)
                {
                    playable.SetTime(times[i]); graph.Evaluate(0); animator.transform.SetLocalPositionAndRotation(anchor, rotation);
                    Bounds bounds = BoundsOf(obj);
                    string image = output + "/roll-" + i.ToString("00") + ".png"; SaveCpuPose(obj, wrapper, floor, camera, image);
                    rows.Add(new Row { clip = roll.name, image = Path.GetFullPath(image), time = times[i],
                        floorRelativeMeshMinimumY = bounds.min.y, floorRelativeMeshMaximumY = bounds.max.y,
                        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot).position - root,
                        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot).position - root,
                        hips = animator.GetBoneTransform(HumanBodyBones.Hips).position - root });
                }
                var idle = clips.Single(c => c.name.EndsWith("|Idle", StringComparison.Ordinal));
                var comparison = AnimationClipPlayable.Create(graph, idle); comparison.SetApplyFootIK(false); comparison.SetApplyPlayableIK(false);
                animation.SetSourcePlayable(comparison); comparison.SetTime(.25); graph.Evaluate(0);
                animator.transform.SetLocalPositionAndRotation(anchor, rotation);
                Bounds idleBounds = BoundsOf(obj); string idleImage = output + "/idle-same-calibration.png"; SaveCpuPose(obj, wrapper, floor, camera, idleImage);
                rows.Add(new Row { clip = idle.name, image = Path.GetFullPath(idleImage), time = .25f,
                    floorRelativeMeshMinimumY = idleBounds.min.y, floorRelativeMeshMaximumY = idleBounds.max.y });
                result.status = "captured";
            }
            catch (Exception e) { result.status = "failed"; result.error = e.ToString(); }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
                if (review.IsValid() && review.isLoaded) EditorSceneManager.CloseScene(review, true);
                if (material != null) Object.DestroyImmediate(material); if (floorMaterial != null) Object.DestroyImmediate(floorMaterial);
                result.sourceAfter = Hash(Source); result.metaAfter = Hash(Source + ".meta");
                result.originalsUnchanged = result.sourceBefore == result.sourceAfter && result.metaBefore == result.metaAfter;
                result.frames = rows.ToArray(); File.WriteAllText(status, JsonUtility.ToJson(result, true));
            }
        }
        private static Bounds BoundsOf(GameObject obj)
        {
            bool first = true; Bounds bounds = default;
            foreach (var skin in obj.GetComponentsInChildren<SkinnedMeshRenderer>())
            {
                var mesh = new Mesh();
                try
                {
                    // Pairing used by the actual imported Warden: false double-counts its 100x skin scale.
                    // This candidate still needs its own live/contact validation; a corrected sampler is not acceptance.
                    skin.BakeMesh(mesh, true);
                    var vertices = mesh.vertices;
                    foreach (var index in mesh.triangles.Distinct())
                    { Vector3 world = skin.localToWorldMatrix.MultiplyPoint3x4(vertices[index]); if (first) { bounds = new Bounds(world, Vector3.zero); first = false; } else bounds.Encapsulate(world); }
                }
                finally { Object.DestroyImmediate(mesh); }
            }
            if (first) throw new InvalidOperationException("No actual skinned geometry.");
            return bounds;
        }
        private static void SaveCpuPose(GameObject obj, GameObject wrapper, GameObject floor, Camera camera, string path)
        {
            // The synchronous Editor loop can advance bones without advancing GPU skinning.
            // Draw explicit CPU snapshots in the original skin matrix; never relabel them live input.
            var copies = new List<GameObject>(); var meshes = new List<Mesh>();
            var skins = new List<SkinnedMeshRenderer>();
            Vector3 actorPosition = wrapper.transform.position, floorPosition = floor.transform.position, cameraPosition = camera.transform.position;
            try
            {
                foreach (var skin in obj.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    if (!skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                    var mesh = new Mesh { name = "NativeKnight_CPU_ReviewOnly" }; meshes.Add(mesh); skin.BakeMesh(mesh, true);
                    var child = new GameObject("CPU_ReviewSkin"); copies.Add(child); child.transform.SetParent(skin.transform, false);
                    child.AddComponent<MeshFilter>().sharedMesh = mesh;
                    var draw = child.AddComponent<MeshRenderer>(); draw.sharedMaterials = skin.sharedMaterials;
                    if (draw.localToWorldMatrix != skin.localToWorldMatrix) throw new InvalidOperationException("CPU review lost the original renderer matrix.");
                    skins.Add(skin); skin.enabled = false;
                }
                // Position only the owned render fixtures away from loaded production geometry.
                // All scale/contact metrics above are evaluated at the original origin, before this translation.
                Vector3 renderOffset = new Vector3(1000f, 0f, 1000f);
                wrapper.transform.position += renderOffset; floor.transform.position += renderOffset; camera.transform.position += renderOffset;
                Save(camera, path);
            }
            finally
            {
                wrapper.transform.position = actorPosition; floor.transform.position = floorPosition; camera.transform.position = cameraPosition;
                foreach (var skin in skins) if (skin != null) skin.enabled = true;
                foreach (var child in copies) if (child != null) Object.DestroyImmediate(child);
                foreach (var mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }
        private static void Save(Camera camera, string path)
        {
            var rt = RenderTexture.GetTemporary(720, 720, 24); var previous = RenderTexture.active; Texture2D texture = null;
            try
            {
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera, new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = rt }); RenderTexture.active = rt;
                texture = new Texture2D(720, 720, TextureFormat.RGB24, false); texture.ReadPixels(new Rect(0, 0, 720, 720), 0, 0); texture.Apply();
                File.WriteAllBytes(path, texture.EncodeToPNG());
            }
            finally { camera.targetTexture = null; RenderTexture.active = previous; RenderTexture.ReleaseTemporary(rt); if (texture != null) Object.DestroyImmediate(texture); }
        }
        private static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
