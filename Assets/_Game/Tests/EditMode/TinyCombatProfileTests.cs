using System;
using System.Linq;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class TinyCombatProfileTests
    {
        const string Root="Assets/_Game/Art/Review/TinyHero/CombatCandidates/20261002-053304-723/";
        [Test]
        public void CandidateController_ContainsEveryOfflineStateAndExplicitLightRecovery_WithoutReplacingProduction()
        {
            var set=AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>(Root+"PlayerAnimationSet_Review_Tiny.asset");Assert.That(set,Is.Not.Null);
            var controller=(AnimatorController)set.Controller;
            var states=controller.layers[0].stateMachine.states.Select(s=>s.state).ToArray();
            foreach(CombatState state in Enum.GetValues(typeof(CombatState)))
                Assert.That(states.Single(s=>s.name==set.GetOfflineStateName(state)).motion,Is.Not.Null);
            Assert.That(states.Single(s=>s.name=="LightAttack1Recovery").motion,Is.SameAs(set.GetRecoveryClip(CombatState.LightAttack1)));
            Assert.That(states.Single(s=>s.name=="LightAttack2Recovery").motion,Is.SameAs(set.GetRecoveryClip(CombatState.LightAttack2)));
            Assert.That(states.All(s=>!s.writeDefaultValues),Is.True);
            Assert.That(AssetDatabase.GetAssetPath(AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>("Assets/_Game/Settings/PlayerAnimationSet_M1.asset").Controller),
                Is.EqualTo("Assets/_Game/Art/Animations/Player/AC_Player_M1.controller"));
        }

        [Test]
        public void LightRecoveryBoundary_UsesIdenticalAllCurveValues_NotOnlyFlattenedRoot()
        {
            for(int i=1;i<=2;i++)
            {
                var attack=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"A_Review_Tiny_LightAttack"+i+".anim");
                var recovery=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"A_Review_Tiny_LightAttack"+i+"Recovery.anim");
                Assert.That(AnimationUtility.GetCurveBindings(recovery).Length,Is.EqualTo(AnimationUtility.GetCurveBindings(attack).Length));
                foreach(var binding in AnimationUtility.GetCurveBindings(attack))
                {
                    var a=AnimationUtility.GetEditorCurve(attack,binding);var b=AnimationUtility.GetEditorCurve(recovery,binding);
                    Assert.That(b,Is.Not.Null,binding.propertyName);
                    Assert.That(a.Evaluate(attack.length),Is.EqualTo(b.Evaluate(0)).Within(.000001f),binding.path+"/"+binding.propertyName);
                }
            }
        }

        [Test]
        public void NativeStrikesAndDeath_KeepBodyOrientationButHaveNoRootPlanarTravelOrAnimationEvents()
        {
            foreach(string name in new[]{"LightAttack1","LightAttack2","LightAttack3","HeavyAttack","Sweep","HitReact","Dead"})
            {
                var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(Root+"A_Review_Tiny_"+name+".anim");
                Assert.That(clip,Is.Not.Null);Assert.That(clip.isLooping,Is.False);Assert.That(AnimationUtility.GetAnimationEvents(clip),Is.Empty);
                foreach(var binding in AnimationUtility.GetCurveBindings(clip).Where(b=>b.propertyName=="RootT.x"||b.propertyName=="RootT.z"))
                    Assert.That(AnimationUtility.GetEditorCurve(clip,binding).keys.All(k=>k.value==0),Is.True);
                // Flattening every RootQ erased the native fall; body orientation isn't the Actor transform.
                Assert.That(AnimationUtility.GetCurveBindings(clip).Where(b=>b.propertyName.StartsWith("RootQ.",StringComparison.Ordinal))
                    .Any(b=>AnimationUtility.GetEditorCurve(clip,b).keys.Select(k=>k.value).Distinct().Count()>1),Is.True);
                Assert.That(AnimationUtility.GetAnimationClipSettings(clip).loopBlendPositionY,Is.True);
            }
        }
    }
}
