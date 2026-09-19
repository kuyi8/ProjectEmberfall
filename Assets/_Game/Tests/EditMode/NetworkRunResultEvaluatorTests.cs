using System;
using Emberfall.Networking;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class NetworkRunResultEvaluatorTests
    {
        [Test]
        public void CleanRunProducesSingleValidSResult()
        {
            NetworkRunResultSummary result = NetworkRunResultEvaluator.Create(
                1, 179.6f, 0, 0, 2, 2, false);

            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Sequence, Is.EqualTo(1));
            Assert.That(result.ElapsedSeconds, Is.EqualTo(180));
            Assert.That(result.Grade, Is.EqualTo(NetworkRunGrade.S));
            Assert.That(result.TeammateLeft, Is.False);
        }

        [Test]
        public void ConnectionCountMarksTeammateAsLeftWithoutChangingServerStatistics()
        {
            NetworkRunResultSummary result = NetworkRunResultEvaluator.Create(
                1, 540f, 1, 0, 2, 1, false);

            Assert.That(result.TeammateLeft, Is.True);
            Assert.That(result.InitialPlayerCount, Is.EqualTo(2));
            Assert.That(result.ConnectedPlayerCount, Is.EqualTo(1));
            Assert.That(result.DownedCount, Is.EqualTo(1));
            Assert.That(result.Grade, Is.EqualTo(NetworkRunGrade.A));
        }

        [TestCase(0, 10f, 0, 0, 2, 2)]
        [TestCase(1, -1f, 0, 0, 2, 2)]
        [TestCase(1, 10f, -1, 0, 2, 2)]
        [TestCase(1, 10f, 0, -1, 2, 2)]
        [TestCase(1, 10f, 0, 0, 3, 2)]
        [TestCase(1, 10f, 0, 0, 2, 0)]
        public void InvalidServerFactsAreRejected(
            int sequence,
            float elapsed,
            int downed,
            int secrets,
            int initialPlayers,
            int connectedPlayers)
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => NetworkRunResultEvaluator.Create(
                sequence,
                elapsed,
                downed,
                secrets,
                initialPlayers,
                connectedPlayers,
                false));
        }

        [Test]
        public void TimeAndDownedCountDegradeEvaluationDeterministically()
        {
            Assert.That(
                NetworkRunResultEvaluator.Create(1, 481f, 0, 0, 2, 2, false).Grade,
                Is.EqualTo(NetworkRunGrade.A));
            Assert.That(
                NetworkRunResultEvaluator.Create(1, 721f, 2, 0, 2, 2, false).Grade,
                Is.EqualTo(NetworkRunGrade.B));
            Assert.That(
                NetworkRunResultEvaluator.Create(1, 1201f, 0, 0, 2, 2, false).Grade,
                Is.EqualTo(NetworkRunGrade.C));
        }
    }
}
