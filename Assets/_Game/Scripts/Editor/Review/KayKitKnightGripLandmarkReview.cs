using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    public static partial class KayKitNativeCharacterReview
    {
        const string GripLandmarkPrefix = "Emberfall.KnightGripLandmarkReview.";
        const string GripLandmarkScript = "Assets/_Game/Scripts/Editor/Review/KayKitKnightGripLandmarkReview.cs";
        const string GripLandmarkBottleRoot = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/07_Environment_Ruins/KayKit_Dungeon_Remastered";
        const string GripLandmarkBottleAssets = GripLandmarkBottleRoot + "/addons/kaykit_dungeon_remastered/Assets";
        static readonly string[] GripLandmarkBottleNames = { "bottle_A_brown", "bottle_A_green", "bottle_A_labeled_brown", "bottle_A_labeled_green", "bottle_B_brown", "bottle_B_green", "bottle_C_brown", "bottle_C_green" };
        static bool GripLandmarkPending;

        [Serializable] sealed class GripLandmarkIdentity
        {
            public string path, name, guid;
            public long localId;
        }
        [Serializable] sealed class GripLandmarkTriangle
        {
            public int id, submesh, triangleInSubmesh, baseVertex;
            public string objectName, rendererPath, rendererSourceResolution;
            public GripLandmarkIdentity rendererSource, mesh;
            public int[] vertexIndices;
            public Vector3[] sourceVertices, evaluatedLocalVertices, worldVertices;
            public Vector2[] uv;
            public bool uvAvailable;
        }
        [Serializable] sealed class GripLandmarkGeometry
        {
            public string objectName, scope;
            public GripLandmarkTriangle[] triangles;
        }
        [Serializable] sealed class GripLandmarkImage
        {
            public string file, geometryFile, scope;
            public Vector3 position, target;
            public Quaternion rotation;
            public float fieldOfView, nearClipPlane, farClipPlane;
            public int width, height;
            public bool isolatedPart;
            public string[] visibleRendererPaths, fittedRendererPaths;
            public Vector3 fittedBoundsMin, fittedBoundsMax;
            public float frameFill, nearCameraMargin, fittedDistance, minVisibleCameraDepth;
        }
        [Serializable] sealed class GripLandmarkBottle
        {
            public string name, geometryFile;
            public GripLandmarkIdentity source;
            public Vector3 sourceDrawnBoundsSize, previewDrawnBoundsSize;
            public float constantPreviewHeight = .20f;
            public string scope = "20cm is an explicit prop-comparison display size, not a Knight hand/mouth fit or a transferred Tiny criterion. Original imported mesh proportions/UVs preserved; source still capped, no cork removal or grip socket.";
        }
        [Serializable] sealed class GripLandmarkMemory
        {
            public string path, name, type, guid, beforeJsonSha256, afterJsonSha256, beforeJson, afterJson;
            public long localId;
            public int beforeJsonLength, afterJsonLength;
            public bool unchanged;
        }
        sealed class GripLandmarkWatch
        {
            public Object target;
            public GripLandmarkMemory row;
            // Keep the exact full string internally, never a normalized/hash-only comparison.
            public string beforeJson;
        }
        [Serializable] sealed class GripLandmarkMemoryStage
        {
            public string stage;
            public int comparedMaterialObjects;
            public bool allFullJsonUnchanged;
            public GripLandmarkMemory[] changedObjects;
        }
        [Serializable] sealed class GripLandmarkOwnedMaterial
        {
            public GripLandmarkIdentity source;
            public string name, afterFirstValidationSha256, afterSecondValidationSha256, afterFirstValidationJson, afterSecondValidationJson;
            public bool officialSecondValidationFullJsonFixedPoint;
        }
        [Serializable] sealed class GripLandmarkReport
        {
            public string label, status, error, directory, startedUtc, finishedUtc, module;
            public string scope = "Editor-only original-avatar Idle0 CPU-baked triangle inventory and neutral close images. Outputs evidence only: no asset/importer/socket/wrist/clip/profile/scene/runtime/test/save/network/build writes. No palm or mouth selected automatically; no distance/angle threshold or visual acceptance is inferred.";
            public string coordinateConvention = "Triangle world points are preview world metres. PNG pixel x is from left and y is from TOP; ReadGripLandmarkPixel uses pixel centres and reports a ray/triangle intersection, never a nearest whole-mesh point. IDs are JSON triangle IDs, not an encoded-colour render pass.";
            public string landmarkPolicy = "Head/RightHand bones may frame cameras ONLY, never serve as mouth/palm landmarks. Context and deliberately isolated renderer images are separate; isolated face does not certify visibility through the helmet. A human/reviewer must choose original mesh triangle IDs/barycentrics from rendered evidence before subsequent actual-input measurements.";
            public string priorFailure = "knight-grip-landmarks-20261003-a1/result.json remains FAILED and immutable (995104650 bytes). Its 8 embedded source bottle materials changed in memory; this job does not retroapprove it or exempt/normalize any cache/property.";
            public string memoryPolicy = "ALL persistent objects retain original full-JSON strings internally and use exact full-string equality through cleanup. Unchanged report rows store identity/SHA256/length only; changed rows retain complete before/after JSON. Additional same-job material-stage checks compare the same original full strings, never replace baselines.";
            public string bottleConstruction = "Manual disposable hierarchy copies original local TRS/active/order, static mesh references and renderer slots. Source materials are read only and are NEVER assigned to a preview renderer. Official URP validation applies ONLY to Stage-owned new materials; no source prefab instantiation, CopySerialized renderer or source material validation.";
            public string prefab = GenericPrefab, native = Source + "Knight.fbx", pose = "Idle", poseTime = "0";
            public float visualScale = .75f, animatorPositionDrift, animatorRotationDrift;
            public bool sourceDiskUnchanged, sourceObjectFullJsonUnchanged, loadedScenesUnchanged, renderSettingsUnchanged,
                enabledSceneClosureUnchanged, candidateExcludedFromEnabledClosure, memoryWarmFixedPoint;
            public bool materialStagesFullJsonUnchanged;
            public bool gripMeasured = false, mouthMeasured = false, actualInputMeasured = false, accepted = false;
            public int protectedFiles, protectedObjects;
            public string[] enabledClosureBefore, enabledClosureAfter;
            public FileRow[] protectedBefore, protectedAfter;
            public GripLandmarkMemory[] sourceObjects;
            public GripLandmarkMemoryStage[] materialStages;
            public GripLandmarkOwnedMaterial[] ownedBottleMaterials;
            public GripLandmarkImage[] images;
            public GripLandmarkBottle[] bottles;
        }
        [Serializable] sealed class GripLandmarkPick
        {
            public string scope = "Read-only explicit image pixel ray pick, NOT an accepted anatomical landmark or grip/mouth measurement.";
            public string label, image, rendererPath;
            public int triangleId, submesh, triangleInSubmesh;
            public GripLandmarkIdentity mesh, rendererSource;
            public Vector2 pixelFromTop;
            public Vector3 barycentric, worldPoint, surfaceNormal;
            public float distance;
        }

        /// <summary>Queues one fresh evidence-only job. A retained label is never overwritten or rerun.</summary>
        public static string RequestGripLandmarkReview(string uniqueLabel)
        {
            GripLandmarkValidateLabel(uniqueLabel);
            string key = GripLandmarkPrefix + uniqueLabel;
            string retained = SessionState.GetString(key, "");
            if (retained.Length != 0)
            {
                if (!File.Exists(retained)) throw new IOException("Retained landmark report missing: " + retained);
                return retained;
            }
            RequireIdle();
            if (GripLandmarkPending) throw new InvalidOperationException("A landmark evidence job is already pending.");
            string active = SessionState.GetString(GripLandmarkPrefix + "active", "");
            if (File.Exists(active))
            {
                var prior = JsonUtility.FromJson<GripLandmarkReport>(File.ReadAllText(active));
                if (prior.status == "queued" || prior.status == "running")
                    throw new InvalidOperationException("Inspect retained active landmark job first: " + active);
            }
            string directory = Path.GetFullPath("Builds/ArtReview/kaykit-landmarks/" + uniqueLabel);
            if (Directory.Exists(directory)) throw new IOException("Fresh landmark evidence directory required.");
            Directory.CreateDirectory(directory);
            string record = Path.Combine(directory, "result.json");
            var report = new GripLandmarkReport { label = uniqueLabel, directory = directory, status = "queued",
                startedUtc = DateTime.UtcNow.ToString("O"), module = typeof(KayKitNativeCharacterReview).Assembly.ManifestModule.ModuleVersionId.ToString() };
            GripLandmarkWrite(record, report);
            SessionState.SetString(key, record); SessionState.SetString(GripLandmarkPrefix + "active", record);
            GripLandmarkPending = true;
            double deadline = EditorApplication.timeSinceStartup + 180;
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < deadline) return;
                EditorApplication.update -= callback;
                try
                {
                    RequireIdle(); report.status = "running"; GripLandmarkWrite(record, report);
                    GripLandmarkCapture(report); report.status = "completed";
                }
                catch (Exception ex) { report.status = "failed"; report.error = ex.ToString(); Debug.LogException(ex); }
                finally
                {
                    report.finishedUtc = DateTime.UtcNow.ToString("O"); GripLandmarkWrite(record, report);
                    GripLandmarkPending = false; SessionState.EraseString(GripLandmarkPrefix + "active");
                }
            };
            EditorApplication.update += callback;
            return record;
        }

        static void GripLandmarkCapture(GripLandmarkReport report)
        {
            var scenes = OriginalScenes(); string sceneMemory = SceneMemory(scenes), renderState = RenderState();
            var renderSettings = Resources.FindObjectsOfTypeAll<RenderSettings>().Select(value => GripLandmarkWatchObject(value, "loaded-scene-RenderSettings")).ToArray();
            report.enabledClosureBefore = GripLandmarkClosure();
            report.protectedBefore = GripLandmarkProtected(); report.protectedFiles = report.protectedBefore.Length;
            var watches = GripLandmarkMemoryBaseline(report.protectedBefore);
            report.sourceObjects = watches.Select(w => w.row).Concat(renderSettings.Select(w => w.row)).ToArray();
            report.protectedObjects = watches.Length + renderSettings.Length;
            report.memoryWarmFixedPoint = true;
            var images = new List<GripLandmarkImage>(); var bottles = new List<GripLandmarkBottle>();
            var materialStages = new List<GripLandmarkMemoryStage>(); var ownedMaterials = new List<GripLandmarkOwnedMaterial>();
            var materialWatches = watches.Where(w => w.target is Material).ToArray();
            try
            {
                using (var stage = new Stage())
                {
                    var candidate = stage.preview.InstantiatePrefabInScene(Required<GameObject>(GenericPrefab));
                    stage.preview.camera.scene = candidate.scene;
                    if (candidate.transform.localScale != Vector3.one * .75f ||
                        candidate.GetComponentsInChildren<MonoBehaviour>(true).Length != 0 || candidate.GetComponentsInChildren<Collider>(true).Length != 0)
                        throw new InvalidOperationException("Exact .75 pure visual Knight candidate required.");
                    var animator = candidate.GetComponentInChildren<Animator>(true);
                    if (animator == null || AssetDatabase.GetAssetPath(animator.avatar) != Source + "Knight.fbx")
                        throw new InvalidOperationException("Exact original Knight Avatar required.");
                    stage.Materials(candidate);
                    using (var rig = new Rig(animator))
                    {
                        rig.Start(AssetDatabase.LoadAllAssetsAtPath(Source + "Knight.fbx").OfType<AnimationClip>().Single(c => c.name == "Idle"));
                        rig.Pose(0); report.animatorPositionDrift = rig.PositionDrift; report.animatorRotationDrift = rig.RotationDrift;
                        using (var drawn = new Drawn(candidate))
                        {
                            var geometry = drawn.GripLandmarkDescribe(candidate.name, Required<GameObject>(GenericPrefab));
                            string geometryFile = "knight-idle0-triangles.json";
                            GripLandmarkWrite(Path.Combine(report.directory, geometryFile), geometry);
                            var renderers = candidate.GetComponentsInChildren<Renderer>(true);
                            var head = renderers.Single(r => r.name == "Knight_Head" && r.enabled);
                            var arm = renderers.Single(r => r.name == "Knight_ArmRight" && r.enabled);
                            var helmet = renderers.Single(r => r.name == "Knight_Helmet" && r.enabled);
                            var headContext = new[] { head, helmet }; var headOnly = new[] { head }; var armOnly = new[] { arm };
                            GripLandmarkShot(stage, candidate, geometry, geometryFile, "01-knight-context.png", new Vector3(2.1f, .45f, 3), 32, false, null, renderers, report, images);
                            GripLandmarkShot(stage, candidate, geometry, geometryFile, "02-head-context-front.png", new Vector3(0, .04f, 1), 35, false, null, headContext, report, images);
                            GripLandmarkShot(stage, candidate, geometry, geometryFile, "03-head-context-side.png", new Vector3(1, .04f, .08f), 35, false, null, headContext, report, images);
                            GripLandmarkShot(stage, candidate, geometry, geometryFile, "04-head-isolated-front.png", new Vector3(0, .04f, 1), 35, true, head, headOnly, report, images);
                            GripLandmarkShot(stage, candidate, geometry, geometryFile, "05-head-isolated-side.png", new Vector3(1, .04f, .08f), 35, true, head, headOnly, report, images);
                            GripLandmarkShot(stage, candidate, geometry, geometryFile, "06-hand-context-front.png", new Vector3(0, .045f, 1), 30, false, null, armOnly, report, images);
                            GripLandmarkShot(stage, candidate, geometry, geometryFile, "07-hand-context-side.png", new Vector3(1, .045f, .03f), 30, false, null, armOnly, report, images);
                            GripLandmarkShot(stage, candidate, geometry, geometryFile, "08-arm-isolated-front.png", new Vector3(0, .045f, 1), 30, true, arm, armOnly, report, images);
                            GripLandmarkShot(stage, candidate, geometry, geometryFile, "09-arm-isolated-side.png", new Vector3(1, .045f, .03f), 30, true, arm, armOnly, report, images);
                            GripLandmarkShot(stage, candidate, geometry, geometryFile, "10-hand-context-three-quarter.png", new Vector3(.75f, .12f, .8f), 30, false, null, armOnly, report, images);
                        }
                    }
                    candidate.SetActive(false);
                    GripLandmarkMaterialStage("after-knight-images", materialWatches, materialStages);
                    for (int index = 0; index < GripLandmarkBottleNames.Length; index++)
                    {
                        string name = GripLandmarkBottleNames[index], path = GripLandmarkBottleAssets + "/fbx/" + name + ".fbx";
                        var sourceBottle = Required<GameObject>(path);
                        var bottle = stage.GripLandmarkCloneStaticBottle(sourceBottle, ownedMaterials);
                        GripLandmarkMaterialStage("after-" + name + "-construction", materialWatches, materialStages);
                        var row = new GripLandmarkBottle { name = name, source = GripLandmarkId(sourceBottle), geometryFile = name + "-triangles.json" };
                        using (var drawn = new Drawn(bottle))
                        {
                            Bounds sourceBounds = drawn.Bounds(); row.sourceDrawnBoundsSize = sourceBounds.size;
                            if (sourceBounds.size.y <= .001f) throw new InvalidOperationException("Nondegenerate original bottle required.");
                            bottle.transform.localScale *= row.constantPreviewHeight / sourceBounds.size.y;
                            Bounds displayed = drawn.Bounds();
                            bottle.transform.position -= new Vector3(displayed.center.x, displayed.min.y, displayed.center.z);
                            row.previewDrawnBoundsSize = drawn.Bounds().size;
                            var geometry = drawn.GripLandmarkDescribe(name, sourceBottle);
                            GripLandmarkWrite(Path.Combine(report.directory, row.geometryFile), geometry);
                            var bottleRenderers = bottle.GetComponentsInChildren<Renderer>(true);
                            GripLandmarkShot(stage, bottle, geometry, row.geometryFile, "bottle-" + name + "-front.png", new Vector3(0, .08f, 1), 32, false, null, bottleRenderers, report, images);
                            GripLandmarkShot(stage, bottle, geometry, row.geometryFile, "bottle-" + name + "-side.png", new Vector3(1, .08f, 0), 32, false, null, bottleRenderers, report, images);
                        }
                        bottles.Add(row); bottle.SetActive(false);
                        GripLandmarkMaterialStage("after-" + name + "-images", materialWatches, materialStages);
                    }
                    GripLandmarkMaterialStage("before-preview-cleanup", materialWatches, materialStages);
                }
            }
            finally
            {
                report.images = images.ToArray(); report.bottles = bottles.ToArray();
                report.materialStages = materialStages.ToArray(); report.ownedBottleMaterials = ownedMaterials.ToArray();
                report.materialStagesFullJsonUnchanged = materialStages.All(s => s.allFullJsonUnchanged);
                foreach (var watch in watches.Concat(renderSettings))
                    GripLandmarkCompareMemory(watch, watch.row);
                report.sourceObjectFullJsonUnchanged = watches.All(w => w.row.unchanged);
                report.protectedAfter = GripLandmarkProtected();
                report.sourceDiskUnchanged = JsonUtility.ToJson(new GripLandmarkFiles { rows = report.protectedBefore }) == JsonUtility.ToJson(new GripLandmarkFiles { rows = report.protectedAfter });
                report.loadedScenesUnchanged = SameScenes(scenes) && sceneMemory == SceneMemory(scenes);
                report.renderSettingsUnchanged = renderState == RenderState() && renderSettings.All(w => w.row.unchanged) &&
                    renderSettings.Select(w => w.target.GetInstanceID()).OrderBy(id => id).SequenceEqual(
                        Resources.FindObjectsOfTypeAll<RenderSettings>().Select(r => r.GetInstanceID()).OrderBy(id => id));
                report.enabledClosureAfter = GripLandmarkClosure();
                report.enabledSceneClosureUnchanged = report.enabledClosureBefore.SequenceEqual(report.enabledClosureAfter);
                report.candidateExcludedFromEnabledClosure = !report.enabledClosureAfter.Any(p => p.StartsWith("Assets/_Game/Art/Review/KayKitHero/", StringComparison.Ordinal) || p.StartsWith("Assets/_Game/Art/Review/KayKitHeroMotion/", StringComparison.Ordinal));
                if (!report.sourceObjectFullJsonUnchanged || !report.materialStagesFullJsonUnchanged || !report.sourceDiskUnchanged || !report.loadedScenesUnchanged ||
                    !report.renderSettingsUnchanged || !report.enabledSceneClosureUnchanged || !report.candidateExcludedFromEnabledClosure)
                    throw new InvalidOperationException("Landmark evidence protection failed; full raw differences retained, no restoration or acceptance.");
            }
        }

        static void GripLandmarkShot(Stage stage, GameObject root, GripLandmarkGeometry geometry, string geometryFile, string file,
            Vector3 viewDirection, float fieldOfView, bool isolate, Renderer selected, Renderer[] framing,
            GripLandmarkReport report, List<GripLandmarkImage> images)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            bool[] enabled = renderers.Select(r => r.enabled).ToArray();
            try
            {
                if (isolate)
                {
                    if (selected == null || !selected.enabled) throw new InvalidOperationException("An originally visible diagnostic part is required.");
                    foreach (var renderer in renderers) renderer.enabled = renderer == selected;
                }
                var visible = new HashSet<string>(renderers.Where(r => r.enabled && !r.forceRenderingOff && r.gameObject.activeInHierarchy)
                    .Select(r => PathOf(r.transform, root.transform)), StringComparer.Ordinal);
                var fitPaths = new HashSet<string>(framing.Select(r => PathOf(r.transform, root.transform)).Where(visible.Contains), StringComparer.Ordinal);
                var fitTriangles = geometry.triangles.Where(t => fitPaths.Contains(t.rendererPath)).ToArray();
                Bounds bounds = GripLandmarkBounds(fitTriangles); Vector3 target = bounds.center;
                var fitCorners = fitTriangles.GroupBy(t => t.rendererPath).SelectMany(group => GripLandmarkBoundsCorners(GripLandmarkBounds(group))).ToArray();
                Quaternion rotation = Quaternion.LookRotation(-viewDirection.normalized, Vector3.up);
                Quaternion inverse = Quaternion.Inverse(rotation);
                const float fill = .82f, near = .02f, cameraMargin = .02f;
                float tangent = Mathf.Tan(fieldOfView * Mathf.Deg2Rad * .5f), distance = near + cameraMargin;
                foreach (Vector3 corner in fitCorners)
                {
                    Vector3 point = inverse * (corner - target);
                    distance = Mathf.Max(distance, Mathf.Abs(point.x) / (fill * tangent) - point.z,
                        Mathf.Abs(point.y) / (fill * tangent) - point.z, near + cameraMargin - point.z);
                }
                var visiblePoints = geometry.triangles.Where(t => visible.Contains(t.rendererPath)).SelectMany(t => t.worldVertices).ToArray();
                if (visiblePoints.Length == 0) throw new InvalidOperationException("Actual visible geometry required for camera near-plane check.");
                // Context parts may be outside the close framing rectangle, but none may cross the camera near plane.
                float minRelativeDepth = visiblePoints.Min(point => (inverse * (point - target)).z);
                distance = Mathf.Max(distance, near + cameraMargin - minRelativeDepth);
                Vector3 position = target + viewDirection.normalized * distance;
                stage.Camera(position, target, fieldOfView);
                var camera = stage.preview.camera;
                camera.orthographic = false;
                camera.farClipPlane = Mathf.Max(camera.farClipPlane, distance + visiblePoints.Max(point => (inverse * (point - target)).z) + cameraMargin);
                var row = new GripLandmarkImage { file = file, geometryFile = geometryFile, position = position, target = target,
                    rotation = camera.transform.rotation, fieldOfView = fieldOfView, nearClipPlane = camera.nearClipPlane,
                    farClipPlane = camera.farClipPlane, width = 1200, height = 1200, isolatedPart = isolate,
                    visibleRendererPaths = visible.OrderBy(path => path, StringComparer.Ordinal).ToArray(), fittedRendererPaths = fitPaths.OrderBy(path => path, StringComparer.Ordinal).ToArray(),
                    fittedBoundsMin = bounds.min, fittedBoundsMax = bounds.max, frameFill = fill, nearCameraMargin = cameraMargin,
                    fittedDistance = distance, minVisibleCameraDepth = distance + minRelativeDepth,
                    scope = (isolate ? "Deliberately isolated original renderer on disposable preview ONLY. Does not prove visibility through helmet/equipment or actual grip/mouth contact." : "Neutral original assembled appearance; camera target is framing only, no anatomical landmark or contact acceptance.") +
                        " Camera distance is solved from actual drawn per-renderer AABB corners and FOV (square image); 82% framing and 2cm camera near-plane margin are composition settings, NOT anatomical/contact criteria. Context outside fitted parts may remain outside the image." };
                stage.Save(Path.Combine(report.directory, file), row.width, row.height); images.Add(row);
            }
            finally { for (int i = 0; i < renderers.Length; i++) renderers[i].enabled = enabled[i]; }
        }

        static IEnumerable<Vector3> GripLandmarkBoundsCorners(Bounds bounds)
        {
            for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                    for (int z = -1; z <= 1; z += 2)
                        yield return bounds.center + Vector3.Scale(bounds.extents, new Vector3(x, y, z));
        }

        // Only this new partial adds bottle-specific helpers; existing Stage methods remain untouched.
        sealed partial class Stage
        {
            readonly Dictionary<Material, Material> GripLandmarkBottleMaterials = new Dictionary<Material, Material>();
            object GripLandmarkLitValidator;
            MethodInfo GripLandmarkLitValidate;

            public GameObject GripLandmarkCloneStaticBottle(GameObject source, List<GripLandmarkOwnedMaterial> materialRows)
            {
                if (!EditorUtility.IsPersistent(source)) throw new InvalidOperationException("Original persistent static bottle required.");
                foreach (var component in source.GetComponentsInChildren<Component>(true))
                    if (component == null || !(component is Transform || component is MeshFilter || component is MeshRenderer))
                        throw new InvalidOperationException("Bottle preview accepts only original Transform/MeshFilter/MeshRenderer; no guessed rig adapter: " + source.name);
                return GripLandmarkCloneStaticNode(source.transform, null, materialRows);
            }

            GameObject GripLandmarkCloneStaticNode(Transform original, Transform parent, List<GripLandmarkOwnedMaterial> materialRows)
            {
                var originalRenderer = original.GetComponent<MeshRenderer>();
                var originalFilter = original.GetComponent<MeshFilter>();
                if ((originalRenderer == null) != (originalFilter == null))
                    throw new InvalidOperationException("Exact paired original MeshFilter/MeshRenderer required: " + original.name);
                // Material preparation is complete before adding the preview renderer. Originals are NEVER assigned.
                Material[] ownSlots = originalRenderer == null ? null : originalRenderer.sharedMaterials
                    .Select(material => GripLandmarkCloneBottleMaterial(material, materialRows)).ToArray();
                var node = New(original.name); node.SetActive(false); node.transform.SetParent(parent, false);
                node.transform.localPosition = original.localPosition; node.transform.localRotation = original.localRotation;
                node.transform.localScale = original.localScale;
                if (originalRenderer != null)
                {
                    if (originalFilter.sharedMesh == null) throw new InvalidOperationException("Original static bottle mesh missing.");
                    node.AddComponent<MeshFilter>().sharedMesh = originalFilter.sharedMesh;
                    var renderer = node.AddComponent<MeshRenderer>(); renderer.sharedMaterials = ownSlots;
                    renderer.enabled = originalRenderer.enabled; renderer.forceRenderingOff = originalRenderer.forceRenderingOff;
                    renderer.shadowCastingMode = originalRenderer.shadowCastingMode; renderer.receiveShadows = originalRenderer.receiveShadows;
                    renderer.lightProbeUsage = originalRenderer.lightProbeUsage; renderer.reflectionProbeUsage = originalRenderer.reflectionProbeUsage;
                    renderer.motionVectorGenerationMode = originalRenderer.motionVectorGenerationMode;
                    renderer.renderingLayerMask = originalRenderer.renderingLayerMask;
                    renderer.sortingLayerID = originalRenderer.sortingLayerID; renderer.sortingOrder = originalRenderer.sortingOrder;
                    if (renderer.sharedMaterials.Any(material => material == null || EditorUtility.IsPersistent(material)))
                        throw new InvalidOperationException("Preview bottle renderer may bind ONLY Stage-owned nonpersistent materials.");
                }
                for (int child = 0; child < original.childCount; child++)
                    GripLandmarkCloneStaticNode(original.GetChild(child), node.transform, materialRows);
                node.SetActive(original.gameObject.activeSelf);
                return node;
            }

            Material GripLandmarkCloneBottleMaterial(Material original, List<GripLandmarkOwnedMaterial> rows)
            {
                if (original == null || !EditorUtility.IsPersistent(original)) throw new InvalidOperationException("Persistent original bottle material required.");
                if (GripLandmarkBottleMaterials.TryGetValue(original, out var cached)) return cached;
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new InvalidOperationException("Installed URP Lit required.");
                if (original.shader != shader) throw new InvalidOperationException("This bounded bottle route requires the actual original URP Lit shader, not a guessed conversion.");
                var result = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = "GripLandmarkPreviewOnly_" + original.name };
                owned.Add(result); // Stage.Dispose explicitly owns cleanup, including preparation/validation failures.
                for (int property = 0; property < shader.GetPropertyCount(); property++)
                {
                    string name = shader.GetPropertyName(property);
                    switch (shader.GetPropertyType(property))
                    {
                        case ShaderPropertyType.Color: result.SetColor(name, original.GetColor(name)); break;
                        case ShaderPropertyType.Vector: result.SetVector(name, original.GetVector(name)); break;
                        case ShaderPropertyType.Float:
                        case ShaderPropertyType.Range: result.SetFloat(name, original.GetFloat(name)); break;
                        case ShaderPropertyType.Int: result.SetInt(name, original.GetInt(name)); break;
                        case ShaderPropertyType.Texture:
                            result.SetTexture(name, original.GetTexture(name)); result.SetTextureScale(name, original.GetTextureScale(name));
                            result.SetTextureOffset(name, original.GetTextureOffset(name)); break;
                        default: throw new InvalidOperationException("Unsupported actual shader property kind: " + name);
                    }
                }
                result.renderQueue = original.renderQueue; result.enableInstancing = original.enableInstancing;
                result.doubleSidedGI = original.doubleSidedGI; result.globalIlluminationFlags = original.globalIlluminationFlags;
                if (GripLandmarkLitValidator == null)
                {
                    Type type = AppDomain.CurrentDomain.GetAssemblies().Select(assembly => assembly.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitShader", false))
                        .FirstOrDefault(value => value != null) ?? throw new InvalidOperationException("Installed official URP Lit ShaderGUI unavailable.");
                    GripLandmarkLitValidator = Activator.CreateInstance(type, true);
                    GripLandmarkLitValidate = type.GetMethod("ValidateMaterial", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Material) }, null)
                        ?? throw new MissingMethodException("Official ValidateMaterial(Material) unavailable.");
                }
                var row = new GripLandmarkOwnedMaterial { source = GripLandmarkId(original), name = result.name }; rows.Add(row);
                GripLandmarkLitValidate.Invoke(GripLandmarkLitValidator, new object[] { result }); string first = EditorJsonUtility.ToJson(result);
                GripLandmarkLitValidate.Invoke(GripLandmarkLitValidator, new object[] { result }); string second = EditorJsonUtility.ToJson(result);
                row.afterFirstValidationSha256 = GripLandmarkJsonHash(first); row.afterSecondValidationSha256 = GripLandmarkJsonHash(second);
                row.officialSecondValidationFullJsonFixedPoint = first == second;
                if (!row.officialSecondValidationFullJsonFixedPoint)
                {
                    row.afterFirstValidationJson = first; row.afterSecondValidationJson = second;
                    throw new InvalidOperationException("Stage-owned bottle material not a full-JSON second-validation fixed point; no source validation or cache exemption.");
                }
                GripLandmarkBottleMaterials.Add(original, result); return result;
            }
        }

        sealed partial class Drawn
        {
            public GripLandmarkGeometry GripLandmarkDescribe(string objectName, GameObject persistentRoot)
            {
                var rows = new List<GripLandmarkTriangle>(); int id = 0;
                foreach (var part in parts.Where(p => p.renderer.enabled && !p.renderer.forceRenderingOff && p.renderer.gameObject.activeInHierarchy))
                {
                    Mesh evaluated = part.source;
                    if (part.scratch != null) { ((SkinnedMeshRenderer)part.renderer).BakeMesh(part.scratch, true); evaluated = part.scratch; }
                    var sourceVertices = part.source.vertices; var local = evaluated.vertices; var uv = part.source.uv;
                    if (sourceVertices.Length != local.Length) throw new InvalidOperationException("Original/evaluated vertex indexing differs.");
                    var persistentRenderer = PrefabUtility.GetCorrespondingObjectFromOriginalSource(part.renderer);
                    string resolution = "PrefabUtility original-source correspondence";
                    if (persistentRenderer == null)
                    {
                        // Some preview instantiation routes omit instance correspondence. Resolve the
                        // exact stored relative path, never a name/prefix or another rig's renderer.
                        if (persistentRoot == null || !EditorUtility.IsPersistent(persistentRoot))
                            throw new InvalidOperationException("Exact persistent preview source root required.");
                        Transform stored = part.path.Length == 0 ? persistentRoot.transform : persistentRoot.transform.Find(part.path);
                        var storedRenderer = stored != null ? stored.GetComponent<Renderer>() : null;
                        Mesh storedMesh = storedRenderer is SkinnedMeshRenderer skin ? skin.sharedMesh : storedRenderer != null ? storedRenderer.GetComponent<MeshFilter>()?.sharedMesh : null;
                        if (storedRenderer == null || storedRenderer.GetType() != part.renderer.GetType() || storedMesh != part.source)
                            throw new InvalidOperationException("Exact stored renderer path/type/mesh identity failed: " + part.path);
                        persistentRenderer = PrefabUtility.GetCorrespondingObjectFromOriginalSource(storedRenderer) ?? storedRenderer;
                        resolution = "Exact persistent source relative-path/type/mesh match, then original-source correspondence when available";
                    }
                    var rendererId = GripLandmarkId(persistentRenderer); var meshId = GripLandmarkId(part.source);
                    for (int sub = 0; sub < part.source.subMeshCount; sub++)
                    {
                        if (part.source.GetTopology(sub) != MeshTopology.Triangles) throw new InvalidOperationException("Triangle topology required for explicit ray picking.");
                        int[] indices = part.source.GetIndices(sub, true);
                        if (indices.Length % 3 != 0) throw new InvalidOperationException("Complete triangle indices required.");
                        for (int offset = 0; offset < indices.Length; offset += 3)
                        {
                            int[] vertices = { indices[offset], indices[offset + 1], indices[offset + 2] };
                            bool uvAvailable = uv.Length == sourceVertices.Length;
                            rows.Add(new GripLandmarkTriangle { id = id++, objectName = objectName, rendererPath = part.path, rendererSourceResolution = resolution,
                                rendererSource = rendererId, mesh = meshId, submesh = sub, triangleInSubmesh = offset / 3,
                                baseVertex = checked((int)part.source.GetBaseVertex(sub)), vertexIndices = vertices,
                                sourceVertices = vertices.Select(v => sourceVertices[v]).ToArray(), evaluatedLocalVertices = vertices.Select(v => local[v]).ToArray(),
                                worldVertices = vertices.Select(v => part.renderer.localToWorldMatrix.MultiplyPoint3x4(local[v])).ToArray(),
                                uvAvailable = uvAvailable, uv = uvAvailable ? vertices.Select(v => uv[v]).ToArray() : Array.Empty<Vector2>() });
                        }
                    }
                }
                if (rows.Count == 0) throw new InvalidOperationException("Actual visible drawn triangles required.");
                return new GripLandmarkGeometry { objectName = objectName, scope = "All actually enabled/non-force-hidden triangle-index geometry at this explicit preview pose. Original source vertices, evaluated local/world positions and original UVs share frozen original indices. No semantic palm/mouth subset guessed.", triangles = rows.ToArray() };
            }
        }

        /// <summary>Root explicitly chooses a visible image pixel. This never writes or freezes a landmark.</summary>
        public static string ReadGripLandmarkPixel(string uniqueLabel, string imageFilename, float pixelX, float pixelYFromTop)
        {
            GripLandmarkValidateLabel(uniqueLabel);
            string directory = Path.GetFullPath("Builds/ArtReview/kaykit-landmarks/" + uniqueLabel);
            var report = JsonUtility.FromJson<GripLandmarkReport>(File.ReadAllText(Path.Combine(directory, "result.json")));
            if (report.status != "completed" || !report.sourceDiskUnchanged || !report.sourceObjectFullJsonUnchanged)
                throw new InvalidOperationException("Only a completed protected capture can be queried.");
            var image = report.images.Single(i => i.file == imageFilename);
            if (!Finite(pixelX) || !Finite(pixelYFromTop) || pixelX < 0 || pixelX >= image.width || pixelYFromTop < 0 || pixelYFromTop >= image.height)
                throw new ArgumentOutOfRangeException("pixelX", "Explicit finite in-image coordinates required.");
            float tangent = Mathf.Tan(image.fieldOfView * Mathf.Deg2Rad * .5f);
            Vector3 direction = image.rotation * new Vector3((2 * (pixelX + .5f) / image.width - 1) * tangent * image.width / image.height,
                (1 - 2 * (pixelYFromTop + .5f) / image.height) * tangent, 1).normalized;
            var geometry = JsonUtility.FromJson<GripLandmarkGeometry>(File.ReadAllText(Path.Combine(directory, image.geometryFile)));
            GripLandmarkTriangle chosen = null; Vector3 barycentric = default; float nearest = float.PositiveInfinity;
            var visible = new HashSet<string>(image.visibleRendererPaths, StringComparer.Ordinal);
            foreach (var triangle in geometry.triangles.Where(t => visible.Contains(t.rendererPath)))
                if (GripLandmarkRayTriangle(image.position, direction, triangle.worldVertices, out float distance, out Vector3 bary) && distance < nearest)
                {
                    float cameraDepth = (Quaternion.Inverse(image.rotation) * direction).z * distance;
                    if (cameraDepth < image.nearClipPlane || cameraDepth > image.farClipPlane) continue;
                    chosen = triangle; nearest = distance; barycentric = bary;
                }
            if (chosen == null) throw new InvalidOperationException("No visible drawn triangle under that explicit pixel; do not substitute a nearest mesh/bone point.");
            return JsonUtility.ToJson(new GripLandmarkPick { label = uniqueLabel, image = imageFilename,
                pixelFromTop = new Vector2(pixelX, pixelYFromTop), triangleId = chosen.id, rendererPath = chosen.rendererPath,
                submesh = chosen.submesh, triangleInSubmesh = chosen.triangleInSubmesh, mesh = chosen.mesh, rendererSource = chosen.rendererSource,
                barycentric = barycentric, worldPoint = chosen.worldVertices[0] * barycentric.x + chosen.worldVertices[1] * barycentric.y + chosen.worldVertices[2] * barycentric.z,
                surfaceNormal = Vector3.Cross(chosen.worldVertices[1] - chosen.worldVertices[0], chosen.worldVertices[2] - chosen.worldVertices[0]).normalized, distance = nearest }, true);
        }

        static bool GripLandmarkRayTriangle(Vector3 origin, Vector3 direction, Vector3[] vertices, out float distance, out Vector3 barycentric)
        {
            distance = 0; barycentric = default;
            Vector3 edge1 = vertices[1] - vertices[0], edge2 = vertices[2] - vertices[0], p = Vector3.Cross(direction, edge2);
            float determinant = Vector3.Dot(edge1, p);
            // Ray/triangle arithmetic only, not a contact/floor tolerance. Keep both sides for explicit selection.
            if (Mathf.Abs(determinant) < 1e-10f) return false;
            float inverse = 1 / determinant; Vector3 offset = origin - vertices[0]; float u = Vector3.Dot(offset, p) * inverse;
            if (u < 0 || u > 1) return false;
            Vector3 q = Vector3.Cross(offset, edge1); float v = Vector3.Dot(direction, q) * inverse;
            if (v < 0 || u + v > 1) return false;
            distance = Vector3.Dot(edge2, q) * inverse; if (distance <= 0) return false;
            barycentric = new Vector3(1 - u - v, u, v); return true;
        }

        static Bounds GripLandmarkBounds(IEnumerable<GripLandmarkTriangle> triangles)
        {
            bool first = true; Bounds bounds = default;
            foreach (Vector3 vertex in triangles.SelectMany(t => t.worldVertices))
            { if (first) { bounds = new Bounds(vertex, Vector3.zero); first = false; } else bounds.Encapsulate(vertex); }
            if (first) throw new InvalidOperationException("Visible framing geometry missing."); return bounds;
        }
        static GripLandmarkIdentity GripLandmarkId(Object value)
        {
            if (value == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out string guid, out long id))
                throw new InvalidOperationException("Persistent original identity missing.");
            return new GripLandmarkIdentity { path = AssetDatabase.GetAssetPath(value), name = value.name, guid = guid, localId = id };
        }
        [Serializable] sealed class GripLandmarkFiles { public FileRow[] rows; }
        static FileRow[] GripLandmarkProtected()
        {
            var paths = new HashSet<string>(Protected().Select(f => f.path), StringComparer.Ordinal);
            Action<string> add = path =>
            {
                path = path.Replace('\\', '/'); if (!File.Exists(path)) return;
                paths.Add(path); if (File.Exists(path + ".meta")) paths.Add(path + ".meta");
            };
            foreach (string path in new[] { GenericPrefab, GripLandmarkScript, GripLandmarkBottleRoot + "/LICENSE.txt", GripLandmarkBottleRoot + "/README.md", GripLandmarkBottleAssets + "/LICENSE.txt" }
                .Concat(GripLandmarkBottleNames.Select(n => GripLandmarkBottleAssets + "/fbx/" + n + ".fbx")))
            { add(path); foreach (string dependency in AssetDatabase.GetDependencies(path, true)) add(dependency); }
            foreach (string path in Directory.GetFiles("Assets/_Game/Scripts/Editor/Review", "*", SearchOption.AllDirectories)) add(path);
            foreach (string path in Directory.GetFiles("Assets/_Game/Scripts", "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".asmdef", StringComparison.Ordinal) || p.EndsWith(".asmref", StringComparison.Ordinal))) add(path);
            foreach (string path in GripLandmarkClosure()) add(path);
            return paths.OrderBy(p => p, StringComparer.Ordinal).Select(p => new FileRow { path = p, bytes = new FileInfo(p).Length, sha256 = Hash(p) }).ToArray();
        }
        static GripLandmarkWatch[] GripLandmarkMemoryBaseline(FileRow[] files)
        {
            var objects = files.Where(f => f.path.StartsWith("Assets/", StringComparison.Ordinal) && !f.path.EndsWith(".meta", StringComparison.Ordinal) && !f.path.EndsWith(".unity", StringComparison.Ordinal))
                .SelectMany(f => AssetDatabase.LoadAllAssetsAtPath(f.path)).Where(o => o != null && EditorUtility.IsPersistent(o)).Distinct().ToArray();
            foreach (var value in objects) EditorJsonUtility.ToJson(value);
            var watches = objects.Select(o => GripLandmarkWatchObject(o, AssetDatabase.GetAssetPath(o))).ToArray();
            foreach (var watch in watches)
            {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(watch.target, out watch.row.guid, out watch.row.localId);
                if (watch.beforeJson != EditorJsonUtility.ToJson(watch.target)) throw new InvalidOperationException("Full-JSON serialization baseline not a fixed point: " + watch.row.path + "::" + watch.row.name);
            }
            return watches;
        }
        static GripLandmarkWatch GripLandmarkWatchObject(Object target, string path)
        {
            string json = EditorJsonUtility.ToJson(target);
            return new GripLandmarkWatch { target = target, beforeJson = json, row = new GripLandmarkMemory {
                path = path, name = target.name, type = target.GetType().FullName,
                beforeJsonSha256 = GripLandmarkJsonHash(json), beforeJsonLength = json.Length } };
        }
        static void GripLandmarkCompareMemory(GripLandmarkWatch watch, GripLandmarkMemory row)
        {
            string after = watch.target == null ? "DESTROYED" : EditorJsonUtility.ToJson(watch.target);
            row.unchanged = watch.target != null && watch.beforeJson == after;
            row.afterJsonSha256 = GripLandmarkJsonHash(after); row.afterJsonLength = after.Length;
            row.beforeJson = row.unchanged ? null : watch.beforeJson;
            row.afterJson = row.unchanged ? null : after;
        }
        static void GripLandmarkMaterialStage(string stage, GripLandmarkWatch[] watches, List<GripLandmarkMemoryStage> stages)
        {
            var changed = new List<GripLandmarkMemory>();
            foreach (var watch in watches)
            {
                var row = new GripLandmarkMemory { path = watch.row.path, name = watch.row.name, type = watch.row.type,
                    guid = watch.row.guid, localId = watch.row.localId, beforeJsonSha256 = watch.row.beforeJsonSha256,
                    beforeJsonLength = watch.row.beforeJsonLength };
                GripLandmarkCompareMemory(watch, row); if (!row.unchanged) changed.Add(row);
            }
            stages.Add(new GripLandmarkMemoryStage { stage = stage, comparedMaterialObjects = watches.Length,
                allFullJsonUnchanged = changed.Count == 0, changedObjects = changed.ToArray() });
            if (changed.Count != 0) throw new InvalidOperationException("Source material full JSON changed at " + stage + "; raw before/after retained, no restoration or baseline replacement.");
        }
        static string GripLandmarkJsonHash(string json)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(json))).Replace("-", "");
        }
        static string[] GripLandmarkClosure() => EditorBuildSettings.scenes.Where(s => s.enabled).SelectMany(s => AssetDatabase.GetDependencies(s.path, true)).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        static void GripLandmarkValidateLabel(string label)
        { if (string.IsNullOrWhiteSpace(label) || !Regex.IsMatch(label, "^[A-Za-z0-9_-]{8,72}$")) throw new ArgumentException("Fresh ASCII landmark label required."); }
        static void GripLandmarkWrite<T>(string path, T value) => File.WriteAllText(path, JsonUtility.ToJson(value, true), new UTF8Encoding(false));
    }
}
