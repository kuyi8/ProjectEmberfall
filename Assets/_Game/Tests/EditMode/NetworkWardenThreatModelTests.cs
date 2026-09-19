using Emberfall.Networking;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class NetworkWardenThreatModelTests
    {
        [Test]
        public void RecentDamageCanSwitchTargetWhileContinuousLockPreventsPingPong()
        {
            var model = new NetworkWardenThreatModel();
            var candidates = new[]
            {
                new NetworkWardenTargetCandidate(1, 2f),
                new NetworkWardenTargetCandidate(2, 4f)
            };

            Assert.That(model.SelectTarget(candidates, 0d), Is.EqualTo(1));
            model.RecordDamage(2, 4f, 0.5d);
            Assert.That(model.SelectTarget(candidates, 0.5d), Is.EqualTo(2));
            model.RecordDamage(1, 4f, 0.6d);
            Assert.That(model.SelectTarget(candidates, 0.6d), Is.EqualTo(2));
        }

        [Test]
        public void NoAliveCandidatesClearTargetSoDownedPlayersAreNotPressured()
        {
            var model = new NetworkWardenThreatModel();
            Assert.That(model.SelectTarget(
                new[] { new NetworkWardenTargetCandidate(3, 1f) }, 0d), Is.EqualTo(3));
            Assert.That(model.SelectTarget(System.Array.Empty<NetworkWardenTargetCandidate>(), 1d),
                Is.EqualTo(ulong.MaxValue));
        }
    }
}
