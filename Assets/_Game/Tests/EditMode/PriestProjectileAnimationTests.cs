using System.Linq;
using Emberfall.Editor.Setup;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class PriestProjectileAnimationTests
    {
        private static PlayerAnimationSet Set => AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>("Assets/_Game/Settings/PlayerAnimationSet_M1.asset");

        [TestCase(EnemyAnimationAction.PriestProjectileWindup, 0f, 13f/30f)]
        [TestCase(EnemyAnimationAction.PriestProjectileRelease, 13f/30f, 1.3f)]
        public void DerivedCurvesPreserveObservedSourceRange(EnemyAnimationAction action, float start, float end)
        {
            var source = Set.GetClip(CombatState.HeavyAttack);
            var clip = Set.GetEnemyClip(action);
            Assert.That(clip, Is.Not.Null);
            Assert.That(clip.length, Is.EqualTo(end-start).Within(.0001f));
            Assert.That(clip.events, Is.Empty);
            Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopTime, Is.False);
            foreach (var binding in AnimationUtility.GetCurveBindings(source))
            {
                var original = AnimationUtility.GetEditorCurve(source, binding);
                var cropped = AnimationUtility.GetEditorCurve(clip, binding);
                Assert.That(cropped, Is.Not.Null, binding.propertyName);
                for (int sample=0; sample<=30; sample++)
                {
                    float t = (end-start)*sample/30f;
                    Assert.That(cropped.Evaluate(t), Is.EqualTo(original.Evaluate(start+t)).Within(.002f), binding.propertyName);
                }
            }
        }

        [Test] public void SplitSharesBoundaryCurvesAndControllerStates()
        {
            var windup = Set.GetEnemyClip(EnemyAnimationAction.PriestProjectileWindup);
            var release = Set.GetEnemyClip(EnemyAnimationAction.PriestProjectileRelease);
            var windupSettings = AnimationUtility.GetAnimationClipSettings(windup);
            var releaseSettings = AnimationUtility.GetAnimationClipSettings(release);
            Assert.That(windupSettings.keepOriginalOrientation, Is.True);
            Assert.That(releaseSettings.keepOriginalOrientation, Is.True);
            Assert.That(releaseSettings.orientationOffsetY, Is.EqualTo(windupSettings.orientationOffsetY));
            foreach (var binding in AnimationUtility.GetCurveBindings(windup))
                Assert.That(AnimationUtility.GetEditorCurve(release,binding).Evaluate(0),
                    Is.EqualTo(AnimationUtility.GetEditorCurve(windup,binding).Evaluate(windup.length)).Within(.0001f), binding.propertyName);
            var states = ((AnimatorController)Set.Controller).layers[0].stateMachine.states.Select(s=>s.state).ToArray();
            Assert.That(states.Single(s=>s.name=="PriestProjectileWindup").motion, Is.SameAs(windup));
            Assert.That(states.Single(s=>s.name=="PriestProjectileRelease").motion, Is.SameAs(release));
            Assert.That(states.Single(s=>s.name=="HeavyAttack").motion, Is.SameAs(Set.GetClip(CombatState.HeavyAttack)));
            Assert.That(states.Single(s=>s.name=="Execution").motion, Is.SameAs(Set.GetClip(CombatState.HeavyAttack)));
        }

        [Test] public void ProjectileIsInSpeedRangeWithoutInventingContact()
        {
            var map = AttackTimingAudit.ReadMappings().Single(m=>m.id=="priest.projectile");
            var row = AttackTimingAudit.Evaluate(map, null);
            Assert.That(row.requiredSpeed, Is.InRange(.35f,3f));
            Assert.That(row.failures, Does.Not.Contain("speed-clamped"));
            Assert.That(row.accepted, Is.False);
            Assert.That(row.failures, Does.Contain("contact-unconfirmed"));
            Assert.That(map.duration, Is.EqualTo(.35f).Within(.0001f));
            Assert.That(map.windowStart, Is.Zero);
            Assert.That(map.windowEnd, Is.Zero);
        }
    }
}
