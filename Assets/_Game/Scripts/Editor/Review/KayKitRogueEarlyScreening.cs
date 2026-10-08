using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
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
    /// <summary>New, isolated Rogue rig screening; never changes a production profile or original importer.</summary>
    [InitializeOnLoad]
    public static class KayKitRogueEarlyScreening
    {
        public const string Source = "Assets/_Game/Art/DownloadResources/UnityFreeAssets/03_Character_Kit/KayKit_Adventurers/addons/kaykit_character_pack_adventures/Characters/fbx/Rogue.fbx";
        const string Key = "Emberfall.RogueEarlyScreen.Pending";
        const string AnimationSet = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        [Serializable] sealed class FileRow { public string path, before, after; }
        [Serializable] sealed class SkinMinimum { public string renderer, dominantBone; public int vertex; public float y; public Vector3 point; }
        [Serializable] sealed class PoseRow { public string purpose, clip, sourcePath, phaseBasis; public float time, duration, fps, normalizedSourceTime; public int sample, sampleCount; public Vector3 animatorRoot, hips, head; public SkinMinimum[] minimum; }
        [Serializable] sealed class SkinContract { public string renderer; public float trueError, falseError; public bool useScale; }
        [Serializable] sealed class Probe { public string joint, skin; public int vertex; public float drivenVertexDistance; }
        [Serializable] sealed class MaterialMemory { public string path, guid, beforeJson, afterJson; public long localId; public bool unchanged; }
        [Serializable] sealed class Report
        {
            public string status, error, label, output, derivative, module, pipeline, sourceImporterJson, derivativeImporterJson;
            public string scope = "Isolated own Humanoid FBX plus controlled CPU-baked pose screening. Fixed Idle .25s body height calibration to 1.8m and one standing floor alignment ONLY; never re-fit/lift any action. Whole clip source seconds/frame rates, not domain timing, blended Animator, live GPU input, contact/grip, all abilities or production replacement. Ranger remains production. Native Rogue Idle and current Player Roll/Light1/offline Throw are distinct motions.";
            public bool avatarValid, avatarHuman, protectedFilesUnchanged, sourceMaterialMemoryUnchanged, originalSceneMemoryUnchanged, buildClosureUnchanged, derivativeOutsideBuildClosure;
            public float sourceIdleHeight, uniformScale, standingFloorTranslation;
            public string[] mappings, unmappedOptional, hierarchy, weightedControls, clips, images;
            public FileRow[] files; public PoseRow[] poses; public SkinContract[] skinContracts; public Probe[] probes; public MaterialMemory[] materialMemory;
        }
        static KayKitRogueEarlyScreening() { if (!string.IsNullOrEmpty(SessionState.GetString(Key, ""))) EditorApplication.update += Run; }

        public static string Request(string label)
        {
            if (Directory.Exists("Builds/ArtReview/rogue-early-screening/rogue-20261007-a1"))
                throw new InvalidOperationException("Current Player retarget combination is stopped; preserve a1. Native comparison is a DIFFERENT diagnostic entry point.");
            return Queue(label, null);
        }
        public static string RequestNative(string label, bool human) => Queue(label, human ? "native-human" : "native-generic");
        static string Queue(string label, string pipeline)
        {
            if (!Regex.IsMatch(label ?? "", "^[a-zA-Z0-9_-]{8,72}$")) throw new ArgumentException("Fresh ASCII label required.");
            RequireIdle();
            if (!string.IsNullOrEmpty(SessionState.GetString(Key, ""))) throw new InvalidOperationException("Inspect pending Rogue job, do not resubmit.");
            string output = Path.GetFullPath("Builds/ArtReview/" + (pipeline == null ? "rogue-early-screening/" : "rogue-native-comparison/") + label);
            string derivative = "Assets/_Game/Art/Review/KayKitRogue/" + label + "/Rogue_Review.fbx";
            if (Directory.Exists(output) || Directory.Exists(Path.GetDirectoryName(derivative))) throw new IOException("Preserve existing screening; fresh label required.");
            Directory.CreateDirectory(output);
            var report = new Report { status = "queued", label = label, output = output, derivative = derivative, pipeline = pipeline };
            if (pipeline != null) report.scope = "Two DIFFERENT isolated animation pipelines, each with its own native Idle .25s one-time 1.8m comparison calibration and standing floor alignment. Native Generic keeps original importer settings except readable; Human changes avatar mapping too. Whole native Dodge_Forward (NOT Roll), 1H Chop and Throw source frame samples. Root/hips/head and triangle minima use the SAME review world-space floor plane y=0, floor fixture top y=0. No per-pose lift/re-fit, root motion authority, runtime retiming, actual projectile release, equipment/grip, gameplay input or production acceptance. Numerical relative improvement alone cannot pass two visibly penetrating motions. No fresh absolute physical tolerance is invented; inspect full source ranges and actual CPU renders, keep visible intersection combinations out of production. Ranger unchanged.";
            Write(output + "/result.json", report);
            SessionState.SetString(Key, output + "/result.json"); EditorApplication.update -= Run; EditorApplication.update += Run;
            return output + "/result.json";
        }
        static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
            string path = SessionState.GetString(Key, ""); if (path.Length == 0) { EditorApplication.update -= Run; return; }
            var report = JsonUtility.FromJson<Report>(File.ReadAllText(path));
            // Consume before any importer operation; a transport retry cannot start this job again.
            SessionState.EraseString(Key); EditorApplication.update -= Run;
            try { RequireIdle(); report.status = "running"; Write(path, report); Screen(report); report.status = "screened"; }
            catch (Exception e) { report.status = "failed"; report.error = e.ToString(); Debug.LogException(e); }
            finally { Write(path, report); }
        }
        static void RequireIdle()
        {
            var scene = SceneManager.GetActiveScene();
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                SceneManager.sceneCount != 1 || scene.isDirty || scene.path != "Assets/_Game/Scenes/10_EmberValley.unity")
                throw new InvalidOperationException("One clean idle original Valley required.");
        }

        [Serializable] sealed class MaterialObservation { public string step; public MaterialMemory[] materials; }
        [Serializable] sealed class MaterialDiagnostic
        {
            public string scope = "Lifecycle observation of existing source and already authored derivative. No import/reimport, animation or intentional original material setters; records possible implicit source-memory mutations. Does not retrospectively explain/approve a1.";
            public string error; public bool sceneMemorySame; public FileRow[] files; public MaterialObservation[] observations;
        }
        public static string ObserveMaterialLifecycle(string label, bool serializedTextureRead = false)
        {
            RequireIdle(); if (!Regex.IsMatch(label ?? "", "^[a-zA-Z0-9_-]{8,72}$")) throw new ArgumentException("Fresh label required.");
            string folder=Path.GetFullPath("Builds/ArtReview/rogue-material-observation/"+label);if(Directory.Exists(folder))throw new IOException("Preserve prior observation.");Directory.CreateDirectory(folder);
            var scene=SceneManager.GetActiveScene();string beforeScene=Memory(scene);
            var watched=AssetDatabase.LoadAllAssetsAtPath(Source).OfType<Material>().Select(m=>{
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(m,out string guid,out long id);
                return (m,new MaterialMemory{path=AssetDatabase.GetAssetPath(m),guid=guid,localId=id,beforeJson=EditorJsonUtility.ToJson(m)});
            }).ToArray();
            var report=new MaterialDiagnostic();var observations=new List<MaterialObservation>();
            report.files=new[]{Source,Source+".meta"}.Select(p=>new FileRow{path=p,before=Hash(p)}).ToArray();
            Action<string> record=step=>observations.Add(new MaterialObservation{step=step,materials=watched.Select(p=>new MaterialMemory{path=p.Item2.path,guid=p.Item2.guid,localId=p.Item2.localId,beforeJson=p.Item2.beforeJson,
                afterJson=p.Item1==null?"DESTROYED":EditorJsonUtility.ToJson(p.Item1),unchanged=p.Item1!=null&&p.Item2.beforeJson==EditorJsonUtility.ToJson(p.Item1)}).ToArray()});
            PreviewRenderUtility preview=null;Material temporary=null;
            try
            {
                record("source-loaded-baseline");AssetDatabase.GetDependencies(Source,true);record("source-dependencies-read");
                preview=new PreviewRenderUtility();preview.InstantiatePrefabInScene(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Art/Review/KayKitRogue/rogue-20261007-a1/Rogue_Review.fbx"));record("existing-owned-derivative-instantiated");
                var shader=Shader.Find("Universal Render Pipeline/Lit");record("URP-shader-found");
                temporary=new Material(shader);record("transient-URP-material-constructed");
                var texture=serializedTextureRead?SerializedTexture(watched[0].Item1):watched[0].Item1.mainTexture;
                record(serializedTextureRead?"source-serialized-texture-reference-read":"source-mainTexture-getter");
                temporary.SetTexture("_BaseMap",texture);record("transient-owned-texture-assigned");
            }
            catch(Exception e){report.error=e.ToString();}
            finally
            {
                if(preview!=null)preview.Cleanup();if(temporary!=null)Object.DestroyImmediate(temporary);record("owned-preview-cleaned");
                foreach(var f in report.files)f.after=Hash(f.path);report.sceneMemorySame=!scene.isDirty&&Memory(scene)==beforeScene;
                report.observations=observations.ToArray();File.WriteAllText(folder+"/report.json",JsonUtility.ToJson(report,true));
            }
            return folder+"/report.json";
        }
        static HumanBone Map(string human, string bone) => new HumanBone { humanName = human, boneName = bone, limit = new HumanLimit { useDefaultValues = true } };
        static HumanBone[] Mapping() => new[] {
            Map("Hips","hips"), Map("Spine","spine"), Map("Chest","chest"), Map("Head","head"),
            Map("LeftUpperArm","upperarm.l"), Map("LeftLowerArm","lowerarm.l"), Map("LeftHand","hand.l"),
            Map("RightUpperArm","upperarm.r"), Map("RightLowerArm","lowerarm.r"), Map("RightHand","hand.r"),
            Map("LeftUpperLeg","upperleg.l"), Map("LeftLowerLeg","lowerleg.l"), Map("LeftFoot","foot.l"), Map("LeftToes","toes.l"),
            Map("RightUpperLeg","upperleg.r"), Map("RightLowerLeg","lowerleg.r"), Map("RightFoot","foot.r"), Map("RightToes","toes.r") };

        static void Screen(Report report)
        {
            report.module = typeof(KayKitRogueEarlyScreening).Assembly.ManifestModule.ModuleVersionId.ToString();
            Scene original = SceneManager.GetActiveScene(); string sceneMemory = Memory(original); string[] closure = Closure();
            var originals = AssetDatabase.LoadAllAssetsAtPath(Source).OfType<Material>().ToDictionary(m => m, EditorJsonUtility.ToJson);
            var materialRows = originals.ToDictionary(p => p.Key, p => {
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(p.Key, out string guid, out long localId);
                return new MaterialMemory { path=AssetDatabase.GetAssetPath(p.Key),guid=guid,localId=localId,beforeJson=p.Value };
            });
            report.materialMemory = materialRows.Values.ToArray();
            var paths = AssetDatabase.GetDependencies(Source, true).Concat(new[] { Source, AnimationSet,
                "Assets/_Game/Scenes/10_EmberValley.unity", "Assets/_Game/Settings/Navigation/EmberValleyNavMesh.asset",
                "Assets/_Game/Art/Characters/M3Art/Derived/Male_Ranger_Emberfall.fbx", "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab",
                "Assets/_Game/Art/DownloadResources/UnityFreeAssets/03_Character_Kit/KayKit_Adventurers/LICENSE.txt" });
            paths = paths.Concat(AssetDatabase.GetDependencies(AnimationSet, true));
            report.files = paths.Where(File.Exists).SelectMany(p => File.Exists(p + ".meta") ? new[] { p, p + ".meta" } : new[] { p })
                .Distinct().OrderBy(p => p).Select(p => new FileRow { path = p, before = Hash(p) }).ToArray();
            PreviewRenderUtility preview = null; PlayableGraph graph = default; Material bodyMaterial = null, floorMaterial = null;
            var poses = new List<PoseRow>(); var images = new List<string>();
            try
            {
                var source = AssetDatabase.LoadAssetAtPath<GameObject>(Source) ?? throw new FileNotFoundException(Source);
                var sourceImporter = (ModelImporter)AssetImporter.GetAtPath(Source);
                report.sourceImporterJson = EditorJsonUtility.ToJson(sourceImporter);
                report.hierarchy = source.GetComponentsInChildren<Transform>(true).Select(t => t.name + " <- " + (t.parent == null ? "none" : t.parent.name)).ToArray();
                foreach (var m in Mapping()) if (source.GetComponentsInChildren<Transform>(true).Count(t => t.name == m.boneName) != 1) throw new InvalidOperationException("Unique mapped bone required: " + m.boneName);
                EnsureFolder(Path.GetDirectoryName(report.derivative).Replace('\\', '/'));
                bool nativePipeline = !string.IsNullOrEmpty(report.pipeline);
                bool humanPipeline = report.pipeline != "native-generic";
                if (nativePipeline)
                {
                    if (!AssetDatabase.CopyAsset(Source, report.derivative)) throw new IOException("Own FBX/import-settings copy failed.");
                }
                else { File.Copy(Source, report.derivative, false); AssetDatabase.ImportAsset(report.derivative, ImportAssetOptions.ForceSynchronousImport); }
                var importer = (ModelImporter)AssetImporter.GetAtPath(report.derivative);
                importer.isReadable = true;
                if (humanPipeline)
                {
                    importer.globalScale = sourceImporter.globalScale; importer.bakeAxisConversion = sourceImporter.bakeAxisConversion;
                    importer.animationType = ModelImporterAnimationType.Human; importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                    importer.importAnimation = true; importer.optimizeGameObjects = false;
                    importer.clipAnimations = sourceImporter.clipAnimations.Length == 0 ? sourceImporter.defaultClipAnimations : sourceImporter.clipAnimations;
                    var description = importer.humanDescription; description.human = Mapping(); importer.humanDescription = description;
                }
                else if (importer.animationType != ModelImporterAnimationType.Generic) throw new InvalidOperationException("Actual copied original Generic importer required.");
                importer.SaveAndReimport();
                report.derivativeImporterJson = EditorJsonUtility.ToJson(importer);
                report.mappings = humanPipeline ? Mapping().Select(m => m.humanName + " => " + m.boneName).ToArray() : new[] { "No Human mapping; original exported Generic transform curves." };
                report.unmappedOptional = humanPipeline ? new[] { "Neck", "UpperChest", "LeftShoulder", "RightShoulder", "all fingers", "eyes/jaw" } : new[] { "Human features are NOT asserted by this Generic diagnostic." };
                var avatar = AssetDatabase.LoadAllAssetsAtPath(report.derivative).OfType<Avatar>().SingleOrDefault();
                report.avatarValid = avatar != null && avatar.isValid; report.avatarHuman = avatar != null && avatar.isHuman;
                if (humanPipeline && (!report.avatarValid || !report.avatarHuman)) throw new InvalidOperationException("Own Human derivative did not produce a valid Human Avatar.");
                preview = new PreviewRenderUtility();
                GameObject model = preview.InstantiatePrefabInScene(AssetDatabase.LoadAssetAtPath<GameObject>(report.derivative));
                var wrapper = PreviewObject(preview, "IsolatedRogue"); model.transform.SetParent(wrapper.transform, false);
                var animator = model.GetComponent<Animator>();
                // The original Generic FBX has no Animator component. Add the evaluation
                // adapter to this transient preview only; do not change its importer.
                if (animator == null && !humanPipeline) animator = model.AddComponent<Animator>();
                if (animator == null) throw new InvalidOperationException("Human derivative must supply its actual Animator.");
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; animator.Rebind(); animator.Update(0);
                // Keep source geometry/rest hierarchy. Hide only bundled alternative equipment, not cape or limbs.
                string[] equipment = { "Knife_Offhand", "1H_Crossbow", "2H_Crossbow", "Knife", "Throwable" };
                foreach (var r in model.GetComponentsInChildren<Renderer>(true)) if (equipment.Contains(r.name)) r.enabled = false;
                var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                report.weightedControls = skins.SelectMany(s => s.sharedMesh.boneWeights.SelectMany(w => new[] {
                    (w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3) })
                    .Where(p => p.Item2 > .00001f && IsControl(s.bones[p.Item1].name)).Select(p => s.name + ":" + s.bones[p.Item1].name)).Distinct().ToArray();
                graph = PlayableGraph.Create("Independent Rogue whole-source screening"); graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Screened only", animator); graph.Play();
                var native = AssetDatabase.LoadAllAssetsAtPath(report.derivative).OfType<AnimationClip>().Where(c => !c.name.StartsWith("__preview__")).ToArray();
                report.clips = native.Select(c => c.name + ":" + c.length + "s Human=" + c.humanMotion).ToArray();
                var idle = native.Single(c => c.name == "Idle"); Evaluate(graph, output, animator, idle, .25f);
                report.skinContracts = skins.Select(SkinPairing).ToArray();
                bool[] modes = report.skinContracts.Select(c => c.useScale).ToArray();
                report.probes = FunctionalProbes(animator, skins, modes, humanPipeline);
                Evaluate(graph, output, animator, idle, .25f);
                var rest = Extents(Minima(skins, modes), skins, modes);
                report.sourceIdleHeight = rest.size.y;
                if (rest.size.y < .5f || rest.size.y > 5) throw new InvalidOperationException("Own native Idle geometry implausible; do not auto-rescue scale.");
                report.uniformScale = 1.8f / rest.size.y; wrapper.transform.localScale = Vector3.one * report.uniformScale;
                rest = Extents(Minima(skins, modes), skins, modes); report.standingFloorTranslation = -rest.min.y; wrapper.transform.position = Vector3.up * report.standingFloorTranslation;
                var texture = SerializedTexture(originals.Keys.First()) ?? throw new InvalidOperationException("Actual original palette texture missing; no guessed replacement.");
                bodyMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit")); bodyMaterial.SetTexture("_BaseMap", texture); bodyMaterial.SetColor("_BaseColor", Color.white); bodyMaterial.SetFloat("_Smoothness", .15f); bodyMaterial.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                foreach (var renderer in model.GetComponentsInChildren<Renderer>(true)) renderer.sharedMaterials = Enumerable.Repeat(bodyMaterial, renderer.sharedMaterials.Length).ToArray();
                var floor = PreviewObject(preview, "ReviewFloor"); floor.transform.localPosition = new Vector3(0, -.05f, 0); floor.transform.localScale = new Vector3(5,.1f,5);
                floor.AddComponent<MeshFilter>().sharedMesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
                floorMaterial = new Material(bodyMaterial); floorMaterial.SetTexture("_BaseMap", null); floorMaterial.color = new Color(.3f,.35f,.38f); floor.AddComponent<MeshRenderer>().sharedMaterial = floorMaterial;
                preview.lights[0].intensity = 1.2f; preview.lights[0].transform.rotation = Quaternion.Euler(45, -35, 0); preview.lights[1].intensity = .65f;
                var camera = preview.camera; camera.scene = model.scene; camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.24f,.29f,.34f);
                camera.orthographic = true; camera.orthographicSize = 1.35f; camera.nearClipPlane = .03f; camera.farClipPlane = 30;
                camera.transform.position = new Vector3(3, 2.0f, 5); camera.transform.LookAt(new Vector3(0,.85f,0));
                SaveCpu(preview, camera, skins, modes, report.output + "/idle.png"); images.Add(report.output + "/idle.png");
                var settings = new SerializedObject(AssetDatabase.LoadAssetAtPath<Object>(AnimationSet));
                string[] motions = nativePipeline ? new[] { "Dodge_Forward", "1H_Melee_Attack_Chop", "Throw" } : new[] { "_dodge", "_lightAttack1", "_offlineRangedAttack" };
                if (nativePipeline) poses.Add(Pose("native-standing-baseline", idle, .25f, 0, 1, animator, skins, modes, humanPipeline));
                foreach (string field in motions)
                {
                    var clip = nativePipeline ? native.Single(c => c.name == field) : (AnimationClip)settings.FindProperty(field).objectReferenceValue;
                    if (clip == null || clip.humanMotion != humanPipeline) throw new InvalidOperationException("Expected actual clip pipeline required: " + field);
                    int count = Mathf.CeilToInt(clip.length * clip.frameRate); PoseRow worst = null;
                    for (int f = 0; f <= count; f++)
                    {
                        float time = Mathf.Min(f / clip.frameRate, clip.length); Evaluate(graph, output, animator, clip, time);
                        var row = Pose(field, clip, time, f, count + 1, animator, skins, modes, humanPipeline);
                        poses.Add(row); if (worst == null || row.minimum.Min(m => m.y) < worst.minimum.Min(m => m.y)) worst = row;
                    }
                    Evaluate(graph, output, animator, clip, worst.time); string image = report.output + "/" + field.TrimStart('_') + "-minimum.png";
                    SaveCpu(preview, camera, skins, modes, image); images.Add(image);
                }
                report.poses = poses.ToArray(); report.images = images.ToArray();
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy(); if (preview != null) preview.Cleanup();
                if (bodyMaterial != null) Object.DestroyImmediate(bodyMaterial); if (floorMaterial != null) Object.DestroyImmediate(floorMaterial);
                foreach (var f in report.files) f.after = Hash(f.path);
                report.protectedFilesUnchanged = report.files.All(f => f.before == f.after);
                foreach (var m in materialRows) { m.Value.afterJson=m.Key==null?"DESTROYED":EditorJsonUtility.ToJson(m.Key);m.Value.unchanged=m.Value.beforeJson==m.Value.afterJson; }
                report.sourceMaterialMemoryUnchanged = materialRows.Values.All(m=>m.unchanged);
                report.originalSceneMemoryUnchanged = original.IsValid() && original.isLoaded && !original.isDirty && SceneManager.GetActiveScene() == original && Memory(original) == sceneMemory;
                string[] afterClosure = Closure(); report.buildClosureUnchanged = closure.SequenceEqual(afterClosure); report.derivativeOutsideBuildClosure = !afterClosure.Contains(report.derivative);
                report.poses = poses.ToArray(); report.images = images.ToArray();
                if (!report.protectedFilesUnchanged || !report.sourceMaterialMemoryUnchanged || !report.originalSceneMemoryUnchanged || !report.buildClosureUnchanged || !report.derivativeOutsideBuildClosure)
                    throw new InvalidOperationException("Protection/production dependency mismatch; retain report and do not roll back unknown changes.");
            }
        }
        static bool IsControl(string name) => name.Contains("IK") || name.StartsWith("control-");
        static Transform Joint(Animator animator, HumanBodyBones kind, bool human)
            => human ? animator.GetBoneTransform(kind) : animator.GetComponentsInChildren<Transform>(true).Single(t => t.name == Mapping().Single(m => m.humanName == kind.ToString()).boneName);
        static PoseRow Pose(string purpose, AnimationClip clip, float time, int frame, int count, Animator animator, SkinnedMeshRenderer[] skins, bool[] modes, bool human)
            => new PoseRow { purpose=purpose, clip=clip.name, sourcePath=AssetDatabase.GetAssetPath(clip), phaseBasis="Whole SOURCE seconds and normalized t/duration, NOT semantic release/entry/landing labels or domain timing. Exact end sampled. All positions relative to SAME review floor y=0.",
                time=time,duration=clip.length,fps=clip.frameRate,normalizedSourceTime=time/clip.length,sample=frame,sampleCount=count,minimum=Minima(skins,modes),animatorRoot=animator.transform.position,hips=Joint(animator,HumanBodyBones.Hips,human).position,head=Joint(animator,HumanBodyBones.Head,human).position };
        // Some imported URP materials initialize/migrate on texture getters. Reading the
        // authored serialized reference avoids deliberately initializing the source material.
        static Texture SerializedTexture(Material material)
        {
            var array=new SerializedObject(material).FindProperty("m_SavedProperties.m_TexEnvs");
            if(array==null||!array.isArray)throw new InvalidOperationException("Expected real serialized texture references.");
            foreach(string name in new[]{"_BaseMap","_MainTex"})
                for(int i=0;i<array.arraySize;i++)
                {
                    var entry=array.GetArrayElementAtIndex(i);
                    if(entry.FindPropertyRelative("first").stringValue==name)
                    {
                        var texture=entry.FindPropertyRelative("second.m_Texture").objectReferenceValue as Texture;
                        if(texture!=null)return texture;
                    }
                }
            return null;
        }
        static void Evaluate(PlayableGraph graph, AnimationPlayableOutput output, Animator animator, AnimationClip clip, float time)
        {
            if (output.GetSourcePlayable().IsValid()) graph.DestroyPlayable(output.GetSourcePlayable());
            var playable = AnimationClipPlayable.Create(graph, clip); playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
            output.SetSourcePlayable(playable); playable.SetTime(time); graph.Evaluate(0);
            // Same visual root anchoring principle as production; no per-pose lift/scale or pelvis edits.
            animator.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        }
        static SkinContract SkinPairing(SkinnedMeshRenderer skin)
        {
            var mesh = skin.sharedMesh; var vertices = mesh.vertices; var weights = mesh.boneWeights;
            var matrices = skin.bones.Select((b,i) => b.localToWorldMatrix * mesh.bindposes[i]).ToArray();
            var yes = new Mesh(); var no = new Mesh();
            try
            {
                skin.BakeMesh(yes, true); skin.BakeMesh(no, false); float trueError=0, falseError=0; var y=yes.vertices; var n=no.vertices;
                foreach (int i in mesh.triangles.Distinct())
                {
                    var w=weights[i]; Vector3 v=vertices[i]; Vector3 manual=matrices[w.boneIndex0].MultiplyPoint3x4(v)*w.weight0 + matrices[w.boneIndex1].MultiplyPoint3x4(v)*w.weight1 + matrices[w.boneIndex2].MultiplyPoint3x4(v)*w.weight2 + matrices[w.boneIndex3].MultiplyPoint3x4(v)*w.weight3;
                    trueError=Mathf.Max(trueError,Vector3.Distance(manual,skin.localToWorldMatrix.MultiplyPoint3x4(y[i]))); falseError=Mathf.Max(falseError,Vector3.Distance(manual,skin.localToWorldMatrix.MultiplyPoint3x4(n[i])));
                }
                if (Mathf.Min(trueError,falseError) > .00001f) throw new InvalidOperationException("No CPU pairing matches this Rogue skin: "+skin.name);
                return new SkinContract {renderer=skin.name,trueError=trueError,falseError=falseError,useScale=trueError<=falseError};
            }
            finally {Object.DestroyImmediate(yes);Object.DestroyImmediate(no);}
        }
        static Vector3[] Baked(SkinnedMeshRenderer skin,bool mode)
        {var mesh=new Mesh();try {skin.BakeMesh(mesh,mode);return mesh.vertices.Select(v=>skin.localToWorldMatrix.MultiplyPoint3x4(v)).ToArray();}finally{Object.DestroyImmediate(mesh);}}
        static Probe[] FunctionalProbes(Animator animator, SkinnedMeshRenderer[] skins, bool[] modes, bool human)
        {
            var rows=new List<Probe>();
            foreach(var kind in new[]{HumanBodyBones.LeftLowerLeg,HumanBodyBones.RightLowerLeg,HumanBodyBones.Head,HumanBodyBones.LeftLowerArm,HumanBodyBones.RightLowerArm})
            {
                Transform joint=Joint(animator,kind,human); if(joint==null)throw new InvalidOperationException("Probe joint missing: "+kind);
                int si=-1,vertex=-1;float best=0;
                for(int s=0;s<skins.Length;s++) {var weights=skins[s].sharedMesh.boneWeights;foreach(int i in skins[s].sharedMesh.triangles.Distinct()) {
                    var w=weights[i];float weight=0;foreach(var pair in new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}) if(skins[s].bones[pair.Item1]==joint || skins[s].bones[pair.Item1].IsChildOf(joint))weight+=pair.Item2;
                    if(weight>best){best=weight;si=s;vertex=i;}
                }}
                if(best<.9f)throw new InvalidOperationException("No clearly deform-driven vertex for "+kind);
                Quaternion rest=joint.localRotation; Vector3 before=Baked(skins[si],modes[si])[vertex];
                joint.localRotation=rest*Quaternion.Euler(20,15,10);Vector3 after=Baked(skins[si],modes[si])[vertex];joint.localRotation=rest;
                float delta=Vector3.Distance(before,after); if(delta<.0001f)throw new InvalidOperationException("Deform probe did not follow joint "+kind);
                rows.Add(new Probe{joint=kind.ToString(),skin=skins[si].name,vertex=vertex,drivenVertexDistance=delta});
            }
            return rows.ToArray();
        }
        static SkinMinimum[] Minima(SkinnedMeshRenderer[] skins,bool[] modes)
        {
            return skins.Select((skin,s)=>{var vertices=Baked(skin,modes[s]);int i=skin.sharedMesh.triangles.Distinct().OrderBy(v=>vertices[v].y).First();var w=skin.sharedMesh.boneWeights[i];var weight=new[]{(w.boneIndex0,w.weight0),(w.boneIndex1,w.weight1),(w.boneIndex2,w.weight2),(w.boneIndex3,w.weight3)}.OrderByDescending(p=>p.Item2).First();return new SkinMinimum{renderer=skin.name,dominantBone=skin.bones[weight.Item1].name,vertex=i,point=vertices[i],y=vertices[i].y};}).ToArray();
        }
        static Bounds Extents(SkinMinimum[] minimum,SkinnedMeshRenderer[] skins,bool[] modes)
        {var bounds=new Bounds(minimum[0].point,Vector3.zero);for(int s=0;s<skins.Length;s++){var v=Baked(skins[s],modes[s]);foreach(int i in skins[s].sharedMesh.triangles.Distinct())bounds.Encapsulate(v[i]);}return bounds;}
        static GameObject PreviewObject(PreviewRenderUtility preview,string name)
        {var obj=EditorUtility.CreateGameObjectWithHideFlags(name,HideFlags.HideAndDontSave);preview.AddSingleGO(obj);return obj;}
        static void SaveCpu(PreviewRenderUtility preview,Camera camera,SkinnedMeshRenderer[] skins,bool[] modes,string path)
        {
            var children=new List<GameObject>();var meshes=new List<Mesh>();var rt=RenderTexture.GetTemporary(720,720,24);var previous=RenderTexture.active;Texture2D image=null;
            try
            {
                for(int i=0;i<skins.Length;i++){var mesh=new Mesh();meshes.Add(mesh);skins[i].BakeMesh(mesh,modes[i]);var obj=PreviewObject(preview,"CPU_skin");children.Add(obj);obj.transform.SetParent(skins[i].transform,false);obj.AddComponent<MeshFilter>().sharedMesh=mesh;obj.AddComponent<MeshRenderer>().sharedMaterials=skins[i].sharedMaterials;skins[i].enabled=false;}
                UnityEngine.Rendering.RenderPipeline.SubmitRenderRequest(camera,new UnityEngine.Rendering.RenderPipeline.StandardRequest{destination=rt});RenderTexture.active=rt;
                image=new Texture2D(720,720,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,720,720),0,0);image.Apply();File.WriteAllBytes(path,image.EncodeToPNG());
            }
            finally {foreach(var s in skins)s.enabled=true;foreach(var o in children)Object.DestroyImmediate(o);foreach(var m in meshes)Object.DestroyImmediate(m);RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);if(image!=null)Object.DestroyImmediate(image);}
        }
        static void EnsureFolder(string path){if(AssetDatabase.IsValidFolder(path))return;string parent=Path.GetDirectoryName(path).Replace('\\','/');EnsureFolder(parent);if(string.IsNullOrEmpty(AssetDatabase.CreateFolder(parent,Path.GetFileName(path))))throw new IOException(path);}
        static string Memory(Scene scene)=>string.Join("\n",scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).SelectMany(t=>new[]{t.GetInstanceID()+"|"+EditorJsonUtility.ToJson(t.gameObject)}.Concat(t.GetComponents<Component>().Select(c=>c==null?"missing":c.GetInstanceID()+"|"+EditorJsonUtility.ToJson(c)))).OrderBy(x=>x));
        static string[] Closure()=>EditorBuildSettings.scenes.Where(s=>s.enabled).SelectMany(s=>AssetDatabase.GetDependencies(s.path,true)).Distinct().OrderBy(x=>x).ToArray();
        static string Hash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","");}
        static void Write(string path,Report report)=>File.WriteAllText(path,JsonUtility.ToJson(report,true));
    }
}
