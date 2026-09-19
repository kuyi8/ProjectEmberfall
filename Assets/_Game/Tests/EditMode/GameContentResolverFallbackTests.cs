using System;
using System.Collections.Generic;
using System.IO;
using Emberfall.Core.Content;
using Emberfall.Core.Diagnostics;
using Emberfall.Infrastructure.Content;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class GameContentResolverFallbackTests
    {
        private string _root;
        private string _builtinRoot;
        private string _contentRoot;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "EmberfallGamePreflight", Guid.NewGuid().ToString("N"));
            _builtinRoot = Path.Combine(_root, "Builtin");
            _contentRoot = Path.Combine(_root, "Content");
            M4ContentTestFactory.WritePackage(
                _builtinRoot,
                M4ContentTestFactory.CreateSnapshot(new SemanticVersion(0, 5, 4)));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [TestCase("schema")]
        [TestCase("stable-id")]
        public void InvalidGameplayPatch_FallsBackToValidatedBuiltin(string mutation)
        {
            var version = new SemanticVersion(0, 5, 5);
            Dictionary<string, byte[]> files = M4ContentTestFactory.LoadAuthoredFiles();
            if (mutation == "schema")
            {
                M4ContentTestFactory.ReplaceText(
                    files,
                    RuntimeContentPaths.Quests,
                    "\"schemaVersion\": 1",
                    "\"schemaVersion\": 9");
            }
            else
            {
                M4ContentTestFactory.ReplaceText(
                    files,
                    RuntimeContentPaths.Quests,
                    "quest:emberfall.main",
                    "quest:emberfall.replaced");
            }

            string patchRoot = Path.Combine(_contentRoot, "Packages", version.ToString());
            M4ContentTestFactory.WritePackage(
                patchRoot,
                M4ContentTestFactory.CreateSnapshot(version, files, "rsa-sha256", "test-signature"));
            Directory.CreateDirectory(_contentRoot);
            File.WriteAllText(Path.Combine(_contentRoot, "current"), version.ToString());

            var resolver = new LocalContentPackageResolver(
                _builtinRoot,
                _contentRoot,
                new SemanticVersion(0, 5, 4),
                new SilentLogger(),
                new AcceptingVerifier(),
                preflight: new GameContentPackagePreflight());
            ContentPackageSelection selection = resolver.Resolve();

            Assert.That(selection.Source, Is.EqualTo(ContentPackageSource.FallbackToBuiltin));
            Assert.That(selection.Snapshot.Manifest.ContentVersion, Is.EqualTo(new SemanticVersion(0, 5, 4)));
            Assert.That(selection.FallbackReason, Does.Contain("preflight"));
        }

        private sealed class AcceptingVerifier : IContentPackageSignatureVerifier
        {
            public bool Verify(ContentPackageSnapshot snapshot, out string failureReason)
            {
                failureReason = string.Empty;
                return true;
            }
        }

        private sealed class SilentLogger : IGameLogger
        {
            public void Log(GameLogLevel level, string message, Exception exception = null)
            {
            }
        }
    }
}
