using System.Linq;
using Emberfall.Editor.Review;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class TinyPoseCandidateTests
    {
        [Test]
        public void EvadeEnvelope_EndsAtActualInvulnerability_NotAtActionEnd()
        {
            var tuning=AssetDatabase.LoadAssetAtPath<Emberfall.Gameplay.Combat.Unity.CombatTuningAsset>("Assets/_Game/Settings/CombatTuning_M1.asset").CreateRuntimeCopy();
            Assert.That(tuning.DodgeDuration,Is.EqualTo(.52f).Within(.0001f));
            Assert.That(tuning.DodgeInvulnerabilitySeconds,Is.EqualTo(.43f).Within(.0001f));
            Assert.That(TinyPoseCandidateReview.Envelope(0,.085f,.31f,tuning.DodgeInvulnerabilitySeconds),Is.Zero);
            Assert.That(TinyPoseCandidateReview.Envelope(.1f,.085f,.31f,tuning.DodgeInvulnerabilitySeconds),Is.EqualTo(1));
            for(float t=tuning.DodgeInvulnerabilitySeconds;t<=tuning.DodgeDuration;t+=.001f)
                Assert.That(TinyPoseCandidateReview.Envelope(t,.085f,.31f,tuning.DodgeInvulnerabilitySeconds),Is.Zero);
        }

        [Test]
        public void MuscleAliases_MatchActualImportedHumanoidBindings()
        {
            var clip=AssetDatabase.LoadAllAssetsAtPath("Assets/RPG Tiny Hero Duo/Animation/SwordAndShield/Idle_Battle_SwordAndShiled.fbx")
                .OfType<AnimationClip>().Single(c=>!c.name.StartsWith("__preview"));
            var properties=AnimationUtility.GetCurveBindings(clip).Select(b=>b.propertyName).ToArray();
            var mapped=HumanTrait.MuscleName.Select(TinyPoseCandidateReview.MuscleProperty).ToArray();
            Assert.That(mapped.Distinct().Count(),Is.EqualTo(HumanTrait.MuscleCount));
            foreach(string property in mapped) Assert.That(properties,Does.Contain(property));
        }

        [Test]
        public void BlendEnvelopes_AreBoundedAndContinuous_WithNoFrameSpike()
        {
            float previous=0;
            for(int i=0;i<=520;i++)
            {
                float weight=TinyPoseCandidateReview.Envelope(i*.001f,.085f,.31f,.43f);
                Assert.That(weight,Is.InRange(0f,1f));
                Assert.That(Mathf.Abs(weight-previous),Is.LessThan(.02f));
                previous=weight;
            }
        }
    }
}
