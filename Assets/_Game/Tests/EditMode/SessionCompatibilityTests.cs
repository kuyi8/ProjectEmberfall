using Emberfall.Core.Content;
using Emberfall.Networking;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class SessionCompatibilityTests
    {
        private static readonly SessionCompatibility Expected = new SessionCompatibility(
            SessionCompatibility.CurrentProtocolVersion,
            new SemanticVersion(0, 7, 0),
            1,
            new SemanticVersion(0, 5, 5));

        [Test]
        public void CodecRoundTripPreservesEveryAuthorityVersion()
        {
            byte[] payload = SessionCompatibilityCodec.Encode(Expected);

            Assert.That(SessionCompatibilityCodec.TryDecode(payload, out SessionCompatibility decoded, out string reason), Is.True, reason);
            Assert.That(decoded, Is.EqualTo(Expected));
        }

        [TestCase("1|0.7.0|1")]
        [TestCase("0|0.7.0|1|0.5.5")]
        [TestCase("1|0.7|1|0.5.5")]
        [TestCase("1|0.7.0|schema|0.5.5")]
        public void CodecRejectsMalformedPayload(string payload)
        {
            Assert.That(
                SessionCompatibilityCodec.TryDecode(System.Text.Encoding.UTF8.GetBytes(payload), out _, out string reason),
                Is.False);
            Assert.That(reason, Is.Not.Empty);
        }

        [Test]
        public void CompatibilityRequiresExactClientSchemaAndContentVersions()
        {
            Assert.That(SessionCompatibilityCodec.IsCompatible(Expected, Expected, out string acceptedReason), Is.True);
            Assert.That(acceptedReason, Is.Empty);

            AssertRejected(new SessionCompatibility(2, Expected.ClientVersion, 1, Expected.ContentVersion), "网络协议");
            AssertRejected(new SessionCompatibility(1, new SemanticVersion(0, 7, 1), 1, Expected.ContentVersion), "客户端版本");
            AssertRejected(new SessionCompatibility(1, Expected.ClientVersion, 2, Expected.ContentVersion), "内容结构版本");
            AssertRejected(new SessionCompatibility(1, Expected.ClientVersion, 1, new SemanticVersion(0, 5, 4)), "内容版本");
        }

        [Test]
        public void OfflineServiceNeverPretendsToHostOrJoin()
        {
            var service = new OfflineSessionService(Expected);

            Assert.That(service.StartHost(7777), Is.False);
            Assert.That(service.Snapshot.Mode, Is.EqualTo(SessionMode.Offline));
            Assert.That(service.StartClient("127.0.0.1", 7777), Is.False);
            Assert.That(service.Snapshot.State, Is.EqualTo(SessionConnectionState.Offline));
            Assert.That(service.StartOffline(), Is.True);
            Assert.That(service.Snapshot.ConnectedPlayers, Is.EqualTo(1));
        }

        private static void AssertRejected(SessionCompatibility candidate, string messageFragment)
        {
            Assert.That(SessionCompatibilityCodec.IsCompatible(Expected, candidate, out string reason), Is.False);
            Assert.That(reason, Does.Contain(messageFragment));
        }
    }
}
