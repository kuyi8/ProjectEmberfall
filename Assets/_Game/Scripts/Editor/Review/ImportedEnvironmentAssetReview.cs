using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Independent, synchronous art evidence. Never authors production scenes or importers.</summary>
    public static class ImportedEnvironmentAssetReview
    {
        private const string Download = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/";
        private const string KayPackage = Download + "07_Environment_Ruins/KayKit_Dungeon_Remastered/";
        private const string KayAssets = KayPackage + "addons/kaykit_dungeon_remastered/Assets/";
        private const string KayLicense = KayPackage + "LICENSE.txt";
        private const int Width = 960, Height = 720, Columns = 4;
        private static readonly Candidate[] Candidates =
        {
            Kay("baseline-wall", "wall", true),
            Kay("baseline-floor", "floor_tile_large", true),
            Kay("baseline-pillar", "pillar_decorated", true),
            Kay("arched-window", "wall_archedwindow_open"),
            Kay("sloped-wall", "wall_sloped"),
            Kay("platform-foundation", "floor_foundation_allsides"),
            Kay("candle-shelf", "shelf_small_candles"),
            Kay("broken-table", "table_long_broken"),
            Kay("blue-pattern-banner", "banner_patternA_blue"),
            new Candidate("tower-roof", Download + "07_Environment_Ruins/Medieval_Village_MegaKit/FBX/Roof_Tower_RoundTiles.fbx",
                Download + "07_Environment_Ruins/Medieval_Village_MegaKit/License_Standard.txt"),
            new Candidate("ritual-bookstand", Download + "09_Nature_Props/Fantasy_Props_MegaKit/Exports/FBX/BookStand.fbx",
                Download + "09_Nature_Props/Fantasy_Props_MegaKit/License_Standard.txt"),
            new Candidate("camp-tent", Download + "09_Nature_Props/Survival Pack - Sept 2020/FBX/Tent.fbx",
                Download + "09_Nature_Props/Survival Pack - Sept 2020/License.txt")
        };

        [Serializable] private sealed class Candidate
        {
            public string id, path, license;
            public bool kay, baseline;
            public Candidate(string id, string path, string license, bool kay = false, bool baseline = false)
            { this.id = id; this.path = path; this.license = license; this.kay = kay; this.baseline = baseline; }
        }

        [Serializable] public sealed class Report
        {
            public string capturedUtc, output, unityVersion, pipeline, palettePath, paletteSha256, contactSheet;
            public string scope = "Synchronous URP preview scene renders; not production integration, gameplay-camera readability, collision, navigation, LOD or performance acceptance.";
            public string scaleScope = "Authored root scale/rotation and mesh hierarchy retained; Unity world units treated as metres. Only translated to centre XZ and put mesh bounds bottom at Y=0. Each camera fits its own object: compare using the 1m grid and 1.8m ruler, NOT screenshot pixel sizes.";
            public string materialScope = "KayKit alone uses its own dungeon palette and temporary project-owned URP Lit material. Other packs retain compatible original material clones or temporary URP conversion of actual source texture/color properties; conversion is not a complete shader-equivalence claim.";
            public string baselineScope = "Baseline means a source model used by the production generator, viewed under the SAME neutral preview setup; not the production object's transform, tint or gameplay camera.";
            public string provenanceScope = "Local source, sidecar and license existence/hash recorded at capture time; actual acquisition/download timestamp is not verified.";
            public int candidateCount, capturedCount, failedCount, contactColumns, contactRows;
            public bool sourcesUnchanged, originalScenesUnchanged;
            public AssetRow[] assets;
        }

        [Serializable] public sealed class AssetRow
        {
            public string id, path, guid, licensePath, licenseSha256, sourceSha256, metaSha256, image, error;
            public bool baseline, kayPalette, rendered, sourceUnchanged;
            public int contactColumn, contactRowFromTop, meshFilterCount, rendererCount, enabledRendererCount, vertices, submeshes, materialSlots;
            public float importerGlobalScale, gridSpacingMetres = 1f, rulerHeightMetres = 1.8f;
            public Vector3 authoredRootScale, authoredRootEuler, boundsMinMetres, boundsMaxMetres, boundsSizeMetres, previewTranslation;
            public Vector3 cameraPosition, cameraTarget;
            public float cameraFieldOfView, preViewYaw = 45f;
            public MeshRow[] meshes;
            public MaterialRow[] materials;
        }

        [Serializable] public sealed class MeshRow
        {
            public string hierarchy, name, assetPath, guid;
            public long localFileId;
            public int vertices, submeshes;
            public bool activeInHierarchy, hasRenderer, rendererEnabled;
            public Vector3 boundsMinMetres, boundsMaxMetres, boundsSizeMetres;
            public Vector3 rendererBoundsMinMetres, rendererBoundsMaxMetres, rendererBoundsSizeMetres;
        }

        [Serializable] public sealed class MaterialRow
        {
            public string renderer, sourcePath, sourceGuid, sourceName, sourceShader, previewShader, adaptation;
            public bool sourceSupported;
            public int slot;
            public TextureRow[] actualTextures;
        }

        [Serializable] public sealed class TextureRow
        {
            public string property, path, guid;
            public Vector2 scale, offset;
        }

        private sealed class SceneSnapshot
        {
            public int handle;
            public string path, diskSha;
            public int[] roots;
        }

        private static Candidate Kay(string id, string mesh, bool baseline = false) =>
            new Candidate(id, KayAssets + "fbx/" + mesh + ".fbx", KayLicense, true, baseline);

        /// <returns>The fresh absolute evidence directory. Individual failures remain in report.json.</returns>
        public static string Capture()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Idle Edit Mode required; no capture during Play, compilation or import.");
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("A real graphics device and the project's URP pipeline are required; headless/NullGfx is not art evidence.");
            var before = CaptureSceneState();
            int activeHandle = SceneManager.GetActiveScene().handle;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(KayAssets + "texture/dungeon_texture.png");
            if (shader == null || !shader.isSupported || palette == null)
                throw new InvalidOperationException("Supported URP Lit shader and actual KayKit palette are required.");
            string output = Path.GetFullPath("Builds/ArtReview/imported-environment/" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            var rows = Candidates.Select((c, index) => new AssetRow
            {
                id = c.id, path = c.path, guid = AssetDatabase.AssetPathToGUID(c.path), licensePath = c.license,
                sourceSha256 = Hash(c.path), metaSha256 = Hash(c.path + ".meta"), licenseSha256 = Hash(c.license),
                baseline = c.baseline, kayPalette = c.kay, contactColumn = index % Columns, contactRowFromTop = index / Columns
            }).ToArray();
            var report = new Report
            {
                capturedUtc = DateTime.UtcNow.ToString("o"), output = output, unityVersion = UnityEngine.Application.unityVersion,
                pipeline = AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline),
                palettePath = AssetDatabase.GetAssetPath(palette), paletteSha256 = Hash(AssetDatabase.GetAssetPath(palette)),
                candidateCount = Candidates.Count(c => !c.baseline), contactColumns = Columns,
                contactRows = (Candidates.Length + Columns - 1) / Columns, assets = rows
            };
            Texture2D contact = null;
            try
            {
                contact = new Texture2D(Width * Columns, Height * report.contactRows, TextureFormat.RGB24, false);
                var grey = Enumerable.Repeat(new Color32(80, 80, 80, 255), contact.width * contact.height).ToArray();
                contact.SetPixels32(grey);
                for (int index = 0; index < Candidates.Length; index++)
                {
                    Texture2D image = null;
                    try
                    {
                        image = CaptureOne(Candidates[index], rows[index], shader, palette);
                        rows[index].image = (index + 1).ToString("D2") + "-" + Candidates[index].id + ".png";
                        File.WriteAllBytes(Path.Combine(output, rows[index].image), image.EncodeToPNG());
                        contact.SetPixels32(rows[index].contactColumn * Width,
                            (report.contactRows - rows[index].contactRowFromTop - 1) * Height, Width, Height, image.GetPixels32());
                        rows[index].rendered = true;
                    }
                    catch (Exception exception) { rows[index].error = exception.ToString(); }
                    finally { if (image != null) Object.DestroyImmediate(image); }
                }
                contact.Apply();
                report.contactSheet = "contact-sheet.png";
                File.WriteAllBytes(Path.Combine(output, report.contactSheet), contact.EncodeToPNG());
            }
            finally
            {
                if (contact != null) Object.DestroyImmediate(contact);
                foreach (var row in rows)
                    row.sourceUnchanged = row.sourceSha256 == Hash(row.path) && row.metaSha256 == Hash(row.path + ".meta") &&
                        row.licenseSha256 == Hash(row.licensePath) && row.guid == AssetDatabase.AssetPathToGUID(row.path);
                report.sourcesUnchanged = rows.All(row => row.sourceUnchanged) && report.paletteSha256 == Hash(report.palettePath);
                report.originalScenesUnchanged = ScenesMatch(before, activeHandle);
                report.capturedCount = rows.Count(row => row.rendered);
                report.failedCount = rows.Length - report.capturedCount;
                File.WriteAllText(Path.Combine(output, "report.json"), JsonUtility.ToJson(report, true));
            }
            if (!report.sourcesUnchanged || !report.originalScenesUnchanged)
                throw new InvalidOperationException("Capture protection invariant failed; no silent restoration. See " + output);
            Debug.Log("[IMPORTED_ENVIRONMENT_REVIEW] " + output + " captured=" + report.capturedCount +
                " failed=" + report.failedCount + " (visual review and production validation pending)");
            return output;
        }

        /// <summary>Only rerenders the selected banner from the opposite side; original evidence is untouched.</summary>
        public static string CaptureBannerFront()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                throw new InvalidOperationException("Idle Edit Mode required; no capture during Play, compilation or import.");
            if (!(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset) || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("A real graphics device and the project's URP pipeline are required.");
            var before = CaptureSceneState();
            int activeHandle = SceneManager.GetActiveScene().handle;
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            var palette = AssetDatabase.LoadAssetAtPath<Texture2D>(KayAssets + "texture/dungeon_texture.png");
            if (shader == null || !shader.isSupported || palette == null)
                throw new InvalidOperationException("Supported URP Lit shader and actual KayKit palette are required.");
            var candidate = Candidates.Single(value => value.id == "blue-pattern-banner");
            string output = Path.GetFullPath("Builds/ArtReview/imported-environment/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") +
                "-banner-front-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            Directory.CreateDirectory(output);
            var row = new AssetRow
            {
                id = candidate.id, path = candidate.path, guid = AssetDatabase.AssetPathToGUID(candidate.path),
                licensePath = candidate.license, licenseSha256 = Hash(candidate.license), sourceSha256 = Hash(candidate.path),
                metaSha256 = Hash(candidate.path + ".meta"), kayPalette = true, image = "front.png", preViewYaw = 225f
            };
            var report = new Report
            {
                capturedUtc = DateTime.UtcNow.ToString("o"), output = output, unityVersion = UnityEngine.Application.unityVersion,
                pipeline = AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline), palettePath = AssetDatabase.GetAssetPath(palette),
                paletteSha256 = Hash(AssetDatabase.GetAssetPath(palette)), candidateCount = 1, assets = new[] { row },
                scope = "One banner reverse-camera check only: view yaw 225 degrees rather than the original 45; source/model rotation, scale and normal back-face culling unchanged. No production integration or style acceptance.",
                baselineScope = "No baseline or other candidate rerendered; previous twelve-image evidence is preserved."
            };
            Texture2D image = null;
            try
            {
                image = CaptureOne(candidate, row, shader, palette, true);
                File.WriteAllBytes(Path.Combine(output, row.image), image.EncodeToPNG());
                row.rendered = true;
            }
            catch (Exception exception) { row.error = exception.ToString(); }
            finally
            {
                if (image != null) Object.DestroyImmediate(image);
                row.sourceUnchanged = row.sourceSha256 == Hash(row.path) && row.metaSha256 == Hash(row.path + ".meta") &&
                    row.licenseSha256 == Hash(row.licensePath) && row.guid == AssetDatabase.AssetPathToGUID(row.path);
                report.sourcesUnchanged = row.sourceUnchanged && report.paletteSha256 == Hash(report.palettePath);
                report.originalScenesUnchanged = ScenesMatch(before, activeHandle);
                report.capturedCount = row.rendered ? 1 : 0; report.failedCount = row.rendered ? 0 : 1;
                File.WriteAllText(Path.Combine(output, "row.json"), JsonUtility.ToJson(report, true));
            }
            if (!report.sourcesUnchanged || !report.originalScenesUnchanged)
                throw new InvalidOperationException("Banner capture protection invariant failed; no silent restoration. See " + output);
            Debug.Log("[IMPORTED_BANNER_FRONT] " + output + " captured=" + report.capturedCount + " failed=" + report.failedCount);
            return output;
        }

        private static Texture2D CaptureOne(Candidate candidate, AssetRow row, Shader shader, Texture2D palette, bool reverseCamera = false)
        {
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(candidate.path);
            if (source == null || string.IsNullOrEmpty(row.guid) || string.IsNullOrEmpty(row.licenseSha256))
                throw new FileNotFoundException("Source model, GUID and actual license must exist.", candidate.path);
            if (source.GetComponentsInChildren<MonoBehaviour>(true).Any(component => component != null))
                throw new InvalidOperationException("Static model-only review refuses script-bearing source prefabs.");
            row.authoredRootScale = source.transform.localScale;
            row.authoredRootEuler = source.transform.localEulerAngles;
            var importer = AssetImporter.GetAtPath(candidate.path) as ModelImporter;
            row.importerGlobalScale = importer == null ? 0 : importer.globalScale;
            PreviewRenderUtility preview = null;
            Mesh unitCube = null;
            RenderTexture readback = null;
            Texture2D image = null;
            var materials = new List<Material>();
            var previousActive = RenderTexture.active;
            bool previewOpen = false;
            try
            {
                preview = new PreviewRenderUtility();
                // Creates the clone directly in the utility's isolated scene, never in the user's active scene.
                var model = preview.InstantiatePrefabInScene(source);
                foreach (var transform in model.GetComponentsInChildren<Transform>(true))
                    transform.gameObject.hideFlags = HideFlags.HideAndDontSave;
                foreach (var animator in model.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
                model.transform.position = Vector3.zero;
                var filters = model.GetComponentsInChildren<MeshFilter>(true).Where(filter => filter.sharedMesh != null).ToArray();
                var renderers = model.GetComponentsInChildren<Renderer>(true);
                if (filters.Length == 0 || renderers.Length == 0)
                    throw new InvalidOperationException("No real MeshFilter/Renderer geometry; empty organizational nodes are not accepted as art.");
                row.meshes = filters.Select(filter => MeasureMesh(filter, model.transform)).ToArray();
                row.meshFilterCount = filters.Length;
                row.rendererCount = renderers.Length;
                row.enabledRendererCount = renderers.Count(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy);
                if (row.enabledRendererCount == 0) throw new InvalidOperationException("No active enabled renderer; hidden source geometry is not visual evidence.");
                row.vertices = row.meshes.Sum(mesh => mesh.vertices);
                row.submeshes = row.meshes.Sum(mesh => mesh.submeshes);
                var bounds = new Bounds(row.meshes[0].boundsMinMetres, Vector3.zero);
                foreach (var mesh in row.meshes) { bounds.Encapsulate(mesh.boundsMinMetres); bounds.Encapsulate(mesh.boundsMaxMetres); }
                row.boundsMinMetres = bounds.min; row.boundsMaxMetres = bounds.max; row.boundsSizeMetres = bounds.size;
                if (bounds.size.sqrMagnitude < .000001f) throw new InvalidOperationException("Zero-size mesh bounds.");
                row.previewTranslation = new Vector3(-bounds.center.x, -bounds.min.y, -bounds.center.z);
                model.transform.position = row.previewTranslation;
                var materialRows = new List<MaterialRow>();
                foreach (var renderer in renderers)
                {
                    var sourceSlots = renderer.sharedMaterials;
                    row.materialSlots += sourceSlots.Length;
                    var temporarySlots = new Material[sourceSlots.Length];
                    for (int slot = 0; slot < sourceSlots.Length; slot++)
                    {
                        var original = sourceSlots[slot];
                        var materialRow = DescribeMaterial(renderer, model.transform, original, slot);
                        var temporary = PreviewMaterial(original, candidate.kay, shader, palette, materialRow);
                        materials.Add(temporary); temporarySlots[slot] = temporary; materialRows.Add(materialRow);
                    }
                    renderer.sharedMaterials = temporarySlots;
                }
                row.materials = materialRows.ToArray();
                var referenceMaterial = new Material(shader) { hideFlags = HideFlags.HideAndDontSave, name = "Review reference only" };
                referenceMaterial.SetColor("_BaseColor", new Color(.58f, .58f, .58f));
                referenceMaterial.SetFloat("_Smoothness", 0);
                materials.Add(referenceMaterial);
                unitCube = CubeMesh();
                Vector3 ruler = new Vector3(bounds.size.x * .5f + .6f, 0, 0);
                var framing = new Bounds(new Vector3(0, bounds.size.y * .5f, 0), bounds.size);
                framing.Encapsulate(ruler + new Vector3(.25f, 1.8f, .1f));
                framing.Encapsulate(ruler + new Vector3(-.25f, 0, -.1f));
                var camera = preview.camera;
                camera.clearFlags = CameraClearFlags.SolidColor;
                camera.backgroundColor = new Color(.25f, .25f, .25f, 1);
                camera.allowHDR = false; camera.allowMSAA = false;
                camera.fieldOfView = 45;
                float distance = Mathf.Max(3f, framing.extents.magnitude / Mathf.Sin(22.5f * Mathf.Deg2Rad) * 1.2f);
                row.preViewYaw = reverseCamera ? 225f : 45f;
                Vector3 direction = Quaternion.Euler(45, row.preViewYaw, 0) * Vector3.back;
                camera.transform.position = framing.center + direction * distance;
                camera.transform.LookAt(framing.center);
                camera.nearClipPlane = .01f; camera.farClipPlane = distance + framing.extents.magnitude + 20;
                var cameraData = camera.GetUniversalAdditionalCameraData();
                cameraData.renderPostProcessing = false; cameraData.volumeLayerMask = 0;
                cameraData.antialiasing = AntialiasingMode.None;
                preview.ambientColor = new Color(.42f, .42f, .42f);
                preview.lights[0].color = Color.white; preview.lights[0].intensity = 1.2f;
                preview.lights[0].transform.rotation = Quaternion.Euler(45, 35, 0);
                preview.lights[1].color = Color.white; preview.lights[1].intensity = .6f;
                preview.lights[1].transform.rotation = Quaternion.Euler(25, 210, 0);
                row.cameraPosition = camera.transform.position; row.cameraTarget = framing.center; row.cameraFieldOfView = camera.fieldOfView;
                preview.BeginPreview(new Rect(0, 0, Width, Height), GUIStyle.none);
                previewOpen = true;
                int gridExtent = Mathf.Clamp(Mathf.CeilToInt(Mathf.Max(bounds.size.x, bounds.size.z) * .5f + 1), 2, 32);
                for (int metre = -gridExtent; metre <= gridExtent; metre++)
                {
                    DrawReference(preview, unitCube, referenceMaterial, new Vector3(metre, -.025f, 0), new Vector3(.012f, .012f, gridExtent * 2));
                    DrawReference(preview, unitCube, referenceMaterial, new Vector3(0, -.025f, metre), new Vector3(gridExtent * 2, .012f, .012f));
                }
                DrawReference(preview, unitCube, referenceMaterial, ruler + Vector3.up * .9f, new Vector3(.045f, 1.8f, .045f));
                foreach (float height in new[] { 0f, 1f, 1.8f })
                    DrawReference(preview, unitCube, referenceMaterial, ruler + Vector3.up * height, new Vector3(.4f, .03f, .045f));
                preview.Render(true, false);
                var rendered = preview.EndPreview(); previewOpen = false;
                // Convert the linear preview output to an sRGB readback; never persist or replace a source material.
                readback = RenderTexture.GetTemporary(Width, Height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                Graphics.Blit(rendered, readback);
                RenderTexture.active = readback;
                image = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                image.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); image.Apply();
                var result = image; image = null; return result;
            }
            finally
            {
                try { if (previewOpen && preview != null) preview.EndPreview(); }
                finally
                {
                    RenderTexture.active = previousActive;
                    if (readback != null) RenderTexture.ReleaseTemporary(readback);
                    if (image != null) Object.DestroyImmediate(image);
                    try { if (preview != null) preview.Cleanup(); }
                    finally
                    {
                        if (unitCube != null) Object.DestroyImmediate(unitCube);
                        foreach (var material in materials) if (material != null) Object.DestroyImmediate(material);
                    }
                }
            }
        }

        private static MeshRow MeasureMesh(MeshFilter filter, Transform root)
        {
            var mesh = filter.sharedMesh;
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(mesh, out string guid, out long localFileId);
            var bounds = mesh.bounds;
            var renderer = filter.GetComponent<Renderer>();
            var rendererBounds = renderer == null ? new Bounds() : renderer.bounds;
            Vector3 min = bounds.min, max = bounds.max;
            var world = new Bounds(filter.transform.TransformPoint(min), Vector3.zero);
            for (int index = 0; index < 8; index++)
                world.Encapsulate(filter.transform.TransformPoint(new Vector3((index & 1) == 0 ? min.x : max.x,
                    (index & 2) == 0 ? min.y : max.y, (index & 4) == 0 ? min.z : max.z)));
            return new MeshRow { hierarchy = Hierarchy(filter.transform, root), name = mesh.name, assetPath = AssetDatabase.GetAssetPath(mesh),
                guid = guid, localFileId = localFileId, vertices = mesh.vertexCount, submeshes = mesh.subMeshCount,
                activeInHierarchy = filter.gameObject.activeInHierarchy, hasRenderer = renderer != null, rendererEnabled = renderer != null && renderer.enabled,
                boundsMinMetres = world.min, boundsMaxMetres = world.max, boundsSizeMetres = world.size,
                rendererBoundsMinMetres = rendererBounds.min, rendererBoundsMaxMetres = rendererBounds.max, rendererBoundsSizeMetres = rendererBounds.size };
        }

        private static MaterialRow DescribeMaterial(Renderer renderer, Transform root, Material material, int slot)
        {
            var textures = new List<TextureRow>();
            if (material != null) foreach (string property in material.GetTexturePropertyNames())
            {
                var texture = material.GetTexture(property);
                if (texture == null) continue;
                string path = AssetDatabase.GetAssetPath(texture);
                textures.Add(new TextureRow { property = property, path = path, guid = AssetDatabase.AssetPathToGUID(path),
                    scale = material.GetTextureScale(property), offset = material.GetTextureOffset(property) });
            }
            string materialPath = material == null ? "" : AssetDatabase.GetAssetPath(material);
            return new MaterialRow { renderer = Hierarchy(renderer.transform, root), slot = slot, sourcePath = materialPath,
                sourceGuid = AssetDatabase.AssetPathToGUID(materialPath), sourceName = material == null ? "<missing>" : material.name,
                sourceShader = material == null || material.shader == null ? "<missing>" : material.shader.name,
                sourceSupported = material != null && material.shader != null && material.shader.isSupported, actualTextures = textures.ToArray() };
        }

        private static Material PreviewMaterial(Material original, bool kay, Shader shader, Texture2D palette, MaterialRow row)
        {
            bool compatible = original != null && original.shader != null && original.shader.isSupported &&
                (original.shader.name.StartsWith("Universal Render Pipeline/", StringComparison.Ordinal) ||
                 original.GetTag("RenderPipeline", false) == "UniversalPipeline");
            var material = !kay && compatible ? new Material(original) : new Material(shader);
            try
            {
                material.hideFlags = HideFlags.HideAndDontSave;
                material.name = "Imported environment review temporary";
                if (kay)
                {
                    material.SetTexture("_BaseMap", palette); material.SetColor("_BaseColor", Color.white);
                    material.SetFloat("_Smoothness", .12f); row.adaptation = "KayKit-only own palette / temporary URP Lit";
                }
                else if (compatible) row.adaptation = "Actual compatible URP source material cloned without property changes";
                else if (original == null)
                {
                    material.SetColor("_BaseColor", new Color(.6f, .6f, .6f));
                    row.adaptation = "MISSING MATERIAL: neutral URP fallback; palette/style cannot be certified";
                }
                else
                {
                    CopyTexture(original, original.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex", material, "_BaseMap");
                    CopyTexture(original, "_BumpMap", material, "_BumpMap");
                    CopyTexture(original, "_EmissionMap", material, "_EmissionMap");
                    CopyTexture(original, "_MetallicGlossMap", material, "_MetallicGlossMap");
                    CopyTexture(original, "_OcclusionMap", material, "_OcclusionMap");
                    string color = original.HasProperty("_BaseColor") ? "_BaseColor" : "_Color";
                    if (original.HasProperty(color)) material.SetColor("_BaseColor", original.GetColor(color));
                    if (original.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", original.GetColor("_EmissionColor"));
                    foreach (string property in new[] { "_Metallic", "_BumpScale", "_OcclusionStrength", "_Cutoff" })
                        if (original.HasProperty(property)) material.SetFloat(property, original.GetFloat(property));
                    if (original.HasProperty("_Glossiness")) material.SetFloat("_Smoothness", original.GetFloat("_Glossiness"));
                    if (material.GetTexture("_BumpMap") != null) material.EnableKeyword("_NORMALMAP");
                    if (original.IsKeywordEnabled("_EMISSION")) material.EnableKeyword("_EMISSION");
                    if (material.GetTexture("_MetallicGlossMap") != null) material.EnableKeyword("_METALLICSPECGLOSSMAP");
                    if (material.GetTexture("_OcclusionMap") != null) material.EnableKeyword("_OCCLUSIONMAP");
                    if (original.GetTag("RenderType", false) == "TransparentCutout")
                    { material.SetFloat("_AlphaClip", 1); material.EnableKeyword("_ALPHATEST_ON"); material.renderQueue = (int)RenderQueue.AlphaTest; }
                    else if (original.renderQueue >= (int)RenderQueue.Transparent)
                    {
                        material.SetFloat("_Surface", 1); material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha); material.SetFloat("_ZWrite", 0);
                        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); material.renderQueue = (int)RenderQueue.Transparent;
                    }
                    row.adaptation = "Temporary URP Lit conversion of actual source maps/color; custom/ORM/specular shader equivalence NOT established";
                }
                row.previewShader = material.shader.name;
                return material;
            }
            catch { Object.DestroyImmediate(material); throw; }
        }

        private static void CopyTexture(Material source, string from, Material target, string to)
        {
            if (!source.HasProperty(from) || !target.HasProperty(to)) return;
            target.SetTexture(to, source.GetTexture(from)); target.SetTextureScale(to, source.GetTextureScale(from));
            target.SetTextureOffset(to, source.GetTextureOffset(from));
        }

        private static void DrawReference(PreviewRenderUtility preview, Mesh mesh, Material material, Vector3 position, Vector3 scale) =>
            preview.DrawMesh(mesh, Matrix4x4.TRS(position, Quaternion.identity, scale), material, 0);

        private static Mesh CubeMesh()
        {
            var mesh = new Mesh { name = "Temporary metric reference cube", hideFlags = HideFlags.HideAndDontSave };
            mesh.vertices = new[] { new Vector3(-.5f,-.5f,-.5f), new Vector3(.5f,-.5f,-.5f), new Vector3(.5f,.5f,-.5f), new Vector3(-.5f,.5f,-.5f),
                new Vector3(-.5f,-.5f,.5f), new Vector3(.5f,-.5f,.5f), new Vector3(.5f,.5f,.5f), new Vector3(-.5f,.5f,.5f) };
            mesh.triangles = new[] { 0,2,1,0,3,2, 4,5,6,4,6,7, 0,1,5,0,5,4, 3,7,6,3,6,2, 0,4,7,0,7,3, 1,2,6,1,6,5 };
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); return mesh;
        }

        private static string Hierarchy(Transform current, Transform root) => current == root ? root.name :
            Hierarchy(current.parent, root) + "/" + current.name;

        private static SceneSnapshot[] CaptureSceneState()
        {
            var result = new List<SceneSnapshot>();
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                var scene = SceneManager.GetSceneAt(index);
                if (!scene.isLoaded || scene.isDirty) throw new InvalidOperationException("All original scenes must be loaded and clean; preserve user work: " + scene.path);
                result.Add(new SceneSnapshot { handle = scene.handle, path = scene.path, diskSha = Hash(scene.path),
                    roots = scene.GetRootGameObjects().Select(root => root.GetInstanceID()).OrderBy(id => id).ToArray() });
            }
            return result.ToArray();
        }

        private static bool ScenesMatch(SceneSnapshot[] before, int activeHandle)
        {
            if (SceneManager.sceneCount != before.Length || SceneManager.GetActiveScene().handle != activeHandle) return false;
            for (int index = 0; index < before.Length; index++)
            {
                var scene = SceneManager.GetSceneAt(index); var previous = before[index];
                if (!scene.isLoaded || scene.isDirty || scene.handle != previous.handle || scene.path != previous.path ||
                    Hash(scene.path) != previous.diskSha || !scene.GetRootGameObjects().Select(root => root.GetInstanceID()).OrderBy(id => id).SequenceEqual(previous.roots)) return false;
            }
            return true;
        }

        private static string Hash(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "";
            using (var sha = SHA256.Create()) using (var stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
