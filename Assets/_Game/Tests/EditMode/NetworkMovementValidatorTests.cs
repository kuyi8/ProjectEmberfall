using Emberfall.Networking;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class NetworkMovementValidatorTests
    {
        [Test]
        public void AcceptsMovementInsideServerSpeedAndToleranceBudget()
        {
            var validator = CreateValidator();
            validator.Reset(0f, 0f, 10d);

            NetworkMovementValidationResult result = validator.Validate(0.5f, 0f, 10.1d, 1);

            Assert.That(result.Status, Is.EqualTo(NetworkMovementValidationStatus.Accepted));
            Assert.That(result.AcceptedX, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void ClampsTeleportToServerMovementBudget()
        {
            var validator = CreateValidator();
            validator.Reset(0f, 0f, 20d);

            NetworkMovementValidationResult result = validator.Validate(10f, 0f, 20.1d, 1);

            Assert.That(result.Status, Is.EqualTo(NetworkMovementValidationStatus.Corrected));
            Assert.That(result.AcceptedX, Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(result.Reason, Is.EqualTo("speed-or-position-budget-exceeded"));
        }

        [Test]
        public void LongPacketGapDoesNotGrantUnboundedTeleportBudget()
        {
            var validator = CreateValidator();
            validator.Reset(0f, 0f, 30d);

            NetworkMovementValidationResult result = validator.Validate(9f, 0f, 40d, 1);

            Assert.That(result.Status, Is.EqualTo(NetworkMovementValidationStatus.Corrected));
            Assert.That(result.AcceptedX, Is.EqualTo(2.025f).Within(0.0001f));
        }

        [Test]
        public void IgnoresOutOfOrderSequenceWithoutMovingAuthority()
        {
            var validator = CreateValidator();
            validator.Reset(0f, 0f, 40d);
            validator.Validate(0.5f, 0f, 40.1d, 2);

            NetworkMovementValidationResult stale = validator.Validate(2f, 0f, 40.2d, 1);

            Assert.That(stale.Status, Is.EqualTo(NetworkMovementValidationStatus.Ignored));
            Assert.That(stale.AcceptedX, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(stale.Reason, Is.EqualTo("stale-sequence"));
        }

        [Test]
        public void IgnoresNonFiniteCandidate()
        {
            var validator = CreateValidator();
            validator.Reset(0f, 0f, 50d);

            NetworkMovementValidationResult result = validator.Validate(float.NaN, 0f, 50.1d, 1);

            Assert.That(result.Status, Is.EqualTo(NetworkMovementValidationStatus.Ignored));
            Assert.That(result.AcceptedX, Is.Zero);
        }

        [Test]
        public void DynamicSprintBudget_AllowsSprintButNormalBudgetStillCorrectsIt()
        {
            var normal = new NetworkMovementValidator(8.2f, 0f, 0.25d);
            normal.Reset(0f, 0f, 60d);
            NetworkMovementValidationResult corrected = normal.Validate(0.75f, 0f, 60.1d, 1, 5.5f);

            var sprint = new NetworkMovementValidator(8.2f, 0f, 0.25d);
            sprint.Reset(0f, 0f, 60d);
            NetworkMovementValidationResult accepted = sprint.Validate(0.75f, 0f, 60.1d, 1, 8.2f);

            Assert.That(corrected.Status, Is.EqualTo(NetworkMovementValidationStatus.Corrected));
            Assert.That(accepted.Status, Is.EqualTo(NetworkMovementValidationStatus.Accepted));
        }

        [Test]
        public void DynamicBudget_RejectsSpeedAboveConfiguredMaximum()
        {
            var validator = new NetworkMovementValidator(8.2f, 0.65f, 0.25d);
            validator.Reset(0f, 0f, 70d);

            NetworkMovementValidationResult result = validator.Validate(0.5f, 0f, 70.1d, 1, 8.3f);

            Assert.That(result.Status, Is.EqualTo(NetworkMovementValidationStatus.Ignored));
            Assert.That(result.Reason, Is.EqualTo("invalid-speed-budget"));
        }

        private static NetworkMovementValidator CreateValidator() =>
            new NetworkMovementValidator(maxSpeed: 5.5f, positionTolerance: 0.65f, maxElapsedSeconds: 0.25d);
    }
}
