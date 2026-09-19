using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Emberfall.Core.Content;
using Emberfall.Core.Diagnostics;
using Emberfall.Core.Identifiers;
using Emberfall.Infrastructure.Content;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class LocalContentPackageResolverTests
    {
        private string _testRoot;
        private string _builtinRoot;
        private string _contentRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(Path.GetTempPath(), "EmberfallContentTests", Guid.NewGuid().ToString("N"));
            _builtinRoot = Path.Combine(_testRoot, "Builtin");
            _contentRoot = Path.Combine(_testRoot, "Content");
            WritePackage(_builtinRoot, new SemanticVersion(0, 5, 1), "builtin", "trusted");
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testRoot)) Directory.Delete(_testRoot, true);
        }

        [Test]
        public void ManifestJson_RoundTripsWithoutChangingContract()
        {
            ContentPackageManifest source = CreateManifest(
                new SemanticVersion(0, 5, 1),
                Encoding.UTF8.GetBytes("{}"),
                "builtin",
                "trusted");

            ContentPackageManifest restored = ContentPackageManifestJson.Deserialize(
                ContentPackageManifestJson.Serialize(source));

            Assert.That(restored.PackageId, Is.EqualTo(source.PackageId));
            Assert.That(restored.ContentVersion, Is.EqualTo(source.ContentVersion));
            Assert.That(restored.Files.Count, Is.EqualTo(1));
            Assert.That(restored.Files[0].Sha256, Is.EqualTo(source.Files[0].Sha256));
        }

        [Test]
        public void NoCurrentPointer_SelectsBuiltin()
        {
            ContentPackageSelection selection = CreateResolver().Resolve();

            Assert.That(selection.Source, Is.EqualTo(ContentPackageSource.Builtin));
            Assert.That(selection.Snapshot.Manifest.ContentVersion, Is.EqualTo(new SemanticVersion(0, 5, 1)));
        }

        [Test]
        public void InvalidCurrentPointer_FallsBackToBuiltin()
        {
            Directory.CreateDirectory(_contentRoot);
            File.WriteAllText(Path.Combine(_contentRoot, "current"), "../0.5.2");

            ContentPackageSelection selection = CreateResolver().Resolve();

            Assert.That(selection.Source, Is.EqualTo(ContentPackageSource.FallbackToBuiltin));
            Assert.That(selection.FallbackReason, Does.Contain("semantic version"));
        }

        [Test]
        public void PatchWithoutVerifier_FallsBackToBuiltin()
        {
            WritePatch(new SemanticVersion(0, 5, 2));

            ContentPackageSelection selection = CreateResolver().Resolve();

            Assert.That(selection.Source, Is.EqualTo(ContentPackageSource.FallbackToBuiltin));
            Assert.That(selection.FallbackReason, Does.Contain("signature verifier"));
        }

        [Test]
        public void VerifiedPatch_IsSelected()
        {
            SemanticVersion patchVersion = new SemanticVersion(0, 5, 2);
            WritePatch(patchVersion);

            ContentPackageSelection selection = CreateResolver(new AcceptingVerifier()).Resolve();

            Assert.That(selection.Source, Is.EqualTo(ContentPackageSource.Patch));
            Assert.That(selection.Snapshot.Manifest.ContentVersion, Is.EqualTo(patchVersion));
        }

        [Test]
        public void TamperedPatch_FallsBackBeforeSignatureVerification()
        {
            SemanticVersion patchVersion = new SemanticVersion(0, 5, 2);
            string patchRoot = WritePatch(patchVersion);
            File.WriteAllText(Path.Combine(patchRoot, "Data", "example.json"), "{\"tampered\":true}");

            ContentPackageSelection selection = CreateResolver(new AcceptingVerifier()).Resolve();

            Assert.That(selection.Source, Is.EqualTo(ContentPackageSource.FallbackToBuiltin));
            Assert.That(selection.FallbackReason, Does.Contain("size changed").Or.Contain("hash changed"));
        }

        [Test]
        public void PatchThatFailsRuntimePreflight_FallsBackToBuiltin()
        {
            SemanticVersion patchVersion = new SemanticVersion(0, 5, 2);
            WritePatch(patchVersion);

            ContentPackageSelection selection = CreateResolver(
                new AcceptingVerifier(),
                new RejectPatchPreflight(patchVersion)).Resolve();

            Assert.That(selection.Source, Is.EqualTo(ContentPackageSource.FallbackToBuiltin));
            Assert.That(selection.FallbackReason, Does.Contain("script preflight rejected"));
        }

        private LocalContentPackageResolver CreateResolver(
            IContentPackageSignatureVerifier verifier = null,
            IContentPackagePreflight preflight = null) =>
            new LocalContentPackageResolver(
                _builtinRoot,
                _contentRoot,
                new SemanticVersion(0, 5, 1),
                new SilentLogger(),
                verifier,
                preflight: preflight);

        private string WritePatch(SemanticVersion version)
        {
            string patchRoot = Path.Combine(_contentRoot, "Packages", version.ToString());
            WritePackage(patchRoot, version, "rsa-sha256", "test-signature");
            Directory.CreateDirectory(_contentRoot);
            File.WriteAllText(Path.Combine(_contentRoot, "current"), version.ToString());
            return patchRoot;
        }

        private static void WritePackage(
            string root,
            SemanticVersion version,
            string signatureAlgorithm,
            string signature)
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{}");
            ContentPackageManifest manifest = CreateManifest(version, bytes, signatureAlgorithm, signature);
            Directory.CreateDirectory(Path.Combine(root, "Data"));
            File.WriteAllBytes(Path.Combine(root, "Data", "example.json"), bytes);
            File.WriteAllText(Path.Combine(root, "manifest.json"), ContentPackageManifestJson.Serialize(manifest));
        }

        private static ContentPackageManifest CreateManifest(
            SemanticVersion version,
            byte[] bytes,
            string signatureAlgorithm,
            string signature) =>
            new ContentPackageManifest(
                1,
                new ContentId("package:test"),
                version,
                new SemanticVersion(0, 5, 0),
                new SemanticVersion(0, 6, 999),
                signatureAlgorithm,
                signature,
                new[]
                {
                    new ContentPackageFile(
                        "Data/example.json",
                        ContentFileKind.Json,
                        bytes.LongLength,
                        ContentPackageValidator.ComputeSha256Hex(bytes))
                });

        private sealed class AcceptingVerifier : IContentPackageSignatureVerifier
        {
            public bool Verify(ContentPackageSnapshot snapshot, out string failureReason)
            {
                failureReason = string.Empty;
                return true;
            }
        }

        private sealed class RejectPatchPreflight : IContentPackagePreflight
        {
            private readonly SemanticVersion _rejectedVersion;

            public RejectPatchPreflight(SemanticVersion rejectedVersion)
            {
                _rejectedVersion = rejectedVersion;
            }

            public bool Validate(ContentPackageSnapshot snapshot, out string failureReason)
            {
                bool accepted = snapshot.Manifest.ContentVersion != _rejectedVersion;
                failureReason = accepted ? string.Empty : "script preflight rejected the patch";
                return accepted;
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
