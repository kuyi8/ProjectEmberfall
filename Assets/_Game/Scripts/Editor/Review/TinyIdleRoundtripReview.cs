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
    /// <summary>One fixed FBX route, isolated from production. Partial success is failure.</summary>
    public static class TinyIdleRoundtripReview
    {
        const string Source = "Assets/RPG Tiny Hero Duo/Animation/SwordAndShield/Idle_Battle_SwordAndShiled.fbx";
        const string SourceGuid = "0308cf4e83cf517488b60af58b290fe0";
        const string Prefab = "Assets/_Game/Art/Review/TinyHero/P_Review_TinyHero_Polyart.prefab";
        const string EvidenceBase = "Builds/ArtReview/tiny-idle-roundtrip/";
        const string AssetBase = "Assets/_Game/Art/Review/TinyHero/BlenderRoundtrip/";
        [Serializable] public sealed class Bone { public string name, parent, path; }
        [Serializable] public sealed class FileRow { public string path, sha256; public long length; }
        [Serializable] public sealed class Tolerances
        {
            public double positionMeters = .001, rotationDegrees = .1, worldScale = .00001,
                timeSeconds = .0005, heightMeters = .001;
            public int sampleRate = 120;
        }
        [Serializable] public sealed class Contract
        {
            public int schema = 1;
            public string projectRoot, outputDir, sourceFbx, sourceMeta, sourceFbxSha256, sourceMetaSha256,
                sourceGuid, createdUtc, expectedVersion = "5.2.2 LTS",
                exportPresetName = "tiny-idle-original-axis-v1";
            public Bone[] expectedBones;
            public string[] expectedMeshes;
            public Tolerances tolerances = new Tolerances();
            public bool allTolerancesFatal = true;
            public float fps = 30;
        }
        [Serializable] public sealed class Pose
        {
            public string path;
            public Vector3 position, scale;
            public Quaternion rotation;
        }
        [Serializable] public sealed class Sample
        {
            public float time;
            public Pose[] bones;
            public Vector3 meshMin, meshMax;
        }
        [Serializable] public sealed class Baseline
        {
            public string avatarGuid, avatarPath, clipName, referenceAsset;
            public string comparison = "Byte-identical original FBX COPY and exported derivative, BOTH CompressionOff/CopyFromOtherAvatar. Production original/meta remain immutable. Compressed original playback is diagnostic only.";
            public long avatarLocalId;
            public float length, timeOrigin;
            public string[] genericBindings;
            public Pose[] importedFbxRest;
            public Sample[] prefabPlayback, compressedOriginalPlayback, rawEndpoints;
        }
        [Serializable] public sealed class Result
        {
            public string scope = "Imported FBX rest and original-Avatar, SAME review prefab at explicit i/120, endpoints and next-loop sample. Not PlayerLoop/input/contact/death or production acceptance; original Light2/3 hips assertions remain OPEN and unchanged.";
            public string label, stage, failure, importedAsset, avatarGuid;
            public bool passed, protectedBytesUnchanged, avatarIdentityExact, boneChainExact, genericBindingsExact;
            public int protectedFiles, boneCount, samples;
            public float restPositionMax, restAngleMax, restScaleMax, playbackPositionMax,
                playbackAngleMax, playbackScaleMax, heightErrorMax, durationError, timeOriginError;
            public float originalLoopPosition, originalLoopAngle, candidateLoopPosition, candidateLoopAngle;
            public string[] missingBones, extraBones, missingBindings, extraBindings;
        }

        public static string RequestPrepare(string label)
        {
            Idle(); Label(label);
            Directory.CreateDirectory(EvidenceBase);
            string request = EvidenceBase + label + "-prepare-request.json";
            if (File.Exists(request)) return "Already requested; inspect contract/prepare-failure: " + label;
            Write(request, new Result { label = label, stage = "prepare-queued-once" });
            QueuePrepare(label);
            return "Prepare queued once: " + label;
        }

        public static string ResumeUnstartedPrepare(string label)
        {
            Idle(); Label(label);
            if (Directory.Exists(EvidenceBase + label)) return "Already started; never repeat: " + label;
            Require(File.Exists(EvidenceBase + label + "-prepare-request.json"), "No original request to resume.");
            string marker = EvidenceBase + label + "-prepare-update-request.json";
            if (File.Exists(marker)) return "Update route already queued; inspect files.";
            // Remove ONLY our exact old delayed callback, never any editor/user callbacks.
            if (EditorApplication.delayCall != null)
                foreach (var callback in EditorApplication.delayCall.GetInvocationList())
                {
                    var target = callback.Target;
                    var field = target?.GetType().GetField("label", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                    if (callback.Method.DeclaringType?.FullName.StartsWith(typeof(TinyIdleRoundtripReview).FullName, StringComparison.Ordinal) == true &&
                        field != null && Equals(field.GetValue(target), label))
                        EditorApplication.delayCall -= (EditorApplication.CallbackFunction)callback;
                }
            Write(marker, new Result { label = label, stage = "unstarted-delay-resumed-via-update-once" });
            QueuePrepare(label);
            return "Unstarted prepare resumed once via Editor update.";
        }

        static void QueuePrepare(string label)
        {
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                EditorApplication.update -= tick;
                try { Prepare(label); }
                catch (Exception e)
                {
                    Write(EvidenceBase + label + "-prepare-callback-failure.json", new Result { label = label, stage = "prepare-callback-failed", failure = e.ToString() });
                    Debug.LogWarning("[TINY_ROUNDTRIP_PREPARE] " + e);
                }
            };
            EditorApplication.update += tick;
        }

        public static string Prepare(string label)
        {
            Idle(); Label(label);
            string dir = Path.GetFullPath(EvidenceBase + label);
            if (Directory.Exists(dir)) throw new InvalidOperationException("Create-only run already exists: " + dir);
            if (AssetDatabase.AssetPathToGUID(Source) != SourceGuid) throw new InvalidOperationException("Original Avatar GUID mismatch.");
            Directory.CreateDirectory(dir);
            var frozen = FreezePaths().Select(p => new FileRow { path = p, sha256 = Hash(p), length = new FileInfo(p).Length }).ToArray();
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Source), scene);
                var boneRoot = model.transform.Find("root");
                var bones = Bones(model);
                if (boneRoot == null || bones.Length != 43) throw new InvalidOperationException("Expected original 43-bone root subtree.");
                var avatar = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<Avatar>().Single();
                var clip = Clip(Source);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), scene);
                var animator = go.GetComponentInChildren<Animator>(true);
                if (animator.avatar != avatar || !avatar.isHuman || !avatar.isValid) throw new InvalidOperationException("Review must use exact original valid Avatar.");
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(avatar, out string guid, out long localId);
                var contract = new Contract { projectRoot = Path.GetFullPath("."), outputDir = dir,
                    sourceFbx = Source, sourceMeta = Source + ".meta", sourceFbxSha256 = Hash(Source),
                    sourceMetaSha256 = Hash(Source + ".meta"), sourceGuid = guid,
                    createdUtc = DateTime.UtcNow.ToString("O"), expectedBones = bones,
                    expectedMeshes = model.GetComponentsInChildren<Renderer>(true).Select(r => r.name).OrderBy(n => n, StringComparer.Ordinal).ToArray() };
                string referenceAsset = AssetBase + label + "/Original_Idle_Uncompressed.fbx";
                Folder(AssetBase + label);
                if (File.Exists(referenceAsset)) throw new InvalidOperationException("Uncompressed reference is create-only.");
                File.Copy(Source, referenceAsset, false);
                AssetDatabase.ImportAsset(referenceAsset, ImportAssetOptions.ForceSynchronousImport);
                var referenceImporter = (ModelImporter)AssetImporter.GetAtPath(referenceAsset);
                var originalImporter = (ModelImporter)AssetImporter.GetAtPath(Source);
                Configure(referenceImporter, originalImporter, avatar, originalImporter.clipAnimations.Single().firstFrame,
                    originalImporter.clipAnimations.Single().lastFrame, referenceImporter.defaultClipAnimations.Single().takeName, clip.name);
                referenceImporter.SaveAndReimport();
                Require(Hash(referenceAsset) == Hash(Source), "Reference FBX bytes differ from original.");
                var referenceClip = Clip(referenceAsset);
                var baseline = new Baseline { avatarGuid = guid, avatarLocalId = localId, avatarPath = Source, referenceAsset = referenceAsset,
                    clipName = clip.name, length = clip.length, timeOrigin = AnimationUtility.GetAnimationClipSettings(clip).startTime,
                    genericBindings = Bindings(referenceClip), importedFbxRest = Rest(model) };
                Require(Mathf.Abs(referenceClip.length - clip.length) <= contract.tolerances.timeSeconds, "Reference copy time differs.");
                baseline.prefabPlayback = Playback(go, referenceClip, Times(clip.length));
                baseline.rawEndpoints = RawEndpoints(scene, referenceClip);
                var originalGo = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), scene);
                baseline.compressedOriginalPlayback = Playback(originalGo, clip, Times(clip.length));
                Write(dir + "/baseline.json", baseline);
                Write(dir + "/contract.json", contract);
                RequireProtected(frozen);
                Write(dir + "/protected-before.json", new Frozen { files = frozen.Concat(new[] { referenceAsset, referenceAsset + ".meta" }
                    .Select(p => new FileRow { path = p, sha256 = Hash(p), length = new FileInfo(p).Length })).ToArray() });
                return dir;
            }
            catch (Exception e) { Write(dir + "/prepare-failure.json", new Result { label = label, stage = "prepare-failed", failure = e.ToString() }); throw; }
            finally { EditorSceneManager.ClosePreviewScene(scene); }
        }

        // Persist a duplicate guard BEFORE queueing work. A transport retry cannot import twice.
        public static string RequestValidate(string label)
        {
            Idle(); Label(label);
            string dir = Path.GetFullPath(EvidenceBase + label);
            string request = dir + "/unity-request.json";
            if (File.Exists(request)) return "Already requested; inspect retained unity-result.json: " + dir;
            if (!File.Exists(dir + "/contract.json") || !File.Exists(dir + "/Idle_Roundtrip.fbx"))
                throw new InvalidOperationException("Prepared contract and single Blender export required.");
            using (var stream = new FileStream(request, FileMode.CreateNew))
            using (var writer = new StreamWriter(stream)) writer.Write("{\"label\":\"" + label + "\",\"status\":\"queued\"}");
            EditorApplication.CallbackFunction tick = null;
            tick = () =>
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
                EditorApplication.update -= tick;
                Validate(label);
            };
            EditorApplication.update += tick;
            return "Queued once: " + dir;
        }

        static void Validate(string label)
        {
            string dir = Path.GetFullPath(EvidenceBase + label);
            var result = new Result { label = label, stage = "starting", importedAsset = AssetBase + label + "/Idle_Roundtrip.fbx" };
            Scene preview = default;
            FileRow[] frozen = null;
            try
            {
                Idle();
                var contract = Read<Contract>(dir + "/contract.json");
                var baseline = Read<Baseline>(dir + "/baseline.json");
                frozen = Read<Frozen>(dir + "/protected-before.json").files;
                result.protectedFiles = frozen.Length;
                RequireProtected(frozen);
                // No authoring after a fatal Blender/native-time or export-content failure.
                string blenderResult = dir + "/blender-result.json";
                if (!File.Exists(blenderResult)) throw new InvalidOperationException("Missing completed Blender record.");
                var blender = Read<BlenderGate>(blenderResult);
                if (!blender.exportCompleted || !blender.acceptedNativeGate) throw new InvalidOperationException("Blender/native-time/export-content gate failed; do not import.");
                Require(string.Equals(blender.contractSha256, Hash(dir + "/contract.json"), StringComparison.OrdinalIgnoreCase), "Blender contract hash mismatch.");
                Require(string.Equals(blender.scriptSha256, Hash("Tools/Art/TinyIdleRoundtrip.py"), StringComparison.OrdinalIgnoreCase), "Blender helper changed after export.");
                var exported = blender.artifacts.Single(a => Path.GetFileName(a.path) == "Idle_Roundtrip.fbx");
                Require(new FileInfo(dir + "/Idle_Roundtrip.fbx").Length == exported.bytes &&
                    string.Equals(exported.sha256, Hash(dir + "/Idle_Roundtrip.fbx"), StringComparison.OrdinalIgnoreCase), "Export FBX no longer matches sole completed attempt.");
                Folder(AssetBase + label);
                if (File.Exists(result.importedAsset)) throw new InvalidOperationException("Import derivative is create-only.");
                File.Copy(dir + "/Idle_Roundtrip.fbx", result.importedAsset, false);
                Require(Hash(result.importedAsset) == Hash(dir + "/Idle_Roundtrip.fbx"), "Unity import copy bytes differ.");
                AssetDatabase.ImportAsset(result.importedAsset, ImportAssetOptions.ForceSynchronousImport);
                var importer = (ModelImporter)AssetImporter.GetAtPath(result.importedAsset);
                var original = (ModelImporter)AssetImporter.GetAtPath(Source);
                var avatar = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<Avatar>().Single();
                var take = importer.defaultClipAnimations.Single();
                // Do NOT replace default exported take endpoints with original 0..20: that could hide trimming/time drift.
                float rawRate = Clip(result.importedAsset).frameRate;
                float rawOrigin = take.firstFrame / rawRate;
                result.durationError = Mathf.Abs((take.lastFrame - take.firstFrame) / rawRate - baseline.length);
                result.timeOriginError = Mathf.Abs(rawOrigin - baseline.timeOrigin);
                Require(result.durationError <= contract.tolerances.timeSeconds && result.timeOriginError <= contract.tolerances.timeSeconds, "Raw imported time extent/origin changed.");
                Configure(importer, original, avatar, take.firstFrame, take.lastFrame, take.takeName, baseline.clipName);
                importer.SaveAndReimport();
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(importer.sourceAvatar, out string actualGuid, out long actualId);
                result.avatarGuid = actualGuid;
                result.avatarIdentityExact = importer.sourceAvatar == avatar && actualGuid == baseline.avatarGuid && actualId == baseline.avatarLocalId;
                Require(result.avatarIdentityExact, "CopyFromOtherAvatar identity changed.");
                preview = EditorSceneManager.NewPreviewScene();
                var model = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(result.importedAsset), preview);
                var candidateBones = Bones(model);
                result.boneCount = candidateBones.Length;
                var expected = contract.expectedBones.Select(b => b.path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                var actual = candidateBones.Select(b => b.path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
                result.missingBones = expected.Except(actual).ToArray(); result.extraBones = actual.Except(expected).ToArray();
                result.boneChainExact = expected.SequenceEqual(actual) && candidateBones.Length == 43 &&
                    candidateBones.All(b => contract.expectedBones.Any(o => o.path == b.path && o.name == b.name && o.parent == b.parent));
                Write(dir + "/candidate-imported-rest.json", new RestRecord { bones = candidateBones, poses = Rest(model) });
                Require(result.boneChainExact, "43 bone names/parent/relative paths changed; wrapper pollution is fatal.");
                Compare(baseline.importedFbxRest, Rest(model), out result.restPositionMax, out result.restAngleMax, out result.restScaleMax);
                Require(result.restPositionMax <= contract.tolerances.positionMeters && result.restAngleMax <= contract.tolerances.rotationDegrees && result.restScaleMax <= contract.tolerances.worldScale, "Imported FBX rest changed.");
                var clip = Clip(result.importedAsset);
                result.durationError = Mathf.Max(result.durationError, Mathf.Abs(clip.length - baseline.length));
                result.timeOriginError = Mathf.Max(result.timeOriginError, Mathf.Abs(AnimationUtility.GetAnimationClipSettings(clip).startTime - baseline.timeOrigin));
                Require(result.durationError <= contract.tolerances.timeSeconds && result.timeOriginError <= contract.tolerances.timeSeconds, "Final humanoid clip time extent/origin changed.");
                var bindings = Bindings(clip);
                result.missingBindings = baseline.genericBindings.Except(bindings).ToArray();
                result.extraBindings = bindings.Except(baseline.genericBindings).ToArray();
                result.genericBindingsExact = baseline.genericBindings.SequenceEqual(bindings);
                Write(dir + "/candidate-bindings.json", new Strings { values = bindings });
                Require(result.genericBindingsExact, "Generic transform bindings changed.");
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), preview);
                var playback = Playback(go, clip, baseline.prefabPlayback.Select(s => s.time).ToArray());
                Write(dir + "/candidate-playback.json", new Samples { values = playback });
                result.samples = playback.Length;
                for (int i = 0; i < playback.Length; i++)
                {
                    Compare(baseline.prefabPlayback[i].bones, playback[i].bones, out float p, out float r, out float s);
                    result.playbackPositionMax = Mathf.Max(result.playbackPositionMax, p);
                    result.playbackAngleMax = Mathf.Max(result.playbackAngleMax, r);
                    result.playbackScaleMax = Mathf.Max(result.playbackScaleMax, s);
                    result.heightErrorMax = Mathf.Max(result.heightErrorMax,
                        Mathf.Abs((playback[i].meshMax.y - playback[i].meshMin.y) - (baseline.prefabPlayback[i].meshMax.y - baseline.prefabPlayback[i].meshMin.y)));
                }
                var rawEndpoints = RawEndpoints(preview, clip);
                Write(dir + "/candidate-nonloop-endpoints.json", new Samples { values = rawEndpoints });
                Compare(baseline.rawEndpoints[0].bones, baseline.rawEndpoints[1].bones, out result.originalLoopPosition, out result.originalLoopAngle, out _);
                Compare(rawEndpoints[0].bones, rawEndpoints[1].bones, out result.candidateLoopPosition, out result.candidateLoopAngle, out _);
                for (int i = 0; i < 2; i++)
                {
                    Compare(baseline.rawEndpoints[i].bones, rawEndpoints[i].bones, out float p, out float r, out float s);
                    result.playbackPositionMax = Mathf.Max(result.playbackPositionMax, p);
                    result.playbackAngleMax = Mathf.Max(result.playbackAngleMax, r);
                    result.playbackScaleMax = Mathf.Max(result.playbackScaleMax, s);
                }
                Require(result.playbackPositionMax <= contract.tolerances.positionMeters && result.playbackAngleMax <= contract.tolerances.rotationDegrees && result.playbackScaleMax <= contract.tolerances.worldScale && result.heightErrorMax <= contract.tolerances.heightMeters, "Same-prefab fixed-time replay/height changed.");
                Require(result.candidateLoopPosition <= contract.tolerances.positionMeters && result.candidateLoopAngle <= contract.tolerances.rotationDegrees, "Loop endpoints do not close.");
                result.passed = true; result.stage = "passed-isolated-idle-only";
            }
            catch (Exception e) { result.passed = false; result.stage = "failed-stop-fixed-export-route"; result.failure = e.ToString(); Debug.LogWarning("[TINY_ROUNDTRIP] " + result.failure); }
            finally
            {
                if (preview.IsValid()) EditorSceneManager.ClosePreviewScene(preview);
                if (frozen != null)
                {
                    result.protectedBytesUnchanged = frozen.All(Same);
                    if (!result.protectedBytesUnchanged) { result.passed = false; result.stage = "protected-byte-failure"; }
                    Write(dir + "/protected-after.json", new Frozen { files = frozen.Select(f => new FileRow { path = f.path, length = File.Exists(f.path) ? new FileInfo(f.path).Length : -1, sha256 = File.Exists(f.path) ? Hash(f.path) : "missing" }).ToArray() });
                }
                Write(dir + "/unity-result.json", result);
            }
        }

        static Sample[] Playback(GameObject go, AnimationClip clip, float[] times)
        {
            var animator = go.GetComponentInChildren<Animator>(true);
            animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind(); animator.Update(0);
            var graph = PlayableGraph.Create("Tiny single-roundtrip explicit sampling");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator); output.SetSourcePlayable(playable); graph.Play();
                var rows = new List<Sample>();
                foreach (float time in times)
                {
                    playable.SetTime(time); graph.Evaluate(0);
                    // No transform anchoring/rescaling here: root and hips must enter the comparison.
                    var bounds = TinyHeroBakeoff.VertexBounds(go);
                    rows.Add(new Sample { time = time, bones = Poses(animator.transform), meshMin = bounds.min, meshMax = bounds.max });
                }
                return rows.ToArray();
            }
            finally { graph.Destroy(); }
        }

        static void Configure(ModelImporter importer, ModelImporter original, Avatar avatar, float firstFrame, float lastFrame, string takeName, string name)
        {
            // Equal lossless import settings on BOTH owned reference and exported derivative.
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
            importer.sourceAvatar = avatar; importer.globalScale = original.globalScale;
            importer.useFileScale = original.useFileScale; importer.resampleCurves = original.resampleCurves;
            importer.animationCompression = ModelImporterAnimationCompression.Off;
            importer.materialImportMode = ModelImporterMaterialImportMode.None;
            var source = original.clipAnimations.Single();
            var setting = new ModelImporterClipAnimation();
            // ModelImporterClipAnimation has private fields; JsonUtility is NOT a verified deep clone here.
            var properties = typeof(ModelImporterClipAnimation).GetProperties().Where(p => p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0).ToArray();
            foreach (var property in properties)
            {
                var value = property.GetValue(source);
                property.SetValue(setting, value is Array array ? array.Clone() : value);
            }
            foreach (var property in properties)
            {
                object a = property.GetValue(source), b = property.GetValue(setting);
                Require(a is Array array ? b is Array other && array.Cast<object>().SequenceEqual(other.Cast<object>()) : Equals(a, b),
                    "Clip setting copy differs: " + property.Name);
            }
            setting.takeName = takeName; setting.name = name; setting.firstFrame = firstFrame; setting.lastFrame = lastFrame;
            importer.clipAnimations = new[] { setting };
        }

        static Sample[] RawEndpoints(Scene preview, AnimationClip source)
        {
            var copy = Object.Instantiate(source);
            try
            {
                var settings = AnimationUtility.GetAnimationClipSettings(copy);
                settings.loopTime = false;
                AnimationUtility.SetAnimationClipSettings(copy, settings);
                var go = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(Prefab), preview);
                return Playback(go, copy, new[] { 0f, source.length });
            }
            finally { Object.DestroyImmediate(copy); }
        }

        static Bone[] Bones(GameObject model)
        {
            // Include a wrapper in relative paths rather than ignoring it by name.
            var root = model.GetComponentsInChildren<Transform>(true).SingleOrDefault(t => t.name == "root");
            if (root == null) return Array.Empty<Bone>();
            return root.GetComponentsInChildren<Transform>(true).Select(t => new Bone { name = t.name,
                parent = t == root ? "" : t.parent.name, path = AnimationUtility.CalculateTransformPath(t, model.transform) })
                .OrderBy(b => b.path, StringComparer.Ordinal).ToArray();
        }
        static Pose[] Rest(GameObject model)
        {
            var root = model.GetComponentsInChildren<Transform>(true).Single(t => t.name == "root");
            return root.GetComponentsInChildren<Transform>(true).Select(t => PoseOf(t, model.transform)).OrderBy(p => p.path, StringComparer.Ordinal).ToArray();
        }
        static Pose[] Poses(Transform animator) => animator.GetComponentsInChildren<Transform>(true).Select(t => PoseOf(t, animator)).OrderBy(p => p.path, StringComparer.Ordinal).ToArray();
        static Pose PoseOf(Transform t, Transform relative) => new Pose { path = AnimationUtility.CalculateTransformPath(t, relative), position = t.position, rotation = t.rotation, scale = t.lossyScale };
        static void Compare(Pose[] reference, Pose[] candidate, out float p, out float r, out float s)
        {
            Require(reference.Select(x => x.path).SequenceEqual(candidate.Select(x => x.path)), "Actual pose paths differ.");
            p = r = s = 0;
            for (int i = 0; i < reference.Length; i++)
            {
                Require(Finite(reference[i]) && Finite(candidate[i]), "Non-finite source/candidate transform.");
                p = Mathf.Max(p, Vector3.Distance(reference[i].position, candidate[i].position));
                r = Mathf.Max(r, Quaternion.Angle(reference[i].rotation, candidate[i].rotation));
                s = Mathf.Max(s, MaxAbs(reference[i].scale - candidate[i].scale));
            }
            Require(!float.IsNaN(p) && !float.IsInfinity(p) && !float.IsNaN(r) && !float.IsNaN(s), "Non-finite metrics.");
        }
        static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
        static bool Finite(Pose p) => new[] { p.position.x, p.position.y, p.position.z, p.rotation.x, p.rotation.y,
            p.rotation.z, p.rotation.w, p.scale.x, p.scale.y, p.scale.z }.All(v => !float.IsNaN(v) && !float.IsInfinity(v));
        static string[] Bindings(AnimationClip clip) => AnimationUtility.GetCurveBindings(clip).Where(b => b.type != typeof(Animator))
            .Select(b => b.path + "|" + b.type.FullName + "|" + b.propertyName).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        static float[] Times(float length)
        {
            var values = Enumerable.Range(0, Mathf.FloorToInt(length * 120) + 1).Select(i => i / 120f).ToList();
            if (Mathf.Abs(values.Last() - length) > .000001f) values.Add(length); else values[values.Count - 1] = length;
            values.Add(length + 1 / 120f); // fixed sample just past the cyclic seam
            return values.ToArray();
        }
        static IEnumerable<string> FreezePaths() => new[] { "Assets/RPG Tiny Hero Duo", "Assets/_Game/Art/Animations/Player", "Assets/_Game/Settings",
            "Assets/_Game/Scenes", "Assets/_Game/Prefabs/Characters", "Assets/_Game/Resources/Networking" }
            .SelectMany(p => Directory.GetFiles(p, "*", SearchOption.AllDirectories)).Concat(AssetDatabase.GetDependencies(Prefab, true))
            .Concat(Directory.GetFiles("Builds/Delivery/ProjectEmberfall_0.9.6-presentation-20261003-r4", "*", SearchOption.AllDirectories))
            .Concat(new[] { "Builds/Delivery/ProjectEmberfall_0.9.6-presentation-20261003-r4.zip" })
            .SelectMany(p => File.Exists(p + ".meta") ? new[] { p, p + ".meta" } : new[] { p }).Where(File.Exists).Distinct().OrderBy(p => p, StringComparer.Ordinal);
        static bool Same(FileRow f) => File.Exists(f.path) && new FileInfo(f.path).Length == f.length && Hash(f.path) == f.sha256;
        static void RequireProtected(FileRow[] frozen) => Require(frozen.All(Same), "Protected source/prefab/production bytes changed.");
        static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        static void Idle()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode && !EditorApplication.isCompiling && !EditorApplication.isUpdating, "Idle Editor required.");
            Require(UnityEngine.Application.dataPath.Replace('\\', '/').Equals("E:/unityproject/3DRPGdemo/Assets", StringComparison.OrdinalIgnoreCase), "Wrong project.");
            for (int i = 0; i < SceneManager.sceneCount; i++) Require(!SceneManager.GetSceneAt(i).isDirty, "Preserve dirty user scenes.");
        }
        static void Label(string label) => Require(!string.IsNullOrEmpty(label) && label.All(c => char.IsLetterOrDigit(c) || c == '-'), "Safe unique label required.");
        static AnimationClip Clip(string path) => AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().Single(c => !c.name.StartsWith("__preview"));
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        static void Folder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path).Replace('\\', '/'); Folder(parent); AssetDatabase.CreateFolder(parent, Path.GetFileName(path)); }
        static T Read<T>(string path) => JsonUtility.FromJson<T>(File.ReadAllText(path));
        static void Write(string path, object value) { using (var stream = new FileStream(path, FileMode.CreateNew)) using (var writer = new StreamWriter(stream)) writer.Write(JsonUtility.ToJson(value, true)); }
        [Serializable] sealed class Frozen { public FileRow[] files; }
        [Serializable] sealed class RestRecord { public Bone[] bones; public Pose[] poses; }
        [Serializable] sealed class Strings { public string[] values; }
        [Serializable] sealed class Samples { public Sample[] values; }
        [Serializable] sealed class BlenderGate { public bool exportCompleted, acceptedNativeGate; public string contractSha256, scriptSha256; public Artifact[] artifacts; }
        [Serializable] sealed class Artifact { public string path, sha256; public long bytes; }
    }
}
