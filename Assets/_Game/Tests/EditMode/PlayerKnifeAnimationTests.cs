using System.IO;
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
using UnityEngine.Playables;

namespace Emberfall.Tests.EditMode
{
    public sealed class PlayerKnifeAnimationTests
    {
        private const string SetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
        [Test]
        public void ProductionScenesPersistGripWithoutChangingNetworkPrefabAndRewireIsIdempotent()
        {
            var previous = EditorSceneManager.GetSceneManagerSetup();
            var paths = new[] { "Assets/_Game/Scenes/10_EmberValley.unity", "Assets/_Game/Scenes/90_CombatGym.unity" };
            var bytes = paths.Select(File.ReadAllBytes).ToArray();
            try
            {
                foreach (var path in paths)
                {
                    var scene = EditorSceneManager.OpenScene(path);
                    var pose = AssetDatabase.LoadAssetAtPath<KnifeGripPose>(KnifeGripPoseSetup.Path);
                    Assert.That(pose, Is.Not.Null);
                    var launcher = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<PlayerThrowingKnifeLauncher>(true)).Single();
                    Assert.That(launcher.PresentationGrip, Is.SameAs(pose), path);
                }
                KnifeGripPoseSetup.WireProduction();
                for (int i = 0; i < paths.Length; i++) Assert.That(File.ReadAllBytes(paths[i]), Is.EqualTo(bytes[i]), paths[i]);
                var network = AssetDatabase.LoadAssetAtPath<GameObject>(M6VisualFoundationSetup.PlayerPath);
                Assert.That(network, Is.Not.Null);
                Assert.That(network.GetComponentsInChildren<PlayerThrowingKnifeLauncher>(true), Is.Empty);
                Assert.That(network.GetComponentsInChildren<PlayerKnifePresentation>(true), Is.Empty);
            }
            finally
            {
                if (previous.Any(x => x.isLoaded && x.isActive)) EditorSceneManager.RestoreSceneManagerSetup(previous);
                else EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            }
        }

