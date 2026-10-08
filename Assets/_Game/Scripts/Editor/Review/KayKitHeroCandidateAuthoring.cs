using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Isolated visual asset authoring, never a Player profile, controller or gameplay migration.</summary>
    public static class KayKitHeroCandidateAuthoring
    {
        public const float HeroVisualScale = .75f, SwordLength = .95f, ShieldLength = .60f;
        public const string CandidateRoot = "Assets/_Game/Art/Review/KayKitHero";
        const string Project = "E:/unityproject/3DRPGdemo";
        const string Pack = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/03_Character_Kit/KayKit_Adventurers";
        public const string Source = Pack + "/addons/kaykit_character_pack_adventures/Characters/fbx/Knight.fbx";
        const string Archive = "Builds/Delivery/ProjectEmberfall_0.9.6-presentation-20261003-r4.zip";
        const string ArchiveSha = "9EF0DB03551996772A066BE77C9DC1EE1BB8C8F743D196D36FE48C5D285E0860";
        const string Prefix = "Emberfall.KayKitHeroCandidateAuthoring.";
        const string Left = "Rig/root/hips/spine/chest/upperarm.l/lowerarm.l/wrist.l/hand.l/handslot.l";
        const string Right = "Rig/root/hips/spine/chest/upperarm.r/lowerarm.r/wrist.r/hand.r/handslot.r";
        static readonly string[] Equipment = { Left + "/1H_Sword_Offhand", Left + "/Badge_Shield", Left + "/Rectangle_Shield", Left + "/Round_Shield", Left + "/Spike_Shield", Right + "/1H_Sword", Right + "/2H_Sword" };
        static readonly string[] Limbs = { "root", "hips", "spine", "chest", "upperarm.l", "lowerarm.l", "wrist.l", "hand.l", "handslot.l", "upperarm.r", "lowerarm.r", "wrist.r", "hand.r", "handslot.r", "head", "upperleg.l", "lowerleg.l", "foot.l", "toes.l", "upperleg.r", "lowerleg.r", "foot.r", "toes.r", "kneeIK.l", "control-toe-roll.l", "control-heel-roll.l", "control-foot-roll.l", "heelIK.l", "IK-foot.l", "IK-toe.l", "kneeIK.r", "control-toe-roll.r", "control-heel-roll.r", "control-foot-roll.r", "heelIK.r", "IK-foot.r", "IK-toe.r", "elbowIK.l", "handIK.l", "elbowIK.r", "handIK.r" };
        static bool pending;
        [Serializable] sealed class Identity { public string path, guid; public long localId; }
        [Serializable] sealed class FileRow { public string path, sha256, guid; public long bytes; }
        [Serializable] sealed class Rest { public string path, parent; public int siblingIndex; public bool activeSelf; public Vector3 position, scale; public Quaternion rotation; }
        sealed class TransformWatch { public Transform target; public Rest rest; }
        sealed class RendererWatch { public Renderer target; public bool enabled; public Material[] materials; }
        [Serializable] sealed class SourceMaterial { public Identity identity; public string beforeJson, afterConstructionJson, afterCleanupJson; public bool unchanged; }
        sealed class SourceWatch { public Material target; public SourceMaterial row; }
        [Serializable] sealed class MaterialRow { public Identity source, owned; public string beforeFirstValidation, afterFirstValidation, beforeSecondValidation, afterSecondValidation, storedSha256, finalGiFlags; public bool fixedPoint, secondSaveBytesSame; }
        [Serializable] sealed class Gear { public string originalPath, socketPath, canonicalLongestAxis; public Identity mesh; public Vector3 attachmentPosition, sourceAttachmentScale, selectedScale, socketWorldScale, idleFarthestWorldVertex; public Quaternion attachmentRotation; public float originalCanonicalLength, selectedCanonicalWorldLength, idleFarthestDistanceFromWrapperOrigin; }
        [Serializable] sealed class RendererRow { public string path; public bool originalEnabled, selectedEnabled; public Identity originalMesh; public Identity[] originalSlots, selectedSlots; }
        [Serializable] sealed class ComponentRow { public string path, type; }
        [Serializable] sealed class Report
        {
            public string label, status, error, startedUtc, finishedUtc, directory, assetDirectory, prefabPath, module;
            public string scope = "Owned pure visual candidate ONLY. Source Knight bones/Avatar/meshes/atlas remain original. No profile, runtime references, derivatives, input, collision, knife/heal/combat/network/performance/build acceptance. Ranger/r4 remain production.";
            public string priorEvidence = "native-20261003-a1 FAILED source-material memory protection; unknown cause, no retroactive approval. native-material-20261003-a2 PASSED four protections; does not erase a1.";
            public string measurement = "Native Idle t=0 Manual Playables, no foot IK/root motion/lift; both BakeMesh modes plus renderer.localToWorldMatrix recorded, selected true supported by independent four-weight manual worldskin in Builds/ArtReview/kaykit-scale/20261003-0227-scale-contract/report.json (six bodies max difference <=2.984e-7m). Triangle-index vertices of six bodies plus helmet; cape excluded from height only, preserved. Static gear farthest drawn vertex measured from wrapper origin, not an Actor query-range target. Source rest restored before saving; no motion/contact/grip acceptance.";
            public bool sourcesUnchanged, sourceMaterialMemoryUnchanged, originalScenesUnchanged, originalBonesUnchanged, prefabSecondSaveBytesSame, enabledBuildSceneClosureUnchanged, candidateNotInEnabledSceneClosure;
            public float heroScale = HeroVisualScale, nativeIdleBodyHeight, candidateIdleBodyHeight, nativeBakeFalseHeight, candidateBakeFalseHeight, rawAnimatorPositionDrift, rawAnimatorRotationDrift;
            public Vector3 nativeIdleMin, nativeIdleMax, candidateIdleMin, candidateIdleMax, candidateBakeFalseMin, candidateBakeFalseMax;
            public int limbCount, mappedHumanBones, materialCount, nativeEquipmentDisabled, componentCount, monoBehaviourCount, colliderCount;
            public Identity avatar; public Rest[] bones; public string[] disabledEquipmentPaths, enabledSceneDependencies, gaps;
            public Gear[] gear; public RendererRow[] renderers; public MaterialRow[] materials; public SourceMaterial[] sourceMaterials; public ComponentRow[] components;
            public FileRow[] protectedBefore, protectedAfter, ownedFiles;
        }

        /// <summary>Queues one unique job. Duplicate labels return the retained record, never repeat writes.</summary>
        public static string Request(string uniqueLabel)
        {
            if (string.IsNullOrWhiteSpace(uniqueLabel) || !Regex.IsMatch(uniqueLabel, "^[A-Za-z0-9_-]{8,72}$")) throw new ArgumentException("Fresh ASCII label required.");
            string key = Prefix + uniqueLabel, prior = SessionState.GetString(key, "");
            if (prior.Length != 0) { if (!File.Exists(prior)) throw new IOException("Retained record missing: " + prior); return prior; }
            RequireIdle(); if (pending) throw new InvalidOperationException("One candidate authoring job at a time.");
            string active = SessionState.GetString(Prefix + "active", ""); if (File.Exists(active)) { var old = JsonUtility.FromJson<Report>(File.ReadAllText(active)); if (old.status == "queued" || old.status == "running") throw new InvalidOperationException("Inspect interrupted job first: " + active); }
            string assets = CandidateRoot + "/" + uniqueLabel, directory = Path.GetFullPath("Builds/ArtReview/kaykit-assembled/" + uniqueLabel);
            if (Directory.Exists(assets) || File.Exists(assets + ".meta") || Directory.Exists(directory)) throw new IOException("Fresh asset/evidence directories required; never overwrite.");
            Directory.CreateDirectory(directory); string record = directory + "/result.json";
            var report = new Report { label = uniqueLabel, status = "queued", startedUtc = DateTime.UtcNow.ToString("O"), directory = directory, assetDirectory = assets, prefabPath = assets + "/Review_Knight_Hero.prefab", module = typeof(KayKitHeroCandidateAuthoring).Assembly.ManifestModule.ModuleVersionId.ToString() };
            Write(record, report); SessionState.SetString(key, record); SessionState.SetString(Prefix + "active", record); pending = true;
            double deadline = EditorApplication.timeSinceStartup + 180; EditorApplication.CallbackFunction callback = null;
            callback = () => { if ((EditorApplication.isCompiling || EditorApplication.isUpdating) && EditorApplication.timeSinceStartup < deadline) return; EditorApplication.update -= callback;
                try { RequireIdle(); report.status = "running"; Write(record, report); Author(report); report.status = "completed"; }
                catch (Exception ex) { report.status = "failed"; report.error = ex.ToString(); Debug.LogException(ex); }
                finally { report.finishedUtc = DateTime.UtcNow.ToString("O"); Write(record, report); pending = false; SessionState.EraseString(Prefix + "active"); } };
            EditorApplication.update += callback; return record;
        }

        static void Author(Report report)
        {
            Scene[] scenes = OriginalScenes(); string sceneMemory = SceneMemory(scenes); string[] closure = BuildClosure();
            report.protectedBefore = Protected(); var sources = MaterialWatches(report.protectedBefore); report.sourceMaterials = sources.Select(s => s.row).ToArray();
            try
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? throw new InvalidOperationException("Installed URP Lit required.");
                Type validatorType = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("UnityEditor.Rendering.Universal.ShaderGUI.LitShader", false)).FirstOrDefault(t => t != null) ?? throw new InvalidOperationException("Installed official URP Lit ShaderGUI unavailable.");
                object validator = Activator.CreateInstance(validatorType, true); MethodInfo validate = validatorType.GetMethod("ValidateMaterial", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(Material) }, null) ?? throw new MissingMethodException("Official ValidateMaterial(Material) unavailable.");
                var materialRows = new List<MaterialRow>(); report.materials = materialRows.ToArray();
                var preview = new PreviewRenderUtility();
                try
                {
                    GameObject model = preview.InstantiatePrefabInScene(Required<GameObject>(Source)); preview.camera.scene = model.scene;
                    GameObject wrapper = PreviewObject(preview, "Review_Knight_Hero"); model.transform.SetParent(wrapper.transform, false); wrapper.transform.localScale = Vector3.one * HeroVisualScale;
                    var transforms = model.GetComponentsInChildren<Transform>(true).Select(t => new TransformWatch { target = t, rest = RestOf(t, model.transform) }).ToArray();
                    var renderers = model.GetComponentsInChildren<Renderer>(true).Select(r => new RendererWatch { target = r, enabled = r.enabled, materials = r.sharedMaterials }).ToArray();
                    var bones = Limbs.Select(name => transforms.Single(t => t.target.name == name)).ToArray(); report.bones = bones.Select(b => b.rest).ToArray(); report.limbCount = bones.Length;
                    Animator animator = model.GetComponent<Animator>() ?? throw new InvalidOperationException("Native root Animator required.");
                    if (model.GetComponentsInChildren<Animator>(true).Length != 1 || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman || animator.runtimeAnimatorController != null) throw new InvalidOperationException("One valid original Avatar with no controller required.");
                    report.avatar = Id(animator.avatar); if (report.avatar.path != Source) throw new InvalidOperationException("Avatar must be embedded in this exact Knight source.");
                    report.mappedHumanBones = ((ModelImporter)AssetImporter.GetAtPath(Source)).humanDescription.human.Length;
                    ValidatePure(wrapper); EnsureFolder(report.assetDirectory); EnsureFolder(report.assetDirectory + "/Materials");
                    var materials = new Dictionary<Material, Material>();
                    foreach (Material original in renderers.SelectMany(r => r.materials).Distinct())
                    {
                        if (original == null) throw new InvalidOperationException("Native material slot cannot be null.");
                        var row = new MaterialRow { source = Id(original) }; materialRows.Add(row); report.materials = materialRows.ToArray();
                        var target = new Material(shader) { name = "M_Review_Knight_" + materialRows.Count.ToString("D2") };
                        try
                        {
                            string texProperty = original.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex"; Texture atlas = original.HasProperty(texProperty) ? original.GetTexture(texProperty) : null;
                            if (atlas == null || !AssetDatabase.GetAssetPath(atlas).StartsWith(Pack + "/", StringComparison.Ordinal)) throw new InvalidOperationException("Expected original licensed Knight atlas.");
                            target.SetTexture("_BaseMap", atlas); target.SetTextureScale("_BaseMap", original.GetTextureScale(texProperty)); target.SetTextureOffset("_BaseMap", original.GetTextureOffset(texProperty));
                            target.SetColor("_BaseColor", original.HasProperty("_BaseColor") ? original.GetColor("_BaseColor") : original.HasProperty("_Color") ? original.GetColor("_Color") : Color.white);
                            target.SetFloat("_Smoothness", original.HasProperty("_Smoothness") ? original.GetFloat("_Smoothness") : .15f); target.SetFloat("_Metallic", original.HasProperty("_Metallic") ? original.GetFloat("_Metallic") : 0);
                            target.SetColor("_EmissionColor", Color.black); target.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                            row.beforeFirstValidation = EditorJsonUtility.ToJson(target); validate.Invoke(validator, new object[] { target }); row.afterFirstValidation = EditorJsonUtility.ToJson(target);
                            string path = report.assetDirectory + "/Materials/" + target.name + ".mat"; AssetDatabase.CreateAsset(target, path); EditorUtility.SetDirty(target); AssetDatabase.SaveAssetIfDirty(target); row.owned = Id(target); row.storedSha256 = Hash(path);
                            row.beforeSecondValidation = EditorJsonUtility.ToJson(target); validate.Invoke(validator, new object[] { target }); row.afterSecondValidation = EditorJsonUtility.ToJson(target); row.fixedPoint = row.beforeSecondValidation == row.afterSecondValidation;
                            AssetDatabase.SaveAssetIfDirty(target); row.secondSaveBytesSame = Hash(path) == row.storedSha256; row.finalGiFlags = target.globalIlluminationFlags.ToString();
                            if (!row.fixedPoint || !row.secondSaveBytesSame || (target.globalIlluminationFlags & (MaterialGlobalIlluminationFlags.BakedEmissive | MaterialGlobalIlluminationFlags.RealtimeEmissive)) != 0) throw new InvalidOperationException("Owned material not an official no-GI fixed point: " + path);
                            materials.Add(original, target);
                        }
                        finally { if (!EditorUtility.IsPersistent(target)) Object.DestroyImmediate(target); }
                    }
                    report.materialCount = materials.Count;
                    report.renderers = renderers.Select(r => new RendererRow { path = PathOf(r.target.transform, model.transform), originalEnabled = r.enabled, selectedEnabled = Equipment.Contains(PathOf(r.target.transform, model.transform)) ? false : r.enabled, originalMesh = Id(r.target is SkinnedMeshRenderer skin ? skin.sharedMesh : r.target.GetComponent<MeshFilter>().sharedMesh), originalSlots = r.materials.Select(Id).ToArray(), selectedSlots = r.materials.Select(m => Id(materials[m])).ToArray() }).ToArray();
                    foreach (var r in renderers) { r.target.sharedMaterials = r.materials.Select(m => materials[m]).ToArray(); PrefabUtility.RecordPrefabInstancePropertyModifications(r.target); }
                    // Hide exactly these existing components; never remove/reorder native objects, bones or slots.
                    foreach (string path in Equipment) { var r = RequireTransform(model, path).GetComponent<MeshRenderer>() ?? throw new InvalidOperationException("Expected exact native static hand item: " + path); r.enabled = false; PrefabUtility.RecordPrefabInstancePropertyModifications(r); }
                    report.disabledEquipmentPaths = Equipment.ToArray(); report.nativeEquipmentDisabled = Equipment.Length;
                    report.gear = new[] { AddGear(preview, model, Right, "1H_Sword", "Review_SwordSocket", SwordLength), AddGear(preview, model, Left, "Badge_Shield", "Review_ShieldSocket", ShieldLength) };
                    MeasureIdle(wrapper, model, animator, transforms, report);
                    ValidateRest(model, transforms.Select(t => t.rest).ToArray()); report.originalBonesUnchanged = true;
                    ValidateRenderers(model, report.renderers);
                    report.components = ValidatePure(wrapper); report.componentCount = report.components.Length;
                    report.monoBehaviourCount = wrapper.GetComponentsInChildren<MonoBehaviour>(true).Length; report.colliderCount = wrapper.GetComponentsInChildren<Collider>(true).Length;
                    GameObject saved = PrefabUtility.SaveAsPrefabAsset(wrapper, report.prefabPath, out bool firstSuccess); if (!firstSuccess || saved == null) throw new IOException("Owned candidate prefab save failed.");
                    string prefabSha = Hash(report.prefabPath), prefabGuid = AssetDatabase.AssetPathToGUID(report.prefabPath);
                    PrefabUtility.SaveAsPrefabAsset(wrapper, report.prefabPath, out bool secondSuccess); report.prefabSecondSaveBytesSame = secondSuccess && Hash(report.prefabPath) == prefabSha && AssetDatabase.AssetPathToGUID(report.prefabPath) == prefabGuid;
                    if (!report.prefabSecondSaveBytesSame) throw new InvalidOperationException("Second owned prefab save not byte/GUID identical.");
                    var persistent = Required<GameObject>(report.prefabPath); ValidatePure(persistent); Transform storedModel = persistent.transform.Find(model.name) ?? throw new InvalidOperationException("Native model root lost.");
                    ValidateRest(storedModel.gameObject, transforms.Select(t => t.rest).ToArray()); ValidateRenderers(storedModel.gameObject, report.renderers); if (persistent.transform.localScale != Vector3.one * HeroVisualScale || !SameId(Id(storedModel.GetComponent<Animator>().avatar), report.avatar)) throw new InvalidOperationException("Saved scale/Avatar changed.");
                    foreach (var source in sources) source.row.afterConstructionJson = source.target == null ? "DESTROYED" : EditorJsonUtility.ToJson(source.target);
                }
                finally { preview.Cleanup(); }
                report.gaps = new[] { "No fingers in 41 native limbs; old Ranger16/Tiny7 grip tests unchanged and not universal candidate criteria. Palm/socket/projectile origin and actual knife readiness remain unverified.", "Fixed .75 visual height is not Actor collision/2.45m sector/attack-origin/grip alignment acceptance; no domain anchors were changed.", "Native Throw leg penetration and DeathA cape floor margin remain; legs/cape are preserved. No derivatives or root lift.", "Persistent candidate needs actual rendering and complete native/derived Knight motion plus real input gates. No build, managed inventory, r4 modification or production replacement claimed." };
            }
            finally
            {
                foreach (var source in sources) { source.row.afterCleanupJson = source.target == null ? "DESTROYED" : EditorJsonUtility.ToJson(source.target); source.row.unchanged = source.row.beforeJson == source.row.afterCleanupJson && (source.row.afterConstructionJson == null || source.row.beforeJson == source.row.afterConstructionJson); }
                report.protectedAfter = Protected(); report.sourcesUnchanged = JsonUtility.ToJson(new Files { rows = report.protectedBefore }) == JsonUtility.ToJson(new Files { rows = report.protectedAfter }); report.sourceMaterialMemoryUnchanged = sources.All(s => s.row.unchanged);
                report.originalScenesUnchanged = OriginalScenes().Select(s => s.handle).SequenceEqual(scenes.Select(s => s.handle)) && scenes.All(s => s.IsValid() && s.isLoaded && !s.isDirty) && SceneMemory(scenes) == sceneMemory;
                report.enabledSceneDependencies = BuildClosure(); report.enabledBuildSceneClosureUnchanged = closure.SequenceEqual(report.enabledSceneDependencies); report.candidateNotInEnabledSceneClosure = !report.enabledSceneDependencies.Any(p => p.StartsWith(report.assetDirectory + "/", StringComparison.Ordinal));
                if (Directory.Exists(report.assetDirectory)) report.ownedFiles = FilesIn(report.assetDirectory).Select(FileOf).ToArray();
                if (!report.sourcesUnchanged || !report.sourceMaterialMemoryUnchanged || !report.originalScenesUnchanged || !report.enabledBuildSceneClosureUnchanged || !report.candidateNotInEnabledSceneClosure) throw new InvalidOperationException("Protected source/material memory/scene/build-dependency closure changed; inspect retained per-file and per-material JSON.");
            }
        }

        static Gear AddGear(PreviewRenderUtility preview, GameObject model, string handPath, string item, string socketName, float length)
        {
            Transform hand = RequireTransform(model, handPath); if (model.GetComponentsInChildren<Transform>(true).Count(t => t.name == hand.name) != 1 || hand.Find(socketName) != null) throw new InvalidOperationException("Unique exact hand/socket required.");
            Transform original = RequireTransform(model, handPath + "/" + item); Mesh source = original.GetComponent<MeshFilter>()?.sharedMesh ?? throw new InvalidOperationException("Original hand-item MeshFilter required.");
            Vector3 size = source.bounds.size; int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2; float canonical = size[axis]; if (canonical <= .001f) throw new InvalidOperationException("Nondegenerate canonical weapon mesh required.");
            Vector3 parentScale = hand.lossyScale; if (parentScale.x <= 0 || Mathf.Abs(parentScale.x - parentScale.y) > .0001f || Mathf.Abs(parentScale.x - parentScale.z) > .0001f) throw new InvalidOperationException("Positive uniform hand-parent world scale required.");
            GameObject socket = PreviewObject(preview, socketName); socket.transform.SetParent(hand, false); socket.transform.localScale = Vector3.one / parentScale.x;
            GameObject visual = PreviewObject(preview, "Review_" + item); visual.transform.SetParent(socket.transform, false); visual.transform.localPosition = original.localPosition; visual.transform.localRotation = original.localRotation; visual.transform.localScale = Vector3.one * (length / canonical);
            visual.AddComponent<MeshFilter>().sharedMesh = source; visual.AddComponent<MeshRenderer>().sharedMaterials = original.GetComponent<MeshRenderer>().sharedMaterials;
            Vector3 world = socket.transform.lossyScale; float actual = canonical * visual.transform.lossyScale[axis];
            if ((world - Vector3.one).sqrMagnitude > .0001f * .0001f || Mathf.Abs(actual - length) > .001f) throw new InvalidOperationException("Metre socket/canonical-length contract failed.");
            return new Gear { originalPath = handPath + "/" + item, socketPath = handPath + "/" + socketName, mesh = Id(source), attachmentPosition = original.localPosition, attachmentRotation = original.localRotation, sourceAttachmentScale = original.localScale, selectedScale = visual.transform.localScale, socketWorldScale = world, canonicalLongestAxis = "XYZ"[axis].ToString(), originalCanonicalLength = canonical, selectedCanonicalWorldLength = actual };
        }
        static void MeasureIdle(GameObject wrapper, GameObject model, Animator animator, TransformWatch[] original, Report report)
        {
            bool rootMotion = animator.applyRootMotion; AnimatorCullingMode culling = animator.cullingMode; PlayableGraph graph = default;
            try
            {
                Vector3 anchor = animator.transform.localPosition; Quaternion rotation = animator.transform.localRotation; animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind(); animator.Update(0);
                AnimationClip idle = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<AnimationClip>().Single(c => c.name == "Idle"); graph = PlayableGraph.Create("KayKit isolated candidate Idle0"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual); var clip = AnimationClipPlayable.Create(graph, idle); clip.SetApplyFootIK(false); clip.SetApplyPlayableIK(false); AnimationPlayableOutput.Create(graph, "Native Idle0", animator).SetSourcePlayable(clip); graph.Play(); clip.SetTime(0); graph.Evaluate(0);
                report.rawAnimatorPositionDrift = Vector3.Distance(anchor, animator.transform.localPosition); report.rawAnimatorRotationDrift = Quaternion.Angle(rotation, animator.transform.localRotation);
                wrapper.transform.localScale = Vector3.one; Bounds native = BodyBounds(model, true); report.nativeIdleMin = native.min; report.nativeIdleMax = native.max; report.nativeIdleBodyHeight = native.size.y; report.nativeBakeFalseHeight = BodyBounds(model, false).size.y;
                wrapper.transform.localScale = Vector3.one * HeroVisualScale; Bounds selected = BodyBounds(model, true), alternative = BodyBounds(model, false); report.candidateIdleMin = selected.min; report.candidateIdleMax = selected.max; report.candidateIdleBodyHeight = selected.size.y; report.candidateBakeFalseHeight = alternative.size.y; report.candidateBakeFalseMin = alternative.min; report.candidateBakeFalseMax = alternative.max;
                foreach (Gear gear in report.gear) { Transform socket = RequireTransform(model, gear.socketPath); MeshFilter filter = socket.GetComponentInChildren<MeshFilter>(true); Vector3[] vertices = filter.sharedMesh.vertices; float max = -1; Vector3 farthest = default; for (int sub = 0; sub < filter.sharedMesh.subMeshCount; sub++) if (filter.sharedMesh.GetTopology(sub) == MeshTopology.Triangles) foreach (int index in filter.sharedMesh.GetIndices(sub)) { Vector3 world = filter.transform.localToWorldMatrix.MultiplyPoint3x4(vertices[index]); float distance = Vector3.Distance(world, wrapper.transform.position); if (distance > max) { max = distance; farthest = world; } } if (max < 0) throw new InvalidOperationException("Selected gear has no drawn geometry."); gear.idleFarthestWorldVertex = farthest; gear.idleFarthestDistanceFromWrapperOrigin = max; }
                if (Mathf.Abs(selected.size.y - 1.85f) > .05f) throw new InvalidOperationException("Fixed .75 candidate native Idle0 body/helmet height outside 1.85m ±.05; no automatic fit/lift permitted.");
            }
            finally { if (graph.IsValid()) graph.Destroy(); animator.applyRootMotion = rootMotion; animator.cullingMode = culling; foreach (var watch in original) { watch.target.localPosition = watch.rest.position; watch.target.localRotation = watch.rest.rotation; watch.target.localScale = watch.rest.scale; watch.target.gameObject.SetActive(watch.rest.activeSelf); } wrapper.transform.localScale = Vector3.one * HeroVisualScale; }
        }
        static Bounds BodyBounds(GameObject model, bool useScale)
        {
            string[] names = { "Knight_ArmLeft", "Knight_ArmRight", "Knight_Body", "Knight_Head", "Knight_LegLeft", "Knight_LegRight", "Knight_Helmet" }; Bounds bounds = default; bool any = false;
            foreach (string name in names)
            {
                Renderer r = model.GetComponentsInChildren<Renderer>(true).Single(p => p.name == name); if (!r.enabled || !r.gameObject.activeInHierarchy) throw new InvalidOperationException("Body/helmet must remain drawn."); Mesh scratch = null;
                try { Mesh mesh = r is SkinnedMeshRenderer skin ? skin.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh; if (r is SkinnedMeshRenderer sk) { scratch = new Mesh(); sk.BakeMesh(scratch, useScale); mesh = scratch; } if (mesh == null) throw new InvalidOperationException("Body mesh missing."); Vector3[] vertices = mesh.vertices; var indices = new HashSet<int>(); for (int i = 0; i < mesh.subMeshCount; i++) if (mesh.GetTopology(i) == MeshTopology.Triangles) foreach (int index in mesh.GetIndices(i)) indices.Add(index); if (indices.Count == 0) throw new InvalidOperationException("Body must have triangle indices."); foreach (int index in indices) { Vector3 p = r.localToWorldMatrix.MultiplyPoint3x4(vertices[index]); if (!any) { bounds = new Bounds(p, Vector3.zero); any = true; } else bounds.Encapsulate(p); } }
                finally { if (scratch != null) Object.DestroyImmediate(scratch); }
            }
            if (!any) throw new InvalidOperationException("No drawn body geometry."); return bounds;
        }
        static Rest RestOf(Transform t, Transform root) => new Rest { path = PathOf(t, root), parent = t == root ? "" : PathOf(t.parent, root), siblingIndex = t.GetSiblingIndex(), activeSelf = t.gameObject.activeSelf, position = t.localPosition, rotation = t.localRotation, scale = t.localScale };
        static void ValidateRest(GameObject model, Rest[] expected) { foreach (Rest row in expected) { Transform t = row.path == "" ? model.transform : RequireTransform(model, row.path); if (JsonUtility.ToJson(RestOf(t, model.transform)) != JsonUtility.ToJson(row)) throw new InvalidOperationException("Original path/rest changed: " + row.path); } }
        static void ValidateRenderers(GameObject model, RendererRow[] expected) { foreach (RendererRow row in expected) { Renderer r = RequireTransform(model, row.path).GetComponent<Renderer>() ?? throw new InvalidOperationException("Original Renderer missing."); Material[] slots = r.sharedMaterials; Mesh mesh = r is SkinnedMeshRenderer skin ? skin.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh; if (r.enabled != row.selectedEnabled || !SameId(Id(mesh), row.originalMesh) || slots.Length != row.originalSlots.Length || slots.Length != row.selectedSlots.Length || slots.Where((m, i) => !SameId(Id(m), row.selectedSlots[i])).Any()) throw new InvalidOperationException("Exact Renderer mesh/enabled/ordered-slot identity changed: " + row.path); } var nativePaths = new HashSet<string>(expected.Select(r => r.path), StringComparer.Ordinal); if (!model.GetComponentsInChildren<Renderer>(true).Select(r => PathOf(r.transform, model.transform)).Where(nativePaths.Contains).SequenceEqual(expected.Select(r => r.path)) || expected.Count(r => Equipment.Contains(r.path) && !r.selectedEnabled) != 7) throw new InvalidOperationException("Original Renderer order / seven hidden equipment contract changed."); }
        static ComponentRow[] ValidatePure(GameObject root) { var rows = new List<ComponentRow>(); foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) foreach (Component c in t.GetComponents<Component>()) { if (c == null || !(c is Transform || c is Animator || c is MeshFilter || c is MeshRenderer || c is SkinnedMeshRenderer)) throw new InvalidOperationException("Pure visual whitelist rejects: " + (c == null ? "missing script" : c.GetType().FullName)); rows.Add(new ComponentRow { path = PathOf(t, root.transform), type = c.GetType().FullName }); } return rows.ToArray(); }
        static GameObject PreviewObject(PreviewRenderUtility preview, string name) { var go = EditorUtility.CreateGameObjectWithHideFlags(name, HideFlags.HideAndDontSave); preview.AddSingleGO(go); go.hideFlags = HideFlags.None; return go; }
        static Transform RequireTransform(GameObject model, string path) => model.transform.Find(path) ?? throw new InvalidOperationException("Exact source path missing: " + path);
        static string PathOf(Transform t, Transform root) => AnimationUtility.CalculateTransformPath(t, root);
        static Identity Id(Object obj) { if (obj == null || !AssetDatabase.TryGetGUIDAndLocalFileIdentifier(obj, out string guid, out long id)) throw new InvalidOperationException("Persistent original/owned asset identity required."); return new Identity { path = AssetDatabase.GetAssetPath(obj), guid = guid, localId = id }; }
        static bool SameId(Identity a, Identity b) => a.path == b.path && a.guid == b.guid && a.localId == b.localId;
        static T Required<T>(string path) where T : Object => AssetDatabase.LoadAssetAtPath<T>(path) ?? throw new FileNotFoundException(path);
        static void EnsureFolder(string path) { if (AssetDatabase.IsValidFolder(path)) return; string parent = Path.GetDirectoryName(path)?.Replace('\\', '/'); if (string.IsNullOrEmpty(parent)) throw new IOException("Invalid owned directory."); EnsureFolder(parent); if (string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent, Path.GetFileName(path)))) throw new IOException("Could not create owned directory: " + path); }
        static void RequireIdle() { if (Path.GetFullPath(UnityEngine.Application.dataPath).Replace('\\', '/') != Project + "/Assets" || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || PrefabStageUtility.GetCurrentPrefabStage() != null) throw new InvalidOperationException("Correct idle project without PrefabStage required."); foreach (Scene scene in OriginalScenes()) if (scene.isDirty) throw new InvalidOperationException("Preserve dirty original scene: " + scene.path); if (Unity.Netcode.NetworkManager.Singleton != null && Unity.Netcode.NetworkManager.Singleton.IsListening) throw new InvalidOperationException("Preserve existing network session."); }
        static Scene[] OriginalScenes() { var scenes = new List<Scene>(); for (int i = 0; i < SceneManager.sceneCount; i++) { Scene scene = SceneManager.GetSceneAt(i); if (!EditorSceneManager.IsPreviewScene(scene)) scenes.Add(scene); } return scenes.ToArray(); }
        static string SceneMemory(Scene[] scenes) { var rows = new List<string> { "active=" + SceneManager.GetActiveScene().handle }; foreach (Scene scene in scenes) { if (!scene.IsValid() || !scene.isLoaded) throw new InvalidOperationException("Original scene lost."); foreach (GameObject root in scene.GetRootGameObjects()) foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) { rows.Add(t.GetInstanceID() + "|" + EditorJsonUtility.ToJson(t.gameObject)); foreach (Component c in t.GetComponents<Component>()) rows.Add(c == null ? "missing" : c.GetInstanceID() + "|" + EditorJsonUtility.ToJson(c)); } } rows.Sort(StringComparer.Ordinal); return string.Join("\n", rows); }
        static string[] BuildClosure() => EditorBuildSettings.scenes.Where(s => s.enabled).SelectMany(s => AssetDatabase.GetDependencies(s.path, true)).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();
        [Serializable] sealed class Files { public FileRow[] rows; }
        static FileRow[] Protected()
        {
            var paths = new HashSet<string>(StringComparer.Ordinal); Action<string> add = p => { p = p.Replace('\\', '/'); if (File.Exists(p)) { paths.Add(p); if (File.Exists(p + ".meta")) paths.Add(p + ".meta"); } };
            foreach (string path in new[] { Source, Pack + "/LICENSE.txt", Pack + "/README.md", Pack + "/addons/kaykit_character_pack_adventures/LICENSE.txt", "Assets/_Game/Scripts/Editor/Review/KayKitHeroCandidateAuthoring.cs" }) { add(path); foreach (string dep in AssetDatabase.GetDependencies(path, true)) add(dep); }
            foreach (string directory in new[] { "ProjectSettings", "Assets/_Game/Settings", "Assets/_Game/Scenes", "Assets/_Game/Prefabs/Characters", "Assets/_Game/Resources/Networking" }) foreach (string path in Directory.GetFiles(directory, "*", SearchOption.AllDirectories)) { add(path); if (path.EndsWith(".prefab", StringComparison.Ordinal) || path.EndsWith(".unity", StringComparison.Ordinal)) foreach (string dep in AssetDatabase.GetDependencies(path.Replace('\\', '/'), true)) add(dep); }
            foreach (string path in Directory.GetFiles("Assets/_Game/Scripts", "*", SearchOption.AllDirectories).Where(p => p.EndsWith(".asmdef", StringComparison.Ordinal) || p.EndsWith(".asmref", StringComparison.Ordinal))) add(path);
            foreach (string path in BuildClosure()) add(path); add(Archive); var result = paths.OrderBy(p => p, StringComparer.Ordinal).Select(FileOf).ToArray(); if (!result.Any(r => r.path == Archive && r.sha256 == ArchiveSha)) throw new InvalidOperationException("Delivered r4 ZIP missing/different."); return result;
        }
        static SourceWatch[] MaterialWatches(FileRow[] files) => files.Where(f => f.path.StartsWith("Assets/", StringComparison.Ordinal) && (f.path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || f.path.EndsWith(".fbx", StringComparison.OrdinalIgnoreCase))).SelectMany(f => AssetDatabase.LoadAllAssetsAtPath(f.path).OfType<Material>()).Distinct().Select(m => new SourceWatch { target = m, row = new SourceMaterial { identity = Id(m), beforeJson = EditorJsonUtility.ToJson(m) } }).ToArray();
        static string[] FilesIn(string directory) => Directory.GetFiles(directory, "*", SearchOption.AllDirectories).Concat(File.Exists(directory + ".meta") ? new[] { directory + ".meta" } : Array.Empty<string>()).Select(p => p.Replace('\\', '/')).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        static FileRow FileOf(string path) => new FileRow { path = path, bytes = new FileInfo(path).Length, sha256 = Hash(path), guid = path.StartsWith("Assets/", StringComparison.Ordinal) && !path.EndsWith(".meta", StringComparison.Ordinal) ? AssetDatabase.AssetPathToGUID(path) : "" };
        static string Hash(string path) { using (var stream = File.OpenRead(path)) using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", ""); }
        static void Write(string path, Report report) => File.WriteAllText(path, JsonUtility.ToJson(report, true), new UTF8Encoding(false));
    }
}
