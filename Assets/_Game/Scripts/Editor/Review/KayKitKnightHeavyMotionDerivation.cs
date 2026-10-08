using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    public static partial class KayKitHeroMotionDerivation
    {
        const string HeavyAuthorPath = "Assets/_Game/Scripts/Editor/Review/KayKitKnightHeavyMotionDerivation.cs";
        const string HeavyPrefix = Prefix + "Heavy.";
        const float HeavyChargeClipSeconds = .55f;
        const float HeavyReleaseClipSeconds = .82f;
        const float HeavySourceCut = 12f / 30f;
        const float HeavyDamageOpen = .235f;
        const float HeavyDamageClose = .475f;
        const float HeavyCurveTolerance = 1e-6f;
        const float HeavySourceDerivativeStep = .0001f;
        static readonly string[] HeavyNames =
        {
            "A_Review_Knight_HeavyCharge_RiseHold",
            "A_Review_Knight_HeavyRelease_Staged"
        };

        [Serializable] sealed class HeavyClipRow
        {
            public string role, curvePolicy;
            public ClipRow derived;
            public float duration, sourceStart, sourceEnd, expectedPresenterBaseSpeed;
            public bool hasFixedDomainStateDuration, unchangedBindingList, sourceRootSettingsPreserved;
            public int authoredSamples;
            public float[] authoredTimes, authoredSourceTimes, timelineAnchors, sourceAnchors;
            public float[] averageSourceRates, timelineStretchRatios, sourceTimeDerivatives;
            public float minimumMapDerivative, maximumResampleKeyError, maximumStartCurveError, maximumEndCurveError;
            public string[] settingsChanged;
        }

        [Serializable] sealed class HeavySeamRow
        {
            public string binding;
            public float sourceCutValue, chargeEndValue, releaseStartValue, absoluteValueDifference;
            public float sourceCentralDifferenceDerivative, chargeMappedApproachDerivative, releaseMappedDepartureDerivative;
            public float chargeAuthoredLeftDerivative, heldDerivative, releaseAuthoredRightDerivative;
            public float authoredApproachDepartureDifference, heldDepartureDifference;
        }

        [Serializable] sealed class HeavyReport
        {
            public string label, status, error, authorError, protectionError, startedUtc, finishedUtc;
            public string directory, assetDirectory, module, authorSha256;
            public string scope = "TWO fresh owned Human animation assets ONLY. Charge .55s maps native Chop 0..frame12, non-loop end-pose hold; release .82s maps the remaining source poses using the declared common stage map. Original full130 bindings/RootT-Q retained. No runtime/profile/controller/Actor/Motor/domain/authority/network/build/r4 replacement.";
            public string timingPolicy = "Damage window .235..475 is relative to HeavyAttack RELEASE entry, not Charge time. Existing Presenter Charge has duration0/base1; release length .82/domain .82 implies base1. Entry remains0 and original .08 fixed fade; actual first-delta error has NOT been measured or synchronized here. No F flags inherited.";
            public string resamplingPolicy = "Source values sampled at120Hz plus exact endpoints/stage anchors; authored curves have piecewise-linear secant tangents and ClampForever wrap. The release time map is C1 PCHIP, but resampled muscle curves are NOT C1 source-spline equivalent. C0 curve seam does NOT prove C1 pose/velocity, shortest-input blend or contact acceptance.";
            public string rootPolicy = "RootT/Q are Humanoid body/pose data, not limited to vertical motion. They are resampled without translation/flattening/lower-body freezing. Native orientation/Y/XZ extraction settings are retained; no guessed Light offset or static calibration. Existing production Presenter anchors its Animator and applyRootMotion=false; actual candidate Actor/rig movement is NOT evaluated by this author tool.";
            public string velocityPolicy = "Per-binding source derivative is a central finite difference h=.0001s, multiplied by each time-map boundary rate. Actual authored boundary derivatives are adjacent-key secants. Long-held Charge departure is0; source/curve velocity components are NOT bone or sword-contact velocities, and a nonzero difference is disclosed, not fitted away.";
            public string warmPolicy = "Describe/GetEditorCurve/settings are materialized BEFORE source full-JSON baselines; two consecutive warm serializations must match. Disk hashes before warming and after warming must also match. No source cache exception or post-failure baseline replacement.";
            public string poseMetricsStatus = "NOT MEASURED: actual original Human/.75 rig bone seam/facing, drawn body/cape/gear floor, short-input fades, Actor movement, contact, hit-stop, actual heavy inputs and NET. Native old floor values are not transferred to this new candidate.";
            public string shortestInputPolicy = "Same Dynamic input batch may enter HeavyCharge then HeavyAttack before Presenter observes Charge. One-Update short hold has not reached the cut pose. Neither is certified by the two assets' matching endpoints; long hold is static, not final breathing presentation.";
            public bool sourceWarmCompleted, diskUnchangedByWarm, sourcesUnchanged, sourceMemoryUnchanged;
            public bool originalScenesUnchanged, enabledBuildSceneClosureUnchanged, candidateNotInEnabledSceneClosure;
            public bool curveContractsPassed;
            public bool actualInputAccepted = false, contactAccepted = false, floorAccepted = false;
            public bool fullAbilityAccepted = false, productionAccepted = false;
            public ClipRow source;
            public string[] candidatePaths, enabledSceneDependencies;
            public HeavyClipRow[] variants;
            public HeavySeamRow[] seam;
            public float sourceCut, damageWindowOpen, damageWindowClose, releaseDomainDuration;
            public float minimumSeamValueDifference, maximumSeamValueDifference;
            public float maximumAuthoredApproachDepartureDifference, maximumHeldDepartureDifference;
            public MemoryRow[] sourceMemory;
            public FileRow[] beforeWarmDisk, protectedBefore, protectedAfter, ownedFiles;
        }

        /// <summary>Order: non-loop Charge rise/hold, fixed .82s Release/recovery; not production references.</summary>
        public static string[] GetHeavyCandidatePaths(string label)
        {
            ValidateLabel(label);
            return HeavyNames.Select(name => CandidateRoot + "/" + label + "/" + name + ".anim").ToArray();
        }

        /// <summary>Queues one fresh protected authoring job; duplicate labels return the retained evidence.</summary>
        public static string RequestHeavyCandidate(string uniqueLabel)
        {
            ValidateLabel(uniqueLabel);
            string key = HeavyPrefix + uniqueLabel;
            string prior = SessionState.GetString(key, "");
            if (prior.Length != 0)
            {
                if (!File.Exists(prior)) throw new IOException("Retained Heavy record missing: " + prior);
                return prior;
            }
            RequireIdle();
            if (pending) throw new InvalidOperationException("One motion authoring job at a time.");
            string active = SessionState.GetString(Prefix + "active", "");
            if (File.Exists(active))
            {
                var previous = JsonUtility.FromJson<HeavyReport>(File.ReadAllText(active));
                if (previous == null || previous.status == "queued" || previous.status == "running")
                    throw new InvalidOperationException("Inspect interrupted motion authoring job first: " + active);
            }
            string assets = CandidateRoot + "/" + uniqueLabel;
            string directory = Path.GetFullPath("Builds/ArtReview/kaykit-motion/" + uniqueLabel);
            if (Directory.Exists(assets) || File.Exists(assets + ".meta") || Directory.Exists(directory))
                throw new IOException("Fresh Heavy asset/evidence directories required; no overwrite.");
            if (!File.Exists(HeavyAuthorPath) || !File.Exists(HeavyAuthorPath + ".meta"))
                throw new FileNotFoundException("Heavy author source and meta must exist before calling the API.");
            Directory.CreateDirectory(directory);
            string record = directory + "/result.json";
            var report = new HeavyReport
            {
                label = uniqueLabel, status = "queued", startedUtc = DateTime.UtcNow.ToString("O"),
                directory = directory, assetDirectory = assets, candidatePaths = GetHeavyCandidatePaths(uniqueLabel),
                module = typeof(KayKitHeroMotionDerivation).Assembly.ManifestModule.ModuleVersionId.ToString(),
                authorSha256 = Hash(HeavyAuthorPath), sourceCut = HeavySourceCut,
                damageWindowOpen = HeavyDamageOpen, damageWindowClose = HeavyDamageClose,
                releaseDomainDuration = HeavyReleaseClipSeconds
            };
            WriteHeavy(record, report);
            SessionState.SetString(key, record);
            SessionState.SetString(Prefix + "active", record);
            pending = true;
            double deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= callback;
                try
                {
                    RequireIdle(); report.status = "running"; WriteHeavy(record, report);
                    AuthorHeavy(report); report.status = "completed";
                }
                catch (Exception ex) { report.status = "failed"; report.error = ex.ToString(); Debug.LogException(ex); }
                finally
                {
                    report.finishedUtc = DateTime.UtcNow.ToString("O");
                    try { WriteHeavy(record, report); }
                    finally { pending = false; SessionState.EraseString(Prefix + "active"); }
                }
            };
            EditorApplication.update += callback;
            return record;
        }

        static void AuthorHeavy(HeavyReport report)
        {
            var scenes = OriginalScenes();
            string sceneMemory = SceneMemory(scenes);
            var closure = BuildClosure();
            var extraProtected = new[] { HeavyAuthorPath };
            report.beforeWarmDisk = Protected(extraProtected);
            report.protectedBefore = report.beforeWarmDisk;
            Watch[] watches = Array.Empty<Watch>();
            var created = new List<AnimationClip>();
            try
            {
                var native = AssetDatabase.LoadAllAssetsAtPath(SourceFbx);
                var source = native.OfType<AnimationClip>().Single(clip => clip.name == "1H_Melee_Attack_Chop");
                var sourceAvatar = native.OfType<Avatar>().Single();
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssembledPrefab);
                var animator = prefab == null ? null : prefab.GetComponentInChildren<Animator>(true);
                if (animator == null || animator.avatar != sourceAvatar || !sourceAvatar.isHuman || !sourceAvatar.isValid)
                    throw new InvalidOperationException("Exact assembled original Human Avatar is required; no rig adapter.");

                // Typed curve/metadata access MUST precede the immutable full-memory baseline.
                report.source = Describe(source);
                var bindings = Bindings(source);
                var curves = bindings.ToDictionary(Key, binding => AnimationUtility.GetEditorCurve(source, binding));
                var sourceSettings = AnimationUtility.GetAnimationClipSettings(source);
                if (report.source.identity.guid != "33196f9d403b6e44ca18f3182d28beb6" ||
                    report.source.identity.localId != 3963595363326709879L ||
                    !report.source.humanMotion || bindings.Length != 130 ||
                    report.source.objectBindingCount != 0 || report.source.eventCount != 0 ||
                    Mathf.Abs(source.length - 32f / 30f) > HeavyCurveTolerance ||
                    bindings.Any(binding => binding.path != "" || binding.type != typeof(Animator)) ||
                    !bindings.Any(binding => binding.propertyName.StartsWith("RootT.", StringComparison.Ordinal)) ||
                    !bindings.Any(binding => binding.propertyName.StartsWith("RootQ.", StringComparison.Ordinal)) ||
                    curves.Any(pair => pair.Value == null) || sourceSettings.loopTime || sourceSettings.loopBlend)
                    throw new InvalidOperationException("Exact known original Chop Human/full130/non-loop source contract changed; do not scale or repair silently.");

                var materialObjects = report.beforeWarmDisk
                    .Where(file => file.path.StartsWith("Assets/", StringComparison.Ordinal) &&
                        (file.path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || file.path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase)))
                    .SelectMany(file => AssetDatabase.LoadAllAssetsAtPath(file.path).OfType<Material>()).Cast<Object>();
                var targets = materialObjects.Concat(new Object[] { source, sourceAvatar }).Distinct().ToArray();
                foreach (var target in targets)
                {
                    string warmJson = EditorJsonUtility.ToJson(target);
                    if (warmJson != EditorJsonUtility.ToJson(target))
                        throw new InvalidOperationException("Source full JSON is not a warm fixed point: " + target.name);
                }
                report.sourceWarmCompleted = true;
                watches = targets.Select(target => new Watch
                {
                    target = target,
                    row = new MemoryRow { identity = Id(target), beforeJson = EditorJsonUtility.ToJson(target) }
                }).ToArray();
                report.sourceMemory = watches.Select(watch => watch.row).ToArray();
                report.protectedBefore = Protected(extraProtected);
                report.diskUnchangedByWarm = SameHeavyFiles(report.beforeWarmDisk, report.protectedBefore);
                if (!report.diskUnchangedByWarm)
                    throw new InvalidOperationException("Source disk changed during warm-up; no baseline substitution.");

                var map = new HeavyReleaseStageMap(source.length);
                report.variants = new HeavyClipRow[2];
                for (int index = 0; index < 2; index++)
                {
                    float duration = index == 0 ? HeavyChargeClipSeconds : HeavyReleaseClipSeconds;
                    float start = index == 0 ? 0f : HeavySourceCut;
                    float end = index == 0 ? HeavySourceCut : source.length;
                    float[] anchors = index == 0 ? new[] { 0f, duration } : map.Times;
                    var times = Enumerable.Range(0, Mathf.CeilToInt(duration * 120) + 1)
                        .Select(sample => Mathf.Min(sample / 120f, duration)).Concat(anchors).Distinct().OrderBy(time => time).ToArray();
                    var sourceTimes = times.Select(time => index == 0
                        ? (time == duration ? HeavySourceCut : time * HeavySourceCut / duration)
                        : map.Evaluate(time)).ToArray();
                    var row = new HeavyClipRow
                    {
                        role = index == 0 ? "HeavyChargeRiseHold" : "HeavyAttackReleaseRecovery",
                        duration = duration, sourceStart = start, sourceEnd = end,
                        expectedPresenterBaseSpeed = 1f, hasFixedDomainStateDuration = index == 1,
                        authoredSamples = times.Length, authoredTimes = times, authoredSourceTimes = sourceTimes,
                        timelineAnchors = anchors.ToArray(), sourceAnchors = index == 0 ? new[] { 0f, HeavySourceCut } : map.Values.ToArray(),
                        averageSourceRates = index == 0 ? new[] { HeavySourceCut / duration } : map.AverageRates.ToArray(),
                        timelineStretchRatios = index == 0 ? new[] { duration / HeavySourceCut } : map.StretchRatios.ToArray(),
                        sourceTimeDerivatives = index == 0 ? new[] { HeavySourceCut / duration, HeavySourceCut / duration } : map.Slopes.ToArray(),
                        minimumMapDerivative = index == 0 ? HeavySourceCut / duration : map.MinimumDerivative,
                        curvePolicy = index == 0 ? "Linear source rise 0..frame12; non-loop endpoint clamps after .55, no repeat or micro-motion." : "Declared four stage anchors, mechanical monotone PCHIP, complete remaining source pose sequence; no contact fit."
                    };
                    report.variants[index] = row;
                    RequireFresh(report.candidatePaths[index]);
                    var clip = Object.Instantiate(source); created.Add(clip);
                    clip.name = HeavyNames[index]; clip.ClearCurves(); clip.frameRate = 120;
                    clip.wrapMode = WrapMode.ClampForever;
                    foreach (var binding in bindings)
                    {
                        var original = curves[Key(binding)];
                        var keys = sourceTimes.Select((sourceTime, sample) => new Keyframe(times[sample], original.Evaluate(sourceTime))).ToArray();
                        for (int sample = 0; sample < keys.Length; sample++)
                        {
                            var keyframe = keys[sample];
                            keyframe.inTangent = sample == 0 ? 0f : (keyframe.value - keys[sample - 1].value) / (keyframe.time - keys[sample - 1].time);
                            keyframe.outTangent = sample == keys.Length - 1 ? 0f : (keys[sample + 1].value - keyframe.value) / (keys[sample + 1].time - keyframe.time);
                            keyframe.weightedMode = WeightedMode.None;
                            if (!Finite(keyframe.value) || !Finite(keyframe.inTangent) || !Finite(keyframe.outTangent))
                                throw new InvalidOperationException("Nonfinite Heavy native sample/tangent: " + Key(binding));
                            keys[sample] = keyframe;
                        }
                        var authored = new AnimationCurve(keys) { preWrapMode = WrapMode.ClampForever, postWrapMode = WrapMode.ClampForever };
                        AnimationUtility.SetEditorCurve(clip, binding, authored);
                    }
                    var settings = AnimationUtility.GetAnimationClipSettings(source);
                    settings.startTime = 0; settings.stopTime = duration; settings.loopTime = false; settings.loopBlend = false;
                    AnimationUtility.SetAnimationClipSettings(clip, settings);
                    AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                    EnsureFolder(report.assetDirectory);
                    AssetDatabase.CreateAsset(clip, report.candidatePaths[index]);
                    EditorUtility.SetDirty(clip); AssetDatabase.SaveAssetIfDirty(clip);
                    row.derived = Describe(clip);
                    row.unchangedBindingList = row.derived.bindingSha256 == report.source.bindingSha256;
                    row.settingsChanged = Differences(report.source, row.derived);
                    row.sourceRootSettingsPreserved = row.settingsChanged.All(field => field == "startTime" || field == "stopTime" || field == "loopTime" || field == "loopBlend");
                    row.maximumStartCurveError = BoundaryError(source, clip, start, 0f);
                    row.maximumEndCurveError = BoundaryError(source, clip, end, duration);
                    foreach (var binding in bindings)
                    {
                        var authored = AnimationUtility.GetEditorCurve(clip, binding);
                        for (int sample = 0; sample < sourceTimes.Length; sample++)
                            row.maximumResampleKeyError = Mathf.Max(row.maximumResampleKeyError,
                                Mathf.Abs(authored.Evaluate(times[sample]) - curves[Key(binding)].Evaluate(sourceTimes[sample])));
                    }
                    if (!row.derived.humanMotion || !row.unchangedBindingList || !row.sourceRootSettingsPreserved ||
                        row.derived.objectBindingCount != 0 || row.derived.eventCount != 0 ||
                        Mathf.Abs(clip.length - duration) > HeavyCurveTolerance ||
                        row.maximumStartCurveError > HeavyCurveTolerance || row.maximumEndCurveError > HeavyCurveTolerance ||
                        row.maximumResampleKeyError > HeavyCurveTolerance)
                        throw new InvalidOperationException("Heavy owned curve/time/settings contract failed; preserve the candidate without correction sweep.");
                }
                report.seam = bindings.Select(binding => HeavySeam(binding, curves[Key(binding)], created[0], created[1], map.Slopes[0])).ToArray();
                report.minimumSeamValueDifference = report.seam.Min(row => row.absoluteValueDifference);
                report.maximumSeamValueDifference = report.seam.Max(row => row.absoluteValueDifference);
                report.maximumAuthoredApproachDepartureDifference = report.seam.Max(row => row.authoredApproachDepartureDifference);
                report.maximumHeldDepartureDifference = report.seam.Max(row => row.heldDepartureDifference);
                if (report.maximumSeamValueDifference > HeavyCurveTolerance)
                    throw new InvalidOperationException("Same-source Heavy curve seam failed; no endpoint-value repair.");
                report.curveContractsPassed = true;
            }
            catch (Exception ex) { report.authorError = ex.ToString(); throw; }
            finally
            {
                foreach (var clip in created) if (clip != null && !EditorUtility.IsPersistent(clip)) Object.DestroyImmediate(clip);
                foreach (var watch in watches)
                {
                    watch.row.afterJson = watch.target == null ? "DESTROYED" : EditorJsonUtility.ToJson(watch.target);
                    watch.row.unchanged = watch.row.beforeJson == watch.row.afterJson;
                }
                report.sourceMemoryUnchanged = report.sourceWarmCompleted && watches.Length > 0 && watches.All(watch => watch.row.unchanged);
                report.protectedAfter = Protected(extraProtected);
                report.sourcesUnchanged = SameHeavyFiles(report.beforeWarmDisk, report.protectedAfter) && SameHeavyFiles(report.protectedBefore, report.protectedAfter);
                report.originalScenesUnchanged = OriginalScenes().Select(scene => scene.handle).SequenceEqual(scenes.Select(scene => scene.IsValid() ? scene.handle : -1)) &&
                    scenes.All(scene => scene.IsValid() && scene.isLoaded && !scene.isDirty) && SceneMemory(scenes) == sceneMemory;
                report.enabledSceneDependencies = BuildClosure();
                report.enabledBuildSceneClosureUnchanged = closure.SequenceEqual(report.enabledSceneDependencies);
                report.candidateNotInEnabledSceneClosure = !report.enabledSceneDependencies.Any(path => path.StartsWith(report.assetDirectory + "/", StringComparison.Ordinal));
                report.ownedFiles = Directory.Exists(report.assetDirectory)
                    ? Directory.GetFiles(report.assetDirectory, "*", SearchOption.AllDirectories).Concat(new[] { report.assetDirectory + ".meta" })
                        .Where(File.Exists).Select(path => path.Replace('\\', '/')).OrderBy(path => path, StringComparer.Ordinal).Select(FileOf).ToArray()
                    : Array.Empty<FileRow>();
                if (!report.sourcesUnchanged || !report.sourceMemoryUnchanged || !report.originalScenesUnchanged ||
                    !report.enabledBuildSceneClosureUnchanged || !report.candidateNotInEnabledSceneClosure)
                {
                    report.protectionError = "Heavy author source/memory/scenes/entry closure protection failed; original failures and candidates retained, no automatic restoration.";
                    throw new InvalidOperationException(report.protectionError);
                }
            }
        }

        static HeavySeamRow HeavySeam(EditorCurveBinding binding, AnimationCurve source, AnimationClip charge, AnimationClip release, float releaseRate)
        {
            var left = AnimationUtility.GetEditorCurve(charge, binding).keys;
            var right = AnimationUtility.GetEditorCurve(release, binding).keys;
            float sourceDerivative = (source.Evaluate(HeavySourceCut + HeavySourceDerivativeStep) - source.Evaluate(HeavySourceCut - HeavySourceDerivativeStep)) / (2 * HeavySourceDerivativeStep);
            float approach = (left[left.Length - 1].value - left[left.Length - 2].value) / (left[left.Length - 1].time - left[left.Length - 2].time);
            float departure = (right[1].value - right[0].value) / (right[1].time - right[0].time);
            var row = new HeavySeamRow
            {
                binding = Key(binding), sourceCutValue = source.Evaluate(HeavySourceCut),
                chargeEndValue = left[left.Length - 1].value, releaseStartValue = right[0].value,
                absoluteValueDifference = Mathf.Abs(left[left.Length - 1].value - right[0].value),
                sourceCentralDifferenceDerivative = sourceDerivative,
                chargeMappedApproachDerivative = sourceDerivative * HeavySourceCut / HeavyChargeClipSeconds,
                releaseMappedDepartureDerivative = sourceDerivative * releaseRate,
                chargeAuthoredLeftDerivative = approach, heldDerivative = 0f, releaseAuthoredRightDerivative = departure,
                authoredApproachDepartureDifference = Mathf.Abs(approach - departure), heldDepartureDifference = Mathf.Abs(departure)
            };
            if (!Finite(sourceDerivative) || !Finite(approach) || !Finite(departure) || !Finite(row.absoluteValueDifference))
                throw new InvalidOperationException("Nonfinite Heavy curve seam diagnostic: " + row.binding);
            return row;
        }

        sealed class HeavyReleaseStageMap
        {
            public readonly float[] Times = { 0f, HeavyDamageOpen, HeavyDamageClose, HeavyReleaseClipSeconds };
            public readonly float[] Values, Slopes, AverageRates, StretchRatios;
            public readonly float MinimumDerivative;
            public HeavyReleaseStageMap(float sourceEnd)
            {
                if (!Finite(sourceEnd) || Mathf.Abs(sourceEnd - 32f / 30f) > HeavyCurveTolerance)
                    throw new InvalidOperationException("Original Chop endpoint changed; fixed stage design cannot be silently scaled.");
                Values = new[] { HeavySourceCut, .5f, 20f / 30f, sourceEnd };
                Slopes = new float[4]; AverageRates = new float[3]; StretchRatios = new float[3];
                var h = new float[3];
                for (int index = 0; index < 3; index++)
                {
                    h[index] = Times[index + 1] - Times[index];
                    AverageRates[index] = (Values[index + 1] - Values[index]) / h[index];
                    StretchRatios[index] = 1f / AverageRates[index];
                    if (!Finite(h[index]) || !Finite(AverageRates[index]) || h[index] <= 0 || AverageRates[index] <= 0)
                        throw new InvalidOperationException("Fixed Heavy stage anchors must strictly increase.");
                }
                Slopes[0] = HeavyEndpoint(h[0], h[1], AverageRates[0], AverageRates[1]);
                Slopes[3] = HeavyEndpoint(h[2], h[1], AverageRates[2], AverageRates[1]);
                for (int index = 1; index < 3; index++)
                {
                    float w1 = 2 * h[index] + h[index - 1], w2 = h[index] + 2 * h[index - 1];
                    Slopes[index] = (w1 + w2) / (w1 / AverageRates[index - 1] + w2 / AverageRates[index]);
                }
                MinimumDerivative = float.PositiveInfinity;
                for (int index = 0; index < 3; index++)
                {
                    float a = -6 * AverageRates[index] + 3 * (Slopes[index] + Slopes[index + 1]);
                    float b = 6 * AverageRates[index] - 4 * Slopes[index] - 2 * Slopes[index + 1];
                    MinimumDerivative = Mathf.Min(MinimumDerivative, Slopes[index], Slopes[index + 1]);
                    if (Mathf.Abs(a) > 1e-7f)
                    {
                        float u = -b / (2 * a);
                        if (u > 0 && u < 1) MinimumDerivative = Mathf.Min(MinimumDerivative, (a * u + b) * u + Slopes[index]);
                    }
                }
                if (!Finite(MinimumDerivative) || MinimumDerivative <= 0)
                    throw new InvalidOperationException("Fixed Heavy PCHIP map is not analytically strictly monotone.");
            }

            static float HeavyEndpoint(float h0, float h1, float d0, float d1)
            {
                float slope = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
                if (Mathf.Sign(slope) != Mathf.Sign(d0)) return 0f;
                return Mathf.Sign(d0) != Mathf.Sign(d1) && Mathf.Abs(slope) > Mathf.Abs(3 * d0) ? 3 * d0 : slope;
            }

            public float Evaluate(float time)
            {
                if (!Finite(time) || time < 0 || time > Times[3]) throw new ArgumentOutOfRangeException(nameof(time));
                for (int index = 0; index < 4; index++) if (time == Times[index]) return Values[index];
                int segment = time < Times[1] ? 0 : time < Times[2] ? 1 : 2;
                float h = Times[segment + 1] - Times[segment], u = (time - Times[segment]) / h;
                float u2 = u * u, u3 = u2 * u;
                float value = (2 * u3 - 3 * u2 + 1) * Values[segment] + (u3 - 2 * u2 + u) * h * Slopes[segment] +
                    (-2 * u3 + 3 * u2) * Values[segment + 1] + (u3 - u2) * h * Slopes[segment + 1];
                if (!Finite(value) || value < Values[segment] || value > Values[segment + 1])
                    throw new InvalidOperationException("Fixed Heavy map exceeded its native stage bounds; no clamp/repair.");
                return value;
            }
        }

        static bool SameHeavyFiles(FileRow[] before, FileRow[] after) =>
            JsonUtility.ToJson(new FileRows { rows = before }) == JsonUtility.ToJson(new FileRows { rows = after });
        static void WriteHeavy(string path, HeavyReport report) => File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
    }
}
