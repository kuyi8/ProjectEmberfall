using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Emberfall.AI.Unity;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using Emberfall.Gameplay.Movement;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Review
{
    /// <summary>Editor-only original-scale visual feasibility. No production authoring or gameplay simulation.</summary>
    public static class TinyHeroBakeoff
    {
        const string Source = "Assets/RPG Tiny Hero Duo";
        const string Owned = "Assets/_Game/Art/Review/TinyHero";
        const string Valley = "Assets/_Game/Scenes/10_EmberValley.unity";
        static readonly List<ModelRow> Models = new List<ModelRow>();
        static readonly List<PoseRow> Poses = new List<PoseRow>();
        [Serializable] public sealed class ModelRow
        {
            public string name, source, avatar, materials;
            public bool valid, human;
            public Vector3 rootScale, size, min, max, idleSize;
            public float idleFootY, idleHeadY;
            public float humanScale;
            public int activeSkins, vertices;
        }
        [Serializable] public sealed class PoseRow
        {
            public string actor, action, clip;
            public float time, feetMeshMinY;
            public Vector3 leftFoot, rightFoot, rightHand, size;
        }
        [Serializable] public sealed class Result
        {
            public string scope = "Original-size active mesh vertex bounds; controlled PlayableGraph poses, NOT natural contact, hardware input, locomotion slip or production acceptance.";
            public ModelRow[] models;
            public PoseRow[] poses;
            public Vector3 currentCapsuleCenter, currentPlayerRoot, currentVisualLocalPosition;
            public float currentCapsuleHeight, currentCapsuleRadius, currentCameraDistance, currentCameraPivotHeight;
            public bool frozenFilesUnchanged;
        }
        internal sealed class Rig : IDisposable
        {
            public GameObject go;
            public Animator animator;
            public PlayableGraph graph;
            AnimationPlayableOutput output;
            readonly Dictionary<AnimationClip, AnimationClipPlayable> clips = new Dictionary<AnimationClip, AnimationClipPlayable>();
            readonly Vector3 anchor;
            readonly Quaternion rotation;
            public Rig(GameObject obj)
            {
                go = new GameObject(obj.name); obj.transform.SetParent(go.transform, true);
                animator = obj.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isValid || !animator.avatar.isHuman)
                    throw new InvalidOperationException("Invalid humanoid: " + obj.name);
                animator.runtimeAnimatorController = null; animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind(); animator.Update(0);
                anchor = animator.transform.localPosition; rotation = animator.transform.localRotation;
                graph = PlayableGraph.Create("TinyHero review " + go.name);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                output = AnimationPlayableOutput.Create(graph, "Pose", animator); graph.Play();
            }
            public void Pose(AnimationClip clip, float time)
            {
                if (!clips.TryGetValue(clip, out var playable))
                {
                    playable = AnimationClipPlayable.Create(graph, clip);
                    playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false); clips.Add(clip, playable);
                }
                output.SetSourcePlayable(playable); playable.SetTime(time); graph.Evaluate(0);
                animator.transform.SetLocalPositionAndRotation(anchor, rotation);
            }
            public void Dispose() { if (graph.IsValid()) graph.Destroy(); }
        }

        public static void Capture()
        {
            if (!UnityEngine.Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                throw new InvalidOperationException("Graphics EditMode batch only.");
            Models.Clear(); Poses.Clear();
            string dir = "Builds/ArtReview/0.9.6-tinyhero/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff");
            Directory.CreateDirectory(dir); Directory.CreateDirectory(Owned); AssetDatabase.Refresh();
            var frozen = Directory.GetFiles(Source, "*", SearchOption.AllDirectories)
                .Concat(new[] { Valley, "Assets/_Game/Scenes/90_CombatGym.unity", "Assets/_Game/Prefabs/Characters/M3Art/P_Player_Ranger.prefab", M1AnimationSetup.AnimationSetPath,
                    "Assets/_Game/Settings/AttackTiming_M6.asset", "Assets/_Game/Settings/CombatTuning_M1.asset",
                    "Assets/_Game/Resources/Networking/P_M5_NetworkGymPlayer.prefab" })
                .Concat(Directory.GetFiles("Assets/_Game", "*KnifeGripPose_Ranger*", SearchOption.AllDirectories))
                .Concat(Directory.GetFiles("Assets/_Game/Prefabs", "*.prefab", SearchOption.AllDirectories).Where(p => p.IndexOf("Network", StringComparison.OrdinalIgnoreCase) >= 0))
                .Where(File.Exists).Distinct().ToDictionary(p => p, Hash);
            File.WriteAllText(dir + "/frozen-before.txt", string.Join("\n", frozen.Select(p => p.Value + " " + p.Key)));
            EditorSceneManager.OpenScene(Valley);
            var player = Object.FindObjectOfType<PlayerCombatActor>();
            var cc = player.GetComponent<CharacterController>();
            var cameraRig = new SerializedObject(Object.FindObjectOfType<ThirdPersonCameraRig>());
            var result = new Result { currentCapsuleHeight = cc.height, currentCapsuleRadius = cc.radius, currentCapsuleCenter = cc.center,
                currentPlayerRoot = player.transform.position, currentVisualLocalPosition = player.GetComponentInChildren<Animator>().transform.localPosition,
                currentCameraDistance = cameraRig.FindProperty("_distance").floatValue, currentCameraPivotHeight = cameraRig.FindProperty("_pivotHeight").floatValue };
            // Copy actual formal-scene visual roots including their world scale. Never normalize height.
            var templates = new List<GameObject>();
            foreach (var actor in new CombatTarget[] { player, Object.FindObjectsOfType<MeleeEnemyActor>().First(a => a.name.Contains("Forest")),
                Object.FindObjectsOfType<RangedEnemyActor>().First(a => a.name.Contains("Forest")), Object.FindObjectOfType<WardenActor>() })
            {
                var anim = actor.GetComponentInChildren<Animator>(true);
                var copy = Object.Instantiate(anim.gameObject);
                copy.name = actor == player ? "Ranger" : actor is WardenActor ? "Warden" : actor is RangedEnemyActor ? "Priest" : "Fogwalker";
                copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Inverse(actor.transform.rotation) * anim.transform.rotation);
                copy.transform.localScale = anim.transform.lossyScale;
                foreach (var script in copy.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(script);
                templates.Add(copy);
            }
            // Move detached templates out before replacing the loaded (never saved) production scene.
            var review = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            foreach (var template in templates) UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(template, review);
            var formal = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(Valley);
            EditorSceneManager.CloseScene(formal, true); UnityEngine.SceneManagement.SceneManager.SetActiveScene(review);
            SetupLight();
            var poly = CreateCandidate("Polyart"); var pbr = CreateCandidate("PBR");
            templates.Insert(0, pbr); templates.Insert(0, poly);
            var rigs = templates.Select(t => new Rig(t)).ToArray();
            try
            {
                var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
                var library = AssetDatabase.LoadAllAssetsAtPath(M1AnimationSetup.Library1Path).OfType<AnimationClip>().ToArray();
                var idle = library.Single(c => c.name == "Rig|Idle_Loop");
                var walk = library.Single(c => c.name == "Rig|Walk_Loop");
                for (int i = 0; i < rigs.Length; i++)
                {
                    var rig = rigs[i]; var bounds = VertexBounds(rig.go);
                    var row = new ModelRow { name = rig.go.name, source = i < 2 ? Source + "/Prefab/MaleCharacter" + rig.go.name + ".prefab" : "Formal scene Animator visual",
                        avatar = AssetDatabase.GetAssetPath(rig.animator.avatar), valid = rig.animator.avatar.isValid, human = rig.animator.avatar.isHuman,
                        humanScale = rig.animator.humanScale, rootScale = rig.animator.transform.lossyScale, size = bounds.size, min = bounds.min, max = bounds.max,
                        activeSkins = rig.go.GetComponentsInChildren<SkinnedMeshRenderer>().Count(r => r.enabled),
                        vertices = rig.go.GetComponentsInChildren<SkinnedMeshRenderer>().Where(r => r.enabled).Sum(r => r.sharedMesh.vertexCount),
                        materials = string.Join(";", rig.go.GetComponentsInChildren<Renderer>().Where(r => r.enabled).SelectMany(r => r.sharedMaterials).Select(m => m.name).Distinct()) };
                    Models.Add(row); rig.go.transform.position += Vector3.up * -bounds.min.y;
                }
                // Save only project-owned visual candidates, before pose sampling, with original scale intact.
                foreach (var rig in rigs.Take(2)) PrefabUtility.SaveAsPrefabAsset(rig.go, Owned + "/P_Review_TinyHero_" + rig.go.name + ".prefab");
                var diagnostics=new List<string>();
                foreach (var rig in rigs)
                foreach (var r in rig.go.GetComponentsInChildren<Renderer>().Where(r=>r.enabled && (r is MeshRenderer || r is SkinnedMeshRenderer)))
                {
                    diagnostics.Add($"actor={rig.go.name} renderer={r.name} type={r.GetType().Name} equipment={Equipment(r.transform,rig.go.transform)} scale={r.transform.lossyScale:F4} worldBounds={r.bounds.size:F4}");
                    if (r is SkinnedMeshRenderer skin)
                    {
                        var m=new Mesh(); skin.BakeMesh(m,false); m.RecalculateBounds(); diagnostics.Add($" bakeFalse={m.bounds.size:F4}");
                        skin.BakeMesh(m,true); m.RecalculateBounds(); diagnostics.Add($" bakeTrue={m.bounds.size:F4}"); Object.DestroyImmediate(m);
                    }
                }
                File.WriteAllLines(dir+"/bounds-diagnostics.txt",diagnostics);
                for (int i = 0; i < rigs.Length; i++)
                {
                    rigs[i].go.transform.position += Vector3.right * (-5f + i * 2f); rigs[i].Pose(idle, .25f);
                    var idleBounds=VertexBounds(rigs[i].go); Models[i].idleSize=idleBounds.size;
                    Models[i].idleFootY=idleBounds.min.y-rigs[i].go.transform.position.y;
                    Models[i].idleHeadY=rigs[i].animator.GetBoneTransform(HumanBodyBones.Head).position.y-rigs[i].go.transform.position.y;
                }
                Cube("Ground", new Vector3(0, -.07f, 0), new Vector3(30, .1f, 20), new Color(.38f,.43f,.46f));
                var camera = new GameObject("Review camera").AddComponent<Camera>();
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.35f,.48f,.61f); camera.nearClipPlane = .03f;
                camera.orthographic = true; camera.orthographicSize = 3.9f;
                View(camera, new Vector3(0, 2.4f, 14), new Vector3(0, 1.2f, 0));
                Save(camera, dir + "/01-lineup-original-scale.png", 1800, 900);
                foreach (var r in rigs) r.go.SetActive(false);
                rigs[0].go.SetActive(true); rigs[0].go.transform.position = Vector3.zero; rigs[0].Pose(idle, .25f);
                // Dimension proxy, explicitly not a replacement or remeasurement of production collision.
                Cube("Wall4m_left", new Vector3(-3.5f, 2, -1.5f), new Vector3(3,4,.65f), new Color(.55f,.61f,.64f));
                Cube("Wall4m_right", new Vector3(3.5f, 2, -1.5f), new Vector3(3,4,.65f), new Color(.55f,.61f,.64f));
                Cube("GateLintel_3.25mClear", new Vector3(0,3.625f,-1.5f), new Vector3(4,.75f,.65f), new Color(.61f,.67f,.69f));
                Ring(2.45f, new Color(.05f,.75f,1)); Ring(2.7f, new Color(1,.65f,.05f));
                camera.orthographicSize = 4.2f; View(camera, new Vector3(6,6,12), new Vector3(0,1.1f,0));
                Save(camera, dir + "/02-gate-ranges-original-scale.png", 1500, 1000);
                foreach (var obj in Object.FindObjectsOfType<MeshRenderer>().Where(r => !r.transform.IsChildOf(rigs[0].go.transform)).Select(r => r.gameObject).ToArray())
                    if (obj.name != "Ground") Object.DestroyImmediate(obj);
                foreach (var line in Object.FindObjectsOfType<LineRenderer>()) Object.DestroyImmediate(line.gameObject);
                rigs[0].go.SetActive(false);
                // Same source clip/time/camera, no per-model foot normalization after sampling.
                var actions = new Dictionary<string, AnimationClip> { {"Idle",idle}, {"Walk",walk},
                    {"Light1",set.GetOfflineClip(CombatState.LightAttack1)}, {"Sweep",set.GetOfflineClip(CombatState.Sweep)},
                    {"HeavyDedicated",set.GetOfflineClip(CombatState.HeavyAttack)}, {"ExecutionDedicated",set.GetOfflineClip(CombatState.Execution)},
                    {"KnifeDedicated",set.GetOfflineClip(CombatState.RangedAttack)}, {"Dodge",set.GetOfflineClip(CombatState.Dodge)} };
                float[] fractions = { 0, .25f, .5f, .75f, .98f };
                foreach (var action in actions)
                {
                    var sheet = new Texture2D(1600, 640, TextureFormat.RGB24, false);
                    for (int row = 0; row < 2; row++)
                    {
                        var rig = rigs[row == 0 ? 0 : 2]; rig.go.SetActive(true); rig.go.transform.position = Vector3.zero;
                        for (int col = 0; col < fractions.Length; col++)
                        {
                            rig.Pose(action.Value, action.Value.length * fractions[col]);
                            var b = VertexBounds(rig.go);
                            Poses.Add(new PoseRow { actor = rig.go.name, action = action.Key, clip = AssetDatabase.GetAssetPath(action.Value), time = action.Value.length * fractions[col],
                                feetMeshMinY = b.min.y, size = b.size, leftFoot = rig.animator.GetBoneTransform(HumanBodyBones.LeftFoot).position,
                                rightFoot = rig.animator.GetBoneTransform(HumanBodyBones.RightFoot).position, rightHand = rig.animator.GetBoneTransform(HumanBodyBones.RightHand).position });
                            camera.orthographicSize = 1.6f; View(camera, new Vector3(3,1.8f,5.5f), new Vector3(0,1,0));
                            var frame = Render(camera, 320,320); sheet.SetPixels(col*320,(1-row)*320,320,320,frame.GetPixels()); Object.DestroyImmediate(frame);
                        }
                        rig.go.SetActive(false);
                    }
                    sheet.Apply(); File.WriteAllBytes(dir + "/pose-" + action.Key + ".png", sheet.EncodeToPNG()); Object.DestroyImmediate(sheet);
                }
                // Target-URP environment proof, all mutations are discarded without saving the formal scene.
                foreach (var root in review.GetRootGameObjects()) root.SetActive(false);
                var scene = EditorSceneManager.OpenScene(Valley, OpenSceneMode.Additive);
                UnityEngine.SceneManagement.SceneManager.SetActiveScene(scene);
                var formalPlayer = Object.FindObjectOfType<PlayerCombatActor>();
                var currentAnim = formalPlayer.GetComponentInChildren<Animator>();
                var currentCopy = Object.Instantiate(currentAnim.gameObject);
                currentCopy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Inverse(formalPlayer.transform.rotation) * currentAnim.transform.rotation);
                currentCopy.transform.localScale = currentAnim.transform.lossyScale;
                foreach (var target in Object.FindObjectsOfType<CombatTarget>()) target.gameObject.SetActive(false);
                using (var currentRig = new Rig(currentCopy))
                using (var tinyRig = new Rig(Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Owned + "/P_Review_TinyHero_Polyart.prefab"))))
                {
                    currentRig.go.transform.SetPositionAndRotation(new Vector3(25,.1f,44.6f), Quaternion.Euler(0,90,0));
                    tinyRig.go.transform.SetPositionAndRotation(new Vector3(25,.1f,41.4f), Quaternion.Euler(0,90,0));
                    currentRig.Pose(idle,.25f); tinyRig.Pose(idle,.25f);
                    var sceneCamera = new GameObject("Tiny Hero formal environment review").AddComponent<Camera>();
                    sceneCamera.CopyFrom(Camera.main); sceneCamera.enabled=false; sceneCamera.fieldOfView=43;
                    View(sceneCamera,new Vector3(35,3.6f,45),new Vector3(25,1.2f,43));
                    Save(sceneCamera,dir+"/03-formal-courtyard-preview.png",1600,900);
                }
                result.models = Models.ToArray(); result.poses = Poses.ToArray(); AssetDatabase.SaveAssets();
                result.frozenFilesUnchanged = frozen.All(p => Hash(p.Key) == p.Value);
                File.WriteAllText(dir + "/measurements.json", JsonUtility.ToJson(result, true));
                File.WriteAllText("Builds/ArtReview/0.9.6-tinyhero/latest.txt", dir);
                if (!result.frozenFilesUnchanged) throw new InvalidOperationException("Frozen source or production asset changed.");
                Debug.Log("[TINY_HERO_REVIEW] " + dir);
            }
            finally { foreach (var rig in rigs) rig.Dispose(); EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single); }
        }

        static GameObject CreateCandidate(string variant)
        {
            var go = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(Source + "/Prefab/MaleCharacter" + variant + ".prefab"));
            go.name = variant; go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var source = AssetDatabase.LoadAssetAtPath<Material>(Source + "/Material/" + variant + "_Default.mat");
            string path = Owned + "/M_TinyHero_" + variant + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null) { mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(mat, path); }
            mat.SetTexture("_BaseMap", source.GetTexture("_MainTex")); mat.SetColor("_BaseColor", source.GetColor("_Color"));
            mat.SetFloat("_Metallic", source.GetFloat("_Metallic")); mat.SetFloat("_Smoothness", source.GetFloat("_Glossiness"));
            mat.SetTexture("_MetallicGlossMap", source.GetTexture("_MetallicGlossMap"));
            if (source.GetTexture("_MetallicGlossMap") != null) mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_EmissionMap", source.GetTexture("_EmissionMap")); mat.SetColor("_EmissionColor", source.GetColor("_EmissionColor"));
            if (source.GetTexture("_EmissionMap") != null) mat.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(mat);
            foreach (var r in go.GetComponentsInChildren<Renderer>(true)) r.sharedMaterials = r.sharedMaterials.Select(_ => mat).ToArray();
            return go;
        }
        internal static Bounds VertexBounds(GameObject go)
        {
            var bounds = new Bounds(); bool first = true;
            // Modular heads/hair can be rigid MeshRenderers, not skins. Include them or height is false.
            foreach (var renderer in go.GetComponentsInChildren<Renderer>().Where(r => r.enabled &&
                (r is SkinnedMeshRenderer || r is MeshRenderer) && !Equipment(r.transform,go.transform)))
            {
                Mesh mesh; bool baked=renderer is SkinnedMeshRenderer;
                // Explicit scaled local snapshot before TransformPoint. Verified against rendered bounds
                // for imported rigs with ~42/60x child scales; the default snapshot double-counts those scales here.
                if (renderer is SkinnedMeshRenderer skin) { mesh=new Mesh(); skin.BakeMesh(mesh, true); }
                else mesh=renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null) continue;
                // Extracted modular meshes may retain unreferenced source vertices; only drawn indices count.
                var vertices=mesh.vertices;
                foreach (var index in mesh.triangles.Distinct()) { var p = renderer.transform.TransformPoint(vertices[index]); if (first) { bounds = new Bounds(p,Vector3.zero); first = false; } else bounds.Encapsulate(p); }
                if (baked) Object.DestroyImmediate(mesh);
            }
            if (first) throw new InvalidOperationException("No active skin: " + go.name);
            return bounds;
        }
        static bool Equipment(Transform t,Transform root)
        {
            while (t != null && t != root)
            {
                string n=t.name.ToLowerInvariant();
                if (new[]{"sword","shield","ohs","staff","weapon","knife","trail","telegraph"}.Any(n.Contains)) return true;
                t=t.parent;
            }
            return false;
        }
        internal static void SetupLight()
        {
            RenderSettings.ambientMode = AmbientMode.Trilight; RenderSettings.ambientSkyColor = new Color(.65f,.72f,.8f);
            RenderSettings.ambientEquatorColor = new Color(.5f,.53f,.57f); RenderSettings.ambientGroundColor = new Color(.3f,.31f,.34f);
            var light = new GameObject("Neutral key").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.25f;
            light.transform.rotation = Quaternion.Euler(35,145,0); light.shadows = LightShadows.Soft;
            var fill = new GameObject("Neutral fill").AddComponent<Light>(); fill.type=LightType.Directional; fill.intensity=.5f;
            fill.transform.rotation=Quaternion.Euler(25,-35,0);
        }
        internal static void Cube(string name, Vector3 position, Vector3 scale, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.position = position; go.transform.localScale = scale;
            Object.DestroyImmediate(go.GetComponent<Collider>()); var mat = new Material(Shader.Find("Universal Render Pipeline/Lit")); mat.color = color;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }
        static void Ring(float radius, Color color)
        {
            var line = new GameObject("Radius_" + radius).AddComponent<LineRenderer>(); line.loop = true; line.positionCount = 96; line.widthMultiplier = .035f;
            var mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")); mat.color = color; line.sharedMaterial = mat;
            for (int i=0;i<96;i++) { float a=i*Mathf.PI*2/96; line.SetPosition(i,new Vector3(Mathf.Cos(a)*radius,.012f,Mathf.Sin(a)*radius)); }
        }
        internal static void View(Camera camera, Vector3 position, Vector3 target) { camera.transform.position = position; camera.transform.LookAt(target); }
        internal static Texture2D Render(Camera camera, int width, int height)
        {
            var rt = RenderTexture.GetTemporary(width,height,24,RenderTextureFormat.ARGB32); var previous=RenderTexture.active;
            try { camera.targetTexture=rt; camera.Render(); RenderTexture.active=rt; var image=new Texture2D(width,height,TextureFormat.RGB24,false); image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply(); return image; }
            finally { camera.targetTexture=null; RenderTexture.active=previous; RenderTexture.ReleaseTemporary(rt); }
        }
        static void Save(Camera c,string path,int w,int h) { var image=Render(c,w,h); File.WriteAllBytes(path,image.EncodeToPNG()); Object.DestroyImmediate(image); }
        static string Hash(string path) { using var sha = SHA256.Create(); return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-",""); }
    }
}
