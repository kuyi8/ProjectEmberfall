using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Movement;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Review
{
    /// <summary>Narrow camera registration only; never rebuilds art, collision or navigation.</summary>
    public static class ImportedDressingCameraReview
    {
        private static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.Ordinal);
        public static string Begin(string label)
        {
            if (string.IsNullOrEmpty(label) || !Regex.IsMatch(label, "^[A-Za-z0-9-]{8,80}$"))
                throw new ArgumentException("Unique ASCII label required.", nameof(label));
            string key = "Emberfall.ImportedDressingCameraReview." + label;
            string retained = SessionState.GetString(key, "");
            if (!string.IsNullOrEmpty(retained)) return retained; // Never replay a queued or finished mutation.
            string path = "Builds/ArtReview/imported-dressing-camera/" + label + "-job.json";
            if (File.Exists(path)) throw new IOException("Retain old job; use a fresh label.");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            Write(path, new { label, state = "queued" });
            SessionState.SetString(key, path); Pending.Add(label);
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                if (!Pending.Remove(label)) return;
                Write(path, new { label, state = "running" });
                try { string result = Apply(); Write(path, new { label, state = "succeeded", result }); }
                catch (Exception error) { Write(path, new { label, state = "failed", error = error.ToString() }); Debug.LogException(error); }
            };
            EditorApplication.update += callback;
            return path;
        }

        public static string Apply()
        {
            var active = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                SceneManager.sceneCount != 1 || active.isDirty || active.path != "Assets/_Game/Scenes/10_EmberValley.unity")
                throw new InvalidOperationException("Single clean idle formal Valley required; preserve user work.");
            string directory = "Builds/ArtReview/imported-dressing-camera/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            if (Directory.Exists(directory)) throw new IOException("Never overwrite prior evidence.");
            Directory.CreateDirectory(directory);
            var setup = EditorSceneManager.GetSceneManagerSetup();
            var frozen = FrozenFiles();
            Write(directory + "/frozen-before.json", frozen);
            var sceneRows = new List<object>();
            try
            {
                foreach (string name in ImportedEnvironmentDressingSetup.Scenes)
                {
                    string path = "Assets/_Game/Scenes/" + name + ".unity";
                    File.Copy(path, directory + "/" + name + "-before.unity");
                    File.Copy(path + ".meta", directory + "/" + name + "-before.unity.meta");
                    var scene = EditorSceneManager.OpenScene(path);
                    string before = CaptureProtected(scene);
                    File.WriteAllText(directory + "/" + name + "-protected-before.jsonl", before);
                    int additions = ImportedEnvironmentDressingSetup.RegisterCameraOccluders(scene);
                    var root = scene.GetRootGameObjects().Single(r => r.name == ImportedEnvironmentDressingSetup.RootName);
                    ImportedEnvironmentDressingSetup.ValidateSceneVisual(root, name);
                    string after = CaptureProtected(scene);
                    File.WriteAllText(directory + "/" + name + "-protected-after.jsonl", after);
                    if (before != after || !SameFiles(frozen, FrozenFiles()))
                        throw new InvalidOperationException("Protected state changed; this scene not saved.");
                    var rays = root.GetComponentsInChildren<CameraOccluder>(true).Select(o =>
                    {
                        var renderer = o.GetComponentsInChildren<Renderer>(true).Single();
                        float reach = renderer.bounds.size.magnitude + 1;
                        var ray = new Ray(renderer.bounds.center - o.transform.forward * reach, o.transform.forward);
                        bool hit = o.TryGetDistance(ray, reach * 2, .22f, out float distance);
                        return new { name = o.name, origin = ray.origin.ToString("R"), direction = ray.direction.ToString("R"), hit, distance, reach };
                    }).ToArray();
                    if (rays.Length != ImportedEnvironmentDressingSetup.CameraOccluderEntries.Count(k => k.StartsWith(name + "/", StringComparison.Ordinal)) ||
                        rays.Any(r => !r.hit || r.distance < 0 || r.distance > r.reach))
                        throw new InvalidOperationException("Missing or invalid per-wrapper world ray; scene not saved.");
                    object birthRay = null;
                    if (name == "10_EmberValley")
                    {
                        var ray = new Ray(new Vector3(0, 2.5f, -4), -(Quaternion.Euler(18, 0, 0) * Vector3.forward));
                        var archives = new[] { "CampArchiveWest", "CampArchiveEast" }.Select(n =>
                        {
                            var occluder = root.transform.Find(n).GetComponent<CameraOccluder>();
                            bool hit = occluder.TryGetDistance(ray, 5.8f, .22f, out float distance);
                            return new { name = n, hit, distance };
                        }).ToArray();
                        if (archives.Any(r => !r.hit || r.distance <= 0 || r.distance >= 5.8f))
                            throw new InvalidOperationException("Actual NewGame camp seam ray not covered; scene not saved.");
                        birthRay = new { origin = ray.origin.ToString("R"), direction = ray.direction.ToString("R"), radius = .22f, maximum = 5.8f, archives };
                    }
                    if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed.");
                    AssertSavedDelta(directory + "/" + name + "-before.unity", path, additions);
                    string once = Hash(path);
                    int repeated = ImportedEnvironmentDressingSetup.RegisterCameraOccluders(scene);
                    if (repeated != 0 || CaptureProtected(scene) != before || !SameFiles(frozen, FrozenFiles()))
                        throw new InvalidOperationException("Repeated camera migration changed frozen state; repeat not saved.");
                    if (!EditorSceneManager.SaveScene(scene) || Hash(path) != once)
                        throw new InvalidOperationException("Registration is not scene-byte-idempotent.");
                    sceneRows.Add(new { scene = name, additions, repeated, protectedStateSame = true,
                        sourceSceneSha = once, byteIdempotent = true, rays, birthRay });
                    Write(directory + "/scenes.json", sceneRows);
                }
                if (!SameFiles(frozen, FrozenFiles())) throw new InvalidOperationException("Protected file membership/bytes changed.");
                Write(directory + "/frozen-after.json", FrozenFiles());
                Write(directory + "/complete.json", new { status = "complete", scenes = sceneRows,
                    scope = "Only 20 explicitly named project-owned structural dressing wrappers receive existing CameraOccluder and their own renderer references. Camera behaviour intentionally changes; parameters, all old components/references, object membership/transform/rendering, physics/navigation, assets and non-target scene bytes frozen. Two saves byte-identical. AABB queries treat arch openings conservatively. Rays are NOT natural traversal, composition, Player, network or performance acceptance; separately inspect real settled formal-camera PNGs." });
                return directory;
            }
            catch (Exception error) { File.WriteAllText(directory + "/failure.txt", error.ToString()); throw; }
            finally { EditorSceneManager.RestoreSceneManagerSetup(setup); }
        }

        public static string CaptureProtected(Scene scene)
        {
            var allowed = new HashSet<string>(ImportedEnvironmentDressingSetup.CameraOccluderEntries, StringComparer.Ordinal);
            var rows = new List<string> { "PHYSICS|" + M6BoundaryArtSetup.CapturePhysics(scene) };
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    string path = PathOf(t);
                    bool ownException = t.parent != null && t.parent.name == ImportedEnvironmentDressingSetup.RootName &&
                        allowed.Contains(scene.name + "/" + t.name);
                    var components = t.GetComponents<Component>();
                    var go = JObject.Parse(EditorJsonUtility.ToJson(t.gameObject));
                    // Actual EditorJsonUtility GameObject output does not include component links.
                    // Compare every old component below; saved YAML links are separately checked exactly.
                    rows.Add(path + "|GameObject|" + go.ToString(Formatting.None));
                    rows.Add(path + "|WorldMatrix|" + JsonUtility.ToJson(t.localToWorldMatrix));
                    rows.Add(path + "|StaticFlags|" + (int)GameObjectUtility.GetStaticEditorFlags(t.gameObject));
                    int index = 0;
                    foreach (var component in components)
                    {
                        if (ownException && component is CameraOccluder) continue;
                        rows.Add(path + "|component" + index++ + "|" + (component == null ? "MISSING_SCRIPT" :
                            component.GetType().FullName + "|" + EditorJsonUtility.ToJson(component)));
                    }
                }
            // Registration must not touch active-scene lighting or asset-backed render state.
            rows.Add("GLOBALS|" + JsonUtility.ToJson(new LightingSnapshot()));
            rows.Sort(StringComparer.Ordinal);
            return string.Join("\n", rows);
        }

        [Serializable] private sealed class LightingSnapshot
        {
            public bool fog = RenderSettings.fog;
            public Color sky = RenderSettings.ambientSkyColor, equator = RenderSettings.ambientEquatorColor,
                ground = RenderSettings.ambientGroundColor, fogColor = RenderSettings.fogColor;
            public float ambient = RenderSettings.ambientIntensity, fogDensity = RenderSettings.fogDensity,
                fogStart = RenderSettings.fogStartDistance, fogEnd = RenderSettings.fogEndDistance;
            public string ambientMode = RenderSettings.ambientMode.ToString(), fogMode = RenderSettings.fogMode.ToString(),
                skybox = RenderSettings.skybox == null ? "null" : EditorJsonUtility.ToJson(RenderSettings.skybox);
        }

        static Dictionary<string, string> FrozenFiles()
        {
            var files = new HashSet<string>(StringComparer.Ordinal);
            foreach (string folder in new[] { "Assets/_Game/Settings", "Assets/_Game/Prefabs/Characters", "Assets/_Game/Resources/Networking",
                "Assets/_Game/Art/Animations", "Assets/_Game/Art/ImportedEnvironmentDressing", "Assets/_Game/Art/CharacterPresentationPalette",
                "Assets/_Game/Scripts/Gameplay", "Assets/_Game/Scripts/AI", "Assets/_Game/Scripts/Networking", "ProjectSettings", "Packages" })
            {
                if (!Directory.Exists(folder)) throw new DirectoryNotFoundException(folder);
                foreach (string path in Directory.GetFiles(folder, "*", SearchOption.AllDirectories)) files.Add(path.Replace('\\', '/'));
                if (File.Exists(folder + ".meta")) files.Add(folder + ".meta");
            }
            foreach (string path in Directory.GetFiles("Assets/_Game/Scenes", "*", SearchOption.AllDirectories))
                if (!(path.EndsWith("/10_EmberValley.unity", StringComparison.Ordinal) || path.EndsWith("/20_Sanctum.unity", StringComparison.Ordinal) ||
                    path.Replace('\\', '/').EndsWith("/10_EmberValley.unity", StringComparison.Ordinal) || path.Replace('\\', '/').EndsWith("/20_Sanctum.unity", StringComparison.Ordinal)))
                    files.Add(path.Replace('\\', '/'));
            foreach (string path in ImportedEnvironmentDressingSetup.SelectedSourcePaths)
                foreach (string dependency in AssetDatabase.GetDependencies(path, true).Concat(new[] { path }).Distinct())
                    if (File.Exists(dependency)) { files.Add(dependency); if (File.Exists(dependency + ".meta")) files.Add(dependency + ".meta"); }
            return files.OrderBy(p => p, StringComparer.Ordinal).ToDictionary(p => p, Hash);
        }
        static bool SameFiles(Dictionary<string, string> left, Dictionary<string, string> right) =>
            left.Count == right.Count && left.All(p => right.TryGetValue(p.Key, out string value) && value == p.Value);
        static string PathOf(Transform t) => t.parent == null ? t.name : PathOf(t.parent) + "/" + t.name;
        static string Hash(string path) { using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        static void Write(string path, object value) => File.WriteAllText(path, JsonConvert.SerializeObject(value, Formatting.Indented));

        static void AssertSavedDelta(string beforePath, string afterPath, int additions)
        {
            // Exact saved record bodies, not an aggregate component-count exemption.
            string previousText = File.ReadAllText(beforePath), currentText = File.ReadAllText(afterPath);
            string headerPattern = @"(?m)^--- !u!\d+ &(?<id>-?\d+)";
            var previousHeaders = Regex.Matches(previousText, headerPattern).Cast<Match>().ToArray();
            var currentHeaders = Regex.Matches(currentText, headerPattern).Cast<Match>().ToArray();
            if (previousHeaders.Length == 0 || currentHeaders.Length == 0 ||
                previousText.Substring(0, previousHeaders[0].Index) != currentText.Substring(0, currentHeaders[0].Index))
                throw new InvalidOperationException("Saved YAML header changed.");
            Func<string, Dictionary<string, string>> records = path => Regex.Matches(File.ReadAllText(path),
                @"(?ms)^--- !u!\d+ &(?<id>-?\d+)(?: stripped)?\r?\n.*?(?=^--- !u!|\z)")
                .Cast<Match>().ToDictionary(m => m.Groups["id"].Value, m => m.Value, StringComparer.Ordinal);
            var before = records(beforePath); var after = records(afterPath);
            var addedIds = after.Keys.Except(before.Keys, StringComparer.Ordinal).ToArray();
            if (!previousHeaders.Select(m => m.Groups["id"].Value).SequenceEqual(currentHeaders
                .Select(m => m.Groups["id"].Value).Where(id => before.ContainsKey(id))))
                throw new InvalidOperationException("Old saved record order changed.");
            if (before.Keys.Any(k => !after.ContainsKey(k)) || addedIds.Length != additions ||
                addedIds.Any(k => !after[k].StartsWith("--- !u!114 &", StringComparison.Ordinal) ||
                    !after[k].Contains("guid: f344a19ec6123594ca8b550acbea5cf9")))
                throw new InvalidOperationException("Saved scene delta exceeds explicit new CameraOccluder records.");
            foreach (var row in before)
            {
                string current = after[row.Key];
                foreach (string id in addedIds)
                    current = Regex.Replace(current, @"(?m)^  - component: \{fileID: " + Regex.Escape(id) + @"\}\r?\n", "");
                if (row.Value != current)
                    throw new InvalidOperationException("Old saved record changed beyond new component links: " + row.Key);
            }
        }
    }
}
