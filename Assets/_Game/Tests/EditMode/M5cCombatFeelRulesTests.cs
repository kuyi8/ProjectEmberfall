using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class M5cCombatFeelRulesTests
    {
        [TestCase(0f, 2.4f, true)]
        [TestCase(84.9f, 2.4f, true)]
        [TestCase(85.1f, 2.4f, false)]
        [TestCase(0f, 2.46f, false)]
        public void MeleeSector_UsesAuthoredRangeAndAngle(float angleDegrees, float distance, bool expected)
        {
            float radians = angleDegrees * (float)(System.Math.PI / 180d);
            float targetX = (float)System.Math.Sin(radians) * distance;
            float targetZ = (float)System.Math.Cos(radians) * distance;

            bool result = MeleeSectorRules.Contains(
                0f, 0f, 0f, 1f,
                targetX, targetZ,
                2.45f, 170f);

            Assert.That(result, Is.EqualTo(expected));
        }

        [TestCase(ExecutionTargetKind.Ordinary, 25f, 100f, true)]
        [TestCase(ExecutionTargetKind.Ordinary, 25.1f, 100f, false)]
        [TestCase(ExecutionTargetKind.Elite, 1f, 100f, false)]
        [TestCase(ExecutionTargetKind.Boss, 1f, 100f, false)]
        public void ExecutionEligibility_EnforcesOrdinaryThresholdAndEliteGuardBreak(
            ExecutionTargetKind kind,
            float current,
            float maximum,
            bool expected)
        {
            Assert.That(
                ExecutionRules.IsEligible(kind, current / maximum, guardBroken: false, alreadyClaimed: false),
                Is.EqualTo(expected));
        }

        [Test]
        public void EliteExecution_IsAllowedOnlyOnceAndBossNeverIs()
        {
            Assert.That(ExecutionRules.IsEligible(
                ExecutionTargetKind.Elite, 1f, guardBroken: true, alreadyClaimed: false), Is.True);
            Assert.That(ExecutionRules.IsEligible(
                ExecutionTargetKind.Elite, 1f, guardBroken: true, alreadyClaimed: true), Is.False);
            Assert.That(ExecutionRules.IsEligible(
                ExecutionTargetKind.Boss, 0.01f, guardBroken: true, alreadyClaimed: false), Is.False);
        }
    }
}
