using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Emberfall.AI.Unity;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Emberfall.Editor.Setup
{
    // Fixed actor, camera and target reference: no per-frame grounding/reframing or gameplay writes.
    public static class AttackContactSampling
    {
        private static readonly Dictionary<string, Vector3[]> SampledPoses = new Dictionary<string, Vector3[]>();
        public static void Capture()
        {
            if (!UnityEngine.Application.isBatchMode)
                throw new InvalidOperationException("Use the isolated batch entry; no interactive scene replacement.");
            if (AnimationMode.InAnimationMode() || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Playback/preview must be stopped.");
            for (int i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    throw new InvalidOperationException("Unsaved scene changes.");
            var previous = EditorSceneManager.GetSceneManagerSetup();
            string output = Path.GetFullPath("Builds/ArtReview/0.9.3-contact-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff"));
            Directory.CreateDirectory(output);
            SampledPoses.Clear();
            try
            {
                var scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity");
                var roots = scene.GetRootGameObjects();
                var player = roots.SelectMany(r => r.GetComponentsInChildren<PlayerCombatActor>(true)).First();
                var priest = roots.SelectMany(r => r.GetComponentsInChildren<RangedEnemyActor>(true)).First();
                var maps = AttackTimingAudit.ReadMappings();
                var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(M1AnimationSetup.AnimationSetPath);
                var priestReference = set.GetClip(CombatState.HeavyAttack);
                foreach (string id in new[] { "player.sweep", "priest.projectile" })
                {
                    var map = maps.Single(m => m.id == id);
                    float[] seconds = Enumerable.Range(0, Mathf.RoundToInt(map.clip.length * 30) + 1)
                        .Select(frame => Mathf.Min(frame / 30f, map.clip.length)).ToArray();
                    CaptureSequence(output, id, id.StartsWith("player.") ? player.gameObject : priest.gameObject,
                        map.clip, seconds, "Source poses at 30fps. Fixed target reference, not a physical hit or confirmed contact.",
                        id.StartsWith("priest.") ? priestReference : null);
                }
                var priestWindup = set.GetEnemyClip(EnemyAnimationAction.PriestProjectileWindup);
                CaptureSequence(output, "priest.windup", priest.gameObject, priestWindup,
                    Enumerable.Range(0,14).Select(f=>f/30f).ToArray(), "Authored projectile windup; source poses only.", priestReference);
                CaptureSequence(output, "priest.before.windup", priest.gameObject, set.GetClip(CombatState.HeavyCharge),
                    new[] { 0f, .1f, .2f }, "Before: idle windup. Same grounding reference.", priestReference);
                CaptureSequence(output, "priest.before.release", priest.gameObject, priestReference,
                    new[] {0f,.2f,.4f,.8f,1.3f}, "Before: full HeavyAttack on Release entry. Same grounding reference.", priestReference);
                // Offline Warden is authored in the valley; Sanctum is the additive network arena shell.
                scene = EditorSceneManager.OpenScene("Assets/_Game/Scenes/10_EmberValley.unity");
                var warden = scene.GetRootGameObjects().SelectMany(r => r.GetComponentsInChildren<WardenActor>(true)).First();
                var charge = maps.Single(m => m.id == "warden.charge");
                var csv = new StringBuilder("variant,phase,phaseProgress,domainSeconds,clipSeconds\n");
                string[] phases = { "windup", "travel", "recovery" };
                float[] starts = { 0, .24f, .78f }, fractions = { .24f, .54f, .22f };
                float[] durations = new float[3];
                // Durations from authoring data, not a second runtime tuning source.
                var json = File.ReadAllText(AttackTimingAudit.EnemyPath);
                var definition = Emberfall.AI.Data.WardenDefinitionJsonLoader.Load(json)
                    .GetRequired(new Emberfall.Core.Identifiers.ContentId("boss:ember-warden"));
                durations[0] = definition.Charge.WindupDuration;
                durations[1] = definition.Charge.AttackDuration;
                durations[2] = definition.Charge.RecoveryDuration;
                foreach (string variant in new[] { "before", "length-only", "compensated-proposal" })
                    for (int phase = 0; phase < 3; phase++)
                    {
                        float speed = Mathf.Clamp(charge.clip.length * fractions[phase] / durations[phase], .25f, 3f);
                        float argument = starts[phase] * (variant == "before" ? 1f : charge.clip.length);
                        if (variant == "compensated-proposal") argument /= speed;
                        // Measured with the actual controller in WardenChargePresentationTests.
                        float offset = argument * speed;
                        float[] times = new float[5];
                        for (int i = 0; i < times.Length; i++)
                        {
                            times[i] = offset + durations[phase] * i / 4f * speed;
                            csv.AppendLine(FormattableString.Invariant($"{variant},{phases[phase]},{i/4f:R},{durations[phase]*i/4f:R},{times[i]:R}"));
                        }
                        CaptureSequence(output, "warden." + variant + "." + phases[phase], warden.gameObject, charge.clip,
                            times, "Same camera/actor. Static phase sampling, no crossfade, travel, freeze or runtime contact proof.");
                    }
                File.WriteAllText(Path.Combine(output, "warden-phases.csv"), csv.ToString());
            }
            finally
            {
                if (previous.Any(s => s.isLoaded && s.isActive && !string.IsNullOrEmpty(s.path)))
                    EditorSceneManager.RestoreSceneManagerSetup(previous);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
            Debug.Log("[CONTACT_SAMPLING] " + output);
        }

        private static void CaptureSequence(string output, string id, GameObject actor, AnimationClip clip,
            float[] seconds, string scope, AnimationClip groundingClip = null)
        {
            Scene review = M6ArtReviewTool.CreateReviewScene();
            Material referenceMaterial = null;
            PlayableGraph graph = default;
            var bakedMeshes = new List<Mesh>();
            try
            {
                GameObject clone = UnityEngine.Object.Instantiate(actor);
                SceneManager.MoveGameObjectToScene(clone, review);
                clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                M6ArtReviewTool.SetLayerRecursively(clone.transform, 31);
                foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) behaviour.enabled = false;
                foreach (var collider in clone.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
                    if (renderer.name.Contains("Telegraph") || renderer.name.Contains("Indicator") || renderer.name.Contains("Warning"))
                        renderer.enabled = false;
                var animator = clone.GetComponentInChildren<Animator>(true);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.Rebind();
                Vector3 anchorPosition = animator.transform.localPosition;
                Quaternion anchorRotation = animator.transform.localRotation;
                graph = PlayableGraph.Create("Isolated contact pose sampling");
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = AnimationClipPlayable.Create(graph, clip);
                playable.SetApplyFootIK(false);
                playable.SetApplyPlayableIK(false);
                var poseOutput = AnimationPlayableOutput.Create(graph, "Pose", animator);
                var referencePlayable = groundingClip != null ? AnimationClipPlayable.Create(graph, groundingClip) : playable;
                referencePlayable.SetApplyFootIK(false);
                referencePlayable.SetApplyPlayableIK(false);
                poseOutput.SetSourcePlayable(referencePlayable);
                graph.Play();
                playable.SetTime(0);
                graph.Evaluate(0);
                animator.transform.SetLocalPositionAndRotation(anchorPosition, anchorRotation);
                // Actors use a center-origin capsule. Ground ONCE at reference pose, not every frame.
                var visible = clone.GetComponentsInChildren<Renderer>(true).Where(r => r.enabled && !(r is ParticleSystemRenderer)).ToArray();
                if (visible.Length == 0) throw new InvalidDataException("No actor renderers: " + actor.name);
                float baselineBottom = visible.Min(r => r.bounds.min.y);
                clone.transform.position += Vector3.up * -baselineBottom;
                poseOutput.SetSourcePlayable(playable);
                // Render the explicitly sampled pose, not a potentially one-frame-old skinning buffer.
                var skins = clone.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r => r.enabled).ToArray();
                foreach (var skin in skins)
                {
                    var snapshot = new GameObject("Sampled mesh", typeof(MeshFilter), typeof(MeshRenderer));
                    snapshot.layer = 31;
                    snapshot.transform.SetParent(clone.transform, false);
                    var mesh = UnityEngine.Object.Instantiate(skin.sharedMesh);
                    mesh.name = "Contact sample (temporary)";
                    bakedMeshes.Add(mesh);
                    snapshot.GetComponent<MeshFilter>().sharedMesh = mesh;
                    snapshot.GetComponent<MeshRenderer>().sharedMaterials = skin.sharedMaterials;
                    skin.enabled = false;
                }
                var target = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                target.name = "Fixed reference (not gameplay target)";
                target.layer = 31;
                target.transform.position = new Vector3(0, 1, 1.15f);
                target.transform.localScale = new Vector3(.28f, .65f, .28f);
                UnityEngine.Object.DestroyImmediate(target.GetComponent<Collider>());
                referenceMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                referenceMaterial.SetColor("_BaseColor", new Color(.15f, .75f, .85f));
                target.GetComponent<Renderer>().sharedMaterial = referenceMaterial;
                var rows = new StringBuilder("index,clipSeconds,rootPositionCorrection,rootAngleCorrection\n");
                for (int i = 0; i < seconds.Length; i++)
                {
                    playable.SetTime(seconds[i]);
                    graph.Evaluate(0);
                    float positionCorrection = Vector3.Distance(animator.transform.localPosition, anchorPosition);
                    float angleCorrection = Quaternion.Angle(animator.transform.localRotation, anchorRotation);
                    // Match the runtime presenters' LateUpdate anchoring, without per-frame re-grounding.
                    animator.transform.SetLocalPositionAndRotation(anchorPosition, anchorRotation);
                    // Identical source times across separate phase captures must produce identical bones.
                    var pose = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                        .Select(b => animator.GetBoneTransform((HumanBodyBones)b)).Where(b => b != null)
                        .Select(b => clone.transform.InverseTransformPoint(b.position)).ToArray();
                    string key = actor.name + ":" + AssetDatabase.GetAssetPath(clip) + ":" + Mathf.RoundToInt(seconds[i] * 100000);
                    if (SampledPoses.TryGetValue(key, out var prior) &&
                        (prior.Length != pose.Length || pose.Where((p, b) => Vector3.Distance(p, prior[b]) > .0001f).Any()))
                        throw new InvalidDataException("Non-repeatable pose sample: " + key);
                    SampledPoses[key] = pose;
                    for (int s = 0; s < skins.Length; s++)
                        SnapshotSkin(skins[s], bakedMeshes[s], clone.transform);
                    M6ArtReviewTool.CaptureScene(review, Path.Combine(output, id + "-" + i.ToString("D3") + ".png"),
                        new Vector3(2.8f, 2.1f, 3.4f), new Vector3(0, 1.0f, .35f), 38, false);
                    rows.AppendLine(FormattableString.Invariant($"{i},{seconds[i]:R},{positionCorrection:R},{angleCorrection:R}"));
                }
                File.WriteAllText(Path.Combine(output, id + ".csv"), rows.ToString());
                File.WriteAllText(Path.Combine(output, id + ".txt"),
                    $"{scope}\nActor={actor.name}\nClip={AssetDatabase.GetAssetPath(clip)}\nHash={AttackTimingAudit.ClipHash(clip)}\nLength={clip.length:R}\nBaselineGroundOffset={-baselineBottom:R}\nNo contact annotation written.\n");
            }
            finally
            {
                if (graph.IsValid()) graph.Destroy();
                M6ArtReviewTool.CloseReviewScene(review);
                if (referenceMaterial != null) UnityEngine.Object.DestroyImmediate(referenceMaterial);
                foreach (var mesh in bakedMeshes) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        private static void SnapshotSkin(SkinnedMeshRenderer skin, Mesh snapshot, Transform root)
        {
            // Explicit CPU skinning in clone space avoids both stale GPU poses and importer-scale
            // ambiguity in BakeMesh. This adapter uses the current sources' four-weight skinning;
            // active blendshapes require their own adapter instead of silently omitting their pose.
            Mesh source = skin.sharedMesh;
            for (int b = 0; b < source.blendShapeCount; b++)
                if (Mathf.Abs(skin.GetBlendShapeWeight(b)) > .0001f)
                    throw new InvalidDataException("Active blendshape needs a dedicated sampling adapter: " + skin.name);
            var vertices = source.vertices;
            var weights = source.boneWeights;
            var bindPoses = source.bindposes;
            var bones = skin.bones;
            var matrices = new Matrix4x4[bindPoses.Length];
            for (int b = 0; b < matrices.Length; b++)
                matrices[b] = root.worldToLocalMatrix * bones[b].localToWorldMatrix * bindPoses[b];
            if (vertices.Length != weights.Length) throw new InvalidDataException("Unsupported skin weights: " + skin.name);
            for (int v = 0; v < vertices.Length; v++)
            {
                var w = weights[v];
                Vector3 p = vertices[v];
                vertices[v] = matrices[w.boneIndex0].MultiplyPoint3x4(p) * w.weight0 +
                    matrices[w.boneIndex1].MultiplyPoint3x4(p) * w.weight1 +
                    matrices[w.boneIndex2].MultiplyPoint3x4(p) * w.weight2 +
                    matrices[w.boneIndex3].MultiplyPoint3x4(p) * w.weight3;
            }
            snapshot.vertices = vertices;
            snapshot.RecalculateNormals();
            snapshot.RecalculateBounds();
        }
    }
}
