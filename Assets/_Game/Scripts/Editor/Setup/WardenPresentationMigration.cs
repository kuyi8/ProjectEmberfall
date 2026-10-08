using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Emberfall.AI.Unity;
using Emberfall.Networking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AI;
using Unity.AI.Navigation;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    /// <summary>
    /// Explicit, uniquely labelled production reference migration. No M6 regeneration,
    /// importer changes, asset preparation, bake, authority or automatic backup restore.
    /// The reviewed selected meshes/material already have to exist.
    /// </summary>
    public static class WardenPresentationMigration
    {
        const string Character = "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Boss_EmberWarden.prefab";
        const string MainScene = "Assets/_Game/Scenes/10_EmberValley.unity";
        static readonly string[] Prefabs = { Character, WardenSilhouetteSetup.CanonicalWeaponPath, WardenNetworkEquipmentSetup.NetworkPrefabPath };
        static readonly string[] Scenes = { MainScene, "Assets/_Game/Scenes/20_Sanctum.unity", "Assets/_Game/Scenes/90_CombatGym.unity", "Assets/_Game/Scenes/91_NetworkGym.unity" };
        static readonly HashSet<string> Pending = new HashSet<string>(StringComparer.Ordinal);

        [Serializable] sealed class Job { public string label, state, directory, error; }
        [Serializable] public sealed class TargetResult
        {
            public string path, beforeSha, firstPassSha, secondPassSha, backup;
            public bool hasWarden, plannedWrite, actuallySaved, inheritedSourceChange;
            public string persistentChangeSource;
            public int meshChanges, materialChanges, equipmentPairsAdded;
        }
        [Serializable] public sealed class MigrationReport
        {
            public string label, state, error;
            public string scope = "Reviewed same-Avatar Warden helmet/sword reference selection and render-only NET sword/shield. All old serialized component fields, reference identities, hierarchy, physics, navigation, transforms, flags and authority frozen except exact mesh filters, sword material slot 0 and three NET equipment references. Scene public render/lightmap values and stable references plus persisted native RenderSettings/LightmapSettings blocks are frozen; generated GI object instance IDs are not compared. No rig, range, timing, gameplay, importer or M6/Bake changes. Not natural/contact/network-runtime/performance acceptance.";
            public WardenSilhouetteSelection.SourceDescription selection = new WardenSilhouetteSelection.SourceDescription();
            public bool preflightNoSaves, protectedSourcesSame, secondPassByteIdentical, originalSetupRestored;
            public List<TargetResult> targets = new List<TargetResult>();
        }
        sealed class Plan { public TargetResult result; public Frame original; public bool prefab; }
        sealed class Stage : IDisposable
        {
            public readonly string path;
            public readonly bool prefab;
            public readonly GameObject[] roots;
            public readonly Scene scene;
            public Stage(string asset, bool isPrefab)
            {
                path = asset; prefab = isPrefab;
                if (prefab) { roots = new[] { PrefabUtility.LoadPrefabContents(path) }; scene = roots[0].scene; }
                else { scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single); roots = scene.GetRootGameObjects(); }
            }
            public void Dispose() { if (prefab) PrefabUtility.UnloadPrefabContents(roots[0]); }
        }
        sealed class Node
        {
            public string id, parent, gameObjectJson;
            public string[] children, componentTypes, componentJson;
        }
        sealed class Frame
        {
            public Dictionary<string, Node> nodes;
            public string physics, sceneGlobals;
            public HashSet<string> permittedAdded;
            public string[] raw;
        }
        [Serializable] sealed class PhysicsSnapshot
        {
            public string target;
            public PhysicsRow[] components;
            public string[] navigation;
        }
        [Serializable] sealed class PhysicsRow
        {
            public string path, type, serialized;
            public Matrix4x4 localToWorld;
            public Vector3 boundsCenter, boundsSize;
            public bool active;
        }
        [Serializable] sealed class SceneGlobalsSnapshot
        {
            public string target, fogMode, ambientMode, skybox, sun, customReflection, lightmapsMode, defaultReflectionMode;
            public string persistedRenderSettings, persistedLightmapSettings;
            public bool fog;
            public Color fogColor, ambientSky, ambientEquator, ambientGround, ambientLight, subtractiveShadowColor;
            public float fogStart, fogEnd, fogDensity, ambientIntensity, reflectionIntensity, haloStrength, flareStrength, flareFadeSpeed;
            public int reflectionBounces, defaultReflectionResolution;
            public float[] ambientProbe;
            public string[] lightmaps;
        }
        sealed class Targets
        {
            public readonly HashSet<MeshFilter> filters = new HashSet<MeshFilter>();
            public readonly HashSet<MeshRenderer> firstSlots = new HashSet<MeshRenderer>();
            public readonly HashSet<NetworkWarden> network = new HashSet<NetworkWarden>();
            public GameObject[] wardens;
        }

        /// <summary>Short MCP entry. A timeout/retry returns this same retained job; it never replays a mutation.</summary>
        public static string BeginProtected(string label)
        {
            if (string.IsNullOrEmpty(label) || !Regex.IsMatch(label, "^[A-Za-z0-9-]{8,80}$"))
                throw new ArgumentException("Use a unique ASCII label.", nameof(label));
            string key = "Emberfall.WardenPresentationMigration.Job." + label;
            string retainedPath = SessionState.GetString(key, "");
            if (!string.IsNullOrEmpty(retainedPath))
            {
                if (!File.Exists(retainedPath)) throw new IOException("Retained migration record is missing.");
                Job retained = JsonUtility.FromJson<Job>(File.ReadAllText(retainedPath));
                if ((retained.state == "queued" || retained.state == "running") && !Pending.Contains(label))
                {
                    retained.state = "interrupted";
                    retained.error = "Domain/session lost callback. Inspect backups and actual bytes; no automatic replay.";
                    File.WriteAllText(retainedPath, JsonUtility.ToJson(retained, true));
                }
                return retainedPath;
            }
            RequireInitialIdle();
            if (Pending.Count != 0) throw new InvalidOperationException("Another Warden migration job is pending.");
            string directory = Fresh(), record = Path.Combine(directory, "job.json");
            var job = new Job { label = label, state = "queued", directory = directory };
            File.WriteAllText(record, JsonUtility.ToJson(job, true));
            SessionState.SetString(key, record); Pending.Add(label);
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                job.state = "running"; File.WriteAllText(record, JsonUtility.ToJson(job, true));
                try { Run(directory, label); job.state = "completed"; }
                catch (Exception error) { job.state = "failed"; job.error = error.ToString(); Debug.LogException(error); }
                finally { Pending.Remove(label); File.WriteAllText(record, JsonUtility.ToJson(job, true)); }
            };
            EditorApplication.update += callback;
            return record;
        }

        static void Run(string directory, string label)
        {
            RequireInitialIdle();
            // Read/validate existing outputs once. This intentionally never calls Prepare,
            // EnsureMaterials, SaveAssets, importers or a full project generator.
            ValidatePreparedSelection();
            string[] targets = Prefabs.Concat(Scenes).ToArray();
            var expected = targets.ToDictionary(p => p, Hash, StringComparer.Ordinal);
            var frozen = ProtectedFiles(targets);
            var materialMemory = MaterialState(frozen.Keys);
            File.WriteAllLines(Path.Combine(directory, "frozen-before.txt"), frozen.Select(p => p.Value + " " + p.Key));
            File.WriteAllLines(Path.Combine(directory, "targets-before.txt"), expected.Select(p => p.Value + " " + p.Key));
            SceneSetup[] previous = EditorSceneManager.GetSceneManagerSetup();
            var report = new MigrationReport { label = label, state = "preflight" };
            var plans = new List<Plan>();
            Stage lastOwnedScene = null;
            Frame lastOwnedSceneBefore = null;
            bool restoreSafe = true;
            try
            {
                foreach (string path in targets)
                {
                    bool prefab = Prefabs.Contains(path);
                    using (var stage = new Stage(path, prefab))
                    {
                        Targets selected = Locate(stage);
                        var row = new TargetResult { path = path, beforeSha = expected[path], hasWarden = selected.wardens.Length != 0 };
                        Frame before = Capture(stage, selected);
                        if (!prefab)
                        {
                            lastOwnedScene = stage; lastOwnedSceneBefore = before;
                            if (stage.scene.isDirty)
                            {
                                restoreSafe = false;
                                throw new InvalidOperationException("Loading the read-only scene produced unexpected dirty state; retained: " + path);
                            }
                        }
                        File.WriteAllLines(Evidence(directory, path, "preflight-before"), before.raw);
                        // Prefab contents are isolated temporary edit scenes, so the full
                        // edit can be preflighted and discarded without saving a source.
                        // Real production scenes are inspected read-only in this phase.
                        if (prefab)
                        {
                            Apply(stage, selected, row);
                            Verify(before, Capture(stage, Locate(stage)));
                            row.plannedWrite = row.meshChanges + row.materialChanges + row.equipmentPairsAdded != 0;
                            row.meshChanges = row.materialChanges = row.equipmentPairsAdded = 0;
                        }
                        else row.plannedWrite = NeedsSelection(selected);
                        CheckFrozen(frozen, materialMemory); CheckTargets(expected);
                        if (!prefab && stage.scene.isDirty)
                        {
                            restoreSafe = false;
                            throw new InvalidOperationException("Read-only preflight dirtied source scene; retained: " + path);
                        }
                        plans.Add(new Plan { result = row, original = before, prefab = prefab }); report.targets.Add(row);
                    }
                }
                CheckTargets(expected); CheckFrozen(frozen, materialMemory);
                report.preflightNoSaves = true;
                EditorSceneManager.RestoreSceneManagerSetup(previous);
                RequireInitialIdle();
                foreach (Plan plan in plans.Where(p => p.result.plannedWrite))
                {
                    string source = plan.result.path;
                    string backup = Path.Combine(directory, "backup", source.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(backup));
                    File.Copy(source, backup, false); File.Copy(source + ".meta", backup + ".meta", false);
                    plan.result.backup = backup;
                }
                report.state = "first-pass";
                foreach (Plan plan in plans)
                {
                    using (var stage = new Stage(plan.result.path, plan.prefab))
                    {
                        if (!plan.prefab) { lastOwnedScene = stage; lastOwnedSceneBefore = plan.original; }
                        try
                        {
                            CheckTargets(expected); CheckFrozen(frozen, materialMemory);
                            Frame before = Capture(stage, Locate(stage));
                            Verify(plan.original, before); // Also audits changes inherited from newly saved source prefabs.
                            Apply(stage, Locate(stage), plan.result);
                            Frame after = Capture(stage, Locate(stage));
                            Verify(plan.original, after); Verify(before, after);
                            File.WriteAllLines(Evidence(directory, plan.result.path, "first-pass"), after.raw);
                            int writes = plan.result.meshChanges + plan.result.materialChanges + plan.result.equipmentPairsAdded;
                            if (writes != 0)
                            {
                                if (!plan.result.plannedWrite || string.IsNullOrEmpty(plan.result.backup))
                                    throw new InvalidOperationException("An unplanned target would be written: " + plan.result.path);
                                CheckTargets(expected); CheckFrozen(frozen, materialMemory);
                                Save(stage);
                                expected[stage.path] = Hash(stage.path);
                                plan.result.actuallySaved = true;
                                plan.result.persistentChangeSource = "This exact target was saved: reviewed helmet/sword mesh or sword slot-0 reference and/or NET render-only nodes + three references. Full stable before/reloaded evidence identifies each changed asset GUID/fileID.";
                                Verify(after, Capture(stage, Locate(stage)));
                                CheckFrozen(frozen, materialMemory); CheckTargets(expected);
                            }
                            else
                            {
                                plan.result.inheritedSourceChange = plan.result.plannedWrite;
                                plan.result.persistentChangeSource = plan.result.plannedWrite ?
                                    "References inherited from earlier saved Character/canonical sword sources; this target was NOT saved." :
                                    "Already selected/no actual Warden instance; this target was NOT saved.";
                                if (!stage.prefab && stage.scene.isDirty)
                                    throw new InvalidOperationException("Zero-delta selection dirtied a production scene; do not save: " + stage.path);
                            }
                            plan.result.firstPassSha = Hash(stage.path);
                        }
                        catch
                        {
                            if (!stage.prefab && stage.scene.isDirty)
                            {
                                try { Verify(lastOwnedSceneBefore, Capture(stage, Locate(stage))); }
                                catch { restoreSafe = false; }
                            }
                            throw;
                        }
                    }
                    // Reopen actual saved/retained bytes, rather than only validating the
                    // still-loaded mutable hierarchy before declaring persisted acceptance.
                    using (var reopened = new Stage(plan.result.path, plan.prefab))
                    {
                        if (!plan.prefab) { lastOwnedScene = reopened; lastOwnedSceneBefore = plan.original; }
                        Frame reloaded = Capture(reopened, Locate(reopened));
                        Verify(plan.original, reloaded);
                        if (NeedsSelection(Locate(reopened))) throw new InvalidOperationException("Saved/retained target has incomplete selected references: " + plan.result.path);
                        File.WriteAllLines(Evidence(directory, plan.result.path, "reloaded"), reloaded.raw);
                    }
                }
                report.state = "second-pass";
                var once = targets.ToDictionary(p => p, Hash, StringComparer.Ordinal);
                foreach (Plan plan in plans)
                {
                    using (var stage = new Stage(plan.result.path, plan.prefab))
                    {
                        if (!plan.prefab) { lastOwnedScene = stage; lastOwnedSceneBefore = plan.original; }
                        Frame before = Capture(stage, Locate(stage));
                        var repeated = new TargetResult { path = stage.path };
                        Apply(stage, Locate(stage), repeated);
                        Verify(before, Capture(stage, Locate(stage)));
                        if (repeated.meshChanges != 0 || repeated.materialChanges != 0 || repeated.equipmentPairsAdded != 0 ||
                            (!stage.prefab && stage.scene.isDirty))
                            throw new InvalidOperationException("Second application is not a true zero-write no-op: " + stage.path);
                        plan.result.secondPassSha = Hash(stage.path);
                        if (plan.result.secondPassSha != once[stage.path])
                            throw new InvalidOperationException("Second pass changed target bytes: " + stage.path);
                    }
                    CheckTargets(expected); CheckFrozen(frozen, materialMemory);
                }
                report.secondPassByteIdentical = targets.All(p => Hash(p) == once[p]);
                File.WriteAllLines(Path.Combine(directory, "targets-first-pass.txt"), once.Select(p => p.Value + " " + p.Key));
                File.WriteAllLines(Path.Combine(directory, "targets-second-pass.txt"), targets.Select(p => Hash(p) + " " + p));
                report.protectedSourcesSame = true; report.state = "completed";
            }
            catch (Exception error) { report.state = "failed"; report.error = error.ToString(); throw; }
            finally
            {
                bool bodyCompleted = report.state == "completed";
                bool targetsSame = false;
                try { CheckFrozen(frozen, materialMemory); report.protectedSourcesSame = true; }
                catch (Exception error) { report.protectedSourcesSame = false; report.error += "\nFinal protected source check: " + error; }
                try { CheckTargets(expected); targetsSame = true; }
                catch (Exception error) { report.error += "\nFinal target byte check: " + error; }
                // Never reopen/discard a dirty real scene until its existing full
                // serialization has been compared to our known baseline. Legitimate
                // own whitelisted edits are discardable; unexplained changes are not.
                if (lastOwnedScene != null && lastOwnedScene.scene.IsValid() && lastOwnedScene.scene.isLoaded && lastOwnedScene.scene.isDirty)
                {
                    try { Verify(lastOwnedSceneBefore, Capture(lastOwnedScene, Locate(lastOwnedScene))); }
                    catch (Exception error) { restoreSafe = false; report.error += "\nUnexpected dirty scene retained: " + error; }
                }
                if (restoreSafe)
                {
                    try
                    {
                        EditorSceneManager.RestoreSceneManagerSetup(previous);
                        report.originalSetupRestored = SceneManager.sceneCount == 1 && SceneManager.GetActiveScene().path == MainScene && !SceneManager.GetActiveScene().isDirty;
                    }
                    catch (Exception error) { report.error += "\nScene restore: " + error; }
                }
                else report.error += "\nUnexpected dirty hierarchy retained. No backup auto-restored; no possible user changes discarded.";
                if (!report.protectedSourcesSame || !targetsSame || !report.originalSetupRestored)
                    report.state = "failed";
                File.WriteAllText(Path.Combine(directory, "report.json"), JsonUtility.ToJson(report, true));
                if (bodyCompleted && report.state != "completed")
                    throw new InvalidOperationException("Final frozen/target/setup gate failed; backups and raw report retained. " + report.error);
            }
        }

        static void Apply(Stage stage, Targets targets, TargetResult result)
        {
            foreach (GameObject root in targets.wardens)
            {
                if (root.GetComponent<NetworkWarden>() != null)
                {
                    var equipment = WardenNetworkEquipmentSetup.Apply(root);
                    if (equipment.created) result.equipmentPairsAdded++;
                }
                var mapped = WardenSilhouetteSelection.Apply(root);
                result.meshChanges += mapped.meshChanges; result.materialChanges += mapped.materialChanges;
            }
        }

        static Targets Locate(Stage stage)
        {
            var result = new Targets();
            result.wardens = stage.prefab ? stage.roots : stage.roots.SelectMany(root =>
                root.GetComponentsInChildren<WardenActor>(true).Select(a => a.gameObject)
                    .Concat(root.GetComponentsInChildren<NetworkWarden>(true).Select(a => a.gameObject))).Distinct().ToArray();
            foreach (GameObject root in result.wardens)
            {
                Transform[] all = root.GetComponentsInChildren<Transform>(true);
                Transform helmet = SingleNamed(all, WardenSilhouetteSetup.HelmetNode);
                bool canonical = stage.prefab && stage.path == WardenSilhouetteSetup.CanonicalWeaponPath;
                bool characterOnly = stage.prefab && stage.path == Character;
                if (!canonical && helmet == null) throw new InvalidDataException("Missing exact Warden helmet: " + root.name);
                if (helmet != null) result.filters.Add(OneFilter(helmet, 1));
                Transform sword = canonical ? root.transform : SingleNamed(all, WardenSilhouetteSetup.SwordNode);
                NetworkWarden network = root.GetComponent<NetworkWarden>();
                if (network != null) result.network.Add(network);
                if (sword == null && network == null && !characterOnly) throw new InvalidDataException("Missing exact offline/canonical Warden sword: " + root.name);
                if (sword != null)
                {
                    Transform model = sword.Find("Model");
                    if (model == null) throw new InvalidDataException("Missing exact Warden sword Model.");
                    MeshFilter filter = OneFilter(model, 3); result.filters.Add(filter); result.firstSlots.Add(filter.GetComponent<MeshRenderer>());
                }
                ValidateNetworkShape(root, network);
            }
            return result;
        }

        static void ValidateNetworkShape(GameObject root, NetworkWarden network)
        {
            if (network == null) return;
            string[] names = { WardenNetworkEquipmentSetup.SwordSocketName, WardenNetworkEquipmentSetup.ShieldSocketName,
                WardenNetworkEquipmentSetup.SwordName, WardenNetworkEquipmentSetup.ShieldName };
            var all = root.GetComponentsInChildren<Transform>(true);
            int[] counts = names.Select(n => all.Count(t => t.name == n)).ToArray();
            if (!counts.All(c => c == 0) && !counts.All(c => c == 1))
                throw new InvalidDataException("Unknown/partial NET equipment; no objects will be deleted.");
            var fields = new SerializedObject(network);
            Object[] values = new[] { "_weaponRenderer", "_shieldRenderer", "_shieldVisualRoot" }
                .Select(f => fields.FindProperty(f).objectReferenceValue).ToArray();
            if (counts.All(c => c == 0) && values.Any(v => v != null))
                throw new InvalidDataException("NET equipment references exist without the exact equipment nodes.");
        }

        static bool NeedsSelection(Targets targets)
        {
            bool needed = false;
            foreach (MeshFilter filter in targets.filters)
            {
                bool helmet = filter.GetComponentsInParent<Transform>(true).Any(t => t.name == WardenSilhouetteSetup.HelmetNode);
                string original = helmet ? WardenSilhouetteSetup.ReferenceHelmetPath : WardenSilhouetteSetup.ReferenceSwordPath;
                string selected = helmet ? WardenSilhouetteSetup.HelmetMeshPath : WardenSilhouetteSetup.SwordMeshPath;
                string current = AssetDatabase.GetAssetPath(filter.sharedMesh);
                if (current != original && current != selected) throw new InvalidDataException("Unexpected Warden mesh source: " + current);
                if (current != selected) needed = true;
            }
            foreach (MeshRenderer renderer in targets.firstSlots)
            {
                string path = AssetDatabase.GetAssetPath(renderer.sharedMaterials[0]);
                if (path != WardenSilhouetteSelection.OriginalBladePath && path != WardenSilhouetteSelection.SelectedBladePath)
                    throw new InvalidDataException("Unexpected first sword slot: " + path);
                if (path != WardenSilhouetteSelection.SelectedBladePath) needed = true;
            }
            foreach (NetworkWarden network in targets.network)
            {
                var fields = new SerializedObject(network);
                if (new[] { "_weaponRenderer", "_shieldRenderer", "_shieldVisualRoot" }.Any(f => fields.FindProperty(f).objectReferenceValue == null))
                    needed = true;
            }
            return needed;
        }

        static Frame Capture(Stage stage, Targets allowed)
        {
            Transform[] transforms = stage.roots.SelectMany(r => r.GetComponentsInChildren<Transform>(true)).ToArray();
            var identities = new Dictionary<int, string>();
            foreach (Transform t in transforms)
            {
                string key = PathOf(t); identities.Add(t.gameObject.GetInstanceID(), key + "|GO");
                Component[] components = t.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    if (components[i] == null) throw new InvalidDataException("Missing script at " + key);
                    identities.Add(components[i].GetInstanceID(), key + "|C" + i + "|" + components[i].GetType().FullName);
                }
            }
            Func<string, string> normalize = json => Regex.Replace(json, "\"instanceID\"\\s*:\\s*(-?\\d+)", match =>
            {
                int instance = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                return "\"stableReference\":\"" + Escape(ReferenceIdentity(instance, identities)) + "\"";
            });
            var frame = new Frame { nodes = new Dictionary<string, Node>(StringComparer.Ordinal), permittedAdded = KnownEquipmentPaths(allowed) };
            var raw = new List<string>();
            foreach (Transform t in transforms)
            {
                string key = PathOf(t);
                Component[] components = t.GetComponents<Component>();
                var node = new Node {
                    id = key, parent = t.parent == null ? "null" : PathOf(t.parent),
                    gameObjectJson = normalize(EditorJsonUtility.ToJson(t.gameObject)),
                    children = Enumerable.Range(0, t.childCount).Select(i => PathOf(t.GetChild(i))).ToArray(),
                    componentTypes = components.Select(c => c.GetType().FullName).ToArray(),
                    componentJson = components.Select(component =>
                    {
                        string json = EditorJsonUtility.ToJson(component);
                        if (component is MeshFilter filter && allowed.filters.Contains(filter))
                        {
                            if (!Regex.IsMatch(json, "\"m_Mesh\"\\s*:")) throw new InvalidDataException("Mesh serialization contract unavailable.");
                            json = Regex.Replace(json, "\"m_Mesh\"\\s*:\\s*\\{[^}]*\\}", "\"m_Mesh\":{}");
                        }
                        if (component is MeshRenderer renderer && allowed.firstSlots.Contains(renderer))
                        {
                            if (!Regex.IsMatch(json, "\"m_Materials\"\\s*:\\s*\\[")) throw new InvalidDataException("Material serialization contract unavailable.");
                            json = new Regex("(\"m_Materials\"\\s*:\\s*\\[\\s*)\\{[^}]*\\}").Replace(json, "$1{}", 1);
                        }
                        if (component is NetworkWarden network && allowed.network.Contains(network))
                            foreach (string field in new[] { "_weaponRenderer", "_shieldRenderer", "_shieldVisualRoot" })
                            {
                                if (!Regex.IsMatch(json, "\"" + field + "\"\\s*:")) throw new InvalidDataException("NET reference whitelist unavailable: " + field);
                                json = Regex.Replace(json, "\"" + field + "\"\\s*:\\s*\\{[^}]*\\}", "\"" + field + "\":{}");
                            }
                        return normalize(json);
                    }).ToArray()
                };
                if (frame.nodes.ContainsKey(key)) throw new InvalidDataException("Ambiguous stable hierarchy key: " + key);
                frame.nodes.Add(key, node);
                raw.Add(key + "|GO|" + node.gameObjectJson);
                raw.Add(key + "|PARENT|" + node.parent);
                raw.Add(key + "|CHILDREN|" + string.Join(";", node.children));
                for (int i = 0; i < components.Length; i++)
                {
                    // Evidence retains the actual selected references; only the
                    // separately compared freeze frame masks permitted exact slots.
                    raw.Add(key + "|C" + i + "|" + node.componentTypes[i] + "|" + normalize(EditorJsonUtility.ToJson(components[i])));
                    raw.Add(key + "|FREEZE-C" + i + "|" + node.componentJson[i]);
                }
            }
            frame.physics = CapturePhysics(stage, normalize);
            raw.Add("PHYSICS|" + frame.physics);
            frame.sceneGlobals = CaptureSceneGlobals(stage, identities);
            raw.Add("SCENE-GLOBALS|" + frame.sceneGlobals);
            raw.Sort(StringComparer.Ordinal); frame.raw = raw.ToArray();
            return frame;
        }

        static string CaptureSceneGlobals(Stage stage, Dictionary<int, string> identities)
        {
            // Prefab edit scenes do not own the active scene's native globals.
            if (stage.prefab) return "prefab-edit-scene: no scene-global claim";
            if (SceneManager.GetActiveScene() != stage.scene)
                throw new InvalidOperationException("Cannot inspect globals for a non-active production scene: " + stage.path);
            Func<Object, string> reference = value => value == null ? "null" : ReferenceIdentity(value.GetInstanceID(), identities);
            LightmapData[] maps = LightmapSettings.lightmaps ?? Array.Empty<LightmapData>();
            var probe = RenderSettings.ambientProbe;
            // Both public property variants are already used by the verified
            // dressing review; no private native API or unsafe LightingSettings getter.
            var reflectionProperty = typeof(RenderSettings).GetProperty("customReflectionTexture", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                ?? typeof(RenderSettings).GetProperty("customReflection", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            if (reflectionProperty == null) throw new InvalidOperationException("Public custom-reflection inspection is unavailable.");
            string yaml = File.ReadAllText(stage.path);
            return JsonUtility.ToJson(new SceneGlobalsSnapshot
            {
                target = stage.path, fog = RenderSettings.fog, fogMode = RenderSettings.fogMode.ToString(),
                fogColor = RenderSettings.fogColor, fogStart = RenderSettings.fogStartDistance,
                fogEnd = RenderSettings.fogEndDistance, fogDensity = RenderSettings.fogDensity,
                ambientMode = RenderSettings.ambientMode.ToString(), ambientSky = RenderSettings.ambientSkyColor,
                ambientEquator = RenderSettings.ambientEquatorColor, ambientGround = RenderSettings.ambientGroundColor,
                ambientLight = RenderSettings.ambientLight, ambientIntensity = RenderSettings.ambientIntensity,
                subtractiveShadowColor = RenderSettings.subtractiveShadowColor,
                ambientProbe = Enumerable.Range(0, 27).Select(i => probe[i / 9, i % 9]).ToArray(),
                skybox = reference(RenderSettings.skybox), sun = reference(RenderSettings.sun),
                customReflection = reference(reflectionProperty.GetValue(null) as Object),
                defaultReflectionMode = RenderSettings.defaultReflectionMode.ToString(),
                defaultReflectionResolution = RenderSettings.defaultReflectionResolution,
                reflectionIntensity = RenderSettings.reflectionIntensity, reflectionBounces = RenderSettings.reflectionBounces,
                haloStrength = RenderSettings.haloStrength, flareStrength = RenderSettings.flareStrength, flareFadeSpeed = RenderSettings.flareFadeSpeed,
                lightmapsMode = LightmapSettings.lightmapsMode.ToString(),
                lightmaps = maps.Select(m => m == null ? "null-entry" : reference(m.lightmapColor) + "|" + reference(m.lightmapDir) + "|" + reference(m.shadowMask)).ToArray(),
                // These are the actual Unity YAML native classes in all four
                // production scenes. Retain every field, including GI/bake settings
                // and unassigned lighting references, across a save/reopen cycle.
                persistedRenderSettings = NativeLightingBlock(yaml, 104, "RenderSettings"),
                persistedLightmapSettings = NativeLightingBlock(yaml, 157, "LightmapSettings")
            });
        }

        static string NativeLightingBlock(string yaml, int classId, string name)
        {
            MatchCollection blocks = Regex.Matches(yaml, @"(?ms)^--- !u!" + classId + @" &-?\d+\r?\n" + name + @":\r?\n.*?(?=^--- !u!|\z)");
            if (blocks.Count != 1) throw new InvalidDataException("Expected one serialized native " + name + " block.");
            return blocks[0].Value;
        }

        static string CapturePhysics(Stage stage, Func<string, string> normalize)
        {
            // Normalize each component before embedding its JSON in the report.
            // Normalizing the outer report instead would leave escaped instanceIDs
            // inside serialized strings and falsely fail the saved/reloaded comparison.
            Physics.SyncTransforms();
            Component[] all = stage.roots.SelectMany(root => root.GetComponentsInChildren<Component>(true)).ToArray();
            var components = all.Where(c => c is Collider || c is NavMeshObstacle || c is NavMeshSurface)
                .OrderBy(c => PathOf(c.transform) + "|" + c.GetType().FullName, StringComparer.Ordinal);
            return JsonUtility.ToJson(new PhysicsSnapshot
            {
                target = stage.path,
                components = components.Select(c => new PhysicsRow
                {
                    path = PathOf(c.transform), type = c.GetType().FullName,
                    serialized = normalize(EditorJsonUtility.ToJson(c)),
                    localToWorld = c.transform.localToWorldMatrix, active = c.gameObject.activeInHierarchy,
                    boundsCenter = c is Collider collider ? collider.bounds.center : Vector3.zero,
                    boundsSize = c is Collider bounds ? bounds.bounds.size : Vector3.zero
                }).ToArray(),
                navigation = all.OfType<NavMeshSurface>().Select(surface =>
                {
                    string path = AssetDatabase.GetAssetPath(surface.navMeshData);
                    return string.IsNullOrEmpty(path) ? "null" : path + ":" + Hash(path);
                }).OrderBy(value => value, StringComparer.Ordinal).ToArray()
            });
        }

        static HashSet<string> KnownEquipmentPaths(Targets targets)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (NetworkWarden network in targets.network)
            {
                Animator[] animators = network.GetComponentsInChildren<Animator>(true);
                if (animators.Length != 1 || !animators[0].isHuman || animators[0].avatar == null || !animators[0].avatar.isValid)
                    throw new InvalidDataException("NET Warden needs its one existing valid Human Avatar.");
                Animator animator = animators[0];
                foreach (var pair in new[] {
                    new { bone = HumanBodyBones.RightHand, socket = WardenNetworkEquipmentSetup.SwordSocketName, gear = WardenNetworkEquipmentSetup.SwordName },
                    new { bone = HumanBodyBones.LeftHand, socket = WardenNetworkEquipmentSetup.ShieldSocketName, gear = WardenNetworkEquipmentSetup.ShieldName } })
                {
                    Transform bone = animator.GetBoneTransform(pair.bone);
                    if (bone == null) throw new InvalidDataException("Missing explicit hand: " + pair.bone);
                    Transform[] sockets = Enumerable.Range(0, bone.childCount).Select(bone.GetChild).Where(t => t.name == pair.socket).ToArray();
                    if (sockets.Length == 0) continue;
                    if (sockets.Length != 1 || sockets[0].childCount != 1 || sockets[0].GetChild(0).name != pair.gear)
                        throw new InvalidDataException("Unexpected exact NET equipment socket subtree.");
                    foreach (Transform item in sockets[0].GetComponentsInChildren<Transform>(true))
                    {
                        if (GameObjectUtility.GetStaticEditorFlags(item.gameObject) != 0 || item.GetComponents<Component>().Any(c =>
                            c == null || (!(c is Transform) && !(c is MeshFilter) && !(c is MeshRenderer))))
                            throw new InvalidDataException("New equipment must be render-only, non-Static and script-free.");
                        paths.Add(PathOf(item));
                    }
                }
            }
            return paths;
        }

        static void Verify(Frame before, Frame after)
        {
            if (before == null) throw new InvalidOperationException("Missing baseline frame; cannot discard or certify state.");
            foreach (var row in before.nodes)
            {
                Node current;
                if (!after.nodes.TryGetValue(row.Key, out current)) throw new InvalidOperationException("Old hierarchy node removed/renamed/reordered: " + row.Key);
                Node original = row.Value;
                if (current.parent != original.parent || current.gameObjectJson != original.gameObjectJson ||
                    !current.componentTypes.SequenceEqual(original.componentTypes) || !current.componentJson.SequenceEqual(original.componentJson))
                    throw new InvalidOperationException("Old serialized GameObject/component/parent changed outside exact whitelist: " + row.Key);
                if (current.children.Length < original.children.Length || !current.children.Take(original.children.Length).SequenceEqual(original.children))
                    throw new InvalidOperationException("Old child order changed: " + row.Key);
                foreach (string added in current.children.Skip(original.children.Length))
                    if (!after.permittedAdded.Contains(added)) throw new InvalidOperationException("Unapproved child appended: " + added);
            }
            foreach (string added in after.nodes.Keys.Except(before.nodes.Keys))
                if (!after.permittedAdded.Contains(added)) throw new InvalidOperationException("Unapproved new node: " + added);
            if (before.physics != after.physics) throw new InvalidOperationException("Physics/navigation-affecting serialized state changed.");
            if (before.sceneGlobals != after.sceneGlobals) throw new InvalidOperationException("Protected scene render/lightmap globals changed.");
        }

        static Dictionary<string, string> ProtectedFiles(string[] targets)
        {
            var outputs = new HashSet<string>(targets, StringComparer.Ordinal);
            var explicitInputs = new[] {
                WardenSilhouetteSetup.HelmetSourcePath, WardenSilhouetteSetup.SwordSourcePath, WardenSilhouetteSetup.ReferenceHelmetPath,
                WardenSilhouetteSetup.ReferenceSwordPath, WardenSilhouetteSetup.LicensePath, WardenSilhouetteSetup.ReferencePack + "/KnightCharacter.fbx",
                WardenSilhouetteSetup.ReferencePack + "/ShoulderPads.fbx", WardenSilhouetteSetup.HelmetMeshPath, WardenSilhouetteSetup.SwordMeshPath,
                WardenSilhouetteSelection.OriginalBladePath, WardenSilhouetteSelection.SelectedBladePath, WardenNetworkEquipmentSetup.ShieldPrefabPath
            };
            foreach (string input in explicitInputs)
                if (!File.Exists(input)) throw new FileNotFoundException("Required frozen presentation source is missing.", input);
            // Resource provenance documents are not necessarily Unity dependencies.
            // Freeze any supplied license/readme/manifests without inventing a license.
            string[] provenance = Directory.GetFiles("Assets/_Game/Art/DownloadResources/UnityFreeAssets/01_Warden_Boss", "*", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles("Assets/_Game/Art/DownloadResources/UnityFreeAssets/03_Character_Kit/KayKit_Adventurers", "*", SearchOption.AllDirectories))
                .Where(p => new[] { ".txt", ".md", ".json" }.Contains(Path.GetExtension(p).ToLowerInvariant())).ToArray();
            var paths = targets.Concat(explicitInputs).SelectMany(p => AssetDatabase.GetDependencies(p, true)).Concat(explicitInputs).Concat(provenance)
                .Concat(Directory.GetFiles("Assets/_Game/Settings", "*", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles("ProjectSettings", "*", SearchOption.AllDirectories))
                .Where(p => File.Exists(p) && !outputs.Contains(p));
            paths = paths.SelectMany(p => File.Exists(p + ".meta") ? new[] { p, p + ".meta" } : new[] { p })
                .Concat(targets.Select(p => p + ".meta")).Distinct().OrderBy(p => p, StringComparer.Ordinal);
            return paths.ToDictionary(p => p, Hash, StringComparer.Ordinal);
        }
        static Dictionary<Material, string> MaterialState(IEnumerable<string> frozen)
        {
            var values = new Dictionary<Material, string>();
            foreach (string path in frozen.Where(p => p.EndsWith(".mat", StringComparison.Ordinal)))
            {
                Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) throw new InvalidDataException("Protected material is unavailable: " + path);
                values.Add(material, EditorJsonUtility.ToJson(material));
            }
            return values;
        }
        static void CheckFrozen(Dictionary<string, string> frozen, Dictionary<Material, string> materials)
        {
            foreach (var row in frozen)
                if (!File.Exists(row.Key) || Hash(row.Key) != row.Value) throw new InvalidOperationException("Protected file changed; no backup auto-restore: " + row.Key);
            foreach (var row in materials)
                if (row.Key == null || EditorJsonUtility.ToJson(row.Key) != row.Value)
                    throw new InvalidOperationException("Protected material changed in memory; do not save shared assets.");
        }
        static void CheckTargets(Dictionary<string, string> expected)
        {
            foreach (var row in expected) if (Hash(row.Key) != row.Value)
                throw new InvalidOperationException("Target changed outside this job; preserve external changes: " + row.Key);
        }
        static void Save(Stage stage)
        {
            if (stage.prefab)
            {
                if (PrefabUtility.SaveAsPrefabAsset(stage.roots[0], stage.path) == null) throw new IOException("Prefab save returned null: " + stage.path);
            }
            else if (!EditorSceneManager.SaveScene(stage.scene, stage.path)) throw new IOException("Scene save failed: " + stage.path);
        }
        static void ValidatePreparedSelection()
        {
            Mesh helmet = AssetDatabase.LoadAssetAtPath<Mesh>(WardenSilhouetteSetup.HelmetMeshPath);
            Mesh sword = AssetDatabase.LoadAssetAtPath<Mesh>(WardenSilhouetteSetup.SwordMeshPath);
            Material blade = AssetDatabase.LoadAssetAtPath<Material>(WardenSilhouetteSelection.SelectedBladePath);
            if (helmet == null || helmet.vertexCount != 610 || helmet.subMeshCount != 1 ||
                sword == null || sword.vertexCount != 417 || sword.subMeshCount != 3 ||
                blade == null || blade.shader == null || blade.shader.name != "Universal Render Pipeline/Lit")
                throw new InvalidDataException("Prepare and render-review the complete selection before production migration.");
        }
        static Transform SingleNamed(Transform[] all, string name)
        {
            var values = all.Where(t => t.name == name).ToArray();
            if (values.Length > 1) throw new InvalidDataException("Ambiguous exact Warden node: " + name);
            return values.Length == 0 ? null : values[0];
        }
        static MeshFilter OneFilter(Transform root, int slots)
        {
            var values = root.GetComponentsInChildren<MeshFilter>(true);
            if (values.Length != 1 || values[0].sharedMesh == null || values[0].GetComponent<MeshRenderer>() == null ||
                values[0].GetComponent<MeshRenderer>().sharedMaterials.Length != slots)
                throw new InvalidDataException("Unexpected exact Warden static mesh/material slots: " + root.name);
            return values[0];
        }
        static string PathOf(Transform transform)
        {
            string path = transform.GetSiblingIndex() + ":" + transform.name;
            while (transform.parent != null) { transform = transform.parent; path = transform.GetSiblingIndex() + ":" + transform.name + "/" + path; }
            return path;
        }
        static string ReferenceIdentity(int id, Dictionary<int, string> local)
        {
            if (id == 0) return "null";
            string identity; if (local.TryGetValue(id, out identity)) return identity;
            Object value = EditorUtility.InstanceIDToObject(id);
            if (value == null) throw new InvalidDataException("Unresolvable serialized reference: " + id);
            string guid; long fileId;
            if (AssetDatabase.TryGetGUIDAndLocalFileIdentifier(value, out guid, out fileId))
                return "asset:" + guid + ":" + fileId.ToString(CultureInfo.InvariantCulture) + ":" + value.GetType().FullName;
            throw new InvalidDataException("Nonpersistent reference outside protected hierarchy: " + value.name + "/" + value.GetType().FullName);
        }
        static string Escape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        static string Evidence(string directory, string path, string phase) => Path.Combine(directory, Path.GetFileNameWithoutExtension(path) + "-" + phase + ".txt");
        static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", ""); }
        static string Fresh()
        {
            string directory = Path.GetFullPath("Builds/ArtReview/warden-production/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            if (Directory.Exists(directory)) throw new IOException("Never overwrite migration evidence.");
            Directory.CreateDirectory(directory); return directory;
        }
        static void RequireInitialIdle()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                SceneManager.sceneCount != 1 || SceneManager.GetActiveScene().path != MainScene || SceneManager.GetActiveScene().isDirty)
                throw new InvalidOperationException("Requires idle Editor with only saved, clean 10_EmberValley; preserve user work.");
            // Test runners may restore memory before Unity flushes EditorSettings.
            // Reject a known stale baseline rather than hiding a delayed disk write.
            string settings = File.ReadAllText("ProjectSettings/EditorSettings.asset");
            Match enabled = Regex.Match(settings, @"(?m)^\s*m_EnterPlayModeOptionsEnabled:\s*([01])\s*$");
            Match options = Regex.Match(settings, @"(?m)^\s*m_EnterPlayModeOptions:\s*(\d+)\s*$");
            if (!enabled.Success || !options.Success ||
                (enabled.Groups[1].Value == "1") != EditorSettings.enterPlayModeOptionsEnabled ||
                int.Parse(options.Groups[1].Value, CultureInfo.InvariantCulture) != (int)EditorSettings.enterPlayModeOptions)
                throw new InvalidOperationException("Editor play options have not settled on disk; wait for the normal Editor settings write. No settings modified.");
        }
    }
}
