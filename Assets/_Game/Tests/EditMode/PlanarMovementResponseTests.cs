using Emberfall.Gameplay.Movement;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class PlanarMovementResponseTests
    {
        [Test]
        public void ReleaseFromFullSpeed_StopsWithinTwelveHundredthsOfASecond()
        {
            PlanarMovementStep result = PlanarMovementResponse.Step(
                5.4f, 0f, 0f, 0f, 5.4f, 36f, 48f, 0.12f);

            Assert.That(result.Speed, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void OppositeInput_RemovesOldDirectionBeforeAccelerating()
        {
            PlanarMovementStep result = PlanarMovementResponse.Step(
                -5.4f, 0f, 1f, 0f, 5.4f, 36f, 48f, 1f / 60f);

            Assert.That(result.Reversed, Is.True);
            Assert.That(result.X, Is.GreaterThan(0f));
            Assert.That(result.Z, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void PerpendicularInput_DoesNotRetainLateralVelocity()
        {
            PlanarMovementStep result = PlanarMovementResponse.Step(
                0f, 5.4f, 1f, 0f, 5.4f, 36f, 48f, 1f / 60f);

            Assert.That(result.X, Is.EqualTo(5.4f).Within(0.0001f));
            Assert.That(result.Z, Is.Zero.Within(0.0001f));
        }

        [Test]
        public void AnalogInputMagnitude_ScalesTargetSpeed()
        {
            PlanarMovementStep result = PlanarMovementResponse.Step(
                0f, 0f, 0.5f, 0f, 5.4f, 36f, 48f, 1f);

            Assert.That(result.Speed, Is.EqualTo(2.7f).Within(0.0001f));
        }
    }
}
