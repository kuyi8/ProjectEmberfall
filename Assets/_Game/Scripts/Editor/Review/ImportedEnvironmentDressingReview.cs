using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>One-shot imported-art migration evidence, not natural gameplay or performance acceptance.</summary>
    public static class ImportedEnvironmentDressingReview
    {
        const string SetupType = "Emberfall.Editor.Setup.ImportedEnvironmentDressingSetup";
        const string RootName = "ImportedEnvironmentDressing";
        const string AssetRoot = "Assets/_Game/Art/ImportedEnvironmentDressing";
        const string OwnSource = "Assets/_Game/Scripts/Editor/Review/ImportedEnvironmentDressingReview.cs";
        static readonly string[] Scenes = { "10_EmberValley", "20_Sanctum" };

        [Serializable] sealed class FileRow { public string path, sha256; public long bytes; }
        [Serializable] sealed class Manifest { public string scope; public FileRow[] files; }
        [Serializable] sealed class CameraRow
        {
            public string image, scene, activeLightingScene, scope;
            public Vector3 position, target;
            public int width = 960, height = 540;
            public float fieldOfView = 60, nearClip = .08f, farClip = 200;
        }
        [Serializable] sealed class LightingRow
        {
            public string activeScene, ambientMode, fogMode, skybox, skyboxSerialized, sun, lightmapsMode, defaultReflectionMode, customReflection;
            public bool fog;
            public Color ambientSky, ambientEquator, ambientGround, fogColor;
            public float ambientIntensity, fogStart, fogEnd, fogDensity, reflectionIntensity, haloStrength, flareStrength, flareFadeSpeed;
            public int lightmapCount, defaultReflectionResolution, reflectionBounces;
            public float[] ambientProbe;
            public string[] lightmaps, lights, cameraOccluders, sceneLightingReferences;
        }
        [Serializable] sealed class Result
        {
            public string scene, scope = "Fixed same-parameter Editor camera images; 20 uses additive loading with10 active. No natural traversal, HUD, displayed Player or GPU/performance acceptance. Structural triangle/renderer counts are not draw calls.";
            public bool oldObjectsSame, physicsAndNavSame, lightingSame, occludersSame, frozenFilesSame, sceneIdempotent, assetsIdempotent;
            public int renderersBefore, renderersAfter, dressingRenderers, dressingColliders, dressingBehaviours, dressingRigidbodies, dressingStaticObjects;
            public long dressingTriangles;
            public string sceneSha256;
        }

        public static string ApplyAndCapture()
        {
            var active = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || SceneManager.sceneCount != 1 ||
                active.isDirty || active.path != "Assets/_Game/Scenes/10_EmberValley.unity")
                throw new InvalidOperationException("Single clean idle formal Valley required; preserve unsaved user work.");
            var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(SetupType)).FirstOrDefault(t => t != null);
            if (type == null) throw new InvalidOperationException("Imported dressing setup is not compiled yet.");
            var apply = type.GetMethod("ApplyToScene", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(Scene) }, null);
            var rootField = type.GetField("RootName", BindingFlags.Public | BindingFlags.Static);
            var assetField = type.GetField("AssetRoot", BindingFlags.Public | BindingFlags.Static);
            if (apply == null || rootField == null || !rootField.IsLiteral || assetField == null || !assetField.IsLiteral ||
                (string)rootField.GetRawConstantValue() != RootName || (string)assetField.GetRawConstantValue() != AssetRoot)
                throw new InvalidOperationException("Imported dressing setup contract does not match this narrowly scoped review.");
            var property = type.GetProperty("SelectedSourcePaths", BindingFlags.Public | BindingFlags.Static);
            var field = type.GetField("SelectedSourcePaths", BindingFlags.Public | BindingFlags.Static);
            var selected = (property != null ? property.GetValue(null) : field?.GetValue(null)) as string[];
            if (selected == null || selected.Length == 0) throw new InvalidOperationException("Actual selected source file manifest is required before mutation.");

            string output = "Builds/ArtReview/imported-dressing/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            if (Directory.Exists(output)) throw new IOException("Evidence directory already exists; never overwrite an earlier run.");
            Directory.CreateDirectory(output);
            var previous = EditorSceneManager.GetSceneManagerSetup();
            try
            {
                var frozen = FrozenFiles(selected);
                WriteManifest(output + "/frozen-before.json", frozen, "Protected settings/navigation/actors/animations/network/Gym and actual selected source dependencies; not an all-project inventory.");
                File.Copy(OwnSource, output + "/review-helper-source.cs");
                if (File.Exists(OwnSource + ".meta")) File.Copy(OwnSource + ".meta", output + "/review-helper-source.cs.meta");
                var originalScenes = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (string name in Scenes)
                {
                    string path = "Assets/_Game/Scenes/" + name + ".unity";
                    originalScenes.Add(path, Hash(path)); originalScenes.Add(path + ".meta", Hash(path + ".meta"));
                    File.Copy(path, output + "/" + name + "-before.unity");
                    File.Copy(path + ".meta", output + "/" + name + "-before.unity.meta");
                }
                WriteManifest(output + "/scene-source-before.json", originalScenes, "Actual original10/20 scene and meta SHA before any production authoring.");
                foreach (string name in Scenes)
                {
                    string path = "Assets/_Game/Scenes/" + name + ".unity";
                    var valley = EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity", OpenSceneMode.Single);
                    var scene = name == "10_EmberValley" ? valley : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
                    SceneManager.SetActiveScene(valley);
                    string oldObjects = CaptureOldObjects(scene), physics = M6BoundaryArtSetup.CapturePhysics(scene);
                    string otherObjects = name == "20_Sanctum" ? CaptureOldObjects(valley, false) : null;
                    string otherSceneHash = name == "20_Sanctum" ? Hash(valley.path) : null;
                    string lighting = CaptureLighting();
                    string occluders = CaptureOccluders(scene);
                    var result = new Result { scene = name, renderersBefore = Renderers(scene) };
                    File.WriteAllText(output + "/" + name + "-objects-before.jsonl", oldObjects);
                    File.WriteAllText(output + "/" + name + "-physics-before.json", physics);
                    File.WriteAllText(output + "/" + name + "-lighting-before.json", lighting);
                    File.WriteAllText(output + "/" + name + "-occluders-before.jsonl", occluders);
                    CaptureShots(scene, output, "before");
                    if (oldObjects != CaptureOldObjects(scene) || lighting != CaptureLighting())
                        throw new InvalidOperationException("Before-camera capture changed protected scene state: " + name);

                    // This is the only authoring call. Never invoke M6/WorldIdentity setup or navigation baking here.
                    apply.Invoke(null, new object[] { scene });
                    string afterObjects = CaptureOldObjects(scene), afterPhysics = M6BoundaryArtSetup.CapturePhysics(scene);
                    result.oldObjectsSame = oldObjects == afterObjects;
                    result.physicsAndNavSame = physics == afterPhysics;
                    result.lightingSame = lighting == CaptureLighting();
                    result.occludersSame = occluders == CaptureOccluders(scene);
                    result.frozenFilesSame = FrozenUnchanged(frozen, selected);
                    var root = scene.GetRootGameObjects().Single(r => r.name == RootName);
                    result.dressingRenderers = root.GetComponentsInChildren<Renderer>(true).Length;
                    result.dressingColliders = root.GetComponentsInChildren<Collider>(true).Length;
                    result.dressingBehaviours = root.GetComponentsInChildren<Behaviour>(true).Length;
                    result.dressingRigidbodies = root.GetComponentsInChildren<Rigidbody>(true).Length;
                    result.dressingStaticObjects = root.GetComponentsInChildren<Transform>(true).Count(t => GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0);
                    result.dressingTriangles = root.GetComponentsInChildren<MeshFilter>(true).Where(f => f.sharedMesh != null)
                        .Sum(f => (long)f.sharedMesh.triangles.Length / 3);
                    result.renderersAfter = Renderers(scene);
                    File.WriteAllText(output + "/" + name + "-objects-after.jsonl", afterObjects);
                    File.WriteAllText(output + "/" + name + "-physics-after.json", afterPhysics);
                    File.WriteAllText(output + "/" + name + "-lighting-after.json", CaptureLighting());
                    File.WriteAllText(output + "/" + name + "-occluders-after.jsonl", CaptureOccluders(scene));
                    File.WriteAllText(output + "/" + name + "-result.json", JsonUtility.ToJson(result, true));
                    if (!result.oldObjectsSame || !result.physicsAndNavSame || !result.lightingSame || !result.occludersSame || !result.frozenFilesSame ||
                        result.dressingColliders != 0 || result.dressingBehaviours != 0 || result.dressingRigidbodies != 0 || result.dressingStaticObjects != 0 ||
                        result.dressingRenderers <= 0 || result.dressingTriangles <= 0)
                        throw new InvalidOperationException("Imported dressing protection failed; source scene NOT saved: " + name);
                    CaptureShots(scene, output, "after");
                    if (oldObjects != CaptureOldObjects(scene) || lighting != CaptureLighting())
                        throw new InvalidOperationException("After-camera capture changed protected scene state; source NOT saved: " + name);
                    if (name == "20_Sanctum" && (otherObjects != CaptureOldObjects(valley, false) || otherSceneHash != Hash(valley.path) || valley.isDirty))
                        throw new InvalidOperationException("Additive review changed protected Valley lighting/objects/scene.");
                    SaveOwnedAssets();
                    if (!FrozenUnchanged(frozen, selected)) throw new InvalidOperationException("Owned-asset save changed frozen source files; scene NOT saved.");
                    if (!EditorSceneManager.SaveScene(scene)) throw new IOException("First scene save failed: " + name);
                    string once = Hash(path);
                    var owned = TreeHashes(AssetRoot);
                    WriteManifest(output + "/" + name + "-owned-once.json", owned, "Every current file under the new owned asset root, including meta.");
                    apply.Invoke(null, new object[] { scene });
                    SaveOwnedAssets();
                    if (oldObjects != CaptureOldObjects(scene) || physics != M6BoundaryArtSetup.CapturePhysics(scene) ||
                        lighting != CaptureLighting() || occluders != CaptureOccluders(scene) || !FrozenUnchanged(frozen, selected))
                        throw new InvalidOperationException("Repeated dressing changed protected state; repeated scene NOT saved: " + name);
                    if (name == "20_Sanctum" && (otherObjects != CaptureOldObjects(valley, false) || otherSceneHash != Hash(valley.path) || valley.isDirty))
                        throw new InvalidOperationException("Repeated additive dressing changed protected Valley objects/scene.");
                    if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Repeated scene save failed: " + name);
                    result.sceneIdempotent = Hash(path) == once;
                    var twice = TreeHashes(AssetRoot);
                    result.assetsIdempotent = SameFiles(owned, twice);
                    result.sceneSha256 = Hash(path);
                    File.WriteAllText(output + "/" + name + "-result.json", JsonUtility.ToJson(result, true));
                    WriteManifest(output + "/" + name + "-owned-twice.json", twice, "Repeated owned asset bytes; added/removed files also fail identity.");
                    if (!result.sceneIdempotent || !result.assetsIdempotent) throw new InvalidOperationException("Imported dressing is not scene/asset byte-idempotent: " + name);
                }
                if (!FrozenUnchanged(frozen, selected)) throw new InvalidOperationException("Final saved state changed protected file bytes or membership.");
                WriteManifest(output + "/frozen-after.json", frozen.Keys.ToDictionary(p => p, Hash), "Actual final hashes of the same protected files.");
                File.WriteAllText(output + "/complete.txt", "Only ImportedEnvironmentDressingSetup.ApplyToScene authored10/20. All old components/transforms/renderers/behaviours, physics/navigation, lighting and camera references frozen; two saves byte-identical. Fixed Editor composition evidence only.");
                return output;
            }
            catch (Exception error)
            {
                File.WriteAllText(output + "/failure.txt", error.ToString());
                // Keep raw differences; never overwrite sources from backups or relabel a failed run.
                throw;
            }
            finally { EditorSceneManager.RestoreSceneManagerSetup(previous); }
        }

        static string CaptureOldObjects(Scene scene, bool excludeDressing = true)
        {
            var rows = new List<string>();
            foreach (var root in scene.GetRootGameObjects().Where(r => !excludeDressing || r.name != RootName))
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string path = EnvironmentKitReview.PathOf(t);
                    rows.Add(path + "|GameObject|" + EditorJsonUtility.ToJson(t.gameObject));
                    rows.Add(path + "|WorldMatrix|" + JsonUtility.ToJson(t.localToWorldMatrix));
                    rows.Add(path + "|StaticFlags|" + (int)GameObjectUtility.GetStaticEditorFlags(t.gameObject));
                    var components = t.GetComponents<Component>();
                    for (int i = 0; i < components.Length; i++)
                        rows.Add(path + "|component" + i + "|" + (components[i] == null ? "MISSING_SCRIPT" : components[i].GetType().FullName + "|" + EditorJsonUtility.ToJson(components[i])));
                }
            rows.Sort(StringComparer.Ordinal); return string.Join("\n", rows);
        }

        static string CaptureOccluders(Scene scene) => string.Join("\n", scene.GetRootGameObjects()
            .SelectMany(r => r.GetComponentsInChildren<CameraOccluder>(true)).OrderBy(o => EnvironmentKitReview.PathOf(o.transform))
            .Select(o => EnvironmentKitReview.PathOf(o.transform) + "|" + EditorJsonUtility.ToJson(o)));

        static string CaptureLighting()
        {
            var maps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            var probe = RenderSettings.ambientProbe;
            return JsonUtility.ToJson(new LightingRow
            {
                activeScene = SceneManager.GetActiveScene().name, ambientMode = RenderSettings.ambientMode.ToString(),
                ambientSky = RenderSettings.ambientSkyColor, ambientEquator = RenderSettings.ambientEquatorColor,
                ambientGround = RenderSettings.ambientGroundColor, ambientIntensity = RenderSettings.ambientIntensity,
                fog = RenderSettings.fog, fogMode = RenderSettings.fogMode.ToString(), fogColor = RenderSettings.fogColor,
                fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance, fogDensity = RenderSettings.fogDensity,
                reflectionIntensity = RenderSettings.reflectionIntensity, reflectionBounces = RenderSettings.reflectionBounces,
                defaultReflectionMode = RenderSettings.defaultReflectionMode.ToString(), defaultReflectionResolution = RenderSettings.defaultReflectionResolution,
                customReflection = AssetDatabase.GetAssetPath((typeof(RenderSettings).GetProperty("customReflectionTexture", BindingFlags.Public | BindingFlags.Static)
                    ?? typeof(RenderSettings).GetProperty("customReflection", BindingFlags.Public | BindingFlags.Static))?.GetValue(null) as Object),
                haloStrength = RenderSettings.haloStrength, flareStrength = RenderSettings.flareStrength, flareFadeSpeed = RenderSettings.flareFadeSpeed,
                ambientProbe = Enumerable.Range(0, 27).Select(i => probe[i / 9, i % 9]).ToArray(),
                skybox = AssetDatabase.GetAssetPath(RenderSettings.skybox), skyboxSerialized = RenderSettings.skybox == null ? "null" : EditorJsonUtility.ToJson(RenderSettings.skybox),
                sun = RenderSettings.sun == null ? "null" : EnvironmentKitReview.PathOf(RenderSettings.sun.transform),
                lightmapsMode = LightmapSettings.lightmapsMode.ToString(), lightmapCount = maps.Length,
                lightmaps = maps.Select(m => AssetDatabase.GetAssetPath(m.lightmapColor) + "|" + AssetDatabase.GetAssetPath(m.lightmapDir) + "|" + AssetDatabase.GetAssetPath(m.shadowMask)).ToArray(),
                lights = LoadedScenes().SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<Light>(true))
                    .OrderBy(l => l.gameObject.scene.name + EnvironmentKitReview.PathOf(l.transform)).Select(l => l.gameObject.scene.name + "/" + EnvironmentKitReview.PathOf(l.transform) + "|" + EditorJsonUtility.ToJson(l)).ToArray(),
                cameraOccluders = LoadedScenes().Select(s => s.name + "|" + CaptureOccluders(s)).ToArray(),
                // Read assigned source IDs without calling Lightmapping.lightingSettings, which throws when unassigned.
                sceneLightingReferences = LoadedScenes().SelectMany(s => File.ReadAllLines(s.path)
                    .Where(l => l.Contains("m_LightingSettings:") || l.Contains("m_LightingDataAsset:"))
                    .Select(l => s.name + "|" + l)).ToArray()
            }, true);
        }

        static Scene[] LoadedScenes() => Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt).Where(s => s.isLoaded).ToArray();
        static int Renderers(Scene scene) => scene.GetRootGameObjects().Sum(r => r.GetComponentsInChildren<Renderer>(true).Length);

        static void CaptureShots(Scene scene, string output, string phase)
        {
            var shots = scene.name == "10_EmberValley" ? new[]
            {
                new CameraRow { image = "camp-" + phase + ".png", position = new Vector3(0, 3.2f, 5.5f), target = new Vector3(0, 1.4f, -7.5f) },
                new CameraRow { image = "courtyard-" + phase + ".png", position = new Vector3(19.7f, 3.4f, 41.2f), target = new Vector3(30, 2.6f, 54) },
                new CameraRow { image = "warden-" + phase + ".png", position = new Vector3(41, 3.4f, 30), target = new Vector3(54, 2.3f, 9.5f) }
            } : new[] { new CameraRow { image = "sanctum-" + phase + ".png", position = new Vector3(1000, -76.7f, 992.7f), target = new Vector3(1000, -78.7f, 1006.5f) } };
            foreach (var shot in shots)
            {
                shot.scene = scene.name; shot.activeLightingScene = SceneManager.GetActiveScene().name;
                shot.scope = "Fixed Editor camera, actual loaded scene render;20 additive with10 active. Not production-camera input, HUD or performance evidence.";
                var original = LoadedScenes().SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<UniversalAdditionalLightData>(true))
                    .Select(c => c.GetInstanceID()).ToHashSet();
                var originallyClean = LoadedScenes().Where(s => !s.isDirty).ToDictionary(s => s.handle, s => CaptureOldObjects(s, false));
                try { var image = EnvironmentKitReview.Shot(output + "/" + shot.image, shot.position, shot.target, shot.fieldOfView); Object.DestroyImmediate(image); }
                finally
                {
                    // Shot also creates and destroys its own camera in the active scene.
                    var cleanedScenes = new HashSet<int> { SceneManager.GetActiveScene().handle };
                    foreach (var data in LoadedScenes().SelectMany(s => s.GetRootGameObjects()).SelectMany(r => r.GetComponentsInChildren<UniversalAdditionalLightData>(true)).ToArray())
                        if (!original.Contains(data.GetInstanceID()))
                        { cleanedScenes.Add(data.gameObject.scene.handle); Object.DestroyImmediate(data); }
                    // Never clear a dirty flag to manufacture a clean scene. Retain a
                    // failed capture if rendering dirtied an originally clean scene.
                    foreach (var loaded in LoadedScenes())
                        if (cleanedScenes.Contains(loaded.handle) && originallyClean.TryGetValue(loaded.handle, out var before) &&
                            (before != CaptureOldObjects(loaded, false) || loaded.isDirty))
                            throw new InvalidOperationException("Temporary capture changed an originally clean scene: " + loaded.name);
                }
                File.WriteAllText(output + "/" + shot.image + ".json", JsonUtility.ToJson(shot, true));
            }
        }

        static Dictionary<string, string> FrozenFiles(string[] selected)
        {
            var files = new HashSet<string>(StringComparer.Ordinal);
            foreach (string folder in new[] { "Assets/_Game/Settings", "Assets/_Game/Prefabs/Characters", "Assets/_Game/Art/Animations",
                "Assets/_Game/Resources/Networking", "Assets/_Game/Scripts/Gameplay", "Assets/_Game/Scripts/AI", "Assets/_Game/Scripts/Networking" })
            {
                foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) files.Add(path.Replace('\\', '/'));
                if (File.Exists(folder + ".meta")) files.Add(folder + ".meta");
            }
            foreach (string name in Scenes) files.Add("Assets/_Game/Scenes/" + name + ".unity.meta");
            foreach (string name in new[] { "90_CombatGym", "91_NetworkGym" })
            { files.Add("Assets/_Game/Scenes/" + name + ".unity"); files.Add("Assets/_Game/Scenes/" + name + ".unity.meta"); }
            files.Add(OwnSource); files.Add(OwnSource + ".meta");
            foreach (string path in selected)
            {
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !File.Exists(path)) throw new FileNotFoundException("Selected source must be an existing project file.", path);
                files.Add(path); if (File.Exists(path + ".meta")) files.Add(path + ".meta");
                foreach (string dependency in AssetDatabase.GetDependencies(path, true).Where(File.Exists))
                { files.Add(dependency); if (File.Exists(dependency + ".meta")) files.Add(dependency + ".meta"); }
            }
            return files.OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => p, Hash);
        }

        static void SaveOwnedAssets()
        {
            if (!Directory.Exists(AssetRoot)) throw new DirectoryNotFoundException(AssetRoot);
            foreach (string path in Directory.GetFiles(AssetRoot, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".meta", StringComparison.Ordinal)))
            { var asset = AssetDatabase.LoadAssetAtPath<Object>(path.Replace('\\', '/')); if (asset != null) AssetDatabase.SaveAssetIfDirty(asset); }
        }
        static Dictionary<string, string> TreeHashes(string folder) => Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .Concat(File.Exists(folder + ".meta") ? new[] { folder + ".meta" } : Array.Empty<string>())
            .OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => p.Replace('\\', '/'), Hash);
        static bool SameFiles(Dictionary<string, string> a, Dictionary<string, string> b) => a.Count == b.Count && a.All(p => b.TryGetValue(p.Key, out var hash) && hash == p.Value);
        static bool FrozenUnchanged(Dictionary<string, string> files, string[] selected) => SameFiles(files, FrozenFiles(selected));
        static void WriteManifest(string path, Dictionary<string, string> files, string scope) => File.WriteAllText(path, JsonUtility.ToJson(new Manifest
        { scope = scope, files = files.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => new FileRow { path = p.Key, sha256 = p.Value, bytes = new FileInfo(p.Key).Length }).ToArray() }, true));
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
    }
}
