using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    public static partial class KayKitNativeCharacterReview
    {
        const string ToeCalibrationSource = "Assets/_Game/Scripts/Editor/Review/KayKitKnightToeAxisCalibration.cs";
        const float ToeCalibrationOffset = .05f;
        static bool toeCalibrationPending;

        [Serializable] sealed class ToeCalibrationPose
        {
            public string role; public float time, bodyY0, capeY0, gearY0, anchorPositionDrift, anchorRotationDrift;
            public HeavyGeometryJoint[] joints; public ToeCalibrationGeometry[] feet;
        }
        [Serializable] sealed class ToeCalibrationGeometry
        {
            public string side, renderer, meshGuid; public long meshLocalId;
            public int toeCohortCount, footCohortCount, toeLowestVertex, footLowestVertex;
            public float toeMinY, footMinY, geometricPlaneTiltDegrees, boneForwardPitchDegrees;
            public int[] idlePlaneVertices;
            public V[] actualPlaneWorldVertices;
            public V actualPlaneNormal, boneForwardWorld;
            public string planeScope = "Three noncollinear ACTUALLY DRAWN points selected once from the lowest equal-height Idle toe-weight>=.5 cohort. Their immutable indices define an independent geometric reference, not an anatomical sole/contact certificate or a Heavy-floor fitted target.";
        }
        [Serializable] sealed class ToeCalibrationJointDelta
        {
            public string path; public bool selectedToeDescendant;
            public float localPosition, localAngle, localScale, worldPosition, worldAngle, worldScale;
        }
        [Serializable] sealed class ToeCalibrationCurveProof
        {
            public string binding, sourceSignature, probeSignature;
            public bool unchanged, selectedToe;
        }
        [Serializable] sealed class ToeCalibrationResult
        {
            public string side, humanBinding, channel = "Human Animator muscle binding, NOT direct Transform keys or HumanPoseHandler";
            public int humanMuscleIndex;
            public float defaultMinDegrees, defaultMaxDegrees, fixedNormalizedOffset, actualNormalizedOffset;
            public float measuredLocalAngleDegrees, measuredWorldAngleDegrees, angleDegreesPerNormalizedUnit;
            public float geometricNormalAngleDegrees, geometricNormalDegreesPerNormalizedUnit, signedPlaneTiltDeltaDegrees;
            public float bonePitchDeltaDegrees, toeMinYDelta, millimetresPerMeasuredDegree;
            public float requiredGeometricAngleAboutMeasuredAxis, projectedSingleAxisResidualDegrees;
            public float chargeReleaseOriginalToeAngle, releaseStartOriginalAngularSpeed, chargeEndOriginalAngularSpeed;
            public float releaseEndOriginalAngularSpeed, releaseStartProbeAngularSpeed, releaseEndProbeAngularSpeed;
            public float offsetStartValue, offsetEndValue, offsetStartDerivative, offsetEndDerivative;
            public float maximumAnalyticAddedMuscleVelocity = .31914894f;
            public float riseSeconds = .235f, plateauEndSeconds = .475f, fallSeconds = .345f;
            public string derivativeScope = "Offset uses fixed smoothstep ramps and zero endpoint value/derivative; analytic max muscle/s is 1.5*.05/.235. Original Charge/Release C1 mismatch is measured, NOT claimed repaired. Actual Human angular-speed response is independently sampled around endpoint phases.";
            public V axisInToeParent, axisWorld;
            public bool curveSetAndOther128Unchanged, sourceAndProbeEndpointsSame, nonToeActualTrsUnchanged, uniformlyScaledActualParentChain, measurableAxis;
            public ToeCalibrationCurveProof[] curves;
            public ToeCalibrationJointDelta[] actualJointDeltas;
            public ToeCalibrationPose sourcePose, probePose;
        }
        [Serializable] sealed class ToeCalibrationReport
        {
            public string label, directory, status, error, primaryError, protectionError, startedUtc, finishedUtc, module, sourceSha256;
            public string scope = "ONE independent left/right +.05 Human Toes calibration, at the most-negative exact retained actual Release sample. No amplitude sweep, candidate authoring, clip asset writes, importer changes, root lift/anchor reset, Foot/ankle changes, limb scaling, Runtime/Profile/production/r4 changes. Controlled manual CPU Playables are not real-input acceptance.";
            public string targetScope = "Reference plane is WORLD Y0 in the isolated .75 native-rig preview. Geometric Idle plane uses fixed original shoe vertices; no actual collider/slope/contact claim. Any future nonzero Toe change would be authored compensation, not recovery of an unchanged source Toe pose.";
            public string memoryScope = "comparison=FULL exact EditorJsonUtility strings before/after cleanup; report=identity+SHA256+length only for unchanged objects, full before/after for changes. No warm/cache exemption or old failed-report acceptance.";
            public bool calibrationOnly = true, clipAssetsWritten = false, floorAccepted = false, actualInputAccepted = false, productionAccepted = false;
            public bool diskUnchangedByWarm, memoryWarmFixedPoint, sourcesUnchanged, sourceMemoryUnchanged, loadedScenesUnchanged, renderSettingsUnchanged, enabledClosureUnchanged, isolatedOutsideEnabledClosure;
            public int memoryObjects; public float exactActualClipTime, fixedNormalizedProbeOffset = ToeCalibrationOffset;
            public HeavyGeometryInputTarget retainedActualTarget;
            public ToeCalibrationPose idle, chargeEnd, releaseStart, releaseEnd;
            public ToeCalibrationResult[] sides;
            public HeavyGeometryMemoryRow[] memory;
            public FileRow[] beforeWarmDisk, protectedBefore, protectedAfter;
            public string[] enabledClosureBefore, enabledClosureAfter;
        }
        sealed class ToeCalibrationPlane
        {
            public string side, renderer, guid; public long id;
            public int[] indices; public Vector3 idleNormal;
        }

        /// <summary>Queues a fresh, bounded, memory-only Human-axis measurement. Repeated labels only return retained evidence.</summary>
        public static string RequestKnightToeAxisCalibration(string uniqueLabel)
        {
            if (string.IsNullOrWhiteSpace(uniqueLabel) || !Regex.IsMatch(uniqueLabel, "^[A-Za-z0-9_-]{8,72}$")) throw new ArgumentException("Fresh ASCII label required.");
            string prefix = JobPrefix + "ToeCalibration.", key = prefix + uniqueLabel, prior = SessionState.GetString(key, "");
            if (prior.Length != 0) { if (!File.Exists(prior)) throw new IOException("Retained calibration missing."); return prior; }
            RequireIdle();
            if (pending || assembledPending || motionPending || throwPending || genericPending || heavyGeometryPending || GripLandmarkPending || toeCalibrationPending) throw new InvalidOperationException("One isolated review job at a time.");
            foreach (string activeKey in new[] { JobPrefix + "active", JobPrefix + "Assembled.active", JobPrefix + "Motion.active", JobPrefix + "Throw.active", JobPrefix + "Generic.active", JobPrefix + "HeavyGeometry.active", prefix + "active" })
            {
                string active = SessionState.GetString(activeKey, ""); if (!File.Exists(active)) continue;
                var old = JsonUtility.FromJson<ToeCalibrationReport>(File.ReadAllText(active));
                if (old != null && (old.status == "queued" || old.status == "running")) throw new InvalidOperationException("Inspect pending retained review: " + active);
            }
            string dir = Path.GetFullPath("Builds/ArtReview/kaykit-toe-calibration/" + uniqueLabel);
            if (Directory.Exists(dir)) throw new IOException("Fresh evidence directory required."); Directory.CreateDirectory(dir);
            string record = dir + "/report.json";
            var report = new ToeCalibrationReport { label = uniqueLabel, directory = dir, status = "queued", startedUtc = DateTime.UtcNow.ToString("O"), module = typeof(KayKitNativeCharacterReview).Assembly.ManifestModule.ModuleVersionId.ToString(), sourceSha256 = Hash(ToeCalibrationSource) };
            ToeCalibrationWrite(record, report); SessionState.SetString(key, record); SessionState.SetString(prefix + "active", record);
            SessionState.SetString(JobPrefix + "Throw.active", record); toeCalibrationPending = throwPending = true;
            double deadline = EditorApplication.timeSinceStartup + 180; EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= callback;
                try { RequireIdle(); if (Hash(ToeCalibrationSource) != report.sourceSha256) throw new IOException("Queued source changed."); report.status = "running"; ToeCalibrationWrite(record, report); CaptureKnightToeAxisCalibration(report); report.status = "completed-calibration-only"; }
                catch (Exception error) { report.status = "failed"; report.error = error.ToString(); Debug.LogException(error); }
                finally { report.finishedUtc = DateTime.UtcNow.ToString("O"); ToeCalibrationWrite(record, report); toeCalibrationPending = throwPending = false; SessionState.EraseString(prefix + "active"); if (SessionState.GetString(JobPrefix + "Throw.active", "") == record) SessionState.EraseString(JobPrefix + "Throw.active"); }
            };
            EditorApplication.update += callback; return record;
        }

        static void CaptureKnightToeAxisCalibration(ToeCalibrationReport report)
        {
            var scenes = OriginalScenes(); string sceneMemory = SceneMemory(scenes), renderState = RenderState(); report.enabledClosureBefore = ThrowEntryClosure();
            var charge = Required<AnimationClip>(HeavyGeometryCharge); var release = Required<AnimationClip>(HeavyGeometryRelease);
            HeavyGeometryDescribeClip(charge, "Charge", .55f, "a90efaf08e083a34a8c4815b1129da3f"); HeavyGeometryDescribeClip(release, "Release", .82f, "c0fb090c6cc7b3241bb7d7b04a96e3c8");
            var nativeAssets = AssetDatabase.LoadAllAssetsAtPath(Source + "Knight.fbx"); var idle = nativeAssets.OfType<AnimationClip>().Single(clip => clip.name == "Idle");
            report.retainedActualTarget = HeavyGeometryReadActualTargets().OrderBy(target => target.actualBodyMin).First(); report.exactActualClipTime = report.retainedActualTarget.clipSeconds;
            if (report.exactActualClipTime <= .235f || report.exactActualClipTime >= .475f) throw new InvalidOperationException("Fixed calibration expects the already-recorded worst in the plateau; no adaptive phase search.");
            report.isolatedOutsideEnabledClosure = new[] { GenericPrefab, HeavyGeometryCharge, HeavyGeometryRelease }.All(path => !report.enabledClosureBefore.Contains(path));
            if (!report.isolatedOutsideEnabledClosure) throw new InvalidOperationException("Isolated candidate unexpectedly belongs to production closure.");
            var extra = AssetDatabase.GetDependencies(GenericPrefab, true).Concat(report.enabledClosureBefore).Concat(new[] { GenericPrefab, HeavyGeometryCharge, HeavyGeometryRelease, ToeCalibrationSource, HeavyGeometrySource, HeavyGeometryAuthorReport, "Packages/manifest.json", "Packages/packages-lock.json" })
                .Concat(HeavyGeometryInputs).Concat(HeavyGeometryInputs.Select(path => Path.GetDirectoryName(path).Replace('\\', '/') + "/actual-input.json"))
                .Concat(Directory.GetFiles("Assets/_Game/Scripts", "*", SearchOption.AllDirectories).Where(path => path.EndsWith(".cs", StringComparison.Ordinal) || path.EndsWith(".asmdef", StringComparison.Ordinal)))
                .Distinct().SelectMany(path => new[] { path, path + ".meta" }).Where(File.Exists);
            report.beforeWarmDisk = Protected().Concat(extra.Select(HeavyGeometryFile)).GroupBy(row => row.path, StringComparer.Ordinal).Select(group => group.First()).OrderBy(row => row.path, StringComparer.Ordinal).ToArray();
            HeavyGeometryMemoryWatch[] watches = null; Exception originalError = null; var probes = new List<AnimationClip>(); var results = new List<ToeCalibrationResult>();
            try
            {
                foreach (var clip in new[] { idle, charge, release })
                {
                    AnimationUtility.GetAnimationClipSettings(clip); AnimationUtility.GetAnimationEvents(clip); AnimationUtility.GetObjectReferenceCurveBindings(clip);
                    foreach (var binding in AnimationUtility.GetCurveBindings(clip)) ToeCalibrationCurveSignature(AnimationUtility.GetEditorCurve(clip, binding));
                }
                var nativeSkins = Required<GameObject>(Source + "Knight.fbx").GetComponentsInChildren<SkinnedMeshRenderer>(true).Select(skin => skin.sharedMesh).ToArray();
                foreach (var mesh in nativeAssets.OfType<Mesh>()) HeavyGeometryProbeEditorMesh(mesh, nativeSkins.Contains(mesh));
                var objects = report.beforeWarmDisk.Select(row => row.path).Where(path => path.StartsWith("Assets/", StringComparison.Ordinal) && !path.EndsWith(".meta", StringComparison.Ordinal)).SelectMany(AssetDatabase.LoadAllAssetsAtPath).Where(asset => asset != null && EditorUtility.IsPersistent(asset)).Distinct().ToArray();
                watches = objects.Select(asset => new HeavyGeometryMemoryWatch { original = asset, json = EditorJsonUtility.ToJson(asset), row = new HeavyGeometryMemoryRow { asset = AssetDatabase.GetAssetPath(asset), name = asset.name, type = asset.GetType().FullName, instanceId = asset.GetInstanceID() } }).ToArray();
                foreach (var watch in watches)
                {
                    string second = EditorJsonUtility.ToJson(watch.original); watch.row.sameWarmFixedPoint = watch.json == second; watch.row.jsonCharacters = watch.json.Length; watch.row.beforeJsonSha = HeavyGeometryStringHash(watch.json);
                    if (!watch.row.sameWarmFixedPoint) { watch.row.beforeJson = watch.json; watch.row.warmSecondJson = second; }
                }
                report.memoryWarmFixedPoint = watches.All(watch => watch.row.sameWarmFixedPoint); report.memoryObjects = watches.Length;
                report.protectedBefore = report.beforeWarmDisk.Select(row => HeavyGeometryFile(row.path)).ToArray(); report.diskUnchangedByWarm = HeavyGeometrySameFiles(report.beforeWarmDisk, report.protectedBefore);
                if (!report.memoryWarmFixedPoint || !report.diskUnchangedByWarm) throw new InvalidOperationException("Source warm fixed point failed; no exceptions.");
                using (var stage = new Stage())
                try
                {
                    var candidate = stage.preview.InstantiatePrefabInScene(Required<GameObject>(GenericPrefab)); stage.preview.camera.scene = candidate.scene;
                    if (candidate.transform.localPosition != Vector3.zero || candidate.transform.localRotation != Quaternion.identity || candidate.transform.localScale != Vector3.one * .75f || candidate.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 || candidate.GetComponentsInChildren<Collider>(true).Length != 0) throw new InvalidOperationException("Exact original .75/Y0 pure visual rig required.");
                    var animator = candidate.GetComponentInChildren<Animator>(true); var original = Required<GameObject>(Source + "Knight.fbx").GetComponentInChildren<Animator>(true);
                    if (animator == null || animator.avatar != original.avatar || !animator.avatar.isValid || !animator.avatar.isHuman) throw new InvalidOperationException("Native unchanged Human Avatar required.");
                    stage.Materials(candidate);
                    using (var rig = new Rig(animator)) using (var drawn = new Drawn(candidate))
                    {
                        rig.Start(idle); rig.Pose(0); var planes = drawn.ToeCalibrationSelectIdlePlanes(animator); report.idle = ToeCalibrationObserve("OriginalIdle", 0, rig, drawn, animator, planes);
                        rig.Start(charge); rig.Pose(charge.length); report.chargeEnd = ToeCalibrationObserve("OriginalChargeEnd", charge.length, rig, drawn, animator, planes);
                        rig.Start(release); rig.Pose(0); report.releaseStart = ToeCalibrationObserve("OriginalReleaseStart", 0, rig, drawn, animator, planes);
                        rig.Pose(release.length); report.releaseEnd = ToeCalibrationObserve("OriginalReleaseEnd", release.length, rig, drawn, animator, planes);
                        foreach (string side in new[] { "l", "r" })
                        {
                            string name = side == "l" ? "Left Toes Up-Down" : "Right Toes Up-Down"; int index = Array.IndexOf(HumanTrait.MuscleName, name);
                            if (index != (side == "l" ? 28 : 36)) throw new InvalidOperationException("Verified actual Human muscle identity changed.");
                            var binding = AnimationUtility.GetCurveBindings(release).Single(item => item.path == "" && item.type == typeof(Animator) && item.propertyName == name);
                            var probe = Object.Instantiate(release); probes.Add(probe); probe.hideFlags = HideFlags.HideAndDontSave; probe.name = "MemoryOnly_Independent_" + name;
                            var sourceCurve = AnimationUtility.GetEditorCurve(release, binding); var keys = sourceCurve.keys;
                            for (int key = 0; key < keys.Length; key++) { ToeCalibrationEnvelope(keys[key].time, release.length, out float value, out float derivative); keys[key].value += value; keys[key].inTangent += derivative; keys[key].outTangent += derivative; }
                            var changed = new AnimationCurve(keys) { preWrapMode = sourceCurve.preWrapMode, postWrapMode = sourceCurve.postWrapMode }; AnimationUtility.SetEditorCurve(probe, binding, changed);
                            var proof = ToeCalibrationCompareCurves(release, probe);
                            var result = new ToeCalibrationResult { side = side, humanBinding = name, humanMuscleIndex = index, defaultMinDegrees = HumanTrait.GetMuscleDefaultMin(index), defaultMaxDegrees = HumanTrait.GetMuscleDefaultMax(index), fixedNormalizedOffset = ToeCalibrationOffset,
                                actualNormalizedOffset = changed.Evaluate(report.exactActualClipTime) - sourceCurve.Evaluate(report.exactActualClipTime), curves = proof, curveSetAndOther128Unchanged = proof.Length == 130 && proof.Where(row => !row.selectedToe).All(row => row.unchanged) && proof.Count(row => !row.unchanged) == 1 };
                            results.Add(result);
                            ToeCalibrationEnvelope(0, release.length, out result.offsetStartValue, out result.offsetStartDerivative); ToeCalibrationEnvelope(release.length, release.length, out result.offsetEndValue, out result.offsetEndDerivative);
                            result.sourceAndProbeEndpointsSame = changed.Evaluate(0) == sourceCurve.Evaluate(0) && changed.Evaluate(release.length) == sourceCurve.Evaluate(release.length) && result.offsetStartDerivative == 0 && result.offsetEndDerivative == 0;
                            string path = "Rig/root/hips/upperleg." + side + "/lowerleg." + side + "/foot." + side + "/toes." + side;
                            result.chargeReleaseOriginalToeAngle = Quaternion.Angle(Quat(report.chargeEnd.joints.Single(joint => joint.path == path).worldRotation), Quat(report.releaseStart.joints.Single(joint => joint.path == path).worldRotation));
                            result.chargeEndOriginalAngularSpeed = ToeCalibrationAngularSpeed(rig, animator, charge, path, charge.length - 1f / 120f, charge.length);
                            result.releaseStartOriginalAngularSpeed = ToeCalibrationAngularSpeed(rig, animator, release, path, 0, 1f / 120f);
                            result.releaseEndOriginalAngularSpeed = ToeCalibrationAngularSpeed(rig, animator, release, path, release.length - 1f / 120f, release.length);
                            result.releaseStartProbeAngularSpeed = ToeCalibrationAngularSpeed(rig, animator, probe, path, 0, 1f / 120f);
                            result.releaseEndProbeAngularSpeed = ToeCalibrationAngularSpeed(rig, animator, probe, path, probe.length - 1f / 120f, probe.length);
                            rig.Start(release); rig.Pose(report.exactActualClipTime); result.sourcePose = ToeCalibrationObserve("OriginalExactActualWorst", report.exactActualClipTime, rig, drawn, animator, planes);
                            rig.Start(probe); rig.Pose(report.exactActualClipTime); result.probePose = ToeCalibrationObserve("Independent" + side + "+0.05", report.exactActualClipTime, rig, drawn, animator, planes);
                            result.actualJointDeltas = ToeCalibrationCompareJoints(result.sourcePose.joints, result.probePose.joints, path);
                            result.nonToeActualTrsUnchanged = result.actualJointDeltas.Where(row => !row.selectedToeDescendant).All(row => row.localPosition <= 1e-6f && row.localAngle <= .001f && row.localScale <= 1e-6f && row.worldPosition <= 1e-6f && row.worldAngle <= .001f && row.worldScale <= 1e-6f);
                            var sourceToe = result.sourcePose.joints.Single(joint => joint.path == path); var probeToe = result.probePose.joints.Single(joint => joint.path == path);
                            var parent = result.sourcePose.joints.Single(joint => joint.path == path.Substring(0, path.LastIndexOf('/')));
                            Quaternion delta = Quat(probeToe.localRotation) * Quaternion.Inverse(Quat(sourceToe.localRotation)); delta.ToAngleAxis(out float angle, out Vector3 axis); if (angle > 180) { angle = 360 - angle; axis = -axis; }
                            result.measuredLocalAngleDegrees = angle; result.measuredWorldAngleDegrees = Quaternion.Angle(Quat(sourceToe.worldRotation), Quat(probeToe.worldRotation)); result.axisInToeParent = new V(axis); result.axisWorld = new V(Quat(parent.worldRotation) * axis);
                            result.angleDegreesPerNormalizedUnit = angle / result.actualNormalizedOffset; var chain = result.sourcePose.joints.Where(joint => path.StartsWith(joint.path + "/", StringComparison.Ordinal)).ToArray();
                            result.uniformlyScaledActualParentChain = chain.All(joint => Mathf.Abs(joint.lossyScale.x - joint.lossyScale.y) <= 1e-6f && Mathf.Abs(joint.lossyScale.y - joint.lossyScale.z) <= 1e-6f && joint.lossyScale.x > 0);
                            var a = result.sourcePose.feet.Single(foot => foot.side == side); var b = result.probePose.feet.Single(foot => foot.side == side); var reference = report.idle.feet.Single(foot => foot.side == side);
                            result.geometricNormalAngleDegrees = Vector3.Angle(Vec(a.actualPlaneNormal), Vec(b.actualPlaneNormal)); result.geometricNormalDegreesPerNormalizedUnit = result.geometricNormalAngleDegrees / result.actualNormalizedOffset;
                            result.signedPlaneTiltDeltaDegrees = b.geometricPlaneTiltDegrees - a.geometricPlaneTiltDegrees; result.bonePitchDeltaDegrees = b.boneForwardPitchDegrees - a.boneForwardPitchDegrees; result.toeMinYDelta = b.toeMinY - a.toeMinY;
                            result.millimetresPerMeasuredDegree = result.toeMinYDelta * 1000 / angle;
                            Vector3 worldAxis = Vec(result.axisWorld), sourceNormal = Vec(a.actualPlaneNormal), idleNormal = Vec(reference.actualPlaneNormal);
                            result.requiredGeometricAngleAboutMeasuredAxis = Vector3.SignedAngle(Vector3.ProjectOnPlane(sourceNormal, worldAxis), Vector3.ProjectOnPlane(idleNormal, worldAxis), worldAxis);
                            result.projectedSingleAxisResidualDegrees = Vector3.Angle(Quaternion.AngleAxis(result.requiredGeometricAngleAboutMeasuredAxis, worldAxis) * sourceNormal, idleNormal);
                            result.measurableAxis = Finite(angle) && angle > .001f && Finite(result.actualNormalizedOffset) && Mathf.Abs(result.actualNormalizedOffset - ToeCalibrationOffset) < 1e-6f && Finite(result.millimetresPerMeasuredDegree) && result.geometricNormalAngleDegrees > .001f;
                            if (!result.curveSetAndOther128Unchanged || !result.sourceAndProbeEndpointsSame || !result.nonToeActualTrsUnchanged || !result.uniformlyScaledActualParentChain || !result.measurableAxis) throw new InvalidOperationException("Independent Human-axis calibration cannot preserve the bounded strategy; retained measured facts, no candidate authoring.");
                        }
                    }
                }
                finally { HeavyGeometryMemoryBeforeCleanup(watches); }
            }
            catch (Exception error) { originalError = error; report.primaryError = error.ToString(); throw; }
            finally
            {
                foreach (var probe in probes) if (probe != null) Object.DestroyImmediate(probe); report.sides = results.ToArray();
                report.protectedAfter = report.beforeWarmDisk.Select(row => File.Exists(row.path) ? HeavyGeometryFile(row.path) : new FileRow { path = row.path, sha256 = "MISSING", bytes = -1 }).ToArray(); report.sourcesUnchanged = HeavyGeometrySameFiles(report.beforeWarmDisk, report.protectedAfter);
                if (watches != null)
                {
                    if (watches.Any(watch => !watch.row.beforeCleanupProbeReached)) HeavyGeometryMemoryBeforeCleanup(watches);
                    foreach (var watch in watches) { watch.row.aliveAfterCleanup = watch.original != null; string after = watch.original == null ? "DESTROYED" : EditorJsonUtility.ToJson(watch.original); watch.row.afterCleanupJsonSha = HeavyGeometryStringHash(after); watch.row.sameAfterCleanup = watch.original != null && after == watch.json; if (!watch.row.sameAfterCleanup || !watch.row.sameBeforeCleanup || !watch.row.sameWarmFixedPoint) { watch.row.beforeJson = watch.json; watch.row.afterCleanupJson = after; } }
                    report.memory = watches.Select(watch => watch.row).ToArray(); report.sourceMemoryUnchanged = watches.All(watch => watch.row.sameWarmFixedPoint && watch.row.aliveBeforeCleanup && watch.row.sameBeforeCleanup && watch.row.aliveAfterCleanup && watch.row.sameAfterCleanup);
                }
                report.loadedScenesUnchanged = sceneMemory == SceneMemory(scenes) && SameScenes(scenes); report.renderSettingsUnchanged = renderState == RenderState(); report.enabledClosureAfter = ThrowEntryClosure(); report.enabledClosureUnchanged = report.enabledClosureBefore.SequenceEqual(report.enabledClosureAfter);
                if (!report.sourcesUnchanged || !report.sourceMemoryUnchanged || !report.loadedScenesUnchanged || !report.renderSettingsUnchanged || !report.enabledClosureUnchanged) { report.protectionError = "Calibration source/memory/scene/render/entry protection failed; no restoration/exemptions."; if (originalError == null) throw new InvalidOperationException(report.protectionError); }
            }
        }

        static ToeCalibrationPose ToeCalibrationObserve(string role, float time, Rig rig, Drawn drawn, Animator animator, ToeCalibrationPlane[] planes)
        {
            ThrowMinima(drawn.PerRendererFloors(time), out float body, out float cape, out float gear, out _, out _);
            return new ToeCalibrationPose { role = role, time = time, bodyY0 = body, capeY0 = cape, gearY0 = gear, anchorPositionDrift = rig.PositionDrift, anchorRotationDrift = rig.RotationDrift,
                joints = animator.GetComponentsInChildren<Transform>(true).Select(transform => HeavyGeometryJointTrs(transform, animator.transform)).OrderBy(joint => joint.path, StringComparer.Ordinal).ToArray(), feet = drawn.ToeCalibrationGeometry(animator, planes) };
        }
        static float ToeCalibrationAngularSpeed(Rig rig, Animator animator, AnimationClip clip, string path, float a, float b)
        {
            rig.Start(clip); rig.Pose(a); Quaternion first = animator.transform.Find(path).rotation; rig.Pose(b); return Quaternion.Angle(first, animator.transform.Find(path).rotation) / (b - a);
        }
        static ToeCalibrationJointDelta[] ToeCalibrationCompareJoints(HeavyGeometryJoint[] a, HeavyGeometryJoint[] b, string path)
        {
            if (!a.Select(joint => joint.path).SequenceEqual(b.Select(joint => joint.path))) throw new InvalidOperationException("Actual rig path set changed.");
            return a.Zip(b, (x, y) => new ToeCalibrationJointDelta { path = x.path, selectedToeDescendant = x.path == path || x.path.StartsWith(path + "/", StringComparison.Ordinal), localPosition = Vector3.Distance(Vec(x.localPosition), Vec(y.localPosition)), localAngle = Quaternion.Angle(Quat(x.localRotation), Quat(y.localRotation)), localScale = Vector3.Distance(Vec(x.localScale), Vec(y.localScale)), worldPosition = Vector3.Distance(Vec(x.worldPosition), Vec(y.worldPosition)), worldAngle = Quaternion.Angle(Quat(x.worldRotation), Quat(y.worldRotation)), worldScale = Vector3.Distance(Vec(x.lossyScale), Vec(y.lossyScale)) }).ToArray();
        }
        static string ToeCalibrationBinding(EditorCurveBinding b) => b.path + "|" + b.type.AssemblyQualifiedName + "|" + b.propertyName;
        static ToeCalibrationCurveProof[] ToeCalibrationCompareCurves(AnimationClip source, AnimationClip probe)
        {
            var a = AnimationUtility.GetCurveBindings(source).OrderBy(ToeCalibrationBinding, StringComparer.Ordinal).ToArray(); var b = AnimationUtility.GetCurveBindings(probe).OrderBy(ToeCalibrationBinding, StringComparer.Ordinal).ToArray();
            if (!a.Select(ToeCalibrationBinding).SequenceEqual(b.Select(ToeCalibrationBinding)) || a.Length != 130 || probe.length != source.length || probe.frameRate != source.frameRate || !probe.humanMotion || AnimationUtility.GetAnimationEvents(probe).Length != 0 || AnimationUtility.GetObjectReferenceCurveBindings(probe).Length != 0) throw new InvalidOperationException("Memory probe changed full Human clip identity/schema.");
            return a.Zip(b, (x, y) => { string before = ToeCalibrationCurveSignature(AnimationUtility.GetEditorCurve(source, x)), after = ToeCalibrationCurveSignature(AnimationUtility.GetEditorCurve(probe, y)); return new ToeCalibrationCurveProof { binding = ToeCalibrationBinding(x), sourceSignature = HeavyGeometryStringHash(before), probeSignature = HeavyGeometryStringHash(after), unchanged = before == after, selectedToe = x.propertyName == "Left Toes Up-Down" || x.propertyName == "Right Toes Up-Down" }; }).ToArray();
        }
        static string ToeCalibrationCurveSignature(AnimationCurve curve)
        {
            if (curve == null || curve.length == 0) throw new InvalidOperationException("Complete source curve required.");
            var text = new StringBuilder().Append(curve.preWrapMode).Append('|').Append(curve.postWrapMode);
            foreach (var key in curve.keys) foreach (float value in new[] { key.time, key.value, key.inTangent, key.outTangent, key.inWeight, key.outWeight, (float)key.weightedMode }) text.Append('|').Append(value.ToString("R", CultureInfo.InvariantCulture));
            return text.ToString();
        }
        static void ToeCalibrationEnvelope(float time, float length, out float value, out float derivative)
        {
            if (time <= 0 || time >= length) { value = derivative = 0; return; }
            if (time >= .235f && time <= .475f) { value = ToeCalibrationOffset; derivative = 0; return; }
            bool rise = time < .235f; float duration = rise ? .235f : length - .475f, u = rise ? time / duration : (length - time) / duration;
            value = ToeCalibrationOffset * u * u * (3 - 2 * u); derivative = ToeCalibrationOffset * 6 * u * (1 - u) / duration * (rise ? 1 : -1);
        }
        static void ToeCalibrationWrite(string path, ToeCalibrationReport report) => File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));

        sealed partial class Drawn
        {
            public ToeCalibrationPlane[] ToeCalibrationSelectIdlePlanes(Animator animator)
            {
                var result = new List<ToeCalibrationPlane>();
                foreach (string side in new[] { "l", "r" })
                {
                    var toe = animator.GetBoneTransform(side == "l" ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes); if (toe == null) throw new InvalidOperationException("Exact Human toe missing.");
                    var matches = parts.Where(part => part.renderer is SkinnedMeshRenderer skin && skin.bones.Contains(toe) && part.renderer.enabled && !part.renderer.forceRenderingOff && part.renderer.gameObject.activeInHierarchy).ToArray();
                    var candidates = new List<Tuple<Part, int[], Vector3[], float>>();
                    foreach (var part in matches)
                    {
                        var skin = (SkinnedMeshRenderer)part.renderer; skin.BakeMesh(part.scratch, true); part.scratch.GetVertices(part.vertices); int[] cohort = ToeCalibrationWeightedVertices(part, skin, toe);
                        if (cohort.Length < 3) continue; Vector3[] points = cohort.Select(index => part.renderer.localToWorldMatrix.MultiplyPoint3x4(part.vertices[index])).ToArray(); float min = points.Min(point => point.y);
                        int[] low = Enumerable.Range(0, cohort.Length).Where(index => Mathf.Abs(points[index].y - min) <= 1e-6f).ToArray();
                        for (int a = 0; a < low.Length; a++) for (int b = a + 1; b < low.Length; b++) for (int c = b + 1; c < low.Length; c++)
                        {
                            Vector3[] triangle = { points[low[a]], points[low[b]], points[low[c]] }; float area = Vector3.Cross(triangle[1] - triangle[0], triangle[2] - triangle[0]).sqrMagnitude;
                            if (area > 1e-12f) candidates.Add(Tuple.Create(part, new[] { cohort[low[a]], cohort[low[b]], cohort[low[c]] }, triangle, area));
                        }
                    }
                    if (candidates.Count == 0) throw new InvalidOperationException("No independent noncollinear Idle toe-weighted reference plane; stop instead of a bone-axis proxy.");
                    var chosen = candidates.OrderByDescending(item => item.Item4).ThenBy(item => item.Item1.path, StringComparer.Ordinal).First(); Vector3 normal = Vector3.Cross(chosen.Item3[1] - chosen.Item3[0], chosen.Item3[2] - chosen.Item3[0]).normalized; if (Vector3.Dot(normal, Vector3.up) < 0) normal = -normal;
                    if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(chosen.Item1.source, out string guid, out long id)) throw new InvalidOperationException("Persistent original shoe identity missing.");
                    result.Add(new ToeCalibrationPlane { side = side, renderer = chosen.Item1.path, guid = guid, id = id, indices = chosen.Item2, idleNormal = normal });
                }
                return result.ToArray();
            }
            public ToeCalibrationGeometry[] ToeCalibrationGeometry(Animator animator, ToeCalibrationPlane[] planes)
            {
                return planes.Select(plane =>
                {
                    var part = parts.Single(item => item.path == plane.renderer); var skin = (SkinnedMeshRenderer)part.renderer;
                    var toe = animator.GetBoneTransform(plane.side == "l" ? HumanBodyBones.LeftToes : HumanBodyBones.RightToes); var foot = animator.GetBoneTransform(plane.side == "l" ? HumanBodyBones.LeftFoot : HumanBodyBones.RightFoot);
                    skin.BakeMesh(part.scratch, true); part.scratch.GetVertices(part.vertices); int[] toes = ToeCalibrationWeightedVertices(part, skin, toe), feet = ToeCalibrationWeightedVertices(part, skin, foot);
                    if (toes.Length == 0 || feet.Length == 0) throw new InvalidOperationException("Independent actual foot and toe weight cohorts required.");
                    Func<int, Vector3> world = index => part.renderer.localToWorldMatrix.MultiplyPoint3x4(part.vertices[index]); var points = plane.indices.Select(world).ToArray(); var normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]).normalized;
                    if (normal.sqrMagnitude < .99f) throw new InvalidOperationException("Fixed geometric reference degenerated."); if (Vector3.Dot(normal, plane.idleNormal) < 0) normal = -normal;
                    int toeLow = toes.OrderBy(index => world(index).y).First(), footLow = feet.OrderBy(index => world(index).y).First(); Vector3 forward = toe.TransformDirection(Vector3.up).normalized;
                    return new ToeCalibrationGeometry { side = plane.side, renderer = part.path, meshGuid = plane.guid, meshLocalId = plane.id, toeCohortCount = toes.Length, footCohortCount = feet.Length, toeLowestVertex = toeLow, footLowestVertex = footLow, toeMinY = world(toeLow).y, footMinY = world(footLow).y, idlePlaneVertices = plane.indices, actualPlaneWorldVertices = points.Select(point => new V(point)).ToArray(), actualPlaneNormal = new V(normal), boneForwardWorld = new V(forward), geometricPlaneTiltDegrees = Vector3.Angle(normal, Vector3.up), boneForwardPitchDegrees = Mathf.Atan2(forward.y, new Vector2(forward.x, forward.z).magnitude) * Mathf.Rad2Deg };
                }).ToArray();
            }
            static int[] ToeCalibrationWeightedVertices(Part part, SkinnedMeshRenderer skin, Transform bone)
            {
                var bones = skin.bones;
                using (var counts = part.source.GetBonesPerVertex()) using (var weights = part.source.GetAllBoneWeights())
                {
                    if (counts.Length != part.source.vertexCount) throw new InvalidOperationException("Original complete variable skin weights required.");
                    var offsets = new int[counts.Length]; for (int index = 1; index < offsets.Length; index++) offsets[index] = offsets[index - 1] + counts[index - 1];
                    return part.indices.Where(vertex => { float influence = 0; for (int slot = 0; slot < counts[vertex]; slot++) { var weight = weights[offsets[vertex] + slot]; if (weight.boneIndex >= 0 && weight.boneIndex < bones.Length && bones[weight.boneIndex] == bone) influence += weight.weight; } return influence >= .5f; }).ToArray();
                }
            }
        }
    }
}
