using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Emberfall.Editor.Setup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Five isolated motion variants; no production profile, pose correction or gameplay authority.</summary>
    public static partial class KayKitHeroMotionDerivation
    {
        public const string CandidateRoot = "Assets/_Game/Art/Review/KayKitHeroMotion";
        const string Project = "E:/unityproject/3DRPGdemo";
        const string Pack = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/03_Character_Kit/KayKit_Adventurers";
        public const string SourceFbx = Pack + "/addons/kaykit_character_pack_adventures/Characters/fbx/Knight.fbx";
        public const string AssembledPrefab = "Assets/_Game/Art/Review/KayKitHero/knight-assembly-20261003-a1/Review_Knight_Hero.prefab";
        const string Archive = "Builds/Delivery/ProjectEmberfall_0.9.6-presentation-20261003-r4.zip";
        const string ArchiveSha = "9EF0DB03551996772A066BE77C9DC1EE1BB8C8F743D196D36FE48C5D285E0860";
        const string Prefix = "Emberfall.KayKitHeroMotionDerivation.";
        static readonly string[] Names = { "A_Review_Knight_Light1", "A_Review_Knight_Light1Recovery", "A_Review_Knight_Light2", "A_Review_Knight_Light2Recovery", "A_Review_Knight_DeathA_YBake" };
        static readonly string[] SourceNames = { "1H_Melee_Attack_Chop", "1H_Melee_Attack_Slice_Diagonal", "Death_A" };
        static bool pending;
        [Serializable] sealed class Identity { public string path, guid; public long localId; }
        [Serializable] sealed class FileRow { public string path, sha256, guid; public long bytes; }
        [Serializable] sealed class FileRows { public FileRow[] rows; }
        [Serializable] sealed class Setting { public string name, value; }
        [Serializable] sealed class ClipRow { public Identity identity; public string name, bindingSha256, curveSha256, storedSha256; public float length, frameRate; public int bindingCount, objectBindingCount, eventCount; public bool humanMotion; public string[] rootBindings; public Setting[] settings; }
        [Serializable] sealed class Variant { public string role, changedSetting, cropPolicy; public ClipRow source, derived, correctionBaseline; public float sourceStart, sourceEnd, fractionalStartFrame, fractionalEndFrame, domainDuration, playbackRate, durationDividedByRate, durationEquationError, sourceSpanLengthError, maxStartCurveValueError, maxEndCurveValueError, facingErrorBefore, facingErrorAfter, facingOffset, sourceTimeWarp, maximumResampleKeyError, maximumConstantIdleCurveError; public int lowerBindingCount, upperBindingCount, authoredSamples; public bool hasDomainTiming, unchangedBindingList, unchangedFullCurveDigest, correctionCurvesUnchanged; public string[] settingsChanged, correctionSettingsChanged, lowerProperties, upperProperties; public float[] timelineAnchors, sourceAnchors, sourceTimeDerivatives, authoredTimes; public float minimumMapDerivative, plannedReleaseSourceTime, plannedForwardWindowSourceStart, plannedForwardWindowSourceEnd; }
        [Serializable] sealed class MemoryRow { public Identity identity; public string beforeJson, afterJson; public bool unchanged; }
        sealed class Watch { public Object target; public MemoryRow row; }
        [Serializable] sealed class Report
        {
            public string label, status, error, startedUtc, finishedUtc, directory, assetDirectory, module;
            public string scope = "Five owned .anim candidates ONLY; four equal-rate crops and one Death_A loopBlendPositionY change. No source FBX/importer/meta, production prefab/profile/controller, Actor/Motor/authority, runtime/test/RPC, build or r4 changes.";
            public string cropLimit = "Existing Crop preserves binding lists and source interior keys with shifted time. Non-key boundaries use Evaluate plus finite-difference tangent, not source weights; curve wrap modes are not copied. It also sets keepOriginalOrientation=true. Numerical boundary equality is NOT full curve, bone-pose, facing, contact or actual-input acceptance.";
            public string timingPolicy = "speed=clip.length/domainDuration; clip.length/speed=domainDuration. Presenter initial clamp .35..3 and recovery .35..4; AnimatorSpeedCoordinator.SetBase has no clamp. All four selected rates also must be <=3. Domain constants are not derived from the clip.";
            public string deathHypothesis = "loopBlendPositionY effect on final floating body is UNKNOWN. keepOriginalPositionY is not BakeIntoPose; all RootT/Q curves remain. Source settings and exactly one owned flag difference recorded; no root lift, translation, flattening or invented contact tolerance.";
            public string poseMetricsStatus = "NOT MEASURED by this authoring tool; NativeCharacterReview must evaluate source/variants on the actual assembled rig at 120Hz before any motion decision.";
            public string[] pendingMetricGroups = { "Maximum hips XZ deviation from settled Idle (old combat assertion <.4; no old pass transferred)", "Final hips XZ deviation from settled Idle (old assertion <.1; no old pass transferred)", "Original Animator anchor total position drift and rotation BEFORE presentation reset; no separate axis measurements", "Signed body/each drawn part floor, with Death .8..2.2s controlled hold; report raw contact status without invented tolerance" };
            public string[] gaps = { "Fractional cuts derive from current domain/native-length ratio, not old contact mapping. Equal rate does not align sword contact/damage windows.", "Four sampled light variants and Death settings do not cover melee AND knife/use-item actual inputs, named state suite, blending/hitstop, death-respawn, production feasibility or networking.", "Native a1 source-material memory failure remains unknown; later a2 protection pass does not retroapprove a1. Ranger/r4 stay playable production." };
            public bool sourcesUnchanged, sourceMemoryUnchanged, originalScenesUnchanged, enabledBuildSceneClosureUnchanged, candidateNotInEnabledSceneClosure, upperBodyThrow, stagedUpperBodyThrow;
            public string[] candidatePaths, baselinePaths, enabledSceneDependencies; public ClipRow[] sources; public Variant[] variants; public MemoryRow[] sourceMemory; public FileRow[] protectedBefore, protectedAfter, ownedFiles;
        }

        /// <summary>Order: L1Attack, L1Recovery, L2Attack, L2Recovery, DeathYBake.</summary>
        public static string[] GetCandidatePaths(string label) { ValidateLabel(label); return Names.Select(n => CandidateRoot + "/" + label + "/" + n + ".anim").ToArray(); }
        /// <summary>Queues once; duplicate labels return the retained record without repeating writes.</summary>
        public static string Request(string uniqueLabel)
        { return RequestInternal(uniqueLabel,null); }
        /// <summary>One deterministic .56s candidate; lower-body/RootT-Q fixed at original Idle t0, upper body resampled from native Throw.</summary>
        public static string RequestUpperBodyThrowCandidate(string uniqueLabel)
        { return RequestInternal(uniqueLabel, null, true); }
        /// <summary>One separately discussed stage mapping, never replaces the retained linear candidate.</summary>
        public static string RequestStagedUpperBodyThrowCandidate(string uniqueLabel)
        { return RequestInternal(uniqueLabel, null, true, true); }
        /// <summary>Four fresh clones of retained crops, per-interval static facing only; reuse the rejected death baseline without rewriting it.</summary>
        public static string RequestFacingCorrection(string uniqueLabel, string[] baselinePaths)
        {
            if(baselinePaths==null || baselinePaths.Length!=5 || baselinePaths.Distinct().Count()!=5 || baselinePaths.Any(p=>string.IsNullOrEmpty(p) || !p.StartsWith(CandidateRoot+"/",StringComparison.Ordinal) || !p.EndsWith(".anim",StringComparison.Ordinal)))throw new ArgumentException("Five retained owned baseline clips required.");
            return RequestInternal(uniqueLabel,baselinePaths.ToArray());
        }
        static string RequestInternal(string uniqueLabel,string[] baselinePaths, bool upperBodyThrow = false, bool stagedUpperBodyThrow = false)
        {
            ValidateLabel(uniqueLabel); string key = Prefix + uniqueLabel, prior = SessionState.GetString(key, "");
            if (prior.Length != 0) { if (!File.Exists(prior)) throw new IOException("Retained record missing: " + prior); return prior; }
            RequireIdle(); if (pending) throw new InvalidOperationException("One motion authoring job at a time.");
            string active = SessionState.GetString(Prefix + "active", ""); if (File.Exists(active)) { var old = JsonUtility.FromJson<Report>(File.ReadAllText(active)); if (old.status == "queued" || old.status == "running") throw new InvalidOperationException("Inspect interrupted job first: " + active); }
            string assets = CandidateRoot + "/" + uniqueLabel, directory = Path.GetFullPath("Builds/ArtReview/kaykit-motion/" + uniqueLabel);
            if (Directory.Exists(assets) || File.Exists(assets + ".meta") || Directory.Exists(directory)) throw new IOException("Fresh asset/evidence directories required; no overwrite.");
            Directory.CreateDirectory(directory); string record = directory + "/result.json";
            var report = new Report { label = uniqueLabel, status = "queued", startedUtc = DateTime.UtcNow.ToString("O"), directory = directory, assetDirectory = assets, candidatePaths = GetCandidatePaths(uniqueLabel), baselinePaths=baselinePaths, module = typeof(KayKitHeroMotionDerivation).Assembly.ManifestModule.ModuleVersionId.ToString() };
            report.upperBodyThrow = upperBodyThrow;
            report.stagedUpperBodyThrow = stagedUpperBodyThrow;
            if (upperBodyThrow)
            {
                report.candidatePaths = new[] { assets + "/A_Review_Knight_InPlace_UpperBody_Throw.anim" };
                report.scope = "ONE owned Human .anim candidate. Native Idle t0 supplies constant RootT/Q, FootT/Q and leg muscles; native Throw supplies spine/chest/neck/head/arms/hands on .56s timeline. Original Avatar/full rig, domain .22/.56, all source/importer/prefab/profile/controller/runtime/network/r4 bytes unchanged. No floor fit, root lift, offsets, Generic, production or whole-ability acceptance.";
                report.cropLimit = "Linear source-time mapping 0..native Throw length to0...56; 120Hz value resampling with piecewise-linear tangents, not full source spline equivalence. All lower curves exactly constant at original Idle t0. Frozen RootT is humanoid body data, NOT proof world feet stay fixed; measured geometry/actual input required.";
                report.timingPolicy = "Owned clip .56s at Presenter base rate1; original domain release .22/duration.56 unchanged. Source sampling warp is native length/.56, distinct from runtime Animator speed. No phase fitting to an already measured result.";
                report.deathHypothesis = "NOT APPLICABLE; all retained Death failures unchanged.";
                report.pendingMetricGroups = new[] { "Y0 CPU body/cape/gear raw nonpenetration screen (native Idle itself slightly negative; never grant it by adding capsule offset)", "NEW actual original-capsule F input: domain single release, full-fade+.15s stable and unchanged world>=0 floor gate", "Hand forward/previous two sampled Z increases plus visible knife transition same event frame; without knife it remains unmeasured", "Chest/hips relative facing, complete joint/grip/props/named-state/NET gates" };
                if (stagedUpperBodyThrow)
                {
                    report.cropLimit = "ONE fixed common monotone C1 PCHIP source-time map: (0,0),(.14,17/30),(.30,26/30),(.56,native end), from whole windup/forward/recovery gesture stages. All upper93 share the map; 120Hz value resampling plus exact stage anchors still uses piecewise-linear authored muscle curves, NOT full C1 source-spline equivalence. Lower37 constant Idle0, endpoints/source sequence preserved; no local hand-value repair or parameter sweep.";
                    report.timingPolicy = "Owned .56 at runtime base1; domain .22 release remains. Source phase averages 4.04762/1.875/1.92308; local derivatives are mechanically computed and retained, not fitted to first-frame delta. Planned .22 +/- .033 source interval is a design margin, not observed continuous-hand/bridge acceptance. Candidate-only offline entry opt-in is verified separately.";
                }
            }
            if(baselinePaths!=null){report.candidatePaths[4]=baselinePaths[4];report.scope="Four fresh owned crop clones, only per-interval orientationOffsetY calibrated against original sourceStart. Reuse the retained rejected death clip for comparison, never rewrite it. No curve/root/body/production/domain changes.";}
            Write(record, report); SessionState.SetString(key, record); SessionState.SetString(Prefix + "active", record); pending = true;
            double deadline = EditorApplication.timeSinceStartup + 180; EditorApplication.CallbackFunction callback = null;
            callback = () => { if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < deadline) return; EditorApplication.update -= callback;
                try { RequireIdle(); report.status = "running"; Write(record, report); if (report.upperBodyThrow) AuthorUpperBodyThrow(report); else Author(report); report.status = "completed"; }
                catch (Exception ex) { report.status = "failed"; report.error = ex.ToString(); Debug.LogException(ex); }
                finally { report.finishedUtc = DateTime.UtcNow.ToString("O"); Write(record, report); pending = false; SessionState.EraseString(Prefix + "active"); } };
            EditorApplication.update += callback; return record;
        }

        static bool LowerThrowProperty(string name) => name.StartsWith("RootT.", StringComparison.Ordinal) || name.StartsWith("RootQ.", StringComparison.Ordinal)
            || Regex.IsMatch(name, "^(Left|Right)Foot[TQ]\\.") || Regex.IsMatch(name, "^(Left|Right) (Upper Leg|Lower Leg|Foot|Toes) ");
        static bool UpperThrowProperty(string name) => Regex.IsMatch(name, "^(Spine|Chest|UpperChest|Neck|Head|Jaw) ") || Regex.IsMatch(name, "^(Left|Right) (Eye|Shoulder|Arm|Forearm|Hand) ") || Regex.IsMatch(name, "^(Left|Right)Hand([TQ]\\.|\\.)");
        static void AuthorUpperBodyThrow(Report report)
        {
            var retained = report.stagedUpperBodyThrow ? new[] { CandidateRoot + "/knight-upperbody-throw-author-20261003-a1/A_Review_Knight_InPlace_UpperBody_Throw.anim" } : null;
            var scenes = OriginalScenes(); string sceneMemory = SceneMemory(scenes); var closure = BuildClosure(); report.protectedBefore = Protected(retained);
            var sources = AssetDatabase.LoadAllAssetsAtPath(SourceFbx).OfType<AnimationClip>().Where(c => c.name == "Idle" || c.name == "Throw").ToDictionary(c => c.name, StringComparer.Ordinal);
            var idle = sources["Idle"]; var source = sources["Throw"];
            var watches = report.protectedBefore.Where(f => f.path.StartsWith("Assets/", StringComparison.Ordinal) && (f.path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || f.path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))).SelectMany(f => AssetDatabase.LoadAllAssetsAtPath(f.path).OfType<Material>()).Cast<Object>().Concat(new Object[] { idle, source }).Distinct().Select(o => new Watch { target = o, row = new MemoryRow { identity = Id(o), beforeJson = EditorJsonUtility.ToJson(o) } }).ToArray(); report.sourceMemory = watches.Select(w => w.row).ToArray();
            AnimationClip clip = null;
            try
            {
                report.sources = new[] { Describe(idle), Describe(source) }; var bindings = Bindings(idle);
                if (!idle.isHumanMotion || !source.isHumanMotion || report.sources.Any(s => s.objectBindingCount != 0 || s.eventCount != 0) || !Finite(source.length) || source.length <= 0 || report.sources[0].bindingSha256 != report.sources[1].bindingSha256 || bindings.Any(b => b.path != "" || b.type != typeof(Animator) || (!LowerThrowProperty(b.propertyName) && !UpperThrowProperty(b.propertyName)))) throw new InvalidOperationException("Exact known same native Human binding contract required before any asset creation.");
                var lower = bindings.Where(b => LowerThrowProperty(b.propertyName)).ToArray(); var upper = bindings.Where(b => UpperThrowProperty(b.propertyName)).ToArray();
                if (lower.Length != 37 || upper.Length != 93 || bindings.Length != 130 || lower.Intersect(upper).Any()) throw new InvalidOperationException("Actual native 37 lower/93 upper partition changed; do not guess new channels.");
                const float duration = .56f; int steps = Mathf.CeilToInt(duration * 120); float warp = source.length / duration;
                var map = report.stagedUpperBodyThrow ? new ThrowStageMap(source.length) : null;
                var times = Enumerable.Range(0, steps + 1).Select(i => Mathf.Min(i / 120f, duration)).Concat(map == null ? Array.Empty<float>() : map.Times).Distinct().OrderBy(t => t).ToArray();
                var row = new Variant { role = "InPlaceUpperBodyThrow", source = report.sources[1], correctionBaseline = report.sources[0], sourceStart = 0, sourceEnd = source.length, domainDuration = duration, hasDomainTiming = true, sourceTimeWarp = warp, lowerBindingCount = lower.Length, upperBindingCount = upper.Length, lowerProperties = lower.Select(b => b.propertyName).ToArray(), upperProperties = upper.Select(b => b.propertyName).ToArray(), authoredSamples = steps + 1, cropPolicy = report.cropLimit };
                row.authoredSamples = times.Length; row.authoredTimes = times;
                if (map != null) { row.timelineAnchors = map.Times; row.sourceAnchors = map.Values; row.sourceTimeDerivatives = map.Slopes; row.minimumMapDerivative = map.MinimumDerivative; row.plannedReleaseSourceTime = map.Evaluate(.22f); row.plannedForwardWindowSourceStart = map.Evaluate(.187f); row.plannedForwardWindowSourceEnd = map.Evaluate(.253f); }
                report.variants = new[] { row }; clip = Object.Instantiate(idle); clip.name = "A_Review_Knight_InPlace_UpperBody_Throw"; clip.ClearCurves(); clip.frameRate = 120;
                foreach (var b in bindings)
                {
                    var curve = AnimationUtility.GetEditorCurve(LowerThrowProperty(b.propertyName) ? idle : source, b); if (curve == null) throw new InvalidOperationException("Missing original curve: " + Key(b));
                    AnimationCurve authored;
                    if (LowerThrowProperty(b.propertyName)) { float v = curve.Evaluate(0); if (!Finite(v)) throw new InvalidOperationException("Nonfinite fixed Idle value."); authored = new AnimationCurve(new Keyframe(0, v, 0, 0), new Keyframe(duration, v, 0, 0)); }
                    else
                    {
                        var keys = new Keyframe[times.Length];
                        for (int i = 0; i < times.Length; i++) { float time = times[i], value = curve.Evaluate(map != null ? map.Evaluate(time) : time == duration ? source.length : time * warp); if (!Finite(value)) throw new InvalidOperationException("Nonfinite resampled native value."); keys[i] = new Keyframe(time, value); }
                        for (int i = 0; i < keys.Length; i++) { var k = keys[i]; k.inTangent = i == 0 ? 0 : (k.value - keys[i - 1].value) / (k.time - keys[i - 1].time); k.outTangent = i == keys.Length - 1 ? 0 : (keys[i + 1].value - k.value) / (keys[i + 1].time - k.time); k.weightedMode = WeightedMode.None; if (!Finite(k.inTangent) || !Finite(k.outTangent)) throw new InvalidOperationException("Invalid resampled tangent/time."); keys[i] = k; }
                        authored = new AnimationCurve(keys);
                    }
                    authored.preWrapMode = authored.postWrapMode = WrapMode.ClampForever; AnimationUtility.SetEditorCurve(clip, b, authored);
                }
                var settings = AnimationUtility.GetAnimationClipSettings(idle); settings.startTime = 0; settings.stopTime = duration; settings.loopTime = false; settings.loopBlend = false; AnimationUtility.SetAnimationClipSettings(clip, settings); AnimationUtility.SetAnimationEvents(clip, Array.Empty<AnimationEvent>());
                RequireFresh(report.candidatePaths[0]); EnsureFolder(report.assetDirectory); AssetDatabase.CreateAsset(clip, report.candidatePaths[0]); EditorUtility.SetDirty(clip); AssetDatabase.SaveAssetIfDirty(clip); row.derived = Describe(clip); row.playbackRate = clip.length / duration; row.durationDividedByRate = clip.length / row.playbackRate; row.durationEquationError = Mathf.Abs(row.durationDividedByRate - duration); Complete(row);
                foreach (var b in lower) { var a = AnimationUtility.GetEditorCurve(clip, b); float v = AnimationUtility.GetEditorCurve(idle, b).Evaluate(0); foreach (var k in a.keys) row.maximumConstantIdleCurveError = Mathf.Max(row.maximumConstantIdleCurveError, Mathf.Abs(k.value - v), Mathf.Abs(k.inTangent), Mathf.Abs(k.outTangent)); }
                foreach (var b in upper)
                {
                    var authored = AnimationUtility.GetEditorCurve(clip, b); var original = AnimationUtility.GetEditorCurve(source, b);
                    foreach (var k in authored.keys) row.maximumResampleKeyError = Mathf.Max(row.maximumResampleKeyError, Mathf.Abs(k.value - original.Evaluate(map != null ? map.Evaluate(k.time) : k.time == duration ? source.length : k.time * warp)));
                    row.maxStartCurveValueError = Mathf.Max(row.maxStartCurveValueError, Mathf.Abs(authored.Evaluate(0) - original.Evaluate(0)));
                    row.maxEndCurveValueError = Mathf.Max(row.maxEndCurveValueError, Mathf.Abs(authored.Evaluate(duration) - original.Evaluate(source.length)));
                }
                if (!row.derived.humanMotion || !row.unchangedBindingList || Mathf.Abs(clip.length - duration) > 1e-6f || Mathf.Abs(row.playbackRate - 1) > 1e-6f || row.maximumConstantIdleCurveError != 0 || row.maximumResampleKeyError > 1e-6f || row.derived.objectBindingCount != 0 || row.derived.eventCount != 0) throw new InvalidOperationException("Owned upper-body curve/time contract failed; preserve candidate, no correction sweep.");
            }
            finally
            {
                if (clip != null && !EditorUtility.IsPersistent(clip)) Object.DestroyImmediate(clip);
                foreach (var watch in watches) { watch.row.afterJson = watch.target == null ? "DESTROYED" : EditorJsonUtility.ToJson(watch.target); watch.row.unchanged = watch.row.beforeJson == watch.row.afterJson; }
                report.protectedAfter = Protected(retained); report.sourcesUnchanged = JsonUtility.ToJson(new FileRows { rows = report.protectedBefore }) == JsonUtility.ToJson(new FileRows { rows = report.protectedAfter }); report.sourceMemoryUnchanged = watches.All(w => w.row.unchanged);
                report.originalScenesUnchanged = OriginalScenes().Select(s => s.handle).SequenceEqual(scenes.Select(s => s.IsValid() ? s.handle : -1)) && scenes.All(s => s.IsValid() && s.isLoaded && !s.isDirty) && SceneMemory(scenes) == sceneMemory;
                report.enabledSceneDependencies = BuildClosure(); report.enabledBuildSceneClosureUnchanged = closure.SequenceEqual(report.enabledSceneDependencies); report.candidateNotInEnabledSceneClosure = !report.enabledSceneDependencies.Any(p => p.StartsWith(report.assetDirectory + "/", StringComparison.Ordinal));
                report.ownedFiles = Directory.Exists(report.assetDirectory) ? Directory.GetFiles(report.assetDirectory, "*", SearchOption.AllDirectories).Concat(new[] { report.assetDirectory + ".meta" }).Where(File.Exists).OrderBy(p => p, StringComparer.Ordinal).Select(FileOf).ToArray() : Array.Empty<FileRow>();
                if (!report.sourcesUnchanged || !report.sourceMemoryUnchanged || !report.originalScenesUnchanged || !report.enabledBuildSceneClosureUnchanged || !report.candidateNotInEnabledSceneClosure) throw new InvalidOperationException("Upper-body author source/memory/scenes/closure protection failed; no auto-restoration.");
            }
        }

        // PCHIP derivative rules depend only on the four declared gesture anchors.
        // A monotone time map cannot remove a backward hand lobe in the source gesture.
        sealed class ThrowStageMap
        {
            public readonly float[] Times = { 0, .14f, .30f, .56f };
            public readonly float[] Values, Slopes;
            public readonly float MinimumDerivative;
            public ThrowStageMap(float sourceEnd)
            {
                if (Mathf.Abs(sourceEnd - 41f / 30f) > 1e-6f) throw new InvalidOperationException("Native Throw endpoint changed; stage design must be reconsidered, not scaled silently.");
                Values = new[] { 0f, 17f / 30f, 26f / 30f, sourceEnd }; Slopes = new float[4];
                var h = new float[3]; var d = new float[3];
                for (int i = 0; i < 3; i++) { h[i] = Times[i + 1] - Times[i]; d[i] = (Values[i + 1] - Values[i]) / h[i]; if (!(h[i] > 0 && d[i] > 0)) throw new InvalidOperationException("Strictly increasing stage anchors required."); }
                Slopes[0] = Endpoint(h[0], h[1], d[0], d[1]); Slopes[3] = Endpoint(h[2], h[1], d[2], d[1]);
                for (int i = 1; i < 3; i++) { float w1 = 2 * h[i] + h[i - 1], w2 = h[i] + 2 * h[i - 1]; Slopes[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i]); }
                MinimumDerivative = float.PositiveInfinity;
                for (int i = 0; i < 3; i++)
                {
                    // The Hermite derivative is a quadratic in normalized interval time.
                    float a = -6 * d[i] + 3 * (Slopes[i] + Slopes[i + 1]), b = 6 * d[i] - 4 * Slopes[i] - 2 * Slopes[i + 1];
                    MinimumDerivative = Mathf.Min(MinimumDerivative, Slopes[i], Slopes[i + 1]);
                    if (Mathf.Abs(a) > 1e-7f) { float u = -b / (2 * a); if (u > 0 && u < 1) MinimumDerivative = Mathf.Min(MinimumDerivative, (a * u + b) * u + Slopes[i]); }
                }
                if (!Finite(MinimumDerivative) || MinimumDerivative <= 0) throw new InvalidOperationException("Fixed map must be analytically strictly monotone.");
            }
            static float Endpoint(float h0, float h1, float d0, float d1)
            {
                float m = ((2 * h0 + h1) * d0 - h0 * d1) / (h0 + h1);
                if (Mathf.Sign(m) != Mathf.Sign(d0)) return 0;
                return Mathf.Sign(d0) != Mathf.Sign(d1) && Mathf.Abs(m) > Mathf.Abs(3 * d0) ? 3 * d0 : m;
            }
            public float Evaluate(float time)
            {
                if (!Finite(time) || time < 0 || time > Times[3]) throw new ArgumentOutOfRangeException(nameof(time));
                for (int i = 0; i < 4; i++) if (time == Times[i]) return Values[i];
                int j = time < Times[1] ? 0 : time < Times[2] ? 1 : 2;
                float h = Times[j + 1] - Times[j], u = (time - Times[j]) / h, u2 = u * u, u3 = u2 * u;
                float value = (2 * u3 - 3 * u2 + 1) * Values[j] + (u3 - 2 * u2 + u) * h * Slopes[j] + (-2 * u3 + 3 * u2) * Values[j + 1] + (u3 - u2) * h * Slopes[j + 1];
                if (!Finite(value) || value < Values[j] || value > Values[j + 1]) throw new InvalidOperationException("Invalid fixed phase map value; no clamp/repair.");
                return value;
            }
        }

        static void Author(Report report)
        {
            Scene[] scenes = OriginalScenes(); string sceneMemory = SceneMemory(scenes); string[] closure = BuildClosure();
            report.protectedBefore = Protected(report.baselinePaths); var clips = SourceNames.Select(n => AssetDatabase.LoadAllAssetsAtPath(SourceFbx).OfType<AnimationClip>().Single(c => c.name == n)).ToArray();
            var baselines=(report.baselinePaths??Array.Empty<string>()).Select(p=>AssetDatabase.LoadAssetAtPath<AnimationClip>(p)??throw new FileNotFoundException(p)).ToArray();
            var watches = report.protectedBefore.Where(f => f.path.StartsWith("Assets/", StringComparison.Ordinal) && (f.path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || f.path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))).SelectMany(f => AssetDatabase.LoadAllAssetsAtPath(f.path).OfType<Material>()).Cast<Object>().Concat(clips).Concat(baselines).Distinct().Select(o => new Watch { target = o, row = new MemoryRow { identity = Id(o), beforeJson = EditorJsonUtility.ToJson(o) } }).ToArray(); report.sourceMemory = watches.Select(w => w.row).ToArray();
            try
            {
                if (AssetDatabase.LoadAssetAtPath<GameObject>(AssembledPrefab) == null) throw new FileNotFoundException(AssembledPrefab);
                report.sources = clips.Select(Describe).ToArray();
                foreach (var source in report.sources) if (!source.humanMotion || source.objectBindingCount != 0 || source.eventCount != 0 || source.length <= 0 || !Finite(source.length) || !source.rootBindings.Any(p => p.Contains("RootT.")) || !source.rootBindings.Any(p => p.Contains("RootQ."))) throw new InvalidOperationException("Native humanoid RootT/Q with no object curves/events required before any owned asset creation: " + source.name);
                var deathSettings = AnimationUtility.GetAnimationClipSettings(clips[2]); if (deathSettings.loopBlendPositionY || !deathSettings.keepOriginalPositionY) throw new InvalidOperationException("Expected native Death_A baseline changed; do not guess settings.");
                float[] durations = { .28f, .30f, .30f, .32f }; float cut1 = clips[0].length * .28f / .58f, cut2 = clips[1].length * .30f / .62f; float[] starts = { 0, cut1, 0, cut2 }, ends = { cut1, clips[0].length, cut2, clips[1].length };
                for (int i = 0; i < 4; i++) if (!Finite(starts[i]) || !Finite(ends[i]) || starts[i] < 0 || ends[i] <= starts[i] || ends[i] > clips[i / 2].length) throw new InvalidOperationException("Invalid fractional cut before asset creation.");
                EnsureFolder(report.assetDirectory); var variants = new List<Variant>(); report.variants = variants.ToArray();
                using(var facing=baselines.Length==0?null:new FacingCalibration())
                {
                for (int i = 0; i < 4; i++)
                {
                    var source = clips[i / 2]; RequireFresh(report.candidatePaths[i]); AnimationClip clip;
                    if(baselines.Length==0)clip=HumanoidClipDerivation.Crop(source,starts[i],ends[i],report.candidatePaths[i]);
                    else{clip=Object.Instantiate(baselines[i]);clip.name=Names[i];AssetDatabase.CreateAsset(clip,report.candidatePaths[i]);}
                    var row = new Variant { role = new[] { "L1Attack", "L1Recovery", "L2Attack", "L2Recovery" }[i], source = report.sources[i / 2], derived = Describe(clip), sourceStart = starts[i], sourceEnd = ends[i], fractionalStartFrame = starts[i] * source.frameRate, fractionalEndFrame = ends[i] * source.frameRate, domainDuration = durations[i], hasDomainTiming = true, cropPolicy = report.cropLimit };
                    variants.Add(row);report.variants=variants.ToArray();
                    if(facing!=null){row.correctionBaseline=Describe(baselines[i]);facing.Apply(source,starts[i],clip,row);}
                    AssetDatabase.SaveAssetIfDirty(clip);row.derived=Describe(clip);
                    if(facing!=null){row.correctionCurvesUnchanged=row.correctionBaseline.curveSha256==row.derived.curveSha256;row.correctionSettingsChanged=Differences(row.correctionBaseline,row.derived);if(!row.correctionCurvesUnchanged || !row.correctionSettingsChanged.SequenceEqual(new[]{"orientationOffsetY"}))throw new InvalidOperationException("Facing correction changed more than one static setting.");}
                    row.playbackRate = clip.length / durations[i]; row.durationDividedByRate = clip.length / row.playbackRate; row.durationEquationError = Mathf.Abs(row.durationDividedByRate - durations[i]); row.sourceSpanLengthError = Mathf.Abs(clip.length - (ends[i] - starts[i])); row.maxStartCurveValueError = BoundaryError(source, clip, starts[i], 0); row.maxEndCurveValueError = BoundaryError(source, clip, ends[i], clip.length); Complete(row); report.variants = variants.ToArray();
                    if (!Finite(row.playbackRate) || row.playbackRate < .35f || row.playbackRate > 3 || row.durationEquationError > .000001f || row.sourceSpanLengthError > .000001f || !row.unchangedBindingList || !row.derived.humanMotion) throw new InvalidOperationException("Owned light metadata contract failed: " + row.role);
                }
                }
                bool reuseDeath=baselines.Length!=0;if(!reuseDeath)RequireFresh(report.candidatePaths[4]); var death = reuseDeath?baselines[4]:Object.Instantiate(clips[2]);
                try { if(!reuseDeath){death.name = Names[4]; var settings = AnimationUtility.GetAnimationClipSettings(death); settings.loopBlendPositionY = true; AnimationUtility.SetAnimationClipSettings(death, settings); AssetDatabase.CreateAsset(death, report.candidatePaths[4]); EditorUtility.SetDirty(death); AssetDatabase.SaveAssetIfDirty(death);}
                    var row = new Variant { role = "DeathYBake", changedSetting = "loopBlendPositionY: false -> true", source = report.sources[2], derived = Describe(death), sourceStart = 0, sourceEnd = clips[2].length, fractionalStartFrame = 0, fractionalEndFrame = clips[2].length * clips[2].frameRate, hasDomainTiming = false, cropPolicy = "Complete native clone, not a crop; sole settings flag change; no domain death timing invented." }; Complete(row); variants.Add(row); report.variants = variants.ToArray();
                    if (!row.unchangedBindingList || !row.unchangedFullCurveDigest || !row.derived.humanMotion || !row.settingsChanged.SequenceEqual(new[] { "loopBlendPositionY" }) || row.derived.length != row.source.length) throw new InvalidOperationException("Death clone is not a one-flag/full-curve comparison."); }
                finally { if (!EditorUtility.IsPersistent(death)) Object.DestroyImmediate(death); }
                report.ownedFiles = Directory.GetFiles(report.assetDirectory, "*", SearchOption.AllDirectories).Concat(new[] { report.assetDirectory + ".meta" }).Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).Select(FileOf).ToArray();
            }
            finally
            {
                foreach (var watch in watches) { watch.row.afterJson = watch.target == null ? "DESTROYED" : EditorJsonUtility.ToJson(watch.target); watch.row.unchanged = watch.row.beforeJson == watch.row.afterJson; }
                report.protectedAfter = Protected(report.baselinePaths); report.sourcesUnchanged = JsonUtility.ToJson(new FileRows { rows = report.protectedBefore }) == JsonUtility.ToJson(new FileRows { rows = report.protectedAfter }); report.sourceMemoryUnchanged = watches.All(w => w.row.unchanged);
                report.originalScenesUnchanged = OriginalScenes().Select(s => s.handle).SequenceEqual(scenes.Select(s => s.handle)) && scenes.All(s => s.IsValid() && s.isLoaded && !s.isDirty) && SceneMemory(scenes) == sceneMemory;
                report.enabledSceneDependencies = BuildClosure(); report.enabledBuildSceneClosureUnchanged = closure.SequenceEqual(report.enabledSceneDependencies); report.candidateNotInEnabledSceneClosure = !report.enabledSceneDependencies.Any(p => p.StartsWith(report.assetDirectory + "/", StringComparison.Ordinal));
                if (!report.sourcesUnchanged || !report.sourceMemoryUnchanged || !report.originalScenesUnchanged || !report.enabledBuildSceneClosureUnchanged || !report.candidateNotInEnabledSceneClosure) throw new InvalidOperationException("Protected source/memory/scenes/entry closure changed; inspect retained raw evidence, never auto-restore source.");
            }
        }

        static string[] Differences(ClipRow a,ClipRow b)=>a.settings.Select(s=>s.name).Union(b.settings.Select(s=>s.name)).OrderBy(n=>n,StringComparer.Ordinal).Where(n=>a.settings.SingleOrDefault(s=>s.name==n)?.value!=b.settings.SingleOrDefault(s=>s.name==n)?.value).ToArray();
        static void Complete(Variant row) { row.unchangedBindingList = row.source.bindingSha256 == row.derived.bindingSha256; row.unchangedFullCurveDigest = row.source.curveSha256 == row.derived.curveSha256; row.settingsChanged = Differences(row.source,row.derived); }
        sealed class FacingCalibration:IDisposable
        {
            readonly PreviewRenderUtility preview; readonly Animator animator; readonly Vector3 anchor; readonly Quaternion rotation;
            public FacingCalibration(){preview=new PreviewRenderUtility();try{var model=preview.InstantiatePrefabInScene(AssetDatabase.LoadAssetAtPath<GameObject>(AssembledPrefab));animator=model.GetComponentInChildren<Animator>(true);if(animator==null || AssetDatabase.GetAssetPath(animator.avatar)!=SourceFbx)throw new InvalidOperationException("Original Knight Avatar required.");animator.runtimeAnimatorController=null;animator.applyRootMotion=false;animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;anchor=animator.transform.position;rotation=animator.transform.rotation;}catch{preview.Cleanup();throw;}}
            Vector3 Face(AnimationClip clip,float time)
            {
                var graph=PlayableGraph.Create("Knight interval static facing");try{graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);var playable=AnimationClipPlayable.Create(graph,clip);playable.SetApplyFootIK(false);playable.SetApplyPlayableIK(false);AnimationPlayableOutput.Create(graph,"Pose",animator).SetSourcePlayable(playable);graph.Play();playable.SetTime(time);graph.Evaluate(0);
                    if(Vector3.Distance(anchor,animator.transform.position)>1e-6f || Quaternion.Angle(rotation,animator.transform.rotation)>.001f)throw new InvalidOperationException("Raw Animator anchor changed; not resetting it.");var forward=Vector3.ProjectOnPlane(animator.GetBoneTransform(HumanBodyBones.Hips).forward,Vector3.up);if(!Finite(forward.sqrMagnitude)||forward.sqrMagnitude<1e-6f)throw new InvalidOperationException("Undefined planar body facing.");return forward.normalized;
                }finally{graph.Destroy();}
            }
            public void Apply(AnimationClip source,float start,AnimationClip derived,Variant row)
            {
                Vector3 reference=Face(source,start),before=Face(derived,0);float delta=Vector3.SignedAngle(before,reference,Vector3.up),originalOffset=AnimationUtility.GetAnimationClipSettings(derived).orientationOffsetY,bestOffset=originalOffset,bestError=float.PositiveInfinity;row.facingErrorBefore=Vector3.Angle(before,reference);
                // Only the two API sign conventions, not a parameter sweep or a floor-driven correction.
                foreach(float offset in new[]{originalOffset+delta,originalOffset-delta}){var settings=AnimationUtility.GetAnimationClipSettings(derived);settings.orientationOffsetY=offset;AnimationUtility.SetAnimationClipSettings(derived,settings);float error=Vector3.Angle(Face(derived,0),reference);if(error<bestError){bestError=error;bestOffset=offset;}}
                if(!Finite(bestError)||bestError>.05f)throw new InvalidOperationException("Interval source facing could not be preserved.");var final=AnimationUtility.GetAnimationClipSettings(derived);final.orientationOffsetY=bestOffset;AnimationUtility.SetAnimationClipSettings(derived,final);EditorUtility.SetDirty(derived);row.facingOffset=bestOffset;row.facingErrorAfter=Vector3.Angle(Face(derived,0),reference);
            }
            public void Dispose(){preview.Cleanup();}
        }
        static ClipRow Describe(AnimationClip clip)
        {
            var bindings = Bindings(clip); string path = AssetDatabase.GetAssetPath(clip);
            return new ClipRow { identity = Id(clip), name = clip.name, length = clip.length, frameRate = clip.frameRate, humanMotion = clip.humanMotion, bindingCount = bindings.Length, objectBindingCount = AnimationUtility.GetObjectReferenceCurveBindings(clip).Length, eventCount = AnimationUtility.GetAnimationEvents(clip).Length, bindingSha256 = HashText(string.Join("\n", bindings.Select(Key))), curveSha256 = CurveDigest(clip, bindings), storedSha256 = Hash(path), rootBindings = bindings.Where(b => b.propertyName.StartsWith("RootT.", StringComparison.Ordinal) || b.propertyName.StartsWith("RootQ.", StringComparison.Ordinal)).Select(Key).ToArray(), settings = Settings(clip) };
        }
        static Setting[] Settings(AnimationClip clip)
        {
            object settings = AnimationUtility.GetAnimationClipSettings(clip); var rows = settings.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => new Setting { name = f.Name, value = Scalar(f.GetValue(settings)) }).ToArray();
            foreach (string field in new[] { "loopBlendPositionY", "keepOriginalPositionY", "keepOriginalPositionXZ", "keepOriginalOrientation" }) if (!rows.Any(r => r.name == field)) throw new MissingFieldException("Actual AnimationClipSettings field missing: " + field); return rows;
        }
        static string Scalar(object value) => value == null ? "null" : value is float f ? f.ToString("R", CultureInfo.InvariantCulture) : value is double d ? d.ToString("R", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture);
        static EditorCurveBinding[] Bindings(AnimationClip clip) => AnimationUtility.GetCurveBindings(clip).OrderBy(Key, StringComparer.Ordinal).ToArray();
        static string Key(EditorCurveBinding b) => b.path + "|" + b.type.FullName + "|" + b.propertyName;
        static string CurveDigest(AnimationClip clip, EditorCurveBinding[] bindings)
        {
            var text = new StringBuilder(); foreach (var binding in bindings) { var curve = AnimationUtility.GetEditorCurve(clip, binding) ?? throw new InvalidOperationException("Missing native/owned float curve."); text.Append(Key(binding)).Append('|').Append((int)curve.preWrapMode).Append('|').Append((int)curve.postWrapMode).Append('\n'); foreach (var k in curve.keys) text.Append(string.Join("|", new[] { Scalar(k.time), Scalar(k.value), Scalar(k.inTangent), Scalar(k.outTangent), Scalar(k.inWeight), Scalar(k.outWeight), Scalar(k.weightedMode) })).Append('\n'); } return HashText(text.ToString());
        }
        static float BoundaryError(AnimationClip source, AnimationClip derived, float sourceTime, float derivedTime) { float max = 0; foreach (var b in Bindings(source)) { float delta = Mathf.Abs(AnimationUtility.GetEditorCurve(source, b).Evaluate(sourceTime) - AnimationUtility.GetEditorCurve(derived, b).Evaluate(derivedTime)); if (!Finite(delta)) throw new InvalidOperationException("Non-finite boundary value error: " + Key(b)); max = Mathf.Max(max, delta); } return max; }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        static void ValidateLabel(string label) { if (string.IsNullOrWhiteSpace(label) || !Regex.IsMatch(label, "^[A-Za-z0-9_-]{8,72}$")) throw new ArgumentException("Fresh ASCII label required."); }
        static void RequireFresh(string path) { if (File.Exists(path) || File.Exists(path + ".meta") || AssetDatabase.LoadAssetAtPath<Object>(path) != null) throw new IOException("Cannot overwrite owned variant: " + path); }
        static Identity Id(Object obj) { if (obj == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long localId)) throw new InvalidOperationException("Persistent original/owned identity required."); return new Identity { path = AssetDatabase.GetAssetPath(obj), guid = guid, localId = localId }; }
        static void EnsureFolder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path)?.Replace('\\', '/'); if (string.IsNullOrEmpty(parent)) throw new IOException("Invalid owned directory."); EnsureFolder(parent); if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path)))) throw new IOException("Could not create owned directory."); }
        static void RequireIdle() { if (Path.GetFullPath(UnityEngine.Application.dataPath).Replace('\\', '/') != Project + "/Assets" || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || PrefabStageUtility.GetCurrentPrefabStage() != null) throw new InvalidOperationException("Correct idle project without PrefabStage required."); foreach (var scene in OriginalScenes()) if (scene.isDirty) throw new InvalidOperationException("Preserve dirty original scene: " + scene.path); if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening) throw new InvalidOperationException("Preserve existing network session."); }
        static Scene[] OriginalScenes() { var result = new List<Scene>(); for (int i = 0; i < SceneManager.sceneCount; i++) { var scene = SceneManager.GetSceneAt(i); if (!EditorSceneManager.IsPreviewScene(scene)) result.Add(scene); } return result.ToArray(); }
        static string SceneMemory(Scene[] scenes) { var rows = new List<string> { "active=" + SceneManager.GetActiveScene().handle }; foreach (var scene in scenes) { if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Original scene lost."); foreach (var root in scene.GetRootGameObjects()) foreach (var t in root.GetComponentsInChildren<Transform>(true)) { rows.Add(t.GetInstanceID() + "|" + EditorJsonUtility.ToJson(t.gameObject)); foreach (var c in t.GetComponents<Component>()) rows.Add(c == null ? "missing" : c.GetInstanceID() + "|" + EditorJsonUtility.ToJson(c)); } } rows.Sort(StringComparer.Ordinal); return string.Join("\n", rows); }
        static string[] BuildClosure() => EditorBuildSettings.scenes.Where(s => s.enabled).SelectMany(s => AssetDatabase.GetDependencies(s.path, true)).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        static FileRow[] Protected(string[] baselinePaths)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal); Action<string> add = p => { p = p.Replace('\\', '/'); if (File.Exists(p)) { paths.Add(p); if (File.Exists(p + ".meta")) paths.Add(p + ".meta"); } };
            foreach (string path in new[] { SourceFbx, AssembledPrefab, Pack + "/LICENSE.txt", Pack + "/README.md", Pack + "/addons/kaykit_character_pack_adventures/LICENSE.txt", "Assets/_Game/Scripts/Editor/Review/KayKitHeroMotionDerivation.cs", "Assets/_Game/Scripts/Editor/Setup/HumanoidClipDerivation.cs", "Assets/_Game/Scripts/Gameplay/Animation/PlayerAnimationPresenter.cs" }) { if (!File.Exists(path)) throw new FileNotFoundException("Required protected source missing.", path); add(path); foreach (string dep in AssetDatabase.GetDependencies(path, true)) add(dep); }
            foreach (string directory in new[] { "ProjectSettings", "Assets/_Game/Settings", "Assets/_Game/Scenes", "Assets/_Game/Prefabs/Characters", "Assets/_Game/Resources/Networking", Path.GetDirectoryName(AssembledPrefab) }) { if (File.Exists(directory + ".meta")) add(directory + ".meta"); foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)) { add(path); if (path.EndsWith(".prefab", StringComparison.Ordinal) || path.EndsWith(".unity", StringComparison.Ordinal)) foreach (string dep in AssetDatabase.GetDependencies(path.Replace('\\', '/'), true)) add(dep); } }
            foreach (string path in Directory.GetFiles("Assets/_Game/Scripts", "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".asmdef", StringComparison.Ordinal) || p.EndsWith(".asmref", StringComparison.Ordinal))) add(path);
            foreach(string path in baselinePaths??Array.Empty<string>()){if(!File.Exists(path))throw new FileNotFoundException(path);add(path);add(Path.GetDirectoryName(path)+".meta");foreach(string dep in AssetDatabase.GetDependencies(path,true))add(dep);}
            foreach (string path in BuildClosure()) add(path); add(Archive); var result = paths.OrderBy(p => p, StringComparer.Ordinal).Select(FileOf).ToArray(); if (!result.Any(r => r.path == Archive && r.sha256 == ArchiveSha)) throw new InvalidOperationException("Delivered r4 ZIP missing/different."); return result;
        }
        static FileRow FileOf(string path) => new FileRow { path = path, bytes = new FileInfo(path).Length, sha256 = Hash(path), guid = path.StartsWith("Assets/", StringComparison.Ordinal) && !path.EndsWith(".meta", StringComparison.Ordinal) ? AssetDatabase.AssetPathToGUID(path) : "" };
        static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        static string HashText(string value) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-", ""); }
        static void Write(string path, Report report) => File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
    }
}
