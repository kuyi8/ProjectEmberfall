using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Combat.Unity;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class HitFeedbackRulesTests
    {
        [TestCase(HitFeedbackGrade.Light, .05f)]
        [TestCase(HitFeedbackGrade.Sweep, .08f)]
        [TestCase(HitFeedbackGrade.Heavy, .10f)]
        [TestCase(HitFeedbackGrade.GuardBreak, .16f)]
        [TestCase(HitFeedbackGrade.Execution, .22f)]
        public void Duration_IsApprovedValue(HitFeedbackGrade grade, float duration) =>
            Assert.That(HitFeedbackRules.Duration(grade), Is.EqualTo(duration));

        [Test]
        public void RejectedOrEvadedDamage_NeverProducesFeedback()
        {
            Assert.That(HitFeedbackRules.Classify(DamageResult.Ignored, AttackTag.Heavy, true), Is.EqualTo(HitFeedbackGrade.None));
            Assert.That(HitFeedbackRules.Classify(DamageResult.Evaded, AttackTag.Light), Is.EqualTo(HitFeedbackGrade.None));
        }

        [Test]
        public void BreakSupersedesHeavy_ExecutionSupersedesBreak()
        {
            var result = new DamageResult(true, false, 20f, false, guardBroken: true);
            Assert.That(HitFeedbackRules.Classify(result, AttackTag.Heavy), Is.EqualTo(HitFeedbackGrade.GuardBreak));
            Assert.That(HitFeedbackRules.Classify(result, AttackTag.Heavy, true), Is.EqualTo(HitFeedbackGrade.Execution));
            var posture = new DamageResult(true, false, 20f, false, staggered: true);
            Assert.That(HitFeedbackRules.Classify(posture, AttackTag.Light), Is.EqualTo(HitFeedbackGrade.GuardBreak));
        }

        [Test]
        public void SameFrameMultiTarget_CoalescesStrongest_AndConfirmedSequenceDoesNotReplay()
        {
            var batch = new HitFeedbackBatch();
            Assert.That(batch.Offer(Impact(1, HitFeedbackGrade.Light)), Is.True);
            Assert.That(batch.Offer(Impact(2, HitFeedbackGrade.GuardBreak)), Is.True);
            Assert.That(batch.Offer(Impact(3, HitFeedbackGrade.Heavy)), Is.True);
            Assert.That(batch.Take(out var strongest), Is.True);
            Assert.That(strongest.Grade, Is.EqualTo(HitFeedbackGrade.GuardBreak));
            Assert.That(batch.Take(out _), Is.False);
            Assert.That(batch.Offer(Impact(2, HitFeedbackGrade.Execution)), Is.False);
            Assert.That(batch.Offer(Impact(3, HitFeedbackGrade.Heavy)), Is.False);
            Assert.That(batch.Offer(Impact(4, HitFeedbackGrade.Heavy)), Is.True);
        }

        [Test]
        public void AudioRouting_IsAuthoredAndSeparateFromVfxStyle()
        {
            var set = UnityEditor.AssetDatabase.LoadAssetAtPath<CombatImpactAudioSet>(
                "Assets/_Game/Settings/CombatImpactAudio_M6.asset");
            Assert.That(set, Is.Not.Null);
            foreach (HitFeedbackGrade grade in new[] { HitFeedbackGrade.Light, HitFeedbackGrade.Sweep,
                         HitFeedbackGrade.Heavy, HitFeedbackGrade.GuardBreak, HitFeedbackGrade.Execution })
                foreach (ImpactSurface surface in new[] { ImpactSurface.Flesh, ImpactSurface.Metal })
                    Assert.That(set.Resolve(grade, surface), Is.Not.Null);
            Assert.That(set.Resolve(HitFeedbackGrade.Light, ImpactSurface.Flesh),
                Is.Not.SameAs(set.Resolve(HitFeedbackGrade.Light, ImpactSurface.Metal)));
        }

        private static CombatImpactPresentationEvent Impact(ulong id, HitFeedbackGrade grade) =>
            new CombatImpactPresentationEvent(Vector3.zero, CombatImpactStyle.Ember, id, 1,
                (int)id, grade, ImpactSurface.Flesh);
    }
}
