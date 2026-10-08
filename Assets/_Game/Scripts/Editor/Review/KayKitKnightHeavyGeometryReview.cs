using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    public static partial class KayKitNativeCharacterReview
    {
        const string HeavyGeometrySource = "Assets/_Game/Scripts/Editor/Review/KayKitKnightHeavyGeometryReview.cs";
        const string HeavyGeometryFolder = "Assets/_Game/Art/Review/KayKitHeroMotion/knight-heavy-charge-release-author-20261003-a1/";
        const string HeavyGeometryCharge = HeavyGeometryFolder + "A_Review_Knight_HeavyCharge_RiseHold.anim";
        const string HeavyGeometryRelease = HeavyGeometryFolder + "A_Review_Knight_HeavyRelease_Staged.anim";
        const string HeavyGeometryAuthorReport = "Builds/ArtReview/kaykit-motion/knight-heavy-charge-release-author-20261003-a1/result.json";
        static readonly string[] HeavyGeometryInputs =
        {
            "Builds/ArtReview/kaykit-input/20261003-143226-023-737aa018616542ff879130a1e4b4d0f8/heavy-input-review.json",
            "Builds/ArtReview/kaykit-input/20261003-143307-630-80dd27aaefaf45a3a6a78970f8cd5df0/heavy-input-review.json",
            "Builds/ArtReview/kaykit-input/20261003-143348-263-cdd92db97c1546a29072e187ab505359/heavy-input-review.json"
        };
        static bool heavyGeometryPending;

#pragma warning disable 0649 // Read-only DTOs are populated by JsonUtility from retained actual-input evidence.
        [Serializable] sealed class HeavyGeometryInputPose
        {
            public int frame; public string domain, currentState; public float elapsed, bodyMin;
            public double time; public bool transition, conservativeRigidFloor; public V root;
        }
        [Serializable] sealed class HeavyGeometryInputFrame { public HeavyGeometryInputPose pose; public float actualClipSeconds; public bool clipTimeMeasured; }
        [Serializable] sealed class HeavyGeometryInputFailure { public string gate; }
        [Serializable] sealed class HeavyGeometryRetainedFile { public string path, before, after; }
        [Serializable] sealed class HeavyGeometryRetainedTrace { public HeavyGeometryRetainedFile[] files; }
        [Serializable] sealed class HeavyGeometryInputRecord
        {
            public string test, status, chargeGuid, releaseGuid; public long chargeLocalId, releaseLocalId;
            public bool sourceAfter, memoryAfter, lifecyclePassed, coveragePassed, resourcePassed, damageWindowPassed, incrementPassed, hipsPassed;
            public V outerLocalPosition, outerLocalScale, innerLocalScale; public Q outerLocalRotation;
            public HeavyGeometryInputFrame[] frames; public HeavyGeometryInputFailure[] failures;
        }
#pragma warning restore 0649

        [Serializable] sealed class HeavyGeometryInputTarget
        {
            public string report, sha256, test, animatorState; public int frame, negativeFrames;
            public double actualTime; public float domainElapsed, clipSeconds, actualBodyMin, originalFixtureTranslationY;
            public bool actualAggregateConservativeRigidFloor;
            public string conservativeScope = "Retained actual flag is aggregate over all sampled renderers; it does NOT identify the lowest BODY renderer as conservative. New CPU per-renderer vertices classify that independently.";
            public V actorRoot, outerLocalPosition, outerLocalScale, innerLocalScale; public Q outerLocalRotation;
        }
        [Serializable] sealed class HeavyGeometryClip
        {
            public string role, asset, guid, name, sha256; public long localId;
            public float length, frameRate; public int bindings; public bool human, nonlooping;
        }
        [Serializable] sealed class HeavyGeometryJoint
        {
            public string name, path; public V localPosition, localScale, worldPosition, lossyScale; public Q localRotation, worldRotation;
        }
        [Serializable] sealed class HeavyGeometryLeg
        {
            public string side, bindingScope = "Exact preserved original Rig Transform paths; heelIK is NOT assumed to be an anatomical heel.";
            public HeavyGeometryJoint hip, knee, ankle, toe; public float thighLength, shankLength, ankleToeLength;
            public V ankleToToeDirection;
        }
        [Serializable] sealed class HeavyGeometryWeight
        {
            public int slot, boneIndex; public float weight; public bool contributes, boneIndexValid;
            public string bonePath; public HeavyGeometryJoint bone;
        }
        [Serializable] sealed class HeavyGeometryTriangle { public int submesh, triangle; public int[] vertices; }
        [Serializable] sealed class HeavyGeometryHeelToe
        {
            public string side, scope = "Drawn vertices with >=.5 actual foot/toe weight, projected on the preserved ankle-to-toe axis. Heel/toe are GEOMETRY EXTREMA candidates, NOT anatomically certified soles or contact.";
            public int cohortVertices, heelVertex, toeVertex; public V heelWorld, toeWorld, heelToToeDirection; public float axisTiltDegrees;
        }
        [Serializable] sealed class HeavyGeometryRendererFloor
        {
            public string renderer, category, meshAsset, meshName, meshGuid; public long meshLocalId;
            public int vertex, vertexCount, drawnVertices, equalHeightVertices; public float time, rawWorldY, existingBoundsFloorY, signedBoundsMinusVertexY, variableWeightSum;
            public bool skinned, readableSource, editorReadProbeComplete, allVariableWeightsMeasured, legacyFourSlotsMeasured;
            public V sourceVertex, bakedVertex, worldVertex; public float[] rendererLocalToWorld;
            public HeavyGeometryTriangle[] triangles; public HeavyGeometryWeight[] actualVariableWeights, rawLegacyFourSlots;
            public HeavyGeometryHeelToe[] heelToe;
        }
        [Serializable] sealed class HeavyGeometryMeshReadProbe
        {
            public string asset, name; public bool runtimeReadWriteFlag, editorNonPlayReadComplete, skinned;
            public int vertices, indices, variableInfluences, legacyFourSlotVertices;
        }
        [Serializable] sealed class HeavyGeometryFrame
        {
            public string role, exactActualReport; public float clipSeconds, analyticNativeSourceSeconds, bodyY0, capeY0, gearY0;
            public float rawAnimatorPositionDrift, rawAnimatorRotationDrift, originalFixtureTranslationY, predictedFixtureBodyY, signedPredictionMinusActual;
            public bool exactActualTime, analyticMapIsNotBetweenKeyPoseEquivalence = true;
            public HeavyGeometryRendererFloor[] renderers; public HeavyGeometryLeg[] legs;
        }
        [Serializable] sealed class HeavyGeometryMemoryRow
        {
            public string asset, name, type, beforeJsonSha, beforeCleanupJsonSha, afterCleanupJsonSha, beforeJson, warmSecondJson, beforeCleanupJson, afterCleanupJson;
            public int instanceId, jsonCharacters; public bool sameWarmFixedPoint, beforeCleanupProbeReached, aliveBeforeCleanup, aliveAfterCleanup, sameBeforeCleanup, sameAfterCleanup;
        }
        sealed class HeavyGeometryMemoryWatch { public Object original; public string json; public HeavyGeometryMemoryRow row; }
        [Serializable] sealed class HeavyGeometryImage { public string file, scope, view; public float clipSeconds; public V markedVertex, camera, target; }
        [Serializable] sealed class HeavyGeometryReport
        {
            public string label, directory, status, error, primaryError, protectionError, startedUtc, finishedUtc, module, sourceSha256;
            public string scope = "ONE zero-asset-write CPU geometry attribution for exact unaccepted owned Heavy a1 clips and three retained actual RMB failures. Original Knight Human Avatar/full rig, pure visual prefab .75 at Y0; no clone lift/warp, root reset, rescale, clip/importer/Runtime/Profile/production/r4 changes. Manual Playables ONLY in this isolated CPU preview, never actual input manipulation.";
            public string floorScope = "Raw CPU drawn triangle Y0 minima are independent of original actual Actor rootY+outerLocalY. That already-authored translation is computed as a DIAGNOSTIC column, never applied to the clone or used to grant the raw Y0 screen. Neither screen grants actual gameplay, contact, ground support, full ability or production acceptance.";
            public string samplingScope = "Charge .55 and Release .82 at 120Hz including endpoints, plus each exact retained nontransition worst Release clip time. Analytic native phase uses the frozen original stage map; between authored keys the clip is linearly resampled, so native pose equivalence is not asserted.";
            public string weightScope = "Every lowest index belongs to an ACTUALLY DRAWN triangle. Original variable influences and raw four legacy slots are recorded separately, including actual zero slots; rigid geometry has null weights, never fabricated zeros. isReadable is retained as the RUNTIME ReadWrite flag, not used to reject verified Editor non-Play reads. Failed/incomplete/nonfinite actual vertex/index/weight reads fail without renderer-bounds fallback or importer changes.";
            public string memoryScope = "Warm settings/curves/mesh weight data BEFORE two identical FULL EditorJsonUtility snapshots. Compare full strings before AND after preview cleanup. Report hashes/lengths for unchanged objects and retain full before/after strings for any change; cache differences are failures, not exemptions.";
            public bool warmCompleted, diskUnchangedByWarm, memoryWarmFixedPoint, samplesCompleted, imagesCompleted, sourcesUnchanged, sourceMemoryUnchanged, loadedScenesUnchanged, renderSettingsUnchanged, enabledSceneClosureUnchanged, candidateOutsideEnabledClosure;
            public bool geometryAttributionOnly = true, y0FloorAcceptanceGranted = false, actualFloorAcceptanceGranted = false, contactAccepted = false, abilityAccepted = false, productionAccepted = false;
            public int expectedChargeSamples = 67, expectedReleaseSamples = 100, actualWorstSamples = 3, memoryObjects;
            public float boundsVertexArithmeticTolerance = .000001f;
            public string boundsVertexScope = "1e-6m numeric cross-check ONLY: old Bounds.Encapsulate floor and exact lowest drawn vertex can round differently. Old floor math and signed>=0 gates are unchanged; neither floor uses this tolerance for acceptance.";
            public HeavyGeometryClip[] clips, nativeClips; public HeavyGeometryInputTarget[] actualTargets; public HeavyGeometryFrame[] frames;
            public HeavyGeometryImage[] images; public HeavyGeometryMemoryRow[] memory;
            public HeavyGeometryMeshReadProbe[] nativeMeshReadProbes;
            public FileRow[] beforeWarmDisk, protectedBefore, protectedAfter; public string[] enabledClosureBefore, enabledClosureAfter;
        }

        /// <summary>Queues ONE read-only CPU attribution; retained labels never resample or author assets.</summary>
        public static string RequestHeavyFloorAttribution(string uniqueLabel)
        {
            if (string.IsNullOrWhiteSpace(uniqueLabel) || !Regex.IsMatch(uniqueLabel, "^[A-Za-z0-9_-]{8,72}$")) throw new ArgumentException("Fresh ASCII label required.");
            string prefix = JobPrefix + "HeavyGeometry.", key = prefix + uniqueLabel, prior = SessionState.GetString(key, "");
            if (prior.Length != 0) { if (!File.Exists(prior)) throw new IOException("Retained Heavy attribution missing."); return prior; }
            RequireIdle(); if (pending || assembledPending || motionPending || throwPending || genericPending || heavyGeometryPending) throw new InvalidOperationException("One preview job at a time.");
            foreach (string activeKey in new[] { JobPrefix + "active", JobPrefix + "Assembled.active", JobPrefix + "Motion.active", JobPrefix + "Throw.active", JobPrefix + "Generic.active", prefix + "active" })
            {
                string active = SessionState.GetString(activeKey, ""); if (!File.Exists(active)) continue;
                var old = JsonUtility.FromJson<HeavyGeometryReport>(File.ReadAllText(active));
                if (old != null && (old.status == "queued" || old.status == "running")) throw new InvalidOperationException("Inspect retained pending preview: " + active);
            }
            string dir = Path.GetFullPath("Builds/ArtReview/kaykit-heavy-floor/" + uniqueLabel);
            if (Directory.Exists(dir)) throw new IOException("Fresh unique evidence directory required."); Directory.CreateDirectory(dir);
            string record = dir + "/report.json";
            var report = new HeavyGeometryReport { label = uniqueLabel, directory = dir, status = "queued", startedUtc = DateTime.UtcNow.ToString("O"),
                module = typeof(KayKitNativeCharacterReview).Assembly.ManifestModule.ModuleVersionId.ToString(), sourceSha256 = Hash(HeavyGeometrySource) };
            HeavyGeometryWrite(record, report); SessionState.SetString(key, record); SessionState.SetString(prefix + "active", record);
            SessionState.SetString(JobPrefix + "Throw.active", record); heavyGeometryPending = throwPending = true;
            double deadline = EditorApplication.timeSinceStartup + 180; EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= callback;
                try { RequireIdle(); if (Hash(HeavyGeometrySource) != report.sourceSha256) throw new IOException("Queued tool source changed."); report.status = "running"; HeavyGeometryWrite(record, report); CaptureHeavyFloorAttribution(report); report.status = "completed-attribution-only"; }
                catch (Exception error) { report.status = "failed"; report.error = error.ToString(); Debug.LogException(error); }
                finally { report.finishedUtc = DateTime.UtcNow.ToString("O"); HeavyGeometryWrite(record, report); heavyGeometryPending = throwPending = false; SessionState.EraseString(prefix + "active"); if (SessionState.GetString(JobPrefix + "Throw.active", "") == record) SessionState.EraseString(JobPrefix + "Throw.active"); }
            };
            EditorApplication.update += callback; return record;
        }

        static HeavyGeometryInputTarget[] HeavyGeometryReadActualTargets()
        {
            var targets = new List<HeavyGeometryInputTarget>(); var names = new[] { "OneEofHold", "SameDynamicBatch", "TenSecondHold" };
            for (int index = 0; index < HeavyGeometryInputs.Length; index++)
            {
                string path = HeavyGeometryInputs[index]; var input = JsonUtility.FromJson<HeavyGeometryInputRecord>(File.ReadAllText(path));
                if (input == null || input.test != names[index] || input.status != "incomplete-or-failed" || !input.sourceAfter || !input.memoryAfter ||
                    !input.lifecyclePassed || !input.coveragePassed || !input.resourcePassed || !input.damageWindowPassed || !input.incrementPassed || !input.hipsPassed ||
                    input.failures == null || input.failures.Length != 1 || input.failures[0].gate != "unchanged-signed-body-cape-gear-floor") throw new InvalidOperationException("Exact retained signed-floor-only failure required: " + path);
                if (input.chargeGuid != "a90efaf08e083a34a8c4815b1129da3f" || input.releaseGuid != "c0fb090c6cc7b3241bb7d7b04a96e3c8" || input.chargeLocalId != 7400000 || input.releaseLocalId != 7400000 ||
                    Vec(input.outerLocalPosition) != new Vector3(0, -1, 0) || Vec(input.outerLocalScale) != Vector3.one * .75f || Vec(input.innerLocalScale) != Vector3.one || Quat(input.outerLocalRotation) != Quaternion.identity) throw new InvalidOperationException("Retained original instance/clip identities changed.");
                string tracePath = Path.GetDirectoryName(path).Replace('\\', '/') + "/actual-input.json";
                var trace = JsonUtility.FromJson<HeavyGeometryRetainedTrace>(File.ReadAllText(tracePath));
                foreach (string frozenAsset in new[] { HeavyGeometryCharge, HeavyGeometryRelease, GenericPrefab, Source + "Knight.fbx" }.SelectMany(asset => new[] { asset, asset + ".meta" }))
                {
                    var retained = trace?.files?.SingleOrDefault(file => file.path.Replace('\\', '/') == frozenAsset);
                    if (retained == null || retained.before != retained.after || !File.Exists(frozenAsset) || retained.after != Hash(frozenAsset)) throw new IOException("Current candidate/native bytes do not match actual failing input: " + frozenAsset);
                }
                var bad = input.frames.Where(frame => frame.pose.bodyMin < 0).ToArray(); if (bad.Length == 0) throw new InvalidOperationException("No retained negative drawn sample.");
                var worst = bad.OrderBy(frame => frame.pose.bodyMin).First(); var first = input.frames[0].pose.root;
                if (bad.Any(frame => !frame.clipTimeMeasured || frame.pose.domain != "HeavyAttack" || frame.pose.currentState != "PlayerHeavyAttack" || frame.pose.transition) ||
                    input.frames.Any(frame => frame.pose.root.y != first.y) || !Finite(worst.actualClipSeconds) || worst.actualClipSeconds < 0 || worst.actualClipSeconds > .82f) throw new InvalidOperationException("Only same-height settled Release attribution is supported; no inferred blend pose.");
                targets.Add(new HeavyGeometryInputTarget { report = path, sha256 = Hash(path), test = input.test, animatorState = worst.pose.currentState,
                    frame = worst.pose.frame, negativeFrames = bad.Length, actualTime = worst.pose.time, domainElapsed = worst.pose.elapsed,
                    clipSeconds = worst.actualClipSeconds, actualBodyMin = worst.pose.bodyMin, actorRoot = first, outerLocalPosition = input.outerLocalPosition,
                    actualAggregateConservativeRigidFloor = worst.pose.conservativeRigidFloor,
                    outerLocalScale = input.outerLocalScale, outerLocalRotation = input.outerLocalRotation, innerLocalScale = input.innerLocalScale,
                    originalFixtureTranslationY = first.y + input.outerLocalPosition.y });
            }
            return targets.ToArray();
        }

        static void CaptureHeavyFloorAttribution(HeavyGeometryReport report)
        {
            Scene[] scenes = OriginalScenes(); string sceneMemory = SceneMemory(scenes), renderState = RenderState(); report.enabledClosureBefore = ThrowEntryClosure();
            var clips = new[] { Required<AnimationClip>(HeavyGeometryCharge), Required<AnimationClip>(HeavyGeometryRelease) };
            var nativeAssets = AssetDatabase.LoadAllAssetsAtPath(Source + "Knight.fbx");
            var idle = nativeAssets.OfType<AnimationClip>().Single(clip => clip.name == "Idle"); var chop = nativeAssets.OfType<AnimationClip>().Single(clip => clip.name == "1H_Melee_Attack_Chop");
            report.actualTargets = HeavyGeometryReadActualTargets();
            report.candidateOutsideEnabledClosure = new[] { GenericPrefab, HeavyGeometryCharge, HeavyGeometryRelease }.All(path => !report.enabledClosureBefore.Contains(path));
            if (!report.candidateOutsideEnabledClosure) throw new InvalidOperationException("Isolated candidate is in enabled production closure.");
            var extra = AssetDatabase.GetDependencies(GenericPrefab, true).Concat(clips.SelectMany(clip => AssetDatabase.GetDependencies(AssetDatabase.GetAssetPath(clip), true)))
                .Concat(report.enabledClosureBefore).Concat(new[] { GenericPrefab, HeavyGeometryCharge, HeavyGeometryRelease, HeavyGeometryFolder.TrimEnd('/') + ".meta", HeavyGeometrySource, HeavyGeometryAuthorReport,
                    "Assets/_Game/Scripts/Editor/Review/KayKitKnightHeavyMotionDerivation.cs", "Assets/_Game/Scripts/Editor/Review/KayKitHeroMotionDerivation.cs",
                    "Assets/_Game/Tests/PlayMode/KayKitKnightHeavyCandidateTests.cs", "Assets/_Game/Tests/PlayMode/KayKitKnightInputCandidateTests.cs", "Packages/manifest.json", "Packages/packages-lock.json" })
                .Concat(HeavyGeometryInputs).Concat(HeavyGeometryInputs.Select(path => Path.GetDirectoryName(path).Replace('\\', '/') + "/actual-input.json"))
                .Concat(Directory.GetFiles("Assets/_Game/Scripts", "*", SearchOption.AllDirectories).Select(path => path.Replace('\\', '/')).Where(path => path.IndexOf("/Editor/", StringComparison.Ordinal) < 0 && (path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".asmdef", StringComparison.Ordinal))))
                .Distinct().SelectMany(path => new[] { path, path + ".meta" }).Where(File.Exists);
            report.beforeWarmDisk = Protected().Concat(extra.Select(HeavyGeometryFile)).GroupBy(row => row.path, StringComparer.Ordinal).Select(group => group.First()).OrderBy(row => row.path, StringComparer.Ordinal).ToArray();
            var frames = new List<HeavyGeometryFrame>(); var images = new List<HeavyGeometryImage>(); HeavyGeometryMemoryWatch[] watches = null; Exception originalError = null;
            try
            {
                foreach (var clip in clips.Concat(new[] { idle, chop }))
                {
                    AnimationUtility.GetAnimationClipSettings(clip);
                    AnimationUtility.GetAnimationEvents(clip); AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip)) { var curve = AnimationUtility.GetEditorCurve(clip, binding); if (curve == null) throw new InvalidOperationException("Missing warm curve."); var keys = curve.keys; if (keys.Length == 0) throw new InvalidOperationException("Empty warm curve."); }
                }
                var nativeSkins = Required<GameObject>(Source + "Knight.fbx").GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(skin => skin.sharedMesh).ToArray();
                report.nativeMeshReadProbes = nativeAssets.OfType<Mesh>().Select(mesh => HeavyGeometryProbeEditorMesh(mesh, nativeSkins.Contains(mesh))).ToArray();
                report.clips = clips.Select((clip, index) => HeavyGeometryDescribeClip(clip, index == 0 ? "Charge" : "Release", index == 0 ? .55f : .82f, index == 0 ? "a90efaf08e083a34a8c4815b1129da3f" : "c0fb090c6cc7b3241bb7d7b04a96e3c8")).ToArray();
                report.nativeClips = new[] { idle, chop }.Select(clip =>
                {
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long id) || guid != "33196f9d403b6e44ca18f3182d28beb6" || !clip.humanMotion || clip.legacy || AnimationUtility.GetCurveBindings(clip).Length != 130 ||
                        (clip == idle && id != -3100369314251171874) ||
                        (clip == chop && (id != 3963595363326709879 || Mathf.Abs(clip.length - 32f / 30f) > 1e-6f))) throw new InvalidOperationException("Exact native Human source identity changed.");
                    return new HeavyGeometryClip { role = "NativeSourceOnly", asset = AssetDatabase.GetAssetPath(clip), name = clip.name, guid = guid, localId = id, sha256 = Hash(AssetDatabase.GetAssetPath(clip)), length = clip.length, frameRate = clip.frameRate, bindings = 130, human = true, nonlooping = !clip.isLooping };
                }).ToArray();
                var objects = report.beforeWarmDisk.Select(row => row.path).Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) && !path.EndsWith(".meta", StringComparison.Ordinal))
                    .SelectMany(AssetDatabase.LoadAllAssetsAtPath).Where(asset => asset != null && EditorUtility.IsPersistent(asset)).Distinct().ToArray();
                watches = objects.Select(asset => new HeavyGeometryMemoryWatch { original = asset, json = EditorJsonUtility.ToJson(asset), row = new HeavyGeometryMemoryRow
                    { asset = AssetDatabase.GetAssetPath(asset), name = asset.name, type = asset.GetType().FullName, instanceId = asset.GetInstanceID() } }).ToArray();
                foreach (var watch in watches)
                {
                    string second = EditorJsonUtility.ToJson(watch.original); watch.row.sameWarmFixedPoint = watch.json == second;
                    watch.row.jsonCharacters = watch.json.Length; watch.row.beforeJsonSha = HeavyGeometryStringHash(watch.json);
                    if (!watch.row.sameWarmFixedPoint) { watch.row.beforeJson = watch.json; watch.row.warmSecondJson = second; }
                }
                report.memoryWarmFixedPoint = watches.All(watch => watch.row.sameWarmFixedPoint); report.memoryObjects = watches.Length;
                report.warmCompleted = true; report.protectedBefore = report.beforeWarmDisk.Select(row => HeavyGeometryFile(row.path)).ToArray();
                report.diskUnchangedByWarm = HeavyGeometrySameFiles(report.beforeWarmDisk, report.protectedBefore);
                if (!report.memoryWarmFixedPoint || !report.diskUnchangedByWarm) throw new InvalidOperationException("Warm full JSON fixed point or source disk protection failed; no cache exemption.");
                using (var stage = new Stage())
                try
                {
                    var candidate = stage.preview.InstantiatePrefabInScene(Required<GameObject>(GenericPrefab)); stage.preview.camera.scene = candidate.scene;
                    if (candidate.transform.localPosition != Vector3.zero || candidate.transform.localRotation != Quaternion.identity || candidate.transform.localScale != Vector3.one * .75f ||
                        candidate.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 || candidate.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("Original .75/Y0 pure visual candidate required.");
                    var animator = candidate.GetComponentInChildren<Animator>(true); var original = Required<GameObject>(Source + "Knight.fbx").GetComponentInChildren<Animator>(true);
                    if (animator == null || original == null || animator.avatar != original.avatar || !animator.avatar.isHuman || !animator.avatar.isValid) throw new InvalidOperationException("Exact original Human Avatar required.");
                    foreach (string limb in Limbs)
                    {
                        var actual = animator.GetComponentsInChildren<Transform>(true).Single(transform => transform.name == limb); var source = original.GetComponentsInChildren<Transform>(true).Single(transform => transform.name == limb);
                        if (PathOf(actual, animator.transform) != PathOf(source, original.transform) || actual.GetSiblingIndex() != source.GetSiblingIndex() || actual.gameObject.activeSelf != source.gameObject.activeSelf ||
                            Vector3.Distance(actual.localPosition, source.localPosition) > 1e-6f || Vector3.Distance(actual.localScale, source.localScale) > 1e-6f || Quaternion.Angle(actual.localRotation, source.localRotation) > .001f) throw new InvalidOperationException("Original limb rest/path/order differs: " + limb);
                    }
                    stage.Materials(candidate);
                    foreach (var material in candidate.GetComponentsInChildren<Renderer>(true).SelectMany(renderer => renderer.sharedMaterials).Distinct())
                    { material.SetColor("_BaseColor", new Color(.66f, .68f, .7f)); material.SetTexture("_BaseMap", null); material.SetFloat("_Metallic", 0); material.SetFloat("_Smoothness", .1f); }
                    stage.Floor(Vector3.zero, new Vector3(8, .02f, 6));
                    using (var rig = new Rig(animator)) using (var drawn = new Drawn(candidate))
                    {
                        rig.Start(idle); rig.Pose(0); frames.Add(HeavyGeometryObserve("IdleBaseline", 0, 0, rig, drawn, animator, null));
                        for (int role = 0; role < 2; role++)
                        {
                            rig.Start(clips[role]); int steps = Mathf.CeilToInt(clips[role].length * 120f);
                            for (int index = 0; index <= steps; index++) { float time = Mathf.Min(index / 120f, clips[role].length); rig.Pose(time); frames.Add(HeavyGeometryObserve(role == 0 ? "Charge" : "Release", time, role == 0 ? time * .4f / .55f : HeavyGeometryNativeTime(time), rig, drawn, animator, null)); }
                        }
                        foreach (var actual in report.actualTargets)
                        {
                            rig.Start(clips[1]); rig.Pose(actual.clipSeconds); frames.Add(HeavyGeometryObserve("ExactActualWorst", actual.clipSeconds, HeavyGeometryNativeTime(actual.clipSeconds), rig, drawn, animator, actual));
                        }
                        report.samplesCompleted = frames.Count(frame => frame.role == "Charge") == report.expectedChargeSamples && frames.Count(frame => frame.role == "Release") == report.expectedReleaseSamples &&
                            frames.Count(frame => frame.role == "ExactActualWorst") == report.actualWorstSamples && frames.First(frame => frame.role == "Charge").clipSeconds == 0 && frames.Last(frame => frame.role == "Charge").clipSeconds == clips[0].length &&
                            frames.First(frame => frame.role == "Release").clipSeconds == 0 && frames.Last(frame => frame.role == "Release").clipSeconds == clips[1].length;
                        if (!report.samplesCompleted) throw new InvalidOperationException("Incomplete fixed full-duration sampling.");
                        var worst = frames.Where(frame => frame.role == "Release" || frame.role == "ExactActualWorst").OrderBy(frame => frame.bodyY0).First();
                        HeavyGeometryImages(stage, candidate, animator, rig, drawn, clips[1], worst, report, images);
                        report.imagesCompleted = images.Count == 3;
                    }
                }
                finally { HeavyGeometryMemoryBeforeCleanup(watches); }
            }
            catch (Exception error) { originalError = error; report.primaryError = error.ToString(); throw; }
            finally
            {
                report.frames = frames.ToArray(); report.images = images.ToArray(); report.protectedAfter = report.beforeWarmDisk.Select(row => File.Exists(row.path) ? HeavyGeometryFile(row.path) : new FileRow { path = row.path, sha256 = "MISSING", bytes = -1 }).ToArray();
                report.sourcesUnchanged = HeavyGeometrySameFiles(report.beforeWarmDisk, report.protectedAfter);
                if (watches != null)
                {
                    if (watches.Any(watch => !watch.row.beforeCleanupProbeReached)) HeavyGeometryMemoryBeforeCleanup(watches);
                    foreach (var watch in watches)
                    {
                        watch.row.aliveAfterCleanup = watch.original != null; string after = watch.original == null ? "DESTROYED" : EditorJsonUtility.ToJson(watch.original);
                        watch.row.afterCleanupJsonSha = HeavyGeometryStringHash(after); watch.row.sameAfterCleanup = watch.original != null && after == watch.json;
                        if (!watch.row.sameAfterCleanup || !watch.row.sameBeforeCleanup || !watch.row.sameWarmFixedPoint) { watch.row.beforeJson = watch.json; watch.row.afterCleanupJson = after; }
                    }
                    report.memory = watches.Select(watch => watch.row).ToArray(); report.sourceMemoryUnchanged = watches.All(watch => watch.row.sameWarmFixedPoint && watch.row.aliveBeforeCleanup && watch.row.sameBeforeCleanup && watch.row.aliveAfterCleanup && watch.row.sameAfterCleanup);
                }
                report.loadedScenesUnchanged = sceneMemory == SceneMemory(scenes) && SameScenes(scenes); report.renderSettingsUnchanged = renderState == RenderState();
                report.enabledClosureAfter = ThrowEntryClosure(); report.enabledSceneClosureUnchanged = report.enabledClosureBefore.SequenceEqual(report.enabledClosureAfter);
                if (!report.sourcesUnchanged || !report.sourceMemoryUnchanged || !report.loadedScenesUnchanged || !report.renderSettingsUnchanged || !report.enabledSceneClosureUnchanged)
                { report.protectionError = "Heavy attribution protection failed; partial raw evidence retained, no restoration or cache exceptions."; if (originalError == null) throw new InvalidOperationException(report.protectionError); }
            }
        }

        static HeavyGeometryClip HeavyGeometryDescribeClip(AnimationClip clip, string role, float length, string expectedGuid)
        {
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(clip, out string guid, out long id) || guid != expectedGuid || id != 7400000 || !clip.humanMotion || clip.legacy || clip.isLooping ||
                Mathf.Abs(clip.length - length) > 1e-6f || clip.frameRate != 120 || AnimationUtility.GetCurveBindings(clip).Length != 130 || AnimationUtility.GetAnimationEvents(clip).Length != 0 || AnimationUtility.GetObjectReferenceCurveBindings(clip).Length != 0) throw new InvalidOperationException("Exact original owned a1 full130 clip required.");
            return new HeavyGeometryClip { role = role, asset = AssetDatabase.GetAssetPath(clip), name = clip.name, guid = guid, localId = id, sha256 = Hash(AssetDatabase.GetAssetPath(clip)), length = clip.length, frameRate = clip.frameRate, bindings = 130, human = true, nonlooping = true };
        }
        static HeavyGeometryFrame HeavyGeometryObserve(string role, float time, float nativeTime, Rig rig, Drawn drawn, Animator animator, HeavyGeometryInputTarget actual)
        {
            var floors = drawn.PerRendererFloors(time); ThrowMinima(floors, out float body, out float cape, out float gear, out _, out _);
            var details = drawn.PerRendererFloorDetails(time, animator);
            foreach (var row in details) { row.existingBoundsFloorY = floors.Single(floor => floor.renderer == row.renderer).minY; row.signedBoundsMinusVertexY = row.existingBoundsFloorY - row.rawWorldY; }
            if (details.Length != floors.Length || details.Any(row => !Finite(row.rawWorldY) || Mathf.Abs(row.signedBoundsMinusVertexY) > .000001f)) throw new InvalidOperationException("Vertex attribution differs beyond the predeclared Bounds arithmetic cross-check; signed floor gates are not relaxed.");
            return new HeavyGeometryFrame { role = role, clipSeconds = time, analyticNativeSourceSeconds = nativeTime, bodyY0 = body, capeY0 = cape, gearY0 = gear,
                rawAnimatorPositionDrift = rig.PositionDrift, rawAnimatorRotationDrift = rig.RotationDrift, renderers = details, legs = HeavyGeometryLegs(animator), exactActualTime = actual != null,
                exactActualReport = actual?.report, originalFixtureTranslationY = actual == null ? 0 : actual.originalFixtureTranslationY,
                predictedFixtureBodyY = actual == null ? 0 : body + actual.originalFixtureTranslationY,
                signedPredictionMinusActual = actual == null ? 0 : body + actual.originalFixtureTranslationY - actual.actualBodyMin };
        }
        static HeavyGeometryJoint HeavyGeometryJointTrs(Transform transform, Transform root) => new HeavyGeometryJoint { name = transform.name, path = PathOf(transform, root), localPosition = new V(transform.localPosition),
            localRotation = new Q(transform.localRotation), localScale = new V(transform.localScale), worldPosition = new V(transform.position), worldRotation = new Q(transform.rotation), lossyScale = new V(transform.lossyScale) };
        static HeavyGeometryLeg[] HeavyGeometryLegs(Animator animator) => new[] { "l", "r" }.Select(side =>
        {
            string prefix = "Rig/root/hips/upperleg." + side; var hip = animator.transform.Find(prefix); var knee = animator.transform.Find(prefix + "/lowerleg." + side);
            var ankle = animator.transform.Find(prefix + "/lowerleg." + side + "/foot." + side); var toe = animator.transform.Find(prefix + "/lowerleg." + side + "/foot." + side + "/toes." + side);
            if (new[] { hip, knee, ankle, toe }.Any(transform => transform == null)) throw new InvalidOperationException("Preserved actual leg path missing: " + side);
            Vector3 direction = toe.position - ankle.position; if (direction.sqrMagnitude <= 1e-12f) throw new InvalidOperationException("No actual ankle-to-toe direction.");
            return new HeavyGeometryLeg { side = side, hip = HeavyGeometryJointTrs(hip, animator.transform), knee = HeavyGeometryJointTrs(knee, animator.transform), ankle = HeavyGeometryJointTrs(ankle, animator.transform), toe = HeavyGeometryJointTrs(toe, animator.transform),
                thighLength = Vector3.Distance(hip.position, knee.position), shankLength = Vector3.Distance(knee.position, ankle.position), ankleToeLength = direction.magnitude, ankleToToeDirection = new V(direction.normalized) };
        }).ToArray();

        sealed partial class Drawn
        {
            readonly Dictionary<Mesh, HeavyGeometryMeshReadProbe> heavyGeometryReadProbes = new Dictionary<Mesh, HeavyGeometryMeshReadProbe>();
            public HeavyGeometryRendererFloor[] PerRendererFloorDetails(float time, Animator animator)
            {
                var rows = new List<HeavyGeometryRendererFloor>();
                foreach (var part in parts.Where(part => part.renderer.enabled && !part.renderer.forceRenderingOff && part.renderer.gameObject.activeInHierarchy))
                {
                    Mesh baked = part.source; var skin = part.renderer as SkinnedMeshRenderer;
                    if (!heavyGeometryReadProbes.TryGetValue(part.source, out var readProbe)) { readProbe = HeavyGeometryProbeEditorMesh(part.source, skin != null); heavyGeometryReadProbes.Add(part.source, readProbe); }
                    if (skin != null) { skin.BakeMesh(part.scratch, true); baked = part.scratch; }
                    baked.GetVertices(part.vertices); if (part.vertices.Count != part.source.vertexCount) throw new InvalidOperationException("Baked/source vertex identity differs.");
                    Matrix4x4 matrix = part.renderer.localToWorldMatrix; int lowest = -1, ties = 0; Vector3 point = default;
                    foreach (int index in part.indices)
                    {
                        var world = matrix.MultiplyPoint3x4(part.vertices[index]); if (!Finite(world.x) || !Finite(world.y) || !Finite(world.z)) throw new InvalidOperationException("Nonfinite actual drawn vertex.");
                        if (lowest < 0 || world.y < point.y) { lowest = index; point = world; ties = 1; } else if (world.y == point.y) ties++;
                    }
                    if (lowest < 0 || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(part.source, out string guid, out long id)) throw new InvalidOperationException("No actual drawn vertex/persistent mesh identity.");
                    var sourceVertices = new List<Vector3>(); part.source.GetVertices(sourceVertices); var triangles = new List<HeavyGeometryTriangle>();
                    for (int submesh = 0; submesh < part.source.subMeshCount; submesh++) if (part.source.GetTopology(submesh) == MeshTopology.Triangles)
                    { var indices = part.source.GetIndices(submesh); for (int index = 0; index < indices.Length; index += 3) if (indices[index] == lowest || indices[index + 1] == lowest || indices[index + 2] == lowest) triangles.Add(new HeavyGeometryTriangle { submesh = submesh, triangle = index / 3, vertices = new[] { indices[index], indices[index + 1], indices[index + 2] } }); }
                    var row = new HeavyGeometryRendererFloor { renderer = part.path, category = part.category, meshAsset = AssetDatabase.GetAssetPath(part.source), meshName = part.source.name, meshGuid = guid, meshLocalId = id,
                        vertex = lowest, vertexCount = part.source.vertexCount, drawnVertices = part.indices.Length, equalHeightVertices = ties, time = time, rawWorldY = point.y, readableSource = part.source.isReadable, editorReadProbeComplete = readProbe.editorNonPlayReadComplete, skinned = skin != null,
                        sourceVertex = new V(sourceVertices[lowest]), bakedVertex = new V(part.vertices[lowest]), worldVertex = new V(point), rendererLocalToWorld = Enumerable.Range(0, 16).Select(index => matrix[index]).ToArray(), triangles = triangles.ToArray() };
                    if (skin != null)
                    {
                        var bones = skin.bones; var legacy = part.source.boneWeights;
                        using (var counts = part.source.GetBonesPerVertex()) using (var weights = part.source.GetAllBoneWeights())
                        {
                            if (counts.Length != part.source.vertexCount) throw new InvalidOperationException("Actual variable skin layout missing.");
                            int offset = 0; for (int index = 0; index < lowest; index++) offset += counts[index];
                            row.actualVariableWeights = Enumerable.Range(0, counts[lowest]).Select(slot => HeavyGeometryReadWeight(slot, weights[offset + slot].boneIndex, weights[offset + slot].weight, bones, animator.transform)).ToArray();
                            row.variableWeightSum = row.actualVariableWeights.Sum(weight => weight.weight); row.allVariableWeightsMeasured = true;
                            row.heelToe = HeavyGeometryFootExtrema(part, counts.ToArray(), weights.ToArray().Select(weight => new KeyValuePair<int, float>(weight.boneIndex, weight.weight)).ToArray(), bones, animator);
                        }
                        if (legacy.Length != part.source.vertexCount) throw new InvalidOperationException("Original raw legacy slots missing.");
                        var raw = legacy[lowest]; row.rawLegacyFourSlots = new[] { HeavyGeometryReadWeight(0, raw.boneIndex0, raw.weight0, bones, animator.transform), HeavyGeometryReadWeight(1, raw.boneIndex1, raw.weight1, bones, animator.transform),
                            HeavyGeometryReadWeight(2, raw.boneIndex2, raw.weight2, bones, animator.transform), HeavyGeometryReadWeight(3, raw.boneIndex3, raw.weight3, bones, animator.transform) }; row.legacyFourSlotsMeasured = true;
                    }
                    rows.Add(row);
                }
                return rows.ToArray();
            }

            static HeavyGeometryHeelToe[] HeavyGeometryFootExtrema(Part part, byte[] counts, KeyValuePair<int, float>[] weights, Transform[] bones, Animator animator)
            {
                var result = new List<HeavyGeometryHeelToe>(); var offsets = new int[counts.Length]; for (int index = 1; index < offsets.Length; index++) offsets[index] = offsets[index - 1] + counts[index - 1];
                foreach (string side in new[] { "l", "r" })
                {
                    string path = "Rig/root/hips/upperleg." + side + "/lowerleg." + side + "/foot." + side;
                    var ankle = animator.transform.Find(path); var toe = animator.transform.Find(path + "/toes." + side); if (ankle == null || toe == null) throw new InvalidOperationException("Actual preserved foot/toe missing.");
                    Vector3 axis = (toe.position - ankle.position).normalized; var cohort = new List<KeyValuePair<int, Vector3>>();
                    foreach (int vertex in part.indices)
                    {
                        float influence = 0; for (int slot = 0; slot < counts[vertex]; slot++) { var weight = weights[offsets[vertex] + slot]; if (weight.Key >= 0 && weight.Key < bones.Length && (bones[weight.Key] == ankle || bones[weight.Key] == toe)) influence += weight.Value; }
                        if (influence >= .5f) cohort.Add(new KeyValuePair<int, Vector3>(vertex, part.renderer.localToWorldMatrix.MultiplyPoint3x4(part.vertices[vertex])));
                    }
                    if (cohort.Count < 2) continue;
                    var ordered = cohort.OrderBy(pair => Vector3.Dot(pair.Value - ankle.position, axis)).ToArray(); var heel = ordered.First(); var front = ordered.Last(); Vector3 direction = front.Value - heel.Value;
                    if (direction.sqrMagnitude <= 1e-12f) continue;
                    result.Add(new HeavyGeometryHeelToe { side = side, cohortVertices = cohort.Count, heelVertex = heel.Key, toeVertex = front.Key, heelWorld = new V(heel.Value), toeWorld = new V(front.Value), heelToToeDirection = new V(direction.normalized),
                        axisTiltDegrees = Mathf.Atan2(direction.y, new Vector2(direction.x, direction.z).magnitude) * Mathf.Rad2Deg });
                }
                return result.Count == 0 ? null : result.ToArray();
            }
        }

        static HeavyGeometryMeshReadProbe HeavyGeometryProbeEditorMesh(Mesh mesh, bool skinned)
        {
            if (UnityEngine.Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Actual source reads require Editor non-Play mode.");
            var probe = new HeavyGeometryMeshReadProbe { asset = AssetDatabase.GetAssetPath(mesh), name = mesh.name, runtimeReadWriteFlag = mesh.isReadable, skinned = skinned };
            var vertices = new List<Vector3>(); mesh.GetVertices(vertices); probe.vertices = vertices.Count;
            if (vertices.Count != mesh.vertexCount || vertices.Count == 0 || vertices.Any(vertex => !Finite(vertex.x) || !Finite(vertex.y) || !Finite(vertex.z))) throw new InvalidOperationException("Incomplete/nonfinite actual Editor vertices: " + mesh.name);
            for (int submesh = 0; submesh < mesh.subMeshCount; submesh++)
            {
                var indices = mesh.GetIndices(submesh); probe.indices += indices.Length;
                if ((ulong)indices.Length != mesh.GetIndexCount(submesh) || indices.Any(index => index < 0 || index >= mesh.vertexCount) || (mesh.GetTopology(submesh) == MeshTopology.Triangles && indices.Length % 3 != 0)) throw new InvalidOperationException("Incomplete/invalid actual Editor indices: " + mesh.name);
            }
            var poses = mesh.bindposes; var legacy = mesh.boneWeights; probe.legacyFourSlotVertices = legacy.Length;
            if (poses.Any(matrix => Enumerable.Range(0, 16).Any(index => !Finite(matrix[index]))) ||
                (skinned && (legacy.Length != mesh.vertexCount || poses.Length == 0)) || (legacy.Length != 0 && legacy.Length != mesh.vertexCount)) throw new InvalidOperationException("Incomplete actual skin/rigid layout: " + mesh.name);
            foreach (var raw in legacy)
            {
                var values = new[] { raw.weight0, raw.weight1, raw.weight2, raw.weight3 }; var indices = new[] { raw.boneIndex0, raw.boneIndex1, raw.boneIndex2, raw.boneIndex3 };
                if (Enumerable.Range(0, 4).Any(slot => !Finite(values[slot]) || values[slot] < 0 || (values[slot] > 0 && (indices[slot] < 0 || indices[slot] >= poses.Length)))) throw new InvalidOperationException("Invalid/nonfinite original four-slot weights: " + mesh.name);
            }
            using (var counts = mesh.GetBonesPerVertex()) using (var weights = mesh.GetAllBoneWeights())
            {
                if ((skinned ? counts.Length != mesh.vertexCount : counts.Length != 0 && counts.Length != mesh.vertexCount) || counts.ToArray().Sum(count => (int)count) != weights.Length) throw new InvalidOperationException("Incomplete actual variable skin/rigid layout: " + mesh.name);
                probe.variableInfluences = weights.Length;
                foreach (var weight in weights) if (!Finite(weight.weight) || weight.weight < 0 || (weight.weight > 0 && (weight.boneIndex < 0 || weight.boneIndex >= poses.Length))) throw new InvalidOperationException("Invalid/nonfinite original variable weights: " + mesh.name);
            }
            probe.editorNonPlayReadComplete = true; return probe;
        }

        static HeavyGeometryWeight HeavyGeometryReadWeight(int slot, int index, float weight, Transform[] bones, Transform root)
        {
            bool valid = index >= 0 && index < bones.Length && bones[index] != null;
            if (!Finite(weight) || weight < 0 || (weight > 0 && !valid)) throw new InvalidOperationException("Invalid actual positive skin influence; no fabricated bone/weight.");
            if (weight > 0 && bones[index] != root && !bones[index].IsChildOf(root)) throw new InvalidOperationException("Positive skin bone is outside the actual candidate Animator hierarchy.");
            return new HeavyGeometryWeight { slot = slot, boneIndex = index, weight = weight, contributes = weight > 0, boneIndexValid = valid,
                bonePath = valid ? PathOf(bones[index], root) : null, bone = valid ? HeavyGeometryJointTrs(bones[index], root) : null };
        }
        static float HeavyGeometryNativeTime(float time)
        {
            if (!Finite(time) || time < 0 || time > .82f) throw new ArgumentOutOfRangeException(nameof(time));
            var t = new[] { 0f, .235f, .475f, .82f }; var y = new[] { .4f, .5f, 20f / 30f, 32f / 30f };
            var m = new[] { .2924908995628357f, .5272603034973145f, .8557793498039246f, 1.4336369037628174f };
            for (int index = 0; index < 4; index++) if (time == t[index]) return y[index];
            int segment = time < t[1] ? 0 : time < t[2] ? 1 : 2; float h = t[segment + 1] - t[segment], u = (time - t[segment]) / h, u2 = u * u, u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * y[segment] + (u3 - 2 * u2 + u) * h * m[segment] + (-2 * u3 + 3 * u2) * y[segment + 1] + (u3 - u2) * h * m[segment + 1];
        }
        static void HeavyGeometryMemoryBeforeCleanup(HeavyGeometryMemoryWatch[] watches)
        {
            foreach (var watch in watches)
            {
                watch.row.beforeCleanupProbeReached = true; watch.row.aliveBeforeCleanup = watch.original != null; string value = watch.original == null ? "DESTROYED" : EditorJsonUtility.ToJson(watch.original);
                watch.row.beforeCleanupJsonSha = HeavyGeometryStringHash(value); watch.row.sameBeforeCleanup = watch.original != null && value == watch.json;
                if (!watch.row.sameBeforeCleanup) { watch.row.beforeJson = watch.json; watch.row.beforeCleanupJson = value; }
            }
        }
        static void HeavyGeometryImages(Stage stage, GameObject candidate, Animator animator, Rig rig, Drawn drawn, AnimationClip clip, HeavyGeometryFrame worst, HeavyGeometryReport report, List<HeavyGeometryImage> images)
        {
            rig.Start(clip); rig.Pose(worst.clipSeconds); var lowest = drawn.PerRendererFloorDetails(worst.clipSeconds, animator).Where(row => !row.category.StartsWith("named equipment", StringComparison.Ordinal) && !row.category.StartsWith("separate named cape", StringComparison.Ordinal)).OrderBy(row => row.rawWorldY).First();
            var owned = new List<Object>();
            try
            {
                var material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave }; material.SetColor("_BaseColor", new Color(1f, .12f, .32f)); material.SetFloat("_Metallic", 0); owned.Add(material);
                var mesh = new Mesh { hideFlags = HideFlags.HideAndDontSave }; float r = .008f; mesh.vertices = new[] { Vector3.up * r, Vector3.down * r, Vector3.left * r, Vector3.right * r, Vector3.forward * r, Vector3.back * r }; mesh.triangles = new[] { 0, 4, 3, 0, 2, 4, 0, 5, 2, 0, 3, 5, 1, 3, 4, 1, 4, 2, 1, 2, 5, 1, 5, 3 }; mesh.RecalculateNormals(); owned.Add(mesh);
                var marker = stage.New("DiagnosticLowestDrawnVertex_OutsideCandidate"); marker.transform.position = Vec(lowest.worldVertex); marker.AddComponent<MeshFilter>().sharedMesh = mesh; marker.AddComponent<MeshRenderer>().sharedMaterial = material;
                var referenceMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")) { hideFlags = HideFlags.HideAndDontSave }; referenceMaterial.SetColor("_BaseColor", new Color(.15f, .72f, .86f)); owned.Add(referenceMaterial);
                var referenceMesh = new Mesh { hideFlags = HideFlags.HideAndDontSave }; float half = 1.2f, width = .003f;
                referenceMesh.vertices = new[] { new Vector3(-half, 0, -width), new Vector3(half, 0, -width), new Vector3(half, 0, width), new Vector3(-half, 0, width), new Vector3(-width, 0, -half), new Vector3(width, 0, -half), new Vector3(width, 0, half), new Vector3(-width, 0, half) };
                referenceMesh.triangles = new[] { 0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6 }; referenceMesh.RecalculateNormals(); owned.Add(referenceMesh);
                var reference = stage.New("DiagnosticY0Reference_OutsideCandidate"); reference.AddComponent<MeshFilter>().sharedMesh = referenceMesh; reference.AddComponent<MeshRenderer>().sharedMaterial = referenceMaterial;
                var views = new[] { "whole", "front-leg", "side-leg" }; var cameras = new[] { new Vector3(3.5f, 2.2f, 6.2f), new Vector3(0, .36f, 2.1f), new Vector3(2.2f, .32f, .45f) }; var targets = new[] { new Vector3(0, .9f, 0), new Vector3(0, .28f, 0), new Vector3(0, .25f, 0) };
                for (int index = 0; index < 3; index++)
                {
                    if (index == 1) stage.HideFloor(); stage.Camera(cameras[index], targets[index], index == 0 ? 38 : 32);
                    string file = (index + 1).ToString("00") + "-heavy-" + views[index] + ".png"; stage.Save(report.directory + "/" + file, index == 0 ? 900 : 700, 700);
                    images.Add(new HeavyGeometryImage { file = file, view = views[index], clipSeconds = worst.clipSeconds, markedVertex = lowest.worldVertex, camera = new V(cameras[index]), target = new V(targets[index]),
                        scope = index == 0 ? "Neutral CPU-baked rawY0 whole pose, Y0 reference plane and marked actual lowest BODY vertex. Not actual GameView/contact." : "Neutral CPU-baked rawY0 legs from fixed diagnostic +Z/front or +X/side world directions; not certified character facing/TPS. Opaque floor HIDDEN to expose the negative vertex; thin cyan reference is exactly Y0. All reference/marker geometry is outside measured candidate, not a pose/collision modification." });
                }
            }
            finally { foreach (var item in owned) if (item != null) Object.DestroyImmediate(item); }
        }
        static FileRow HeavyGeometryFile(string path) => new FileRow { path = path.Replace('\\', '/'), sha256 = Hash(path), bytes = new FileInfo(path).Length };
        static bool HeavyGeometrySameFiles(FileRow[] before, FileRow[] after) => before.Length == after.Length && before.Zip(after, (a, b) => a.path == b.path && a.sha256 == b.sha256 && a.bytes == b.bytes).All(equal => equal);
        static string HeavyGeometryStringHash(string value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", ""); }
        static void HeavyGeometryWrite(string path, HeavyGeometryReport report) => File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
    }
}