        [Test]
        public void RangerGripIsExactlyOneSameSourceWholeHandFrame()
        {
            var pose = AssetDatabase.LoadAssetAtPath<KnifeGripPose>(KnifeGripPoseSetup.Path);
            Assert.That(pose, Is.Not.Null);
            Assert.That(pose.avatarPath, Is.EqualTo(KnifeGripPoseSetup.AvatarPath));
            Assert.That(pose.sourcePath, Is.EqualTo(PlayerKnifeAnimationSetup.SourcePath));
            Assert.That(pose.sourceTime, Is.EqualTo(1.3f));
            Assert.That(pose.sourceHash, Is.EqualTo(AssetDatabase.GetAssetDependencyHash(pose.sourcePath).ToString()));
            Assert.That(pose.joints.Length, Is.EqualTo(16));
            Assert.That(pose.joints[0].bone, Is.EqualTo(HumanBodyBones.RightHand));
            var clone = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(pose.avatarPath));
            var graph = PlayableGraph.Create("Verify actual Ranger whole hand");
            try
            {
                var animator = clone.GetComponentInChildren<Animator>();
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                var playable = UnityEngine.Animations.AnimationClipPlayable.Create(graph,
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(pose.sourcePath));
                playable.SetApplyFootIK(false); playable.SetApplyPlayableIK(false);
                UnityEngine.Animations.AnimationPlayableOutput.Create(graph,"Pose",animator).SetSourcePlayable(playable);
                graph.Play(); playable.SetTime(pose.sourceTime); graph.Evaluate(0);
                foreach (var joint in pose.joints)
                    Assert.That(Quaternion.Angle(joint.rotation,animator.GetBoneTransform(joint.bone).localRotation),
                        Is.LessThan(.01f), joint.bone.ToString());
            }
            finally { graph.Destroy(); Object.DestroyImmediate(clone); }
        }

        [Test]
        public void OfflineThrowHasIsolatedMappingAndPreservedSourceCurves()
        {
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(SetPath);
            var source = AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerKnifeAnimationSetup.SourcePath);
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerKnifeAnimationSetup.CandidatePath);
            Assert.That(clip, Is.Not.Null);
            Assert.That(source.length, Is.EqualTo(40f / 30f).Within(.00001f));
            Assert.That(clip.length, Is.EqualTo(PlayerKnifeAnimationSetup.SourceEnd).Within(.00001f));
            Assert.That(set.GetClip(CombatState.RangedAttack), Is.SameAs(source));
            Assert.That(set.GetOfflineClip(CombatState.RangedAttack), Is.SameAs(clip));
            var states = ((AnimatorController)set.Controller).layers[0].stateMachine.states.Select(s => s.state).ToArray();
            Assert.That(states.Single(s => s.name == "RangedAttack").motion, Is.SameAs(source));
            Assert.That(states.Single(s => s.name == set.GetOfflineStateName(CombatState.RangedAttack)).motion, Is.SameAs(clip));
            Assert.That(states.Count(s => s.motion == clip), Is.EqualTo(1));
            Assert.That(clip.events, Is.Empty);
            Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.False);
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var original = AnimationUtility.GetEditorCurve(source, binding);
                var cropped = AnimationUtility.GetEditorCurve(clip, binding);
                Assert.That(cropped, Is.Not.Null, binding.propertyName);
                for (int i = 0; i <= 30; i++)
                {
                    float t = clip.length * i / 30f;
                    Assert.That(cropped.Evaluate(t), Is.EqualTo(original.Evaluate(t)).Within(.002f), binding.propertyName);
                }
            }
        }

        [Test]
        public void ReleaseRulesStayUnchangedAndAuditCannotInferHandRelease()
        {
            foreach (var tuning in new[] { CombatTuning.CreateDefault(),
                AssetDatabase.LoadAssetAtPath<CombatTuningAsset>(AttackTimingAudit.TuningPath).CreateRuntimeCopy() })
            {
                Assert.That(tuning.RangedDuration, Is.EqualTo(.56f));
                Assert.That(tuning.RangedReleaseTime, Is.EqualTo(.22f));
                Assert.That(tuning.RangedDamage, Is.EqualTo(30f));
                Assert.That(tuning.RangedCooldown, Is.EqualTo(3.5f));
            }
            var map = AttackTimingAudit.ReadMappings().Single(m => m.id == "player.knife");
            Assert.That(map.clip, Is.SameAs(AssetDatabase.LoadAssetAtPath<AnimationClip>(PlayerKnifeAnimationSetup.CandidatePath)));
            Assert.That(map.pointEvent, Is.True);
            Assert.That(AttackTimingAudit.Evaluate(map, null).accepted, Is.False);
        }

        [Test]
        public void ReapplyingSetupIsIdempotentAndDoesNotModifySharedSource()
        {
            string Hash(string path) => AssetDatabase.GetAssetDependencyHash(path).ToString();
            var sourceBefore = Hash(PlayerKnifeAnimationSetup.SourcePath);
            // Unity restores trailing spaces on empty YAML values after repository formatting.
            // Compare every serialized field first, then require exact dependency-hash stability
            // on a second generation. Do not let formatting mask curve/configuration changes.
            string Canonical(string path) => string.Join("\n", File.ReadAllLines(path).Select(line => line.TrimEnd()));
            var set = AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(SetPath);
            var paths = new[] { PlayerKnifeAnimationSetup.CandidatePath, SetPath,
                AssetDatabase.GetAssetPath(set.Controller), AttackTimingAudit.AnnotationPath };
            var before = paths.Select(Canonical).ToArray();
            PlayerKnifeAnimationSetup.Apply();
            Assert.That(Hash(PlayerKnifeAnimationSetup.SourcePath), Is.EqualTo(sourceBefore));
            for (int i = 0; i < paths.Length; i++) Assert.That(Canonical(paths[i]), Is.EqualTo(before[i]), paths[i]);
            var once = paths.Select(Hash).ToArray();
            PlayerKnifeAnimationSetup.Apply();
            Assert.That(Hash(PlayerKnifeAnimationSetup.SourcePath), Is.EqualTo(sourceBefore));
            for (int i = 0; i < paths.Length; i++) Assert.That(Hash(paths[i]), Is.EqualTo(once[i]), paths[i]);
        }
    }
}
