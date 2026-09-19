using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emberfall.Core.Content;
using Emberfall.Core.Diagnostics;
using Emberfall.Infrastructure.Content;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class ContentPackageUpdaterTests
    {
        private string _root;
        private RSA _signer;
        private RsaContentPackageSignatureVerifier _verifier;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "EmberfallUpdaterTests", Guid.NewGuid().ToString("N"));
            _signer = new RSACryptoServiceProvider(2048);
            RSAParameters publicKey = _signer.ExportParameters(false);
            _verifier = new RsaContentPackageSignatureVerifier(
                Convert.ToBase64String(publicKey.Modulus),
                Convert.ToBase64String(publicKey.Exponent));
        }

        [TearDown]
        public void TearDown()
        {
            _signer?.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public void ValidPackage_InstallsAndActivatesForNextLaunch()
        {
            SemanticVersion version = new SemanticVersion(0, 5, 2);
            FakeTransport transport = CreateTransport(version, out _);
            ContentPackageUpdater updater = CreateUpdater(transport, new SemanticVersion(0, 5, 1));

            ContentUpdateResult result = updater.DownloadAndActivateAsync(
                new Uri("https://content.test/patch/"),
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(ContentUpdateStatus.ActivatedForNextLaunch));
            Assert.That(result.RequiresRestart, Is.True);
            Assert.That(File.ReadAllText(Path.Combine(_root, "current")), Is.EqualTo("0.5.2"));
            Assert.That(File.Exists(Path.Combine(_root, "Packages", "0.5.2", "manifest.json")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(_root, "Staging")) &&
                        Directory.GetDirectories(Path.Combine(_root, "Staging")).Length > 0, Is.False);
        }

        [Test]
        public void TamperedPayload_IsRejectedWithoutChangingPointer()
        {
            SemanticVersion version = new SemanticVersion(0, 5, 2);
            FakeTransport transport = CreateTransport(version, out Dictionary<string, byte[]> responses);
            responses["https://content.test/patch/Data/example.json"] = Encoding.UTF8.GetBytes("{\"tampered\":true}");
            ContentPackageUpdater updater = CreateUpdater(transport, new SemanticVersion(0, 5, 1));

            ContentUpdateResult result = updater.DownloadAndActivateAsync(
                new Uri("https://content.test/patch/"),
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(ContentUpdateStatus.Rejected));
            Assert.That(File.Exists(Path.Combine(_root, "current")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_root, "Packages", "0.5.2")), Is.False);
        }

        [Test]
        public void SecondActivation_PreservesPreviousAndRollbackSwapsPointers()
        {
            ContentPackageUpdater first = CreateUpdater(
                CreateTransport(new SemanticVersion(0, 5, 2), out _),
                new SemanticVersion(0, 5, 1));
            Assert.That(first.DownloadAndActivateAsync(
                new Uri("https://content.test/patch/"), CancellationToken.None).GetAwaiter().GetResult().IsSuccess, Is.True);

            ContentPackageUpdater second = CreateUpdater(
                CreateTransport(new SemanticVersion(0, 5, 3), out _),
                new SemanticVersion(0, 5, 2));
            Assert.That(second.DownloadAndActivateAsync(
                new Uri("https://content.test/patch/"), CancellationToken.None).GetAwaiter().GetResult().IsSuccess, Is.True);
            Assert.That(File.ReadAllText(Path.Combine(_root, "current")), Is.EqualTo("0.5.3"));
            Assert.That(File.ReadAllText(Path.Combine(_root, "previous")), Is.EqualTo("0.5.2"));

            ContentUpdateResult rollback = second.RollbackToPrevious();

            Assert.That(rollback.Status, Is.EqualTo(ContentUpdateStatus.RolledBackForNextLaunch));
            Assert.That(File.ReadAllText(Path.Combine(_root, "current")), Is.EqualTo("0.5.2"));
            Assert.That(File.ReadAllText(Path.Combine(_root, "previous")), Is.EqualTo("0.5.3"));
        }

        [Test]
        public void Downgrade_IsRejectedAndCurrentPointerIsPreserved()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllText(Path.Combine(_root, "current"), "0.5.3");
            ContentPackageUpdater updater = CreateUpdater(
                CreateTransport(new SemanticVersion(0, 5, 2), out _),
                new SemanticVersion(0, 5, 1));

            ContentUpdateResult result = updater.DownloadAndActivateAsync(
                new Uri("https://content.test/patch/"), CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(ContentUpdateStatus.Rejected));
            Assert.That(File.ReadAllText(Path.Combine(_root, "current")), Is.EqualTo("0.5.3"));
        }

        [Test]
        public void RuntimePreflightFailure_RejectsBeforeInstallAndPointerChange()
        {
            SemanticVersion version = new SemanticVersion(0, 5, 2);
            ContentPackageUpdater updater = CreateUpdater(
                CreateTransport(version, out _),
                new SemanticVersion(0, 5, 1),
                new RejectingPreflight());

            ContentUpdateResult result = updater.DownloadAndActivateAsync(
                new Uri("https://content.test/patch/"),
                CancellationToken.None).GetAwaiter().GetResult();

            Assert.That(result.Status, Is.EqualTo(ContentUpdateStatus.Rejected));
            Assert.That(result.Message, Does.Contain("script preflight rejected"));
            Assert.That(File.Exists(Path.Combine(_root, "current")), Is.False);
            Assert.That(Directory.Exists(Path.Combine(_root, "Packages", "0.5.2")), Is.False);
        }

        private ContentPackageUpdater CreateUpdater(
            FakeTransport transport,
            SemanticVersion activeVersion,
            IContentPackagePreflight preflight = null) =>
            new ContentPackageUpdater(
                _root,
                new SemanticVersion(0, 5, 2),
                activeVersion,
                transport,
                _verifier,
                new SilentLogger(),
                preflight: preflight);

        private FakeTransport CreateTransport(
            SemanticVersion version,
            out Dictionary<string, byte[]> responses)
        {
            var files = new Dictionary<string, byte[]> { ["Data/example.json"] = Encoding.UTF8.GetBytes("{}") };
            ContentPackageManifest manifest = ContentPackageSignatureTests.CreateSignedManifest(version, files, _signer);
            responses = new Dictionary<string, byte[]>(StringComparer.Ordinal)
            {
                ["https://content.test/patch/manifest.json"] = Encoding.UTF8.GetBytes(ContentPackageManifestJson.Serialize(manifest)),
                ["https://content.test/patch/Data/example.json"] = files["Data/example.json"]
            };
            return new FakeTransport(responses);
        }

        private sealed class FakeTransport : IContentPackageTransport
        {
            private readonly IReadOnlyDictionary<string, byte[]> _responses;

            public FakeTransport(IReadOnlyDictionary<string, byte[]> responses)
            {
                _responses = responses;
            }

            public Task<byte[]> DownloadAsync(Uri uri, long maximumBytes, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!_responses.TryGetValue(uri.AbsoluteUri, out byte[] bytes))
                    throw new ContentPackageDownloadException($"No fake response for {uri}.");
                if (bytes.LongLength > maximumBytes)
                    throw new ContentPackageDownloadException("Fake response exceeds the requested limit.");
                return Task.FromResult((byte[])bytes.Clone());
            }
        }

        private sealed class RejectingPreflight : IContentPackagePreflight
        {
            public bool Validate(ContentPackageSnapshot snapshot, out string failureReason)
            {
                failureReason = "script preflight rejected the package";
                return false;
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
