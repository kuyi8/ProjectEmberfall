using System.Collections.Generic;
using Emberfall.Core.Content;
using Emberfall.Infrastructure.Content;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class GameContentPackagePreflightTests
    {
        private readonly GameContentPackagePreflight _preflight = new GameContentPackagePreflight();

        [Test]
        public void AuthoredPackage_PassesCompleteGameContract()
        {
            bool accepted = _preflight.Validate(
                M4ContentTestFactory.CreateSnapshot(new SemanticVersion(0, 5, 4)),
                out string reason);

            Assert.That(accepted, Is.True, reason);
        }

        [Test]
        public void UnsupportedJsonSchema_IsRejectedBeforeSelection()
        {
            Dictionary<string, byte[]> files = M4ContentTestFactory.LoadAuthoredFiles();
            M4ContentTestFactory.ReplaceText(
                files,
                RuntimeContentPaths.Enemies,
                "\"schemaVersion\": 1",
                "\"schemaVersion\": 2");

            bool accepted = _preflight.Validate(
                M4ContentTestFactory.CreateSnapshot(new SemanticVersion(0, 5, 5), files),
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("schema"));
        }

        [Test]
        public void MissingRequiredStableId_IsRejectedBeforeSelection()
        {
            Dictionary<string, byte[]> files = M4ContentTestFactory.LoadAuthoredFiles();
            M4ContentTestFactory.ReplaceText(
                files,
                RuntimeContentPaths.Enemies,
                "enemy:fogwalker",
                "enemy:fogwalker-replaced");

            bool accepted = _preflight.Validate(
                M4ContentTestFactory.CreateSnapshot(new SemanticVersion(0, 5, 5), files),
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("enemy:fogwalker").And.Contain("missing"));
        }

        [Test]
        public void MissingCrossReferencedTextId_IsRejectedBeforeSelection()
        {
            Dictionary<string, byte[]> files = M4ContentTestFactory.LoadAuthoredFiles();
            M4ContentTestFactory.ReplaceText(
                files,
                RuntimeContentPaths.Enemies,
                "text:enemy.fogwalker.name",
                "text:enemy.fogwalker.unknown");

            bool accepted = _preflight.Validate(
                M4ContentTestFactory.CreateSnapshot(new SemanticVersion(0, 5, 5), files),
                out string reason);

            Assert.That(accepted, Is.False);
            Assert.That(reason, Does.Contain("missing text ID"));
        }

        [TestCase("\"summoners\"", "\"unregisteredSummoners\"", "summoner records")]
        [TestCase("enemy:ash-caller", "enemy:ash-caller-missing", "enemy:ash-caller")]
        [TestCase("text:enemy.ash-caller.name", "text:enemy.ash-caller.unknown", "missing text ID")]
        [TestCase("\"minionId\": \"enemy:fogwalker\"", "\"minionId\": \"enemy:rune-priest\"", "missing melee minion")]
        [TestCase("\"id\": \"enemy:ash-caller\"", "\"id\": \"enemy:fogwalker\"", "Duplicate enemy ID")]
        public void SummonerContract_IsValidatedBeforeContentSelection(string from, string to, string message)
        {
            var files = M4ContentTestFactory.LoadAuthoredFiles();
            M4ContentTestFactory.ReplaceText(files, RuntimeContentPaths.Enemies, from, to);
            Assert.That(_preflight.Validate(M4ContentTestFactory.CreateSnapshot(new SemanticVersion(0, 8, 12), files), out string reason), Is.False);
            Assert.That(reason, Does.Contain(message));
        }

    }
}
