using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Networking;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class NetworkCombatValidationTests
    {
        [Test]
        public void NetworkPlayerMeleeSectorMatchesOfflineForZeroAndMultiTargetSets()
        {
            var targetSets = new[]
            {
                new[] { (x: 0f, z: -1f), (x: 2.46f, z: 0f) },
                new[] { (x: -1.5f, z: 1.5f), (x: 0f, z: 2.4f), (x: 1.5f, z: 1.5f), (x: 0f, z: -1f) }
            };
            var expectedCounts = new[] { 0, 3 };

            for (int setIndex = 0; setIndex < targetSets.Length; setIndex++)
            {
                int offlineCount = 0;
                int networkCount = 0;
                foreach (var target in targetSets[setIndex])
                {
                    if (MeleeSectorRules.Contains(
                            0f, 0f, 0f, 1f, target.x, target.z,
                            MeleeSectorRules.DefaultRadius,
                            MeleeSectorRules.DefaultFullAngleDegrees))
                        offlineCount++;
                    if (NetworkCombatSpatialValidator.IsValidPlayerMeleeHit(
                            0f, 0f, 0f, 1f, target.x, target.z))
                        networkCount++;
                }

                Assert.That(networkCount, Is.EqualTo(offlineCount));
                Assert.That(networkCount, Is.EqualTo(expectedCounts[setIndex]));
            }
        }

        [Test]
        public void IntentValidatorAcceptsIncreasingLightAttackSequence()
        {
            var validator = new NetworkCombatIntentValidator();

            Assert.That(validator.TryAccept(1, CombatCommand.LightAttack, out string firstReason), Is.True);
            Assert.That(firstReason, Is.Empty);
            Assert.That(validator.TryAccept(2, CombatCommand.LightAttack, out string secondReason), Is.True);
            Assert.That(secondReason, Is.Empty);
            Assert.That(validator.LastAcceptedSequence, Is.EqualTo(2));
        }

        [Test]
        public void IntentValidatorRejectsReplayAndDoesNotAdvanceAuthoritySequence()
        {
            var validator = new NetworkCombatIntentValidator();
            Assert.That(validator.TryAccept(7, CombatCommand.LightAttack, out _), Is.True);

            Assert.That(validator.TryAccept(7, CombatCommand.LightAttack, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("stale-sequence"));
            Assert.That(validator.LastAcceptedSequence, Is.EqualTo(7));
        }

        [Test]
        public void IntentValidatorAcceptsMigratedDefenseCommands()
        {
            var validator = new NetworkCombatIntentValidator();

            Assert.That(validator.TryAccept(1, CombatCommand.Dodge, out _), Is.True);
            Assert.That(validator.TryAccept(2, CombatCommand.GuardPressed, out _), Is.True);
            Assert.That(validator.TryAccept(3, CombatCommand.GuardReleased, out _), Is.True);
            Assert.That(validator.LastAcceptedSequence, Is.EqualTo(3));
        }

        [Test]
        public void IntentValidatorAcceptsMigratedAttackAndHealCommands()
        {
            var validator = new NetworkCombatIntentValidator();

            Assert.That(validator.TryAccept(1, CombatCommand.HeavyPressed, out _), Is.True);
            Assert.That(validator.TryAccept(2, CombatCommand.HeavyReleased, out _), Is.True);
            Assert.That(validator.TryAccept(3, CombatCommand.RangedAttack, out _), Is.True);
            Assert.That(validator.TryAccept(4, CombatCommand.Heal, out _), Is.True);
            Assert.That(validator.LastAcceptedSequence, Is.EqualTo(4));
        }

        [Test]
        public void IntentValidatorRejectsUnknownCommandWithoutAdvancingSequence()
        {
            var validator = new NetworkCombatIntentValidator();

            Assert.That(validator.TryAccept(1, (CombatCommand)255, out string reason), Is.False);
            Assert.That(reason, Is.EqualTo("unsupported-command"));
            Assert.That(validator.LastAcceptedSequence, Is.Zero);
        }

        [Test]
        public void HeavyReleaseWithoutAcceptedChargeIsRejectedByDomain()
        {
            var validator = new NetworkCombatIntentValidator();
            var combat = new CombatStateMachine(CombatTuning.CreateDefault());

            Assert.That(validator.TryAccept(1, CombatCommand.HeavyReleased, out _), Is.True);
            Assert.That(combat.Submit(CombatCommand.HeavyReleased), Is.False);
            Assert.That(combat.State, Is.EqualTo(CombatState.Locomotion));
            Assert.That(combat.AttackSequence, Is.Zero);
        }

        [Test]
        public void DodgeDirectionValidatorNormalizesFiniteInput()
        {
            Assert.That(NetworkCombatIntentValidator.TryNormalizeDodgeDirection(
                0.6f, 0.8f, out float x, out float z, out string reason), Is.True);
            Assert.That(reason, Is.Empty);
            Assert.That(x, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(z, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void DodgeDirectionValidatorRejectsNonFiniteAndOversizedInput()
        {
            Assert.That(NetworkCombatIntentValidator.TryNormalizeDodgeDirection(
                float.NaN, 0f, out _, out _, out string nonFiniteReason), Is.False);
            Assert.That(nonFiniteReason, Is.EqualTo("non-finite-dodge-direction"));
            Assert.That(NetworkCombatIntentValidator.TryNormalizeDodgeDirection(
                2f, 0f, out _, out _, out string oversizedReason), Is.False);
            Assert.That(oversizedReason, Is.EqualTo("dodge-direction-out-of-range"));
        }

        [Test]
        public void AimDirectionValidatorNormalizesFiniteBoundedInput()
        {
            Assert.That(NetworkCombatIntentValidator.TryNormalizeAimDirection(
                0.6f, 0.8f, out float x, out float z, out string reason), Is.True);
            Assert.That(reason, Is.Empty);
            Assert.That(x, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(z, Is.EqualTo(0.8f).Within(0.0001f));
        }

        [Test]
        public void AimDirectionValidatorRejectsZeroNonFiniteAndOversizedInput()
        {
            Assert.That(NetworkCombatIntentValidator.TryNormalizeAimDirection(
                0f, 0f, out _, out _, out string zeroReason), Is.False);
            Assert.That(zeroReason, Is.EqualTo("aim-direction-out-of-range"));
            Assert.That(NetworkCombatIntentValidator.TryNormalizeAimDirection(
                float.PositiveInfinity, 0f, out _, out _, out string nonFiniteReason), Is.False);
            Assert.That(nonFiniteReason, Is.EqualTo("non-finite-aim-direction"));
            Assert.That(NetworkCombatIntentValidator.TryNormalizeAimDirection(
                2f, 0f, out _, out _, out string oversizedReason), Is.False);
            Assert.That(oversizedReason, Is.EqualTo("aim-direction-out-of-range"));
        }

        [Test]
        public void ProjectileSweepAcceptsFrontLaneTargetAndReturnsDistance()
        {
            Assert.That(NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                0f, 0f, 0f, 1f, 0.3f, 8f, 16f, 0.56f, out float distance), Is.True);
            Assert.That(distance, Is.EqualTo(8f).Within(0.0001f));
        }

        [Test]
        public void ProjectileSweepRejectsRearOffLaneOutOfRangeAndNonFiniteTargets()
        {
            Assert.That(NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                0f, 0f, 0f, 1f, 0f, -1f, 16f, 0.56f, out _), Is.False);
            Assert.That(NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                0f, 0f, 0f, 1f, 0.57f, 8f, 16f, 0.56f, out _), Is.False);
            Assert.That(NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                0f, 0f, 0f, 1f, 0f, 16.01f, 16f, 0.56f, out _), Is.False);
            Assert.That(NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                0f, 0f, 0f, 1f, float.NaN, 8f, 16f, 0.56f, out _), Is.False);
        }

        [Test]
        public void SpatialValidatorAcceptsTargetInsideRangeAndFrontArc()
        {
            Assert.That(NetworkCombatSpatialValidator.IsValidMeleeHit(
                0f, 0f, 0f, 1f, 0.5f, 2f, 2.5f, 0.1f), Is.True);
        }

        [Test]
        public void SpatialValidatorRejectsTargetBehindAttacker()
        {
            Assert.That(NetworkCombatSpatialValidator.IsValidMeleeHit(
                0f, 0f, 0f, 1f, 0f, -1f, 2.5f, 0.1f), Is.False);
        }

        [Test]
        public void SpatialValidatorRejectsTargetOutsideAuthoritativeRange()
        {
            Assert.That(NetworkCombatSpatialValidator.IsValidMeleeHit(
                0f, 0f, 0f, 1f, 0f, 2.51f, 2.5f, 0.1f), Is.False);
        }

        [Test]
        public void SpatialValidatorRejectsNonFiniteClientFacts()
        {
            Assert.That(NetworkCombatSpatialValidator.IsValidMeleeHit(
                float.NaN, 0f, 0f, 1f, 0f, 1f, 2.5f, 0.1f), Is.False);
        }

        [Test]
        public void DefenseArcUsesServerPoseAndRejectsRearThreat()
        {
            Assert.That(NetworkCombatSpatialValidator.IsThreatInFrontArc(
                0f, 0f, 0f, 1f, 0f, 2f), Is.True);
            Assert.That(NetworkCombatSpatialValidator.IsThreatInFrontArc(
                0f, 0f, 0f, 1f, 0f, -2f), Is.False);
        }

        [Test]
        public void ProjectileSweepAcceptsTargetInsideFiniteLane()
        {
            Assert.That(NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                0f, 0f, 0f, 1f, 0.3f, 6f, 8f, 0.5f, out float distance), Is.True);
            Assert.That(distance, Is.EqualTo(6f).Within(0.0001f));
        }

        [Test]
        public void ProjectileSweepRejectsBehindOutsideLaneAndBeyondRange()
        {
            Assert.That(NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                0f, 0f, 0f, 1f, 0f, -1f, 8f, 0.5f, out _), Is.False);
            Assert.That(NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                0f, 0f, 0f, 1f, 0.51f, 4f, 8f, 0.5f, out _), Is.False);
            Assert.That(NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                0f, 0f, 0f, 1f, 0f, 8.01f, 8f, 0.5f, out _), Is.False);
        }

        [Test]
        public void ProjectileSweepAndFacingRejectNonFiniteOrBackwardAim()
        {
            Assert.That(NetworkCombatSpatialValidator.TryEvaluateProjectileSweep(
                0f, 0f, float.NaN, 1f, 0f, 2f, 8f, 0.5f, out _), Is.False);
            Assert.That(NetworkCombatSpatialValidator.IsAimWithinFacingArc(
                0f, 1f, 0f, 1f, 0f), Is.True);
            Assert.That(NetworkCombatSpatialValidator.IsAimWithinFacingArc(
                0f, 1f, 0f, -1f, 0f), Is.False);
        }
    }
}
