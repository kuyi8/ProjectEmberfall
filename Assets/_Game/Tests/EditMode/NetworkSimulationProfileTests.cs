using Emberfall.Networking;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class NetworkSimulationProfileTests
    {
        [TestCase(0, 0, 0, false)]
        [TestCase(100, 0, 0, true)]
        [TestCase(200, 0, 2, true)]
        public void ValidProfilePreservesConditionValues(
            int delayMilliseconds,
            int jitterMilliseconds,
            int packetLossPercent,
            bool enabled)
        {
            Assert.That(
                NetworkSimulationProfile.TryCreate(
                    delayMilliseconds,
                    jitterMilliseconds,
                    packetLossPercent,
                    out NetworkSimulationProfile profile,
                    out string reason),
                Is.True,
                reason);
            Assert.That(profile.DelayMilliseconds, Is.EqualTo(delayMilliseconds));
            Assert.That(profile.JitterMilliseconds, Is.EqualTo(jitterMilliseconds));
            Assert.That(profile.PacketLossPercent, Is.EqualTo(packetLossPercent));
            Assert.That(profile.IsEnabled, Is.EqualTo(enabled));
        }

        [TestCase(-1, 0, 0)]
        [TestCase(1001, 0, 0)]
        [TestCase(0, -1, 0)]
        [TestCase(0, 251, 0)]
        [TestCase(0, 0, -1)]
        [TestCase(0, 0, 26)]
        public void UnsafeProfileIsRejected(int delayMilliseconds, int jitterMilliseconds, int packetLossPercent)
        {
            Assert.That(
                NetworkSimulationProfile.TryCreate(
                    delayMilliseconds,
                    jitterMilliseconds,
                    packetLossPercent,
                    out _,
                    out string reason),
                Is.False);
            Assert.That(reason, Is.Not.Empty);
        }
    }
}
