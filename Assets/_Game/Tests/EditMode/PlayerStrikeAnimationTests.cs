using System.Linq;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
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
    public sealed class PlayerStrikeAnimationTests
    {
        [Test]
        public void DedicatedStatesDoNotReplaceSharedEnemyOrNetworkMappings()
        {
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>("Assets/_Game/Settings/PlayerAnimationSet_M1.asset");
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerStrikeAnimationSetup.SourcePath);
            var states = ((AnimatorController)set.Controller).layers[0].stateMachine.states.Select(s => s.state).ToArray();
            foreach (var pair in new[] { (CombatState.HeavyAttack, PlayerStrikeAnimationSetup.HeavyPath),
                (CombatState.Execution, PlayerStrikeAnimationSetup.ExecutionPath) })
            {
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(pair.Item2);
                Assert.That(clip, Is.Not.Null);
                Assert.That(set.GetOfflineClip(pair.Item1), Is.SameAs(clip));
                Assert.That(set.GetClip(pair.Item1), Is.SameAs(source));
                Assert.That(states.Single(s => s.name == pair.Item1.ToString()).motion, Is.SameAs(source));
                Assert.That(states.Single(s => s.name == set.GetOfflineStateName(pair.Item1)).motion, Is.SameAs(clip));
                Assert.That(states.Count(s => s.motion == clip), Is.EqualTo(1));
                Assert.That(clip.events, Is.Empty);
                Assert.That(clip.length, Is.EqualTo(source.length));
            }
            Assert.That(set.GetOfflineClip(CombatState.HeavyAttack), Is.Not.SameAs(set.GetOfflineClip(CombatState.Execution)));
        }

        [Test]
        public void CalibrationPreservesDurationWidthDamageAndPointSemantics()
        {
            var asset = AssetDatabase.LoadAssetAtPath<CombatTuningAsset>(AttackTimingAudit.TuningPath).CreateRuntimeCopy();
            foreach (var tuning in new[] { asset, CombatTuning.CreateDefault() })
            {
                Assert.That(tuning.HeavyDamageOpen, Is.EqualTo(.235f));
                Assert.That(tuning.HeavyDamageClose - tuning.HeavyDamageOpen, Is.EqualTo(.24f).Within(.000001f));
                Assert.That(tuning.HeavyDuration, Is.EqualTo(.82f));
                Assert.That(tuning.HeavyDamage, Is.EqualTo(55f));
                Assert.That(tuning.ExecutionResolveTime, Is.EqualTo(.21f));
                Assert.That(tuning.ExecutionDuration, Is.EqualTo(.72f));
                Assert.That(tuning.ExecutionStaminaCost, Is.EqualTo(22f));
            }
            var maps = AttackTimingAudit.ReadMappings();
            Assert.That(maps.Single(m => m.id == "player.execution").pointEvent, Is.True);
            Assert.That(maps.Single(m => m.id == "player.heavy").pointEvent, Is.False);
        }

        [TestCase(PlayerStrikeAnimationSetup.HeavyPath)]
        [TestCase(PlayerStrikeAnimationSetup.ExecutionPath)]
        [TestCase(PlayerKnifeAnimationSetup.CandidatePath)]
        public void FullSourceAvatarPosesArePreserved(string path)
        {
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(path == PlayerKnifeAnimationSetup.CandidatePath
                ? PlayerKnifeAnimationSetup.SourcePath : PlayerStrikeAnimationSetup.SourcePath);
            var candidate = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            var scene = EditorSceneManager.NewPreviewScene();
            var graph = PlayableGraph.Create("Dedicated strike pose regression");
            try
            {
                var model = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/_Game/Prefabs/Characters/M6Art/P_M6_Player_Warrior.prefab"));
                SceneManager.MoveGameObjectToScene(model, scene);
                var animator = model.GetComponentInChildren<Animator>(true);
                animator.applyRootMotion = false; animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                var anchorPosition = animator.transform.localPosition; var anchorRotation = animator.transform.localRotation;
                var bones = Enumerable.Range(0, (int)HumanBodyBones.LastBone)
                    .Select(b => animator.GetBoneTransform((HumanBodyBones)b)).Where(b => b != null).ToArray();
                Assert.That(bones.Length, Is.GreaterThan(15));
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var output = AnimationPlayableOutput.Create(graph, "Pose", animator);
                var original = AnimationClipPlayable.Create(graph, source);
                var copy = AnimationClipPlayable.Create(graph, candidate);
                original.SetApplyFootIK(false); original.SetApplyPlayableIK(false);
                copy.SetApplyFootIK(false); copy.SetApplyPlayableIK(false); graph.Play();
                void Sample(AnimationClipPlayable playable, float time)
                {
                    output.SetSourcePlayable(playable); playable.SetTime(time); graph.Evaluate(0);
                    animator.transform.SetLocalPositionAndRotation(anchorPosition, anchorRotation);
                }
                float maxDistance = 0, maxAngle = 0;
                for (int frame = 0; frame < 24; frame++)
                {
                    float time = candidate.length * frame / 23f;
                    Sample(original, time);
                    var positions = bones.Select(b => b.position).ToArray();
                    var rotations = bones.Select(b => b.rotation).ToArray();
                    Sample(copy, time);
                    maxDistance = Mathf.Max(maxDistance, bones.Select((b, i) => Vector3.Distance(b.position, positions[i])).Max());
                    maxAngle = Mathf.Max(maxAngle, bones.Select((b, i) => Quaternion.Angle(b.rotation, rotations[i])).Max());
                }
                Debug.Log($"[PLAYER_STRIKE_POSE] clip={candidate.name} samples=24 maxDistance={maxDistance:R} maxAngle={maxAngle:R}");
                Assert.That(maxDistance, Is.LessThan(.001f));
                Assert.That(maxAngle, Is.LessThan(.1f));
            }
            finally { graph.Destroy(); EditorSceneManager.ClosePreviewScene(scene); }
        }
    }
}
