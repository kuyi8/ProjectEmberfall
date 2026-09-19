using System;
using System.IO;
using System.Text;
using Emberfall.Core.Content;
using Emberfall.Core.Diagnostics;

namespace Emberfall.Infrastructure.Content
{
    /// <summary>Selects content once at startup. Any patch failure keeps the trusted built-in package active.</summary>
    public sealed class LocalContentPackageResolver
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly string _builtinRoot;
        private readonly string _contentRoot;
        private readonly SemanticVersion _clientVersion;
        private readonly IGameLogger _logger;
        private readonly IContentPackageSignatureVerifier _signatureVerifier;
        private readonly IContentPackagePreflight _preflight;
        private readonly DirectoryContentPackageReader _reader;

        public LocalContentPackageResolver(
            string builtinRoot,
            string contentRoot,
            SemanticVersion clientVersion,
            IGameLogger logger,
            IContentPackageSignatureVerifier signatureVerifier = null,
            DirectoryContentPackageReader reader = null,
            IContentPackagePreflight preflight = null)
        {
            _builtinRoot = string.IsNullOrWhiteSpace(builtinRoot)
                ? throw new ArgumentException("Built-in content root is required.", nameof(builtinRoot))
                : builtinRoot;
            _contentRoot = string.IsNullOrWhiteSpace(contentRoot)
                ? throw new ArgumentException("Content root is required.", nameof(contentRoot))
                : contentRoot;
            _clientVersion = clientVersion;
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _signatureVerifier = signatureVerifier;
            _reader = reader ?? new DirectoryContentPackageReader();
            _preflight = preflight;
        }

        public ContentPackageSelection Resolve()
        {
            ContentPackageSnapshot builtin = _reader.Read(_builtinRoot, _clientVersion, false);
            ValidatePreflight(builtin, "Built-in content");
            string pointerPath = Path.Combine(_contentRoot, "current");
            if (!File.Exists(pointerPath))
                return new ContentPackageSelection(builtin, ContentPackageSource.Builtin);

            try
            {
                string pointer = StrictUtf8.GetString(File.ReadAllBytes(pointerPath));
                if (!string.Equals(pointer, pointer.Trim(), StringComparison.Ordinal) ||
                    !SemanticVersion.TryParse(pointer, out SemanticVersion requestedVersion))
                    throw new FormatException("The current content pointer is not a strict semantic version.");

                string patchRoot = Path.Combine(_contentRoot, "Packages", requestedVersion.ToString());
                ContentPackageSnapshot patch = _reader.Read(patchRoot, _clientVersion, true);
                if (patch.Manifest.ContentVersion != requestedVersion)
                    throw new FormatException("The current content pointer does not match the package version.");
                if (_signatureVerifier == null)
                    throw new InvalidOperationException("No patch signature verifier is configured.");
                if (!_signatureVerifier.Verify(patch, out string verificationFailure))
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(verificationFailure)
                            ? "Patch signature verification failed."
                            : verificationFailure);
                ValidatePreflight(patch, "Patch content");

                _logger.Log(
                    GameLogLevel.Info,
                    $"Content package {patch.Manifest.ContentVersion} selected from local patch storage.");
                return new ContentPackageSelection(patch, ContentPackageSource.Patch);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                               exception is DecoderFallbackException || exception is FormatException ||
                                               exception is InvalidOperationException || exception is ContentPackageLoadException)
            {
                string reason = exception.Message;
                _logger.Log(
                    GameLogLevel.Warning,
                    $"Patch content was rejected; built-in content {builtin.Manifest.ContentVersion} remains active. {reason}");
                return new ContentPackageSelection(builtin, ContentPackageSource.FallbackToBuiltin, reason);
            }
        }

        private void ValidatePreflight(ContentPackageSnapshot snapshot, string label)
        {
            if (_preflight == null) return;
            if (!_preflight.Validate(snapshot, out string failureReason))
                throw new InvalidOperationException(
                    $"{label} preflight failed. " +
                    (string.IsNullOrWhiteSpace(failureReason) ? "No diagnostic was provided." : failureReason));
        }
    }
}
