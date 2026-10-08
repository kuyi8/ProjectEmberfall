using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
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
    /// <summary>CPU rest-pose material A/B and protected material-only migration. Not animation/contact acceptance.</summary>
    public static class CharacterPaletteReview
    {
        public static readonly string[] VisualPrefabs = {
            "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab",
            "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_FogwalkerSkeleton.prefab",
            "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Enemy_RunePriest.prefab",
            "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Boss_EmberWarden.prefab",
            "Assets/_Game/Prefabs/Characters/M5c/P_M5c_Enemy_RuinGuardScorched_Knight.prefab"
        };
        public static readonly string[] Scenes = { "01_MainMenu", "10_EmberValley", "20_Sanctum", "90_CombatGym", "91_NetworkGym" };
        [Serializable] sealed class Row { public string asset, before, after; public int changedRenderers; public Vector3 bounds; }
        [Serializable] sealed class Report {
            public string scope = "Same neutral URP lighting/camera per before-after pair, actual production prefabs, CPU-baked rest poses (not locomotion, attack, contacts, natural camera, HUD or performance). Original assets and loaded scenes unchanged.";
            public bool originalsSame; public Row[] rows;
        }
        [Serializable] sealed class Migration {
            public string scope = "Prepare only JSON-authored owned palette outputs while freezing all original inputs; then freeze prepared outputs and migrate only approved character Renderer material references. All other serialized component fields, transforms, meshes, enabled flags, GameObjects and navigation frozen. No M6 rebuild/Bake. Not full-chain regeneration proof.";
            public bool frozenSame; public List<string> changes = new List<string>();
        }
        sealed class Draw { public Mesh mesh; public Matrix4x4 matrix; public Material[] materials; public bool ownsMesh; }
        [Serializable] sealed class Job { public string label, state, result, error; }
        static readonly HashSet<string> PendingLabels = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Short MCP entry: one uniquely labelled job, never automatic replay of a long mutation.</summary>
        public static string BeginProtected(string label)
        {
            if (string.IsNullOrEmpty(label) || !Regex.IsMatch(label, "^[A-Za-z0-9-]{8,80}$"))
                throw new ArgumentException("Use a unique ASCII batch label.", nameof(label));
            string key = "Emberfall.CharacterPaletteReview.Job." + label;
            string existing = SessionState.GetString(key, "");
            if (!string.IsNullOrEmpty(existing))
            {
                if (!File.Exists(existing)) throw new IOException("Retained palette job record is missing: " + existing);
                var retained = JsonUtility.FromJson<Job>(File.ReadAllText(existing));
                if ((retained.state == "queued" || retained.state == "running") && !PendingLabels.Contains(label))
                {
                    retained.state = "interrupted";
                    retained.error = "Editor domain/session lost the callback; inspect retained evidence. No automatic replay.";
                    File.WriteAllText(existing, JsonUtility.ToJson(retained, true));
                }
                return existing;
            }
            RequireIdle();
            string directory = Fresh("character-palette-jobs"), record = directory + "/job.json";
            var job = new Job { label = label, state = "queued" };
            File.WriteAllText(record, JsonUtility.ToJson(job, true));
            SessionState.SetString(key, record);
            PendingLabels.Add(label);
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                job.state = "running"; File.WriteAllText(record, JsonUtility.ToJson(job, true));
                try { job.result = ApplyProtected(); job.state = "completed"; }
                catch (Exception error) { job.error = error.ToString(); job.state = "failed"; Debug.LogException(error); }
                finally { PendingLabels.Remove(label); File.WriteAllText(record, JsonUtility.ToJson(job, true)); }
            };
            EditorApplication.update += callback;
            return record;
        }

        public static string CapturePreview()
        {
            RequireIdle();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !(GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset))
                throw new InvalidOperationException("Real URP graphics required.");
            Material[] materials;
            var frozen = PrepareThenFreeze(out materials);
            var sceneHashes = Scenes.ToDictionary(s => "Assets/_Game/Scenes/" + s + ".unity", s => Hash("Assets/_Game/Scenes/" + s + ".unity"));
            string dir = Fresh("character-palette-preview");
            var rows = new List<Row>();
            try
            {
                for (int i = 0; i < VisualPrefabs.Length; i++)
                {
                    string path = VisualPrefabs[i], label = Path.GetFileNameWithoutExtension(path);
                    var row = new Row { asset = path, before = label + "-before.png", after = label + "-after.png" };
                    rows.Add(row);
                    using (var stage = new Stage(path))
                    {
                        row.bounds = stage.bounds.size;
                        stage.Capture(dir + "/" + row.before);
                        row.changedRenderers = CharacterPresentationPaletteSetup.ApplyPrepared(stage.root, materials);
                        stage.ReloadMaterials();
                        stage.Capture(dir + "/" + row.after);
                    }
                }
            }
            finally
            {
                bool same = frozen.All(p => Hash(p.Key) == p.Value) && sceneHashes.All(p => Hash(p.Key) == p.Value);
                File.WriteAllText(dir + "/report.json", JsonUtility.ToJson(new Report { originalsSame = same, rows = rows.ToArray() }, true));
                if (!same) throw new InvalidOperationException("Preview mutated protected sources/scenes; evidence retained.");
            }
            return dir;
        }

        public static string ApplyProtected()
        {
            RequireIdle();
            string dir = Fresh("character-palette-production");
            Material[] materials;
            var frozen = PrepareThenFreeze(out materials);
            File.WriteAllLines(dir + "/frozen-before.txt", frozen.Select(p => p.Value + " " + p.Key));
            var previous = EditorSceneManager.GetSceneManagerSetup();
            var report = new Migration();
            var targets = VisualPrefabs.Concat(AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/_Game/Resources/Networking" }).Select(AssetDatabase.GUIDToAssetPath)).Distinct().ToArray();
            foreach (string path in targets.Concat(Scenes.Select(s => "Assets/_Game/Scenes/" + s + ".unity")))
            {
                File.Copy(path, dir + "/" + Path.GetFileName(path));
                File.Copy(path + ".meta", dir + "/" + Path.GetFileName(path) + ".meta");
            }
            try
            {
                foreach (string path in targets)
                {
                    var root = PrefabUtility.LoadPrefabContents(path);
                    try
                    {
                        string before = NonMaterialSnapshot(new[] { root });
                        int count = CharacterPresentationPaletteSetup.ApplyPrepared(root, materials);
                        if (before != NonMaterialSnapshot(new[] { root })) throw new InvalidOperationException("Non-material prefab delta: " + path);
                        if (count > 0 && PrefabUtility.SaveAsPrefabAsset(root, path) == null)
                            throw new IOException("Prefab save failed: " + path);
                        string once = Hash(path);
                        if (CharacterPresentationPaletteSetup.ApplyPrepared(root, materials) != 0) throw new InvalidOperationException("Prefab remap not idempotent: " + path);
                        if (once != Hash(path)) throw new InvalidOperationException("Prefab changed during second remap: " + path);
                        report.changes.Add(path + " changedRenderers=" + count);
                    }
                    finally { PrefabUtility.UnloadPrefabContents(root); }
                }
                foreach (string name in Scenes)
                {
                    string path = "Assets/_Game/Scenes/" + name + ".unity";
                    var scene = EditorSceneManager.OpenScene(path);
                    string before = NonMaterialSnapshot(scene.GetRootGameObjects());
                    string physics = M6BoundaryArtSetup.CapturePhysics(scene);
                    int count = scene.GetRootGameObjects().Sum(root => CharacterPresentationPaletteSetup.ApplyPrepared(root, materials));
                    if (before != NonMaterialSnapshot(scene.GetRootGameObjects()) || physics != M6BoundaryArtSetup.CapturePhysics(scene))
                        throw new InvalidOperationException("Non-material scene/physics delta, NOT saved: " + name);
                    if (count > 0 && !EditorSceneManager.SaveScene(scene)) throw new IOException("Scene save failed: " + name);
                    string once = Hash(path);
                    if (scene.GetRootGameObjects().Sum(root => CharacterPresentationPaletteSetup.ApplyPrepared(root, materials)) != 0) throw new InvalidOperationException("Scene remap not idempotent: " + name);
                    if (Hash(path) != once || scene.isDirty) throw new InvalidOperationException("Second remap dirtied scene: " + name);
                    report.changes.Add(path + " changedRenderers=" + count);
                }
                report.frozenSame = frozen.All(p => Hash(p.Key) == p.Value);
                if (!report.frozenSame) throw new InvalidOperationException("Original material/model/animation/nav dependency changed.");
                return dir;
            }
            finally
            {
                File.WriteAllText(dir + "/report.json", JsonUtility.ToJson(report, true));
                EditorSceneManager.RestoreSceneManagerSetup(previous);
            }
        }

        public static string NonMaterialSnapshot(IEnumerable<GameObject> roots)
        {
            var rows = new List<string>();
            foreach (var root in roots) foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                string path = EnvironmentKitReview.PathOf(t);
                rows.Add(path + "|GO|" + t.gameObject.activeSelf + "|" + t.gameObject.layer + "|" + t.gameObject.tag + "|" + GameObjectUtility.GetStaticEditorFlags(t.gameObject));
                foreach (var c in t.GetComponents<Component>())
                {
                    if (c == null) throw new InvalidOperationException("Missing component: " + path);
                    string json = EditorJsonUtility.ToJson(c);
                    if (c is Renderer) json = Regex.Replace(json, "\"m_Materials\":\\s*\\[[^\\]]*\\]", "\"m_Materials\":[]");
                    rows.Add(path + "|" + c.GetType().FullName + "|" + json);
                }
            }
            rows.Sort(StringComparer.Ordinal); return string.Join("\n", rows);
        }
        public static Dictionary<string, string> ProtectedDependencies()
        {
            // Inputs stay protected even after prefabs no longer reference their old material.
            // Outputs are included explicitly, including a first migration before any prefab references them.
            var authored = CharacterPresentationPaletteSetup.SourcePaths
                .Concat(new[] { CharacterPresentationPaletteSetup.PalettePath })
                .Concat(CharacterPresentationPaletteSetup.ReadPalette().entries.Select(e => CharacterPresentationPaletteSetup.TargetPath(e.id)));
            var paths = VisualPrefabs.Concat(authored).SelectMany(p => AssetDatabase.GetDependencies(p, true))
                .Concat(authored)
                .Where(p => File.Exists(p) && !p.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".cs", StringComparison.OrdinalIgnoreCase));
            paths = paths.Concat(Directory.GetFiles("Assets/_Game/Settings/Navigation", "*", SearchOption.AllDirectories));
            return paths.SelectMany(p => File.Exists(p + ".meta") ? new[] { p, p + ".meta" } : new[] { p }).Distinct().OrderBy(p => p).ToDictionary(p => p, Hash);
        }
        static Dictionary<string, string> PrepareThenFreeze(out Material[] materials)
        {
            var outputPaths = new HashSet<string>(CharacterPresentationPaletteSetup.ReadPalette().entries
                .SelectMany(e => new[] { CharacterPresentationPaletteSetup.TargetPath(e.id), CharacterPresentationPaletteSetup.TargetPath(e.id) + ".meta" }), StringComparer.Ordinal);
            var originals = ProtectedDependencies().Where(p => !outputPaths.Contains(p.Key)).ToDictionary(p => p.Key, p => p.Value);
            materials = CharacterPresentationPaletteSetup.EnsureMaterials();
            if (!originals.All(p => Hash(p.Key) == p.Value))
                throw new InvalidOperationException("Palette preparation modified an original source dependency.");
            // Own palette assets are intended outputs of preparation, not immutable
            // third-party sources. Once prepared, freeze their bytes too for mapping.
            var frozen = ProtectedDependencies();
            foreach (var p in originals) frozen[p.Key] = p.Value;
            return frozen;
        }
        static void RequireIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) throw new InvalidOperationException("Idle Editor required.");
            for (int i = 0; i < SceneManager.sceneCount; i++) if (SceneManager.GetSceneAt(i).isDirty) throw new InvalidOperationException("Preserve unsaved user scene work.");
        }
        static string Fresh(string label) { string dir = Path.GetFullPath("Builds/ArtReview/" + label + "/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")); if (Directory.Exists(dir)) throw new IOException("Never overwrite evidence."); Directory.CreateDirectory(dir); return dir; }
        public static string Hash(string path) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }

        sealed class Stage : IDisposable
        {
            readonly PreviewRenderUtility preview = new PreviewRenderUtility();
            readonly List<Draw> draws = new List<Draw>();
            readonly List<Renderer> renderers = new List<Renderer>();
            public GameObject root; public Bounds bounds;
            public Stage(string path)
            {
                root = preview.InstantiatePrefabInScene(AssetDatabase.LoadAssetAtPath<GameObject>(path));
                foreach (var a in root.GetComponentsInChildren<Animator>(true)) { a.enabled = true; a.Rebind(); a.Update(0); a.enabled = false; }
                foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                {
                    if (!r.enabled || !r.gameObject.activeInHierarchy) continue;
                    Mesh mesh = null; bool owns = false;
                    // Explicit useScale=true cancels imported renderer scale before the
                    // localToWorld application (these rigs include 37x/100x mesh children).
                    if (r is SkinnedMeshRenderer skin) { mesh = new Mesh { name = "RestPose_CPU_ReviewOnly" }; skin.BakeMesh(mesh, true); owns = true; }
                    else { var filter = r.GetComponent<MeshFilter>(); if (filter != null) mesh = filter.sharedMesh; }
                    if (mesh == null) continue;
                    if (r.sharedMaterials.Length == 0) throw new InvalidOperationException("Missing material slots: " + r.name);
                    renderers.Add(r); draws.Add(new Draw { mesh = mesh, matrix = r.localToWorldMatrix, materials = r.sharedMaterials, ownsMesh = owns });
                    // Draw ONLY the baked CPU snapshot, never duplicate/stale GPU skinning.
                    r.enabled = false;
                }
                if (draws.Count == 0) throw new InvalidOperationException("No visible geometry: " + path);
                bounds = new Bounds(draws[0].matrix.MultiplyPoint3x4(draws[0].mesh.bounds.center), Vector3.zero);
                foreach (var draw in draws) for (int c = 0; c < 8; c++)
                {
                    var b = draw.mesh.bounds;
                    bounds.Encapsulate(draw.matrix.MultiplyPoint3x4(new Vector3((c & 1) == 0 ? b.min.x : b.max.x, (c & 2) == 0 ? b.min.y : b.max.y, (c & 4) == 0 ? b.min.z : b.max.z)));
                }
                if (bounds.size.y < 1 || bounds.size.y > 3.5f) throw new InvalidOperationException("Implausible production metre scale: " + path + " " + bounds.size);
                var camera = preview.camera; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.24f, .27f, .29f);
                camera.fieldOfView = 36; camera.allowHDR = false; camera.allowMSAA = false;
                float distance = bounds.extents.magnitude / Mathf.Sin(18 * Mathf.Deg2Rad) * 1.28f;
                camera.transform.position = bounds.center + Quaternion.Euler(12, 20, 0) * Vector3.back * distance;
                camera.transform.LookAt(bounds.center); camera.nearClipPlane = .01f; camera.farClipPlane = distance + 20;
                camera.GetUniversalAdditionalCameraData().renderPostProcessing = false;
                preview.ambientColor = new Color(.42f, .42f, .42f);
                preview.lights[0].intensity = 1.2f; preview.lights[0].color = Color.white; preview.lights[0].transform.rotation = Quaternion.Euler(40, 30, 0);
                preview.lights[1].intensity = .6f; preview.lights[1].color = Color.white; preview.lights[1].transform.rotation = Quaternion.Euler(20, 210, 0);
            }
            public void ReloadMaterials() { for (int i = 0; i < draws.Count; i++) draws[i].materials = renderers[i].sharedMaterials; }
            public void Capture(string path)
            {
                const int width = 600, height = 700;
                var prior = RenderTexture.active; RenderTexture rt = null; Texture2D image = null; bool open = false;
                try
                {
                    preview.BeginPreview(new Rect(0, 0, width, height), GUIStyle.none); open = true;
                    foreach (var d in draws) for (int s = 0; s < d.mesh.subMeshCount; s++) preview.DrawMesh(d.mesh, d.matrix, d.materials[Mathf.Min(s, d.materials.Length - 1)], s);
                    preview.Render(true, false); var texture = preview.EndPreview(); open = false;
                    rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                    Graphics.Blit(texture, rt); RenderTexture.active = rt;
                    image = new Texture2D(width, height, TextureFormat.RGB24, false); image.ReadPixels(new Rect(0, 0, width, height), 0, 0); image.Apply();
                    File.WriteAllBytes(path, image.EncodeToPNG());
                }
                finally { if (open) preview.EndPreview(); RenderTexture.active = prior; if (rt != null) RenderTexture.ReleaseTemporary(rt); if (image != null) Object.DestroyImmediate(image); }
            }
            public void Dispose() { foreach (var d in draws) if (d.ownsMesh) Object.DestroyImmediate(d.mesh); preview.Cleanup(); }
        }
    }
}
