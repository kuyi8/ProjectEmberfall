using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

namespace Emberfall.Editor.Setup
{
    // Isolated authoring feasibility probe. Never saves assets, scenes or contact annotations.
    public static class Light2FacingPreview
    {
        [Serializable] public sealed class Sample
        { public float time; public Vector3 root, tip, pivot; }
        [Serializable] public sealed class Result
        { public float distance, yaw, gap, time; public bool intersects; }
        [Serializable] public sealed class Report
        {
            public string scope = "Static actual-Avatar geometry only; no natural synchronization/production acceptance. One rigid group yaw at rotationNode; actor authority and within-group relative bone poses unchanged. Main-swing search starts at source .20s, not entry blend. Minimum on a 1-degree/120Hz grid, not a continuous optimum.";
            public string sourceHash, recoveryHash, rotationNode;
            public float radius, height, selectedYaw, maxPoseDistance, maxPoseAngle;
            public Vector3 capsuleCenter, pivot;
            public Result[] minimumAngles, selectedResults;
            public Sample[] samples;
        }

        public static void Capture()
        {
            if (!UnityEngine.Application.isBatchMode || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Isolated edit-mode batch only.");
            string folder = Path.GetFullPath("Builds/ArtReview/0.9.3-light2-facing-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(folder);
            bool upperBody = Environment.GetCommandLineArgs().Contains("-emberfall-upper-body");
            var previous = EditorSceneManager.GetSceneManagerSetup();
            var graph = PlayableGraph.Create("Light2 rigid facing feasibility");
            var meshes = new List<Mesh>();
            Material material = null;
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/90_CombatGym.unity");
                var actor = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<PlayerCombatActor>(true)).First();
                var enemy = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<MeleeEnemyActor>(true)).First();
                var body = new SerializedObject(enemy).FindProperty("_bodyCollider").objectReferenceValue as CapsuleCollider;
                if (body == null || body.direction != 1) throw new InvalidDataException("Upright target capsule required.");
                float radius = body.radius * Mathf.Max(Mathf.Abs(body.transform.lossyScale.x), Mathf.Abs(body.transform.lossyScale.z));
                float height = Mathf.Max(radius * 2, body.height * Mathf.Abs(body.transform.lossyScale.y));
                Vector3 center = body.transform.TransformPoint(body.center) - new Vector3(enemy.transform.position.x, 0, enemy.transform.position.z);
                var review = M6ArtReviewTool.CreateReviewScene();
                var clone = Object.Instantiate(actor.gameObject);
                clone.transform.SetPositionAndRotation(new Vector3(0, 1.08f, 0), Quaternion.identity);
                M6ArtReviewTool.SetLayerRecursively(clone.transform, 31);
                foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                    if (renderer is ParticleSystemRenderer || renderer.name.Contains("Indicator") || renderer.name.Contains("Trail")) renderer.enabled = false;
                var animator = clone.GetComponentInChildren<Animator>(true);
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                Vector3 anchor = animator.transform.localPosition;
                Quaternion rotation = animator.transform.localRotation;
                Vector3 pivot = animator.transform.position;
                var trail = new SerializedObject(clone.GetComponent<SwordTrailPresenter>());
                var bladeRoot = (Transform)trail.FindProperty("_bladeRoot").objectReferenceValue;
                var bladeTip = (Transform)trail.FindProperty("_bladeTip").objectReferenceValue;
                var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone).Select(b => animator.GetBoneTransform((HumanBodyBones)b)).Where(b => b != null).ToArray();
                var rotationNode = upperBody ? animator.GetBoneTransform(HumanBodyBones.Spine) : animator.transform;
                if (rotationNode == null) throw new InvalidDataException("Missing rotation group.");
                var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
                var strike = set.GetOfflineClip(CombatState.LightAttack2);
                var recovery = set.GetRecoveryClip(CombatState.LightAttack2);
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                var playables = new Dictionary<AnimationClip, AnimationClipPlayable>();
                foreach (var clip in new[] { strike, recovery, set.GetOfflineClip(CombatState.LightAttack1), set.GetOfflineClip(CombatState.LightAttack3) })
                {
                    var p = AnimationClipPlayable.Create(graph, clip);
                    p.SetApplyFootIK(false); p.SetApplyPlayableIK(false); playables.Add(clip, p);
                }
                graph.Play();
                void Pose(AnimationClip clip, float time, float yaw)
                {
                    animator.transform.SetLocalPositionAndRotation(anchor, rotation);
                    var playable = playables[clip]; output.SetSourcePlayable(playable); playable.SetTime(time); graph.Evaluate(0);
                    animator.transform.SetLocalPositionAndRotation(anchor, rotation);
                    rotationNode.rotation = Quaternion.Euler(0, yaw, 0) * rotationNode.rotation;
                }
                var samples = new List<Sample>();
                for (int frame = 0; frame <= Mathf.CeilToInt(strike.length * 120); frame++)
                {
                    float time = Mathf.Min(frame / 120f, strike.length);
                    Pose(strike, time, 0);
                    samples.Add(new Sample { time = time, root = bladeRoot.position, tip = bladeTip.position, pivot = rotationNode.position });
                }
                Result Best(float distance, float yaw)
                {
                    var result = new Result { distance = distance, yaw = yaw, gap = float.PositiveInfinity };
                    var turn = Quaternion.Euler(0, yaw, 0);
                    Vector3 c = center + Vector3.forward * distance;
                    foreach (var sample in samples.Where(s => s.time >= .20f))
                    {
                        Vector3 a = sample.pivot + turn * (sample.root - sample.pivot), b = sample.pivot + turn * (sample.tip - sample.pivot);
                        for (int i = 0; i <= 100; i++)
                        {
                            Vector3 point = Vector3.Lerp(a, b, i / 100f);
                            var axis = new Vector3(c.x, Mathf.Clamp(point.y, c.y - height / 2 + radius, c.y + height / 2 - radius), c.z);
                            float gap = Mathf.Max(0, Vector3.Distance(point, axis) - radius);
                            if (gap < result.gap) { result.gap = gap; result.time = sample.time; }
                        }
                    }
                    result.intersects = result.gap == 0;
                    return result;
                }
                float[] distances = { 1f, 1.25f, 1.65f };
                var all = new List<Result[]>();
                // Increasing absolute yaw makes the first common solution the minimum on a 1-degree grid.
                foreach (int magnitude in Enumerable.Range(0, 181))
                    foreach (int sign in magnitude == 0 ? new[] { 1 } : new[] { 1, -1 })
                        all.Add(distances.Select(d => Best(d, magnitude * sign)).ToArray());
                var chosen = all.FirstOrDefault(rows => rows.All(r => r.intersects));
                if (chosen == null) chosen = all.OrderBy(rows => rows.Max(r => r.gap)).First();
                float selected = chosen[0].yaw;
                var report = new Report {
                    sourceHash = AttackTimingAudit.ClipHash(strike), recoveryHash = AttackTimingAudit.ClipHash(recovery),
                    radius = radius, height = height, capsuleCenter = center, pivot = pivot, selectedYaw = selected,
                    rotationNode = upperBody ? "HumanBodyBones.Spine (one rigid subtree, hips/legs untouched)" : "Animator visual root (whole rigid hierarchy)",
                    samples = samples.ToArray(), selectedResults = chosen,
                    minimumAngles = distances.Select((d, i) => all.Select(rows => rows[i]).FirstOrDefault(r => r.intersects)
                        ?? all.Select(rows => rows[i]).OrderBy(r => r.gap).First()).ToArray()
                };
                // Exact inverse rigid-transform test, not an exemption from the existing pose thresholds.
                foreach (var clip in new[] { strike, recovery })
                    for (int i = 0; i < 24; i++)
                    {
                        float time = clip.length * i / 23f;
                        Pose(clip, time, 0);
                        var positions = bones.Select(b => b.position).ToArray();
                        var rotations = bones.Select(b => b.rotation).ToArray();
                        Pose(clip, time, selected);
                        Quaternion inverse = Quaternion.Inverse(Quaternion.Euler(0, selected, 0));
                        for (int b = 0; b < bones.Length; b++)
                        {
                            bool turned = bones[b] == rotationNode || bones[b].IsChildOf(rotationNode);
                            Vector3 restored = turned ? rotationNode.position + inverse * (bones[b].position - rotationNode.position) : bones[b].position;
                            Quaternion restoredRotation = turned ? inverse * bones[b].rotation : bones[b].rotation;
                            report.maxPoseDistance = Mathf.Max(report.maxPoseDistance, Vector3.Distance(positions[b], restored));
                            report.maxPoseAngle = Mathf.Max(report.maxPoseAngle, Quaternion.Angle(rotations[b], restoredRotation));
                        }
                    }
                if (report.maxPoseDistance >= .001f || report.maxPoseAngle >= .1f) throw new InvalidDataException("Rigid pose regression failed.");
                var skins = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(s => s.enabled).ToArray();
                foreach (var skin in skins)
                {
                    var snapshot = new GameObject("Current sampled skin", typeof(MeshFilter), typeof(MeshRenderer));
                    snapshot.layer = 31; snapshot.transform.SetParent(clone.transform, false);
                    var mesh = Object.Instantiate(skin.sharedMesh); meshes.Add(mesh);
                    snapshot.GetComponent<MeshFilter>().sharedMesh = mesh;
                    snapshot.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                    skin.enabled = false;
                }
                var target = GameObject.CreatePrimitive(PrimitiveType.Capsule); target.layer = 31;
                Object.DestroyImmediate(target.GetComponent<Collider>());
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", new Color(.12f, .75f, .8f));
                target.GetComponent<Renderer>().sharedMaterial = material;
                // Unity's primitive is radius .5, height 2. Scale hemispheres uniformly,
                // extending only the cylinder so rendered geometry matches the queried capsule.
                var targetMesh = Object.Instantiate(target.GetComponent<MeshFilter>().sharedMesh);
                meshes.Add(targetMesh);
                targetMesh.vertices = targetMesh.vertices.Select(v => new Vector3(v.x * radius * 2,
                    (v.y - Mathf.Sign(v.y) * .5f) * radius * 2 + Mathf.Sign(v.y) * (height / 2 - radius),
                    v.z * radius * 2)).ToArray();
                targetMesh.RecalculateBounds();
                target.GetComponent<MeshFilter>().sharedMesh = targetMesh;
                void Render(string label, AnimationClip clip, float time, float yaw, float distance)
                {
                    Pose(clip, time, yaw); target.transform.position = center + Vector3.forward * distance;
                    for (int s = 0; s < skins.Length; s++) AttackContactSampling.SnapshotSkin(skins[s], meshes[s], clone.transform);
                    M6ArtReviewTool.CaptureScene(review, Path.Combine(folder, label + ".png"), new Vector3(3.5f, 2.5f, -3.1f), new Vector3(0, 1, .55f), 43, false);
                }
                foreach (var result in chosen)
                    foreach (float delta in new[] { -1f / 30, 0, 1f / 30 })
                    {
                        float time = Mathf.Clamp(result.time + delta, 0, strike.length);
                        string name = $"distance-{result.distance:F2}-time-{time:F3}";
                        Render(name + "-original", strike, time, 0, result.distance);
                        Render(name + "-yaw", strike, time, selected, result.distance);
                    }
                target.SetActive(false);
                Render("seam-1-light1-end", set.GetOfflineClip(CombatState.LightAttack1), set.GetOfflineClip(CombatState.LightAttack1).length, 0, 1.25f);
                Render("seam-2-light2-start", strike, 0, selected, 1.25f);
                Render("seam-3-light2-end", strike, strike.length, selected, 1.25f);
                Render("seam-4-light3-start", set.GetOfflineClip(CombatState.LightAttack3), 0, 0, 1.25f);
                Render("recovery-start", recovery, 0, selected, 1.25f);
                Render("recovery-end", recovery, recovery.length, selected, 1.25f);
                File.WriteAllText(Path.Combine(folder, "preview.json"), JsonUtility.ToJson(report, true));
                Debug.Log($"[LIGHT2_FACING_PREVIEW] yaw={selected:R} maxPoseDistance={report.maxPoseDistance:R} maxPoseAngle={report.maxPoseAngle:R} output={folder}");
            }
            finally
            {
                graph.Destroy();
                if (material != null) Object.DestroyImmediate(material);
                foreach (var mesh in meshes) Object.DestroyImmediate(mesh);
                if (previous.Any(s => s.isLoaded && s.isActive && !string.IsNullOrEmpty(s.path))) EditorSceneManager.RestoreSceneManagerSetup(previous);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }
    }
}
