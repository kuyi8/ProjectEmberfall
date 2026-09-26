using System.Linq;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.SceneManagement;

namespace Emberfall.Tests.EditMode
{
    public sealed class SweepAnimationTests
    {
        private static AnimationClip Source => AssetDatabase.LoadAssetAtPath<AnimationClip>(SweepAnimationSetup.SourcePath);
        private static AnimationClip Candidate => AssetDatabase.LoadAssetAtPath<AnimationClip>(SweepAnimationSetup.CandidatePath);

        [Test]
        public void SingleSwingPreservesOnlyObservedMiddleRange()
        {
            Assert.That(Source.length, Is.EqualTo(3).Within(.0001f));
            Assert.That(Candidate.length, Is.EqualTo(23f / 30f).Within(.0001f));
            Assert.That(Candidate.events, Is.Empty);
            Assert.That(AnimationUtility.GetAnimationClipSettings(Candidate).loopTime, Is.False);
            Assert.That(AnimationUtility.GetAnimationClipSettings(Candidate).keepOriginalOrientation, Is.True);
            foreach (var binding in AnimationUtility.GetCurveBindings(Source))
            {
                var sourceCurve = AnimationUtility.GetEditorCurve(Source, binding);
                var curve = AnimationUtility.GetEditorCurve(Candidate, binding);
                Assert.That(curve, Is.Not.Null, binding.propertyName);
                for (int sample = 0; sample <= 30; sample++)
                {
                    float t = Candidate.length * sample / 30f;
                    Assert.That(curve.Evaluate(t), Is.EqualTo(sourceCurve.Evaluate(SweepAnimationSetup.SourceStart + t))
                        .Within(.002f), binding.propertyName);
                }
            }
        }

        [Test]
        public void ActualAvatarPreservesEverySampledSourcePose()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            var graph = PlayableGraph.Create("Sweep crop regression");
            try
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Game/Prefabs/Characters/M6Art/P_M6_Player_Warrior.prefab");
                var model = Object.Instantiate(prefab);
                SceneManager.MoveGameObjectToScene(model, scene);
                var animator = model.GetComponentInChildren<Animator>(true);
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var anchorPosition = animator.transform.localPosition;
                var anchorRotation = animator.transform.localRotation;
                var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                    .Select(b => animator.GetBoneTransform((HumanBodyBones)b)).Where(b => b != null).ToArray();
                Assert.That(bones.Length, Is.GreaterThan(15));
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                var original = AnimationClipPlayable.Create(graph, Source);
                var cropped = AnimationClipPlayable.Create(graph, Candidate);
                original.SetApplyFootIK(false);
                original.SetApplyPlayableIK(false);
                cropped.SetApplyFootIK(false);
                cropped.SetApplyPlayableIK(false);
                graph.Play();
                void Sample(AnimationClipPlayable playable, float time)
                {
                    output.SetSourcePlayable(playable);
                    playable.SetTime(time);
                    graph.Evaluate(0);
                    animator.transform.SetLocalPositionAndRotation(anchorPosition, anchorRotation);
                }
                float maxDistance = 0, maxAngle = 0;
                for (int frame = 0; frame <= 23; frame++)
                {
                    float t = Mathf.Min(frame / 30f, Candidate.length);
                    Sample(original, SweepAnimationSetup.SourceStart + t);
                    var positions = bones.Select(b => b.position).ToArray();
                    var rotations = bones.Select(b => b.rotation).ToArray();
                    Sample(cropped, t);
                    maxDistance = Mathf.Max(maxDistance, bones.Select((b, i) => Vector3.Distance(b.position, positions[i])).Max());
                    maxAngle = Mathf.Max(maxAngle, bones.Select((b, i) => Quaternion.Angle(b.rotation, rotations[i])).Max());
                }
                Debug.Log($"[SWEEP_POSE] samples=24 maxBoneDistance={maxDistance:R} maxBoneAngle={maxAngle:R}");
                Assert.That(maxDistance, Is.LessThan(.001f));
                Assert.That(maxAngle, Is.LessThan(.1f));
            }
            finally
            {
                graph.Destroy();
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void OnlySweepUsesCandidateAndAuditStillRequiresContactEvidence()
        {
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>("Assets/_Game/Settings/PlayerAnimationSet_M1.asset");
            Assert.That(set.GetClip(CombatState.Sweep), Is.SameAs(Candidate));
            var states = ((AnimatorController)set.Controller).layers[0].stateMachine.states.Select(s => s.state).ToArray();
            Assert.That(states.Single(s => s.name == "Sweep").motion, Is.SameAs(Candidate));
            Assert.That(states.Where(s => s.name != "Sweep").Any(s => s.motion == Candidate), Is.False);
            var map = AttackTimingAudit.ReadMappings().Single(m => m.id == "player.sweep");
            var row = AttackTimingAudit.Evaluate(map, null);
            Assert.That(map.duration, Is.EqualTo(.82f).Within(.0001f));
            Assert.That(map.windowStart, Is.EqualTo(.24f).Within(.0001f));
            Assert.That(map.windowEnd, Is.EqualTo(.48f).Within(.0001f));
            Assert.That(row.requiredSpeed, Is.EqualTo((23f / 30f) / .82f).Within(.0001f));
            Assert.That(row.failures, Does.Not.Contain("speed-clamped"));
            Assert.That(row.failures, Does.Contain("contact-unconfirmed"));
            Assert.That(row.accepted, Is.False);
        }
    }
}
