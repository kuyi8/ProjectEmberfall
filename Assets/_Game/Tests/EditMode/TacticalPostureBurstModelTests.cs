using Emberfall.Gameplay.Combat.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class TacticalPostureBurstModelTests
    {
        [Test]
        public void Burst_ResolvesOnceThenRequiresAttemptReset()
        {
            var model = new TacticalPostureBurstModel(1f);

            Assert.That(model.TryArm(), Is.True);
            Assert.That(model.TryArm(), Is.False);
            Assert.That(model.Tick(0.6f), Is.False);
            Assert.That(model.Tick(0.4f), Is.True);
            Assert.That(model.Tick(1f), Is.False);
            Assert.That(model.IsSpent, Is.True);

            model.Reset();
            Assert.That(model.IsReady, Is.True);
            Assert.That(model.TryArm(), Is.True);
        }
    }
}
