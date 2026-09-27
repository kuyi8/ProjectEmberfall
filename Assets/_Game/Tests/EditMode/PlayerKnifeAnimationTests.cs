using System.IO;
using System.Linq;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class PlayerKnifeAnimationTests
    {
        private const string SetPath = "Assets/_Game/Settings/PlayerAnimationSet_M1.asset";
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
