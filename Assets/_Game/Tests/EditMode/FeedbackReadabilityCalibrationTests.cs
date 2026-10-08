using System;
using Emberfall.Gameplay.Animation;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class FeedbackReadabilityCalibrationTests
    {
        [TestCase(0f, 0f)]
        [TestCase(1.1f, -3.65f)]
        [TestCase(2.2f, -7.3f)]
        [TestCase(3.8f, -17.3f)]
        [TestCase(5.4f, -27.3f)]
        [TestCase(6.8f, -23.2f)]
        [TestCase(8.2f, -19.1f)]
        public void OfflineYaw_UsesOwnedTreeLinearThresholdWeights(float speed, float yaw)
        {
            var set=AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>("Assets/_Game/Settings/PlayerAnimationSet_M1.asset");
            Assert.That(set.GetOfflineLocomotionYaw(CombatState.Locomotion,speed),Is.EqualTo(yaw).Within(.001f));
        }

        [Test]
        public void OfflineYaw_NeverAppliesToActionsOrLegacySets_AndDoesNotMutateAssets()
        {
            var set=AssetDatabase.LoadAssetAtPath<PlayerAnimationSet>("Assets/_Game/Settings/PlayerAnimationSet_M1.asset");
            string before=EditorJsonUtility.ToJson(set);
            foreach(CombatState state in Enum.GetValues(typeof(CombatState)))
                if(state!=CombatState.Locomotion)Assert.That(set.GetOfflineLocomotionYaw(state,5.4f),Is.Zero,state.ToString());
            foreach(float speed in new[]{float.NaN,float.PositiveInfinity,-1f})
                Assert.That(set.GetOfflineLocomotionYaw(CombatState.Locomotion,speed),Is.Zero);
            var legacy=ScriptableObject.CreateInstance<PlayerAnimationSet>();
            try{Assert.That(legacy.GetOfflineLocomotionYaw(CombatState.Locomotion,5.4f),Is.Zero);}
            finally{UnityEngine.Object.DestroyImmediate(legacy);}
            Assert.That(EditorJsonUtility.ToJson(set),Is.EqualTo(before));
        }

        [TestCase(HitFeedbackGrade.Light)]
        [TestCase(HitFeedbackGrade.Heavy)]
        [TestCase(HitFeedbackGrade.Sweep)]
        public void LocalOrdinaryFlesh_AllThreeVariantsGainFiveDb_WithoutClamping(HitFeedbackGrade grade)
        {
            var set=AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>("Assets/_Game/Settings/CombatImpactAudio_M6.asset");
            string before=EditorJsonUtility.ToJson(set);
            for(int i=0;i<3;i++){
                var p=set.Select(grade,ImpactSurface.Flesh,-1,(i+.1f)/3f,.5f);
                float baseline=set.VoiceVolume(true)*p.Gain;
                float revised=set.PlaybackVolume(true,grade,ImpactSurface.Flesh,p.Gain);
                Assert.That(20*Math.Log10(revised/baseline),Is.EqualTo(5d).Within(.0001d));
                Assert.That(revised,Is.LessThan(1f),"The final .7 * variant * presence product must not clip.");
            }
            Assert.That(EditorJsonUtility.ToJson(set),Is.EqualTo(before));
        }

        [Test]
        public void MetalAndSpecialsAndRemote_AllVariantsRetainExactLegacyProduct_AfterLocalSelection()
        {
            var set=AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>("Assets/_Game/Settings/CombatImpactAudio_M6.asset");
            foreach(HitFeedbackGrade grade in Enum.GetValues(typeof(HitFeedbackGrade)))
                foreach(ImpactSurface surface in Enum.GetValues(typeof(ImpactSurface)))
                    for(int i=0;i<3;i++){
                        var p=set.Select(grade,surface,-1,(i+.1f)/3f,.5f);
                        set.PlaybackVolume(true,HitFeedbackGrade.Light,ImpactSurface.Flesh,.5f);
                        Assert.That(set.PlaybackVolume(false,grade,surface,p.Gain),Is.EqualTo(.6f*p.Gain));
                        if(surface==ImpactSurface.Metal||grade==HitFeedbackGrade.GuardBreak||grade==HitFeedbackGrade.Execution||grade==HitFeedbackGrade.PerfectDefense||grade==HitFeedbackGrade.None)
                            Assert.That(set.PlaybackVolume(true,grade,surface,p.Gain),Is.EqualTo(.7f*p.Gain));
                    }
        }
    }
}
