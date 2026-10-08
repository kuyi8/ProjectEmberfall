using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Emberfall.AI.Unity;
using Emberfall.Editor.Setup;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Copy-only CPU geometry review. Never enters Play, saves a source,
    /// rebuilds M6/navigation, reimports originals or changes combat/Avatar/socket data.</summary>
    public static class WardenSilhouetteReview
    {
        const string Valley = "Assets/_Game/Scenes/10_EmberValley.unity";
        const string NetworkPrefab = "Assets/_Game/Resources/Networking/P_M5_NetworkWarden.prefab";
        const string TempPrefix = "Assets/_Game/Scenes/__WardenSilhouetteReview_";
        const string JobPrefix = "Emberfall.WardenSilhouetteReview.";
        const string Scope = "Controlled Edit-mode scene COPY and CPU rest pose, source URP lighting. Diagnostic 60-degree cameras, not formal TPS/natural route/input/contact/network spawn/server/hurtbox/performance acceptance. Actual NET source equipment existence is recorded, not assumed absent. Display-RGB grayscale is a QA derivative, not a photometric measurement. Collider geometry remains unchanged despite visible mesh changes.";

        [Serializable] sealed class FileRow { public string path, sha256; public long bytes; }
        [Serializable] sealed class Manifest { public string scope; public FileRow[] files; }
        [Serializable] sealed class ActorRow
        {
            public string actor, stage, avatarPath, avatarGuid, bodyBoundsMethod = "Drawn CPU skin/static vertices in world space; excludes named weapon/shield branches.";
            public Vector3 position, rotation, visualScale, rootScale, headBone;
            public Bounds bodyBounds, helmetWorldBounds, swordWorldBounds, originalGpuSkinBounds;
            public int renderers, meshFilters, colliders, rigidbodies, animators;
            public long triangles;
            public float bodyHeight, boneHeight, gpuSkinHeight, helmetWidthOverBodyWidth;
            public string[] importerScales;
        }
        [Serializable] sealed class Pair
        {
            public string path;
            public bool protectedComponentsSame, helmetFound, swordFound;
            public int changedMeshFilters;
            public string[] matchedFilters;
            public WardenSilhouetteSetup.Diagnostics preparation;
            public WardenSilhouetteSelection.SourceDescription selection;
            public int changedMaterialSlots;
            public bool swordTailSlotsSame, selectedBladeKnownClone;
            public string originalBladeAssetPath;
            public ActorRow before, after;
        }
        [Serializable] sealed class EquipmentRow
        {
            public bool sourcePrefabHasSword, sourcePrefabHasShield, fixtureCreated, protectedPhysicsCountsSame;
            public string scope = "Actual source sword/shield existence and fixtureCreated are recorded. Any required equipment creation applies ONLY to the controlled copy, not a source prefab or actual network spawn/phase/authority.";
            public string rightHandPath, leftHandPath;
            public Vector3 rightSocketWorldScale, leftSocketWorldScale;
            public int transformsBefore, transformsAfter, renderersBefore, renderersAfter, filtersBefore, filtersAfter;
            public int collidersBefore, collidersAfter, rigidbodiesBefore, rigidbodiesAfter, animatorsBefore, animatorsAfter, behavioursBefore, behavioursAfter;
        }
        [Serializable] sealed class Shot
        {
            public string actor, stage, image, grayscale, silhouette;
            public string scope;
            public Vector3 position, target;
            public float distance, fieldOfView = 60;
            public int width = 960, height = 600, silhouettePixels;
        }
        [Serializable] sealed class Report
        {
            public string label, output, temporaryScene, status = "pending", scope = Scope, error, cleanupError, startedUtc, finishedUtc, lightingBefore, lightingAfter;
            public bool sourcesUnchanged, setupRestored, temporaryAssetRemoved;
            public int frozenFiles, backedUpFiles;
            public string cameraDirectionBasis;
            public Vector3 rawBoneFront, horizontalBoneFront, boneRight, sourceActorForward;
            public bool selectedPresentation;
            public EquipmentRow networkEquipment;
            public string endpointScope;
            public Pair[] actors;
            public Shot[] shots;
            public bool middleBlendOnly, sourceMaterialsSame;
            public string baselineReport, baselineReportSha;
            public MiddleSample[] middleSamples;
        }
        [Serializable] sealed class MiddleSample {
            public string actor; public Color steelColor, runeColor, blendColor;
            public bool knownSelectedClone, tailSlotsSame; public bool[] shieldEnabled;
        }

        /// <summary>Returns the evidence path immediately. Repeated requests with
        /// the same label in this Editor session NEVER dispatch a second mutation.</summary>
        public static string BeginCandidate(string label, bool selectedPresentation = false)
        {
            if (string.IsNullOrWhiteSpace(label) || label.Length > 64 || !Regex.IsMatch(label, "^[A-Za-z0-9_-]+$"))
                throw new ArgumentException("Use a unique ASCII alphanumeric/hyphen/underscore label.", nameof(label));
            string existing = SessionState.GetString(JobPrefix + label, "");
            if (!string.IsNullOrEmpty(existing)) return existing;
            RequireSource();
            string output = Path.GetFullPath("Builds/ArtReview/warden-silhouette/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + label);
            if (Directory.Exists(output)) throw new IOException("Never overwrite a review run.");
            Directory.CreateDirectory(output);
            var report = new Report { label = label, output = output, selectedPresentation = selectedPresentation,
                startedUtc = DateTime.UtcNow.ToString("O"), temporaryScene = TempPrefix + Guid.NewGuid().ToString("N") + ".unity" };
            if (selectedPresentation) {
                report.scope = Scope + " Optional selected fixture may add needed equipment ONLY to the copy; source existence and actual fixture creation are recorded.";
                report.endpointScope = "Selected-after battle is the retained steel-material endpoint, not actual PhaseOne simulation. Additional battle color/grayscale images show ONLY exact sword slot 0 with controlled runeColor(1,0.16,0.035), copied shield renderers temporarily hidden and restored. Separate temporary blade material prevents tinting other users of the shared WardenMetal clone. Same CPU pose/camera/source URP lighting; no PhaseTransition/clock/light/AI/network/gameplay invoked or true interpolation accepted.";
            }
            return QueueCapture(report, JobPrefix + label);
        }

        /// <summary>Four controlled middle-color PNGs only; no Selection, equipment
        /// creation, phase invocation or full before/after gallery.</summary>
        public static string BeginMiddleBlend(string label, string baselineEvidenceDirectory)
        {
            if (string.IsNullOrWhiteSpace(label) || label.Length > 64 || !Regex.IsMatch(label, "^[A-Za-z0-9_-]+$"))
                throw new ArgumentException("Use a unique ASCII alphanumeric/hyphen/underscore label.");
            string key = JobPrefix + "middle." + label;
            string existing = SessionState.GetString(key, "");
            if (!string.IsNullOrEmpty(existing)) return existing;
            string baselinePath = MiddleBaselinePath(baselineEvidenceDirectory);
            LoadMiddleBaseline(baselinePath); RequireSource();
            string output = Path.GetFullPath("Builds/ArtReview/warden-silhouette/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-middle-" + label);
            if (Directory.Exists(output)) throw new IOException("Never overwrite a review run.");
            Directory.CreateDirectory(output);
            var report = new Report { label = label, output = output, selectedPresentation = true, middleBlendOnly = true,
                baselineReport = baselinePath, baselineReportSha = Hash(baselinePath), startedUtc = DateTime.UtcNow.ToString("O"),
                temporaryScene = TempPrefix + Guid.NewGuid().ToString("N") + ".unity",
                scope = "Controlled Edit-mode scene COPY and CPU rest pose, actual current selected Warden helmet/sword and actual equipped NET source. Exact accepted baseline battle camera positions/targets; current source URP lighting. Four color/display-RGB-grayscale PNGs only. Temporary separate exact sword slot-0 material = Color.Lerp(actual selected steel color, runeColor(1,0.16,0.035),0.5); shield visibility preserved. No Selection, new equipment, source save, actual clock/phase/transition, natural contact, real NGO spawn or performance acceptance.",
                endpointScope = "Controlled 50-percent color blend, NOT a phase timestamp or tested runtime interpolation." };
            File.Copy(baselinePath, output + "/baseline-report.json", false);
            return QueueCapture(report, key);
        }

        static string QueueCapture(Report report, string key)
        {
            SaveReport(report);
            SessionState.SetString(key, report.output);
            EditorApplication.CallbackFunction callback = null;
            callback = () => {
                EditorApplication.update -= callback;
                try { CaptureCandidate(report); Debug.Log("[WARDEN_SILHOUETTE] " + report.output); }
                catch (Exception error) {
                    if (report.status != "failed") { report.status = "failed"; report.error = error.ToString(); SaveReport(report); }
                    Debug.LogException(error);
                }
            };
            EditorApplication.update += callback;
            return report.output;
        }

        static void CaptureCandidate(Report report)
        {
            RequireSource();
            RequireOwnedTemp(report.temporaryScene);
            if (File.Exists(report.temporaryScene) || AssetDatabase.LoadAssetAtPath<SceneAsset>(report.temporaryScene) != null)
                throw new IOException("Owned temporary scene already exists.");
            var previous = EditorSceneManager.GetSceneManagerSetup();
            var frozen = FreezeSources();
            if (report.middleBlendOnly)
                foreach (string mesh in new[] { WardenSilhouetteSetup.HelmetMeshPath, WardenSilhouetteSetup.SwordMeshPath })
                    foreach (string file in new[] { mesh, mesh + ".meta" }) frozen[file] = Hash(file);
            var sourceMaterials = report.middleBlendOnly ? frozen.Keys.Where(p => p.EndsWith(".mat", StringComparison.Ordinal))
                .ToDictionary(p => AssetDatabase.LoadAssetAtPath<Material>(p), p => EditorJsonUtility.ToJson(AssetDatabase.LoadAssetAtPath<Material>(p))) : null;
            var pairs = new List<Pair>(); var shots = new List<Shot>();
            var materialCopies = new Dictionary<Material, Material>();
            Exception failure = null;
            report.status = "running"; SaveReport(report);
            try
            {
                WriteManifest(report.output + "/sources-before.json", frozen);
                var backedUp = frozen.Where(p => BackupSource(p.Key)).ToDictionary(p => p.Key, p => p.Value);
                report.frozenFiles = frozen.Count; report.backedUpFiles = backedUp.Count;
                WriteManifest(report.output + "/backup-manifest.json", backedUp, "Byte snapshots only of original scenes/project prefabs/navigation/settings and directly selected model/license inputs; full dependency SHA freeze remains in sources-before/after.json. Third-party texture/animation copies intentionally not duplicated.");
                foreach (string source in backedUp.Keys)
                {
                    string target = Path.Combine(report.output, "source-snapshots", source);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)); File.Copy(source, target);
                }
                var original = SceneManager.GetActiveScene();
                File.WriteAllText(report.output + "/original-actor-components.jsonl", Snapshot(FindWarden(original), false));
                if (!EditorSceneManager.SaveScene(original, report.temporaryScene, true)) throw new IOException("Save-as-copy failed.");
                if (original.isDirty) throw new InvalidOperationException("Saving a copy dirtied the original scene.");
                var copy = EditorSceneManager.OpenScene(report.temporaryScene, OpenSceneMode.Single);
                foreach (var root in copy.GetRootGameObjects()) CopyMaterialReferences(root, materialCopies);
                report.lightingBefore = Lighting();
                var offline = FindWarden(copy);
                PreparePose(offline);
                Camera sourceCamera = copy.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Camera>(true)).FirstOrDefault();
                if (report.middleBlendOnly)
                {
                    Report baseline = LoadMiddleBaseline(report.baselineReport);
                    if (Hash(report.baselineReport) != report.baselineReportSha) throw new IOException("Accepted baseline report changed.");
                    var samples = new List<MiddleSample>();
                    pairs.Add(CaptureMiddleActor(offline, "offline", copy, sourceCamera, baseline, report.output, shots, samples, materialCopies));
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkPrefab);
                    if (prefab == null) throw new FileNotFoundException("Actual NetworkWarden prefab required.", NetworkPrefab);
                    bool sourceSword = prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == WardenNetworkEquipmentSetup.SwordName);
                    bool sourceShield = prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == WardenNetworkEquipmentSetup.ShieldName);
                    report.networkEquipment = new EquipmentRow { sourcePrefabHasSword = sourceSword, sourcePrefabHasShield = sourceShield,
                        fixtureCreated = false, scope = "Existing production NET source equipment retained; controlled prefab instance at offline pose, NOT an actual NGO spawn or phase." };
                    if (!sourceSword || !sourceShield) throw new InvalidOperationException("Middle-only capture requires the actual equipped NET source; no equipment will be created.");
                    var network = (GameObject)PrefabUtility.InstantiatePrefab(prefab, copy);
                    network.transform.SetPositionAndRotation(offline.transform.position, offline.transform.rotation);
                    PreparePose(network); CopyMaterialReferences(network, materialCopies);
                    Renderer[] oldRenderers = offline.GetComponentsInChildren<Renderer>(true);
                    bool[] enabled = oldRenderers.Select(r => r.enabled).ToArray();
                    try {
                        // Isolate the independent fixture, not scene obstacles,
                        // shields or any geometry blocking its accepted camera.
                        foreach (var renderer in oldRenderers) renderer.enabled = false;
                        pairs.Add(CaptureMiddleActor(network, "network-prefab-controlled", copy, sourceCamera, baseline, report.output, shots, samples, materialCopies));
                    }
                    finally { for (int i = 0; i < oldRenderers.Length; i++) oldRenderers[i].enabled = enabled[i]; }
                    report.middleSamples = samples.ToArray();
                    report.cameraDirectionBasis = "Exact positions/targets copied from accepted selected-after battle Shots in " + report.baselineReport + "; no recomputed framing or hidden obstacles.";
                }
                else
                {
                Bounds frameBounds;
                using (var pose = new CpuPose(offline)) frameBounds = pose.BodyBounds;
                if (frameBounds.size.y < 1.7f || frameBounds.size.y > 3.5f) throw new InvalidOperationException("Implausible actual scene CPU Warden height: " + frameBounds.size.y);
                Vector3 front = BoneFront(offline, out Vector3 rawFront);
                Vector3 right = Vector3.Cross(Vector3.up, front).normalized;
                report.rawBoneFront = rawFront; report.horizontalBoneFront = front;
                report.boneRight = right; report.sourceActorForward = offline.transform.forward;
                report.cameraDirectionBasis = "Current controlled-pose bone front = Cross(RightUpperArm-LeftUpperArm, Head-Hips), normalized then projected onto XZ. Camera offset = normalize(front + Cross(up, front)*0.32 + up*0.16), at 4.5m/8m from original CPU body centre. Same fixed positions/target before and after, shared by the separate NET fixture. Actor.forward is recorded but NOT used for framing; no actor, indicator or background changes to improve visibility.";
                var positions = new[] { 4.5f, 8f }.Select(distance => frameBounds.center +
                    (front + right * .32f + Vector3.up * .16f).normalized * distance).ToArray();
                pairs.Add(CapturePair(offline, "offline", copy, sourceCamera, positions, frameBounds.center, report.output, shots, report.selectedPresentation, materialCopies));
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(NetworkPrefab);
                if (prefab == null) throw new FileNotFoundException("Actual NetworkWarden prefab required.", NetworkPrefab);
                var network = (GameObject)PrefabUtility.InstantiatePrefab(prefab, copy);
                network.transform.SetPositionAndRotation(offline.transform.position, offline.transform.rotation);
                PreparePose(network);
                if (report.selectedPresentation) report.networkEquipment = EquipFixture(network, prefab);
                else report.networkEquipment = new EquipmentRow {
                    sourcePrefabHasSword = prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == WardenNetworkEquipmentSetup.SwordName),
                    sourcePrefabHasShield = prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == WardenNetworkEquipmentSetup.ShieldName), fixtureCreated = false };
                CopyMaterialReferences(network, materialCopies);
                // Only hide the copied offline visuals while inspecting the independent
                // prefab instance. No AI, network or active-state manipulation.
                Renderer[] oldRenderers = offline.GetComponentsInChildren<Renderer>(true);
                bool[] enabled = oldRenderers.Select(r => r.enabled).ToArray();
                try {
                    foreach (var renderer in oldRenderers) renderer.enabled = false;
                    Pair pair = CapturePair(network, "network-prefab-controlled", copy, sourceCamera, positions, frameBounds.center, report.output, shots, report.selectedPresentation, materialCopies);
                    pairs.Add(pair);
                    bool sourceSword = prefab.GetComponentsInChildren<Transform>(true).Any(t => t.name == WardenNetworkEquipmentSetup.SwordName);
                    if (!pair.helmetFound || pair.swordFound != (report.selectedPresentation || sourceSword)) throw new InvalidOperationException("NET actual-source/default/selected fixture equipment contract changed.");
                }
                finally { for (int i = 0; i < oldRenderers.Length; i++) oldRenderers[i].enabled = enabled[i]; }
                }
                report.lightingAfter = Lighting();
                if (report.lightingBefore != report.lightingAfter) throw new InvalidOperationException("Capture changed copied source lighting settings.");
                if (!Unchanged(frozen)) throw new InvalidOperationException("Protected source bytes changed; never restore them silently.");
                // Save ONLY our explicit disposable copy. Preserve its actual candidate
                // references as an evidence artifact before deleting the owned asset.
                RequireOwnedTemp(copy.path);
                if (!EditorSceneManager.SaveScene(copy)) throw new IOException("Owned candidate-copy save failed.");
                File.Copy(copy.path, report.output + "/candidate-copy.unity");
                if (File.Exists(copy.path + ".meta")) File.Copy(copy.path + ".meta", report.output + "/candidate-copy.unity.meta");
            }
            catch (Exception error) { failure = error; report.error = error.ToString(); File.WriteAllText(report.output + "/failure.txt", report.error); }
            finally
            {
                report.actors = pairs.ToArray(); report.shots = shots.ToArray();
                try {
                    // Retain the actual on-disk owned copy even on a failed capture.
                    // It may be the pre-candidate copy: never relabel it as a saved
                    // candidate or silently save failed state into an original.
                    RequireOwnedTemp(report.temporaryScene);
                    if (File.Exists(report.temporaryScene)) {
                        File.Copy(report.temporaryScene, report.output + "/owned-copy-on-disk-before-cleanup.unity");
                        if (File.Exists(report.temporaryScene + ".meta")) File.Copy(report.temporaryScene + ".meta", report.output + "/owned-copy-on-disk-before-cleanup.unity.meta");
                    }
                }
                catch (Exception backup) { report.cleanupError = backup.ToString(); if (failure == null) failure = backup; }
                try {
                    EditorSceneManager.RestoreSceneManagerSetup(previous);
                    report.setupRestored = SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().path == Valley && !SceneManager.GetActiveScene().isDirty;
                    if (!report.setupRestored) throw new InvalidOperationException("Original clean scene setup was not restored.");
                    RequireOwnedTemp(report.temporaryScene);
                    if (File.Exists(report.temporaryScene) && !AssetDatabase.DeleteAsset(report.temporaryScene)) throw new IOException("Could not remove our owned temporary scene asset.");
                    report.temporaryAssetRemoved = !File.Exists(report.temporaryScene) && !File.Exists(report.temporaryScene + ".meta");
                    if (!report.temporaryAssetRemoved) throw new IOException("Owned temporary scene/meta remain.");
                }
                catch (Exception cleanup) { report.cleanupError += "\n" + cleanup; if (failure == null) failure = cleanup; }
                foreach (var material in materialCopies.Values) if (material != null) Object.DestroyImmediate(material);
                if (sourceMaterials != null) {
                    report.sourceMaterialsSame = sourceMaterials.All(row => row.Key != null && EditorJsonUtility.ToJson(row.Key) == row.Value);
                    if (!report.sourceMaterialsSame && failure == null) failure = new InvalidOperationException("Persistent source material changed in memory; do not save shared assets.");
                    if (Hash(report.baselineReport) != report.baselineReportSha && failure == null) failure = new IOException("Baseline report bytes changed.");
                }
                report.sourcesUnchanged = Unchanged(frozen);
                WriteManifest(report.output + "/sources-after.json", frozen.Keys.Where(File.Exists).ToDictionary(p => p, Hash));
                if (!report.sourcesUnchanged && failure == null) failure = new InvalidOperationException("Source SHA freeze failed after cleanup.");
                report.status = failure == null ? "complete" : "failed";
                report.finishedUtc = DateTime.UtcNow.ToString("O"); SaveReport(report);
            }
            if (failure != null) throw new InvalidOperationException("Warden copy-only review failed; raw evidence: " + report.output, failure);
        }

        static string MiddleBaselinePath(string supplied)
        {
            string path = Path.GetFullPath(supplied ?? throw new ArgumentNullException(nameof(supplied)));
            if (Directory.Exists(path)) path = Path.Combine(path, "report.json");
            string directory = Path.GetDirectoryName(path), root = Path.GetFullPath("Builds/ArtReview/warden-silhouette");
            if (Path.GetFileName(path) != "report.json" || !Directory.Exists(directory) ||
                !string.Equals(Path.GetDirectoryName(directory), root, StringComparison.OrdinalIgnoreCase) ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                !File.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Baseline must be report.json in one real direct child of the project's exact Warden evidence root.");
            return path;
        }
        static Report LoadMiddleBaseline(string path)
        {
            path = MiddleBaselinePath(path);
            Report baseline = JsonUtility.FromJson<Report>(File.ReadAllText(path));
            if (baseline == null || baseline.status != "complete" || !baseline.selectedPresentation || baseline.middleBlendOnly ||
                !baseline.sourcesUnchanged || !baseline.setupRestored || !baseline.temporaryAssetRemoved || baseline.shots == null)
                throw new InvalidDataException("Require a complete, protected, selected before/after baseline; never reuse failed or middle-only evidence.");
            foreach (string actor in new[] { "offline", "network-prefab-controlled" }) MiddleBattleShot(baseline, actor);
            return baseline;
        }
        static Shot MiddleBattleShot(Report baseline, string actor)
        {
            Shot shot = baseline.shots.Single(s => s.actor == actor && s.stage == "after" && s.image == actor + "-after-battle-color.png");
            if (shot.width != 960 || shot.height != 600 || shot.fieldOfView != 60f || Mathf.Abs(shot.distance - 8f) > .001f ||
                float.IsNaN(shot.position.sqrMagnitude) || float.IsInfinity(shot.position.sqrMagnitude) ||
                float.IsNaN(shot.target.sqrMagnitude) || float.IsInfinity(shot.target.sqrMagnitude) ||
                Mathf.Abs(Vector3.Distance(shot.position, shot.target) - shot.distance) > .001f)
                throw new InvalidDataException("Accepted battle camera parameters are invalid.");
            return shot;
        }
        static Pair CaptureMiddleActor(GameObject actor, string label, Scene scene, Camera source, Report baseline, string output,
            List<Shot> shots, List<MiddleSample> samples, Dictionary<Material, Material> materialCopies)
        {
            MeshRenderer sword = ExactSword(actor); Material[] slots = sword.sharedMaterials;
            var selected = AssetDatabase.LoadAssetAtPath<Material>(WardenSilhouetteSelection.SelectedBladePath);
            if (slots.Length != 3 || selected == null || !materialCopies.TryGetValue(selected, out Material known) || slots[0] != known || EditorUtility.IsPersistent(known))
                throw new InvalidOperationException("Exact sword slot 0 must be the positively known selected steel clone.");
            MeshFilter helmet = actor.GetComponentsInChildren<MeshFilter>(true).Single(f => f.name == WardenSilhouetteSetup.HelmetNode);
            if (helmet.sharedMesh != AssetDatabase.LoadAssetAtPath<Mesh>(WardenSilhouetteSetup.HelmetMeshPath) ||
                sword.GetComponent<MeshFilter>().sharedMesh != AssetDatabase.LoadAssetAtPath<Mesh>(WardenSilhouetteSetup.SwordMeshPath))
                throw new InvalidOperationException("Middle-only view requires existing selected meshes; no selection will be applied.");
            Renderer[] shields = actor.GetComponentsInChildren<Transform>(true).Where(t => t.name == WardenNetworkEquipmentSetup.ShieldName)
                .SelectMany(t => t.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray();
            if (shields.Length == 0) throw new InvalidOperationException("Existing exact shield is required; never create or hide it.");
            string before = Snapshot(actor, false);
            File.WriteAllText(output + "/" + label + "-protected-before.jsonl", before);
            var pair = new Pair { path = label, helmetFound = true, swordFound = true, selectedBladeKnownClone = true };
            var sample = new MiddleSample { actor = label, knownSelectedClone = true, steelColor = known.color,
                runeColor = new Color(1f, .16f, .035f), shieldEnabled = shields.Select(r => r.enabled).ToArray() };
            sample.blendColor = Color.Lerp(sample.steelColor, sample.runeColor, .5f);
            Shot accepted = MiddleBattleShot(baseline, label);
            var cameraObject = new GameObject("__WardenMiddle_DiagnosticCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            if (source != null) camera.CopyFrom(source);
            camera.enabled = false; camera.targetTexture = null; camera.fieldOfView = accepted.fieldOfView;
            camera.aspect = 960f / 600f; camera.nearClipPlane = .04f; camera.farClipPlane = 200;
            camera.allowHDR = false; camera.allowMSAA = false;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            camera.transform.position = accepted.position; camera.transform.LookAt(accepted.target);
            var middle = new Material(known) { name = "__ReviewOnly_MiddleBlend", color = sample.blendColor };
            try {
                using (var pose = new CpuPose(actor)) {
                    pair.before = Metrics(actor, pose, label, "current-selected");
                    var temporary = (Material[])slots.Clone(); temporary[0] = middle; sword.sharedMaterials = temporary;
                    var shot = new Shot { actor = label, stage = "controlled-middle-blend-50pct", position = accepted.position, target = accepted.target,
                        distance = accepted.distance, fieldOfView = accepted.fieldOfView, image = label + "-middle-blend-color.png",
                        grayscale = label + "-middle-blend-display-grayscale.png", scope = "Controlled slot-0 50-percent color blend only; shields/obstacles retained. Not actual phase/clock/network spawn." };
                    WriteColorAndGrayscale(camera, output + "/" + shot.image, output + "/" + shot.grayscale); shots.Add(shot);
                    sword.sharedMaterials = slots;
                    pair.after = Metrics(actor, pose, label, "restored-selected");
                }
            }
            finally { sword.sharedMaterials = slots; Object.DestroyImmediate(middle); Object.DestroyImmediate(cameraObject); }
            sample.tailSlotsSame = sword.sharedMaterials.Length == 3 && sword.sharedMaterials[1] == slots[1] && sword.sharedMaterials[2] == slots[2];
            pair.swordTailSlotsSame = sample.tailSlotsSame; pair.protectedComponentsSame = before == Snapshot(actor, false);
            File.WriteAllText(output + "/" + label + "-protected-after.jsonl", Snapshot(actor, false));
            samples.Add(sample);
            if (!pair.protectedComponentsSame || !sample.tailSlotsSame || !sample.shieldEnabled.SequenceEqual(shields.Select(r => r.enabled)))
                throw new InvalidOperationException("Middle-only capture failed to restore exact actor/material/shield state.");
            return pair;
        }

        static Pair CapturePair(GameObject actor, string label, Scene scene, Camera source, Vector3[] positions, Vector3 target, string output, List<Shot> shots,
            bool selectedPresentation, Dictionary<Material, Material> materialCopies)
        {
            var pair = new Pair { path = label };
            MeshRenderer sword = selectedPresentation ? ExactSword(actor) : null;
            Material[] swordSlotsBefore = sword == null ? null : sword.sharedMaterials;
            string before = Snapshot(actor, true, selectedPresentation);
            File.WriteAllText(output + "/" + label + "-protected-before.jsonl", before);
            using (var pose = new CpuPose(actor)) {
                pair.before = Metrics(actor, pose, label, "before");
                CaptureViews(actor, pose, label, "before", scene, source, positions, target, output, shots, selectedPresentation);
            }
            if (selectedPresentation) {
                // Selection accepts exact persistent source references, not clone
                // names. Restore ONLY a positively identified known slot-0 clone;
                // no render happens before selection and its immediate re-clone.
                pair.originalBladeAssetPath = RestoreKnownBladeReference(sword, materialCopies);
                var selected = WardenSilhouetteSelection.Apply(actor);
                pair.changedMeshFilters = selected.meshChanges; pair.changedMaterialSlots = selected.materialChanges;
                pair.matchedFilters = selected.matchedPaths; pair.selection = selected.source;
                pair.helmetFound = actor.GetComponentsInChildren<Transform>(true).Any(t => t.name == WardenSilhouetteSetup.HelmetNode);
                pair.swordFound = sword != null;
                CopyMaterialReferences(sword.gameObject, materialCopies);
                Material[] afterSlots = sword.sharedMaterials;
                pair.swordTailSlotsSame = afterSlots.Length == 3 && swordSlotsBefore.Length == 3 && afterSlots[1] == swordSlotsBefore[1] && afterSlots[2] == swordSlotsBefore[2];
                Material blade = AssetDatabase.LoadAssetAtPath<Material>(WardenSilhouetteSelection.SelectedBladePath);
                pair.selectedBladeKnownClone = materialCopies.TryGetValue(blade, out Material copy) && afterSlots[0] == copy;
                if (!pair.swordTailSlotsSame || !pair.selectedBladeKnownClone) throw new InvalidOperationException("Sword slot 1/2 or strict selected-clone reference changed.");
            }
            else {
                var result = WardenSilhouetteSetup.ApplyToTemporary(actor);
                pair.helmetFound = result.helmetFound; pair.swordFound = result.swordFound;
                pair.changedMeshFilters = result.changedMeshFilters; pair.matchedFilters = result.matchedFilters;
                pair.preparation = result.prepared?.diagnostics;
            }
            string after = Snapshot(actor, true, selectedPresentation);
            File.WriteAllText(output + "/" + label + "-protected-after.jsonl", after);
            pair.protectedComponentsSame = before == after;
            File.WriteAllText(output + "/" + label + "-mapping.json", JsonUtility.ToJson(pair, true));
            if (!pair.protectedComponentsSame || !pair.helmetFound || (label == "offline" && !pair.swordFound))
                throw new InvalidOperationException("Exact mesh-only actor mapping/protection failed: " + label);
            using (var pose = new CpuPose(actor)) {
                pair.after = Metrics(actor, pose, label, "after");
                CaptureViews(actor, pose, label, "after", scene, source, positions, target, output, shots, selectedPresentation);
            }
            if (after != Snapshot(actor, true, selectedPresentation)) throw new InvalidOperationException("Capture/endpoint failed to restore protected actor references/state: " + label);
            if (pair.before.colliders != pair.after.colliders || pair.before.renderers != pair.after.renderers || pair.before.animators != pair.after.animators)
                throw new InvalidOperationException("Unexpected component delta: " + label);
            File.WriteAllText(output + "/" + label + "-mapping.json", JsonUtility.ToJson(pair, true));
            return pair;
        }

        static void CaptureViews(GameObject actor, CpuPose pose, string label, string stage, Scene scene, Camera source, Vector3[] positions, Vector3 target, string output, List<Shot> shots, bool selectedPresentation)
        {
            var cameraObject = new GameObject("__WardenSilhouette_DiagnosticCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            if (source != null) camera.CopyFrom(source);
            camera.enabled = false; camera.targetTexture = null; camera.fieldOfView = 60;
            camera.aspect = 960f / 600f; camera.nearClipPlane = .04f; camera.farClipPlane = 200;
            camera.allowHDR = false; camera.allowMSAA = false;
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
            try {
                for (int view = 0; view < positions.Length; view++) {
                    camera.transform.position = positions[view]; camera.transform.LookAt(target);
                    string basename = label + "-" + stage + "-" + (view == 0 ? "near" : "battle");
                    var shot = new Shot { actor = label, stage = stage, position = positions[view], target = target,
                        distance = Vector3.Distance(positions[view], target), image = basename + "-color.png", grayscale = basename + "-display-grayscale.png", silhouette = basename + "-geometry-silhouette.png" };
                    WriteColorAndGrayscale(camera, output + "/" + shot.image, output + "/" + shot.grayscale);
                    shot.silhouettePixels = CaptureSilhouette(camera, pose, scene, output + "/" + shot.silhouette);
                    shots.Add(shot);
                    if (shot.silhouettePixels < 100) throw new InvalidOperationException("Empty/occluded controlled actor silhouette: " + basename);
                    if (selectedPresentation && stage == "after" && view == positions.Length - 1) CaptureEndpoint(actor, camera, label, target, output, shots);
                }
            }
            finally { Object.DestroyImmediate(cameraObject); }
        }
        static void WriteColorAndGrayscale(Camera camera, string color, string grayscale)
        {
            using (var image = new CapturedImage(camera)) {
                File.WriteAllBytes(color, image.texture.EncodeToPNG());
                Color32[] pixels = image.texture.GetPixels32();
                for (int i = 0; i < pixels.Length; i++) { byte gray = (byte)((77 * pixels[i].r + 150 * pixels[i].g + 29 * pixels[i].b + 128) >> 8); pixels[i] = new Color32(gray, gray, gray, 255); }
                image.texture.SetPixels32(pixels); image.texture.Apply(); File.WriteAllBytes(grayscale, image.texture.EncodeToPNG());
            }
        }
        static void CaptureEndpoint(GameObject actor, Camera camera, string label, Vector3 target, string output, List<Shot> shots)
        {
            MeshRenderer sword = ExactSword(actor); Material[] slots = sword.sharedMaterials;
            if (slots.Length != 3 || slots[0] == null || EditorUtility.IsPersistent(slots[0])) throw new InvalidOperationException("Endpoint must use an isolated blade clone.");
            Renderer[] shields = actor.GetComponentsInChildren<Transform>(true).Where(t => t.name == WardenNetworkEquipmentSetup.ShieldName)
                .SelectMany(t => t.GetComponentsInChildren<Renderer>(true)).Distinct().ToArray();
            if (shields.Length == 0) throw new InvalidOperationException("Exact copied Warden shield required for controlled endpoint.");
            bool[] enabled = shields.Select(r => r.enabled).ToArray();
            // WardenMetal's review clone may also be shared by body armour. A
            // separate disposable material confines this endpoint to sword slot 0.
            var endpoint = new Material(slots[0]) { name = "__ReviewOnly_RuneEndpoint", color = new Color(1f, .16f, .035f) };
            try {
                var temporary = (Material[])slots.Clone(); temporary[0] = endpoint; sword.sharedMaterials = temporary;
                foreach (var shield in shields) shield.enabled = false;
                string basename = label + "-after-battle-phase-two-endpoint";
                var shot = new Shot { actor = label, stage = "controlled-phase-two-color-endpoint", position = camera.transform.position, target = target,
                    distance = Vector3.Distance(camera.transform.position, target), image = basename + "-color.png", grayscale = basename + "-display-grayscale.png",
                    scope = "Exact sword slot 0 isolated runeColor(1,0.16,0.035), copied shield Renderer hidden and restored. Same camera/CPU pose/lighting. NOT actual phase/transition/clock/gameplay/network simulation." };
                WriteColorAndGrayscale(camera, output + "/" + shot.image, output + "/" + shot.grayscale); shots.Add(shot);
            }
            finally { sword.sharedMaterials = slots; for (int i = 0; i < shields.Length; i++) shields[i].enabled = enabled[i]; Object.DestroyImmediate(endpoint); }
        }

        static MeshRenderer ExactSword(GameObject root)
        {
            return root.GetComponentsInChildren<Transform>(true).Single(t => t.name == "Model" && t.parent?.name == WardenSilhouetteSetup.SwordNode).GetComponent<MeshRenderer>()
                ?? throw new InvalidDataException("Exact sword Model has no MeshRenderer.");
        }
        static string RestoreKnownBladeReference(MeshRenderer sword, Dictionary<Material, Material> copies)
        {
            Material[] slots = sword.sharedMaterials;
            if (slots.Length != 3) throw new InvalidDataException("Exact three-slot sword required.");
            foreach (string path in new[] { WardenSilhouetteSelection.OriginalBladePath, WardenSilhouetteSelection.SelectedBladePath }) {
                var original = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (original != null && copies.TryGetValue(original, out Material known) && slots[0] == known) {
                    slots[0] = original; sword.sharedMaterials = slots; return path;
                }
            }
            throw new InvalidDataException("Blade first slot is not the known original/selected review clone; no name-based acceptance.");
        }
        static EquipmentRow EquipFixture(GameObject root, GameObject source)
        {
            var row = new EquipmentRow {
                sourcePrefabHasSword = source.GetComponentsInChildren<Transform>(true).Any(t => t.name == WardenNetworkEquipmentSetup.SwordName),
                sourcePrefabHasShield = source.GetComponentsInChildren<Transform>(true).Any(t => t.name == WardenNetworkEquipmentSetup.ShieldName),
                transformsBefore = root.GetComponentsInChildren<Transform>(true).Length, renderersBefore = root.GetComponentsInChildren<Renderer>(true).Length,
                filtersBefore = root.GetComponentsInChildren<MeshFilter>(true).Length, collidersBefore = root.GetComponentsInChildren<Collider>(true).Length,
                rigidbodiesBefore = root.GetComponentsInChildren<Rigidbody>(true).Length, animatorsBefore = root.GetComponentsInChildren<Animator>(true).Length,
                behavioursBefore = root.GetComponentsInChildren<Behaviour>(true).Length
            };
            var result = WardenNetworkEquipmentSetup.Apply(root);
            row.fixtureCreated = result.created; row.rightHandPath = result.rightHandPath; row.leftHandPath = result.leftHandPath;
            row.rightSocketWorldScale = result.rightSocketWorldScale; row.leftSocketWorldScale = result.leftSocketWorldScale;
            row.transformsAfter = root.GetComponentsInChildren<Transform>(true).Length; row.renderersAfter = root.GetComponentsInChildren<Renderer>(true).Length;
            row.filtersAfter = root.GetComponentsInChildren<MeshFilter>(true).Length; row.collidersAfter = root.GetComponentsInChildren<Collider>(true).Length;
            row.rigidbodiesAfter = root.GetComponentsInChildren<Rigidbody>(true).Length; row.animatorsAfter = root.GetComponentsInChildren<Animator>(true).Length;
            row.behavioursAfter = root.GetComponentsInChildren<Behaviour>(true).Length;
            row.protectedPhysicsCountsSame = row.collidersBefore == row.collidersAfter && row.rigidbodiesBefore == row.rigidbodiesAfter &&
                row.animatorsBefore == row.animatorsAfter && row.behavioursBefore == row.behavioursAfter;
            if (!row.protectedPhysicsCountsSame) throw new InvalidOperationException("Controlled equipment unexpectedly added physics/animation/behaviours.");
            return row;
        }

        static int CaptureSilhouette(Camera camera, CpuPose pose, Scene scene, string path)
        {
            Renderer[] all = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Renderer>(true)).ToArray();
            bool[] enabled = all.Select(r => r.enabled).ToArray();
            var materials = pose.Draws.ToDictionary(r => r, r => r.sharedMaterials);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) throw new InvalidOperationException("Actual URP Unlit shader required.");
            var black = new Material(shader) { name = "__ReviewOnly_Black", color = Color.black };
            bool fog = RenderSettings.fog; var clear = camera.clearFlags; Color background = camera.backgroundColor;
            try {
                foreach (var renderer in all) renderer.enabled = pose.Draws.Contains(renderer);
                foreach (var renderer in pose.Draws) renderer.sharedMaterials = Enumerable.Repeat(black, materials[renderer].Length).ToArray();
                RenderSettings.fog = false; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = Color.white;
                using (var image = new CapturedImage(camera)) {
                    File.WriteAllBytes(path, image.texture.EncodeToPNG());
                    return image.texture.GetPixels32().Count(p => p.r < 32 && p.g < 32 && p.b < 32);
                }
            }
            finally {
                foreach (var entry in materials) entry.Key.sharedMaterials = entry.Value;
                for (int i = 0; i < all.Length; i++) all[i].enabled = enabled[i];
                RenderSettings.fog = fog; camera.clearFlags = clear; camera.backgroundColor = background;
                Object.DestroyImmediate(black);
            }
        }

        sealed class CapturedImage : IDisposable
        {
            public readonly Texture2D texture;
            public CapturedImage(Camera camera)
            {
                RenderTexture previous = RenderTexture.active;
                var target = RenderTexture.GetTemporary(960, 600, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Texture2D image = null;
                try {
                    RenderPipeline.SubmitRenderRequest(camera, new RenderPipeline.StandardRequest { destination = target });
                    RenderTexture.active = target; image = new Texture2D(960, 600, TextureFormat.RGB24, false);
                    image.ReadPixels(new Rect(0, 0, 960, 600), 0, 0); image.Apply(); texture = image;
                }
                catch { if (image != null) Object.DestroyImmediate(image); throw; }
                finally { RenderTexture.active = previous; RenderTexture.ReleaseTemporary(target); }
            }
            public void Dispose() { Object.DestroyImmediate(texture); }
        }

        sealed class CpuPose : IDisposable
        {
            public readonly List<Renderer> Draws = new List<Renderer>();
            public Bounds BodyBounds, GpuSkinBounds;
            readonly List<GameObject> copies = new List<GameObject>();
            readonly List<Mesh> meshes = new List<Mesh>();
            readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
            bool firstBody = true, firstGpu = true;
            public CpuPose(GameObject actor)
            {
                try {
                    Animator animator = actor.GetComponentsInChildren<Animator>(true).Single();
                    foreach (var renderer in animator.GetComponentsInChildren<Renderer>(true)) {
                        if (!renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
                        Mesh mesh; Renderer draw = renderer;
                        if (renderer is SkinnedMeshRenderer skin) {
                            if (firstGpu) { GpuSkinBounds = skin.bounds; firstGpu = false; } else GpuSkinBounds.Encapsulate(skin.bounds);
                            mesh = new Mesh { name = "__Warden_CPU_ReviewOnly" }; meshes.Add(mesh);
                            // Same verified pairing as CharacterPaletteReview/TinyHeroBakeoff.
                            // Imported ~37x rigs require true with this localToWorld matrix.
                            skin.BakeMesh(mesh, true);
                            var child = new GameObject("__CPU_ReviewSkin"); copies.Add(child); child.transform.SetParent(skin.transform, false);
                            child.layer = skin.gameObject.layer; child.AddComponent<MeshFilter>().sharedMesh = mesh;
                            var staticRenderer = child.AddComponent<MeshRenderer>(); staticRenderer.sharedMaterials = skin.sharedMaterials;
                            staticRenderer.shadowCastingMode = skin.shadowCastingMode; staticRenderer.receiveShadows = skin.receiveShadows;
                            staticRenderer.lightProbeUsage = skin.lightProbeUsage; staticRenderer.reflectionProbeUsage = skin.reflectionProbeUsage;
                            staticRenderer.probeAnchor = skin.probeAnchor;
                            if (staticRenderer.localToWorldMatrix != skin.localToWorldMatrix) throw new InvalidOperationException("CPU copy lost the original renderer matrix.");
                            skins.Add(skin); skin.enabled = false; draw = staticRenderer;
                        }
                        else mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                        if (mesh == null) continue;
                        Draws.Add(draw);
                        if (Equipment(renderer.transform, actor.transform)) continue;
                        Bounds bounds = MeshWorldBounds(mesh, renderer.localToWorldMatrix);
                        if (firstBody) { BodyBounds = bounds; firstBody = false; } else BodyBounds.Encapsulate(bounds);
                    }
                    if (firstBody || Draws.Count == 0) throw new InvalidOperationException("No visible Warden CPU body.");
                }
                catch { Dispose(); throw; }
            }
            public void Dispose() {
                foreach (var skin in skins) if (skin != null) skin.enabled = true;
                foreach (var child in copies) if (child != null) Object.DestroyImmediate(child);
                foreach (var mesh in meshes) if (mesh != null) Object.DestroyImmediate(mesh);
            }
        }

        static ActorRow Metrics(GameObject actor, CpuPose pose, string label, string stage)
        {
            var animator = actor.GetComponentsInChildren<Animator>(true).Single();
            var originalRenderers = actor.GetComponentsInChildren<Renderer>(true).Where(r => !r.name.StartsWith("__CPU_", StringComparison.Ordinal)).ToArray();
            var bones = actor.GetComponentsInChildren<SkinnedMeshRenderer>(true).SelectMany(s => s.bones).Where(t => t != null).Distinct().ToArray();
            var helmet = actor.GetComponentsInChildren<MeshFilter>(true).SingleOrDefault(f => f.name == WardenSilhouetteSetup.HelmetNode);
            var sword = actor.GetComponentsInChildren<MeshFilter>(true).SingleOrDefault(f => f.name == "Model" && f.transform.parent?.name == WardenSilhouetteSetup.SwordNode);
            var filters = actor.GetComponentsInChildren<MeshFilter>(true).Where(f => !f.name.StartsWith("__CPU_", StringComparison.Ordinal)).ToArray();
            Bounds helmetBounds = helmet != null ? MeshWorldBounds(helmet.sharedMesh, helmet.transform.localToWorldMatrix) : new Bounds();
            var row = new ActorRow { actor = label, stage = stage, position = actor.transform.position, rotation = actor.transform.eulerAngles,
                visualScale = animator.transform.lossyScale, rootScale = actor.transform.lossyScale, bodyBounds = pose.BodyBounds, bodyHeight = pose.BodyBounds.size.y,
                originalGpuSkinBounds = pose.GpuSkinBounds, gpuSkinHeight = pose.GpuSkinBounds.size.y, helmetWorldBounds = helmetBounds,
                swordWorldBounds = sword != null ? MeshWorldBounds(sword.sharedMesh, sword.transform.localToWorldMatrix) : new Bounds(),
                renderers = originalRenderers.Length, meshFilters = filters.Length, colliders = actor.GetComponentsInChildren<Collider>(true).Length,
                rigidbodies = actor.GetComponentsInChildren<Rigidbody>(true).Length, animators = actor.GetComponentsInChildren<Animator>(true).Length,
                boneHeight = bones.Length == 0 ? 0 : bones.Max(t => t.position.y) - bones.Min(t => t.position.y),
                helmetWidthOverBodyWidth = helmetBounds.size.x / Mathf.Max(.0001f, pose.BodyBounds.size.x),
                avatarPath = animator.avatar == null ? "" : AssetDatabase.GetAssetPath(animator.avatar) };
            row.avatarGuid = AssetDatabase.AssetPathToGUID(row.avatarPath);
            if (animator.avatar != null && animator.avatar.isHuman) row.headBone = animator.GetBoneTransform(HumanBodyBones.Head)?.position ?? Vector3.zero;
            foreach (var renderer in pose.Draws) {
                Mesh mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh != null) for (int s = 0; s < mesh.subMeshCount; s++) if (mesh.GetTopology(s) == MeshTopology.Triangles) row.triangles += mesh.GetIndexCount(s) / 3;
            }
            row.importerScales = originalRenderers.Select(r => r is SkinnedMeshRenderer skin ? skin.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh)
                .Where(m => m != null).Select(AssetDatabase.GetAssetPath).Distinct().Where(p => AssetImporter.GetAtPath(p) is ModelImporter)
                .Select(p => p + " | globalScale=" + ((ModelImporter)AssetImporter.GetAtPath(p)).globalScale.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " | metaSHA=" + Hash(p + ".meta")).ToArray();
            return row;
        }

        static Bounds MeshWorldBounds(Mesh mesh, Matrix4x4 matrix)
        {
            if (mesh == null) throw new InvalidOperationException("Missing drawn mesh.");
            Vector3[] vertices = mesh.vertices; var indices = mesh.triangles.Distinct().ToArray();
            if (indices.Length == 0) throw new InvalidOperationException("No drawn mesh indices.");
            var bounds = new Bounds(matrix.MultiplyPoint3x4(vertices[indices[0]]), Vector3.zero);
            foreach (int index in indices) bounds.Encapsulate(matrix.MultiplyPoint3x4(vertices[index]));
            return bounds;
        }
        static bool Equipment(Transform child, Transform root)
        {
            for (var t = child; t != null && t != root; t = t.parent) {
                string name = t.name.ToLowerInvariant(); if (name.Contains("sword") || name.Contains("shield") || name.Contains("weapon")) return true;
            }
            return false;
        }
        static void PreparePose(GameObject root)
        {
            var animator = root.GetComponentsInChildren<Animator>(true).Single();
            bool enabled = animator.enabled;
            try { animator.enabled = true; animator.Rebind(); animator.Update(0); }
            finally { animator.enabled = enabled; }
        }
        static Vector3 BoneFront(GameObject root, out Vector3 rawFront)
        {
            var animator = root.GetComponentsInChildren<Animator>(true).Single();
            if (animator.avatar == null || !animator.avatar.isHuman) throw new InvalidOperationException("Humanoid bone basis required; do not silently use the mismatched Actor.forward.");
            Transform left = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm), right = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            Transform head = animator.GetBoneTransform(HumanBodyBones.Head), hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (left == null || right == null || head == null || hips == null) throw new InvalidOperationException("Missing required front-basis bones.");
            rawFront = Vector3.Cross(right.position - left.position, head.position - hips.position).normalized;
            Vector3 front = Vector3.ProjectOnPlane(rawFront, Vector3.up);
            if (front.sqrMagnitude < .000001f) throw new InvalidOperationException("Degenerate horizontal bone front.");
            return front.normalized;
        }
        static GameObject FindWarden(Scene scene) => scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WardenActor>(true)).Single().gameObject;
        static string Snapshot(GameObject root, bool allowExactMeshes, bool allowSwordFirstSlot = false)
        {
            var rows = new List<string>();
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) {
                string path = EnvironmentKitReview.PathOf(t);
                rows.Add(path + "|GO|" + t.gameObject.activeSelf + "|" + t.gameObject.layer + "|" + t.gameObject.tag + "|" + GameObjectUtility.GetStaticEditorFlags(t.gameObject));
                foreach (var component in t.GetComponents<Component>()) {
                    if (component == null) throw new InvalidOperationException("Missing actor component: " + path);
                    string json = EditorJsonUtility.ToJson(component);
                    if (allowExactMeshes && component is MeshFilter && (t.name == WardenSilhouetteSetup.HelmetNode || (t.name == "Model" && t.parent?.name == WardenSilhouetteSetup.SwordNode)))
                        json = Regex.Replace(json, "\"m_Mesh\"\\s*:\\s*\\{[^}]*\\}", "\"m_Mesh\":{}");
                    if (allowSwordFirstSlot && component is MeshRenderer && t.name == "Model" && t.parent?.name == WardenSilhouetteSetup.SwordNode) {
                        const string firstSlot = "(\"m_Materials\"\\s*:\\s*\\[)\\s*\\{[^}]*\\}";
                        if (Regex.Matches(json, firstSlot).Count != 1) throw new InvalidOperationException("Cannot isolate exact serialized sword first slot.");
                        json = Regex.Replace(json, firstSlot, "$1{}"); // slot 1/2 and every other property remain frozen.
                    }
                    rows.Add(path + "|" + component.GetType().FullName + "|" + json);
                }
            }
            rows.Sort(StringComparer.Ordinal); return string.Join("\n", rows);
        }
        static Dictionary<string, string> FreezeSources()
        {
            string[] scenes = Directory.GetFiles("Assets/_Game/Scenes", "*.unity", SearchOption.TopDirectoryOnly);
            var roots = scenes.Concat(new[] { NetworkPrefab, WardenSilhouetteSetup.CanonicalWeaponPath, WardenNetworkEquipmentSetup.ShieldPrefabPath,
                WardenSilhouetteSelection.OriginalBladePath, WardenSilhouetteSelection.SelectedBladePath });
            var paths = roots.Concat(roots.SelectMany(p => AssetDatabase.GetDependencies(p, true)))
                .Concat(Directory.GetFiles("Assets/_Game/Settings/Navigation", "*", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles("ProjectSettings", "*", SearchOption.TopDirectoryOnly))
                .Concat(WardenSilhouetteSetup.CaptureSourceFingerprints().Keys)
                .Concat(new[] { "Docs/Art/Warden-来源与许可核对.md" }).Select(p => p.Replace('\\', '/')).Where(File.Exists)
                .Where(p => !p.StartsWith(WardenSilhouetteSetup.AssetRoot + "/", StringComparison.Ordinal));
            return paths.SelectMany(p => File.Exists(p + ".meta") ? new[] { p, p + ".meta" } : new[] { p }).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => p, Hash);
        }
        static bool BackupSource(string path)
        {
            string source = path.EndsWith(".meta", StringComparison.Ordinal) ? path.Substring(0, path.Length - 5) : path;
            return source.StartsWith("Assets/_Game/Scenes/", StringComparison.Ordinal) ||
                source.EndsWith(".mat", StringComparison.Ordinal) ||
                (source.StartsWith("Assets/_Game/", StringComparison.Ordinal) && source.EndsWith(".prefab", StringComparison.Ordinal)) ||
                source.StartsWith("Assets/_Game/Settings/Navigation/", StringComparison.Ordinal) || source.StartsWith("ProjectSettings/", StringComparison.Ordinal) ||
                new[] { WardenSilhouetteSetup.HelmetSourcePath, WardenSilhouetteSetup.SwordSourcePath, WardenSilhouetteSetup.LicensePath,
                    WardenSilhouetteSetup.ReferenceHelmetPath, WardenSilhouetteSetup.ReferenceSwordPath, "Docs/Art/Warden-来源与许可核对.md" }.Contains(source);
        }
        static void CopyMaterialReferences(GameObject root, Dictionary<Material, Material> copies)
        {
            // URP may validate keyword/color compatibility when it renders a
            // material for the first time. Render disposable copies, never use
            // a persistent project material as the validation/write target.
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                Material[] slots = renderer.sharedMaterials;
                for (int i = 0; i < slots.Length; i++)
                {
                    Material source = slots[i];
                    if (source == null) continue;
                    if (copies.Values.Contains(source)) continue; // already a known isolated clone: preserve its reference.
                    if (!copies.TryGetValue(source, out Material candidate))
                    {
                        candidate = Object.Instantiate(source);
                        candidate.name = source.name + "__ReviewOnly";
                        candidate.hideFlags = HideFlags.None;
                        copies.Add(source, candidate);
                    }
                    slots[i] = candidate;
                }
                renderer.sharedMaterials = slots;
            }
        }
        static string Lighting() => JsonUtility.ToJson(new LightingRow {
            fog = RenderSettings.fog, fogMode = RenderSettings.fogMode.ToString(), fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance,
            fogDensity = RenderSettings.fogDensity, fogColor = RenderSettings.fogColor, ambientMode = RenderSettings.ambientMode.ToString(),
            ambientSky = RenderSettings.ambientSkyColor, ambientEquator = RenderSettings.ambientEquatorColor, ambientGround = RenderSettings.ambientGroundColor,
            ambientIntensity = RenderSettings.ambientIntensity, skybox = RenderSettings.skybox == null ? "" : AssetDatabase.GetAssetPath(RenderSettings.skybox),
            sun = RenderSettings.sun == null ? "" : EnvironmentKitReview.PathOf(RenderSettings.sun.transform), lightmapCount = LightmapSettings.lightmaps.Length,
            lights = SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<Light>(true)).Select(l => EnvironmentKitReview.PathOf(l.transform) + "|" + EditorJsonUtility.ToJson(l)).OrderBy(p => p, StringComparer.Ordinal).ToArray()
        }, true);
        [Serializable] sealed class LightingRow {
            public bool fog; public string fogMode, ambientMode, skybox, sun; public float fogStart, fogEnd, fogDensity, ambientIntensity;
            public Color fogColor, ambientSky, ambientEquator, ambientGround; public int lightmapCount; public string[] lights;
        }
        static void RequireSource()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || SceneManager.sceneCount != 1 ||
                SceneManager.GetActiveScene().path != Valley || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Single clean idle 10_EmberValley required; preserve unsaved user work.");
            // Test runners restore these flags in memory before Unity may flush
            // them to disk. Do not freeze a known-stale settings file or exclude
            // ProjectSettings from the invariant to hide that delayed write.
            string settings = File.ReadAllText("ProjectSettings/EditorSettings.asset");
            Match enabled = Regex.Match(settings, @"(?m)^\s*m_EnterPlayModeOptionsEnabled:\s*([01])\s*$");
            Match options = Regex.Match(settings, @"(?m)^\s*m_EnterPlayModeOptions:\s*(\d+)\s*$");
            if (!enabled.Success || !options.Success ||
                (enabled.Groups[1].Value == "1") != EditorSettings.enterPlayModeOptionsEnabled ||
                int.Parse(options.Groups[1].Value) != (int)EditorSettings.enterPlayModeOptions)
                throw new InvalidOperationException("Editor play options have not settled on disk; wait for the normal Editor settings write. No settings modified.");
        }
        static void RequireOwnedTemp(string path)
        {
            if (!Regex.IsMatch(path ?? "", "^Assets/_Game/Scenes/__WardenSilhouetteReview_[a-f0-9]{32}\\.unity$") ||
                !Path.GetFullPath(path).StartsWith(Path.GetFullPath("Assets/_Game/Scenes") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Not our exact owned temporary scene path.");
        }
        static bool Unchanged(Dictionary<string, string> frozen) => frozen.All(p => File.Exists(p.Key) && Hash(p.Key) == p.Value);
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        static void SaveReport(Report report) => File.WriteAllText(Path.Combine(report.output, "report.json"), JsonUtility.ToJson(report, true));
        static void WriteManifest(string path, Dictionary<string, string> hashes, string scope = null) => File.WriteAllText(path, JsonUtility.ToJson(new Manifest {
            scope = scope ?? "Original scenes/metas, referenced prefabs/Avatar/animation/materials, navigation/settings and actual selected source/license/dependencies frozen; owned candidate mesh outputs excluded.",
            files = hashes.Select(p => new FileRow { path = p.Key, sha256 = p.Value, bytes = new FileInfo(p.Key).Length }).ToArray()
        }, true));
    }
}
