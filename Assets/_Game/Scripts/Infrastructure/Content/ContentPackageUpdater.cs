using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Emberfall.Core.Content;
using Emberfall.Core.Diagnostics;

namespace Emberfall.Infrastructure.Content
{
    public enum ContentUpdateStatus
    {
        ActivatedForNextLaunch = 0,
        AlreadyCurrent = 1,
        RolledBackForNextLaunch = 2,
        Rejected = 3
    }

    public readonly struct ContentUpdateResult
    {
        public ContentUpdateResult(ContentUpdateStatus status, SemanticVersion version, string message)
        {
            Status = status;
            Version = version;
            Message = message ?? string.Empty;
        }

        public ContentUpdateStatus Status { get; }
        public SemanticVersion Version { get; }
        public string Message { get; }
        public bool IsSuccess => Status != ContentUpdateStatus.Rejected;
        public bool RequiresRestart => Status == ContentUpdateStatus.ActivatedForNextLaunch ||
                                       Status == ContentUpdateStatus.RolledBackForNextLaunch;
    }

    /// <summary>Downloads into managed staging and changes only the next-launch pointer after every check succeeds.</summary>
    public sealed class ContentPackageUpdater
    {
        public const long MaximumManifestBytes = 64L * 1024L;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly string _contentRoot;
        private readonly SemanticVersion _clientVersion;
        private readonly SemanticVersion _activeVersion;
        private readonly IContentPackageTransport _transport;
        private readonly IContentPackageSignatureVerifier _signatureVerifier;
        private readonly IGameLogger _logger;
        private readonly DirectoryContentPackageReader _reader;
        private readonly ContentPointerStore _pointerStore;
        private readonly IContentPackagePreflight _preflight;

        public ContentPackageUpdater(
            string contentRoot,
            SemanticVersion clientVersion,
            SemanticVersion activeVersion,
            IContentPackageTransport transport,
            IContentPackageSignatureVerifier signatureVerifier,
            IGameLogger logger,
            DirectoryContentPackageReader reader = null,
            ContentPointerStore pointerStore = null,
            IContentPackagePreflight preflight = null)
        {
            _contentRoot = string.IsNullOrWhiteSpace(contentRoot)
                ? throw new ArgumentException("Content root is required.", nameof(contentRoot))
                : contentRoot;
            _clientVersion = clientVersion;
            _activeVersion = activeVersion;
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _signatureVerifier = signatureVerifier ?? throw new ArgumentNullException(nameof(signatureVerifier));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _reader = reader ?? new DirectoryContentPackageReader();
            _pointerStore = pointerStore ?? new ContentPointerStore(contentRoot);
            _preflight = preflight;
        }

        public async Task<ContentUpdateResult> DownloadAndActivateAsync(
            Uri packageBaseUri,
            CancellationToken cancellationToken)
        {
            string stagingRoot = null;
            try
            {
                Uri normalizedBaseUri = NormalizeBaseUri(packageBaseUri);
                byte[] manifestBytes = await _transport.DownloadAsync(
                    new Uri(normalizedBaseUri, "manifest.json"),
                    MaximumManifestBytes,
                    cancellationToken).ConfigureAwait(false);
                ContentPackageManifest manifest = ContentPackageManifestJson.Deserialize(
                    StrictUtf8.GetString(manifestBytes));
                ContentPackageValidationResult manifestResult = ContentPackageValidator.ValidateManifest(
                    manifest,
                    _clientVersion,
                    true);
                if (!manifestResult.IsValid) throw new ContentPackageLoadException(manifestResult.Code, manifestResult.Message);

                SemanticVersion currentFloor = _activeVersion;
                if (_pointerStore.TryReadCurrent(out SemanticVersion pointedVersion) && pointedVersion > currentFloor)
                    currentFloor = pointedVersion;
                if (manifest.ContentVersion < currentFloor)
                    throw new InvalidOperationException(
                        $"Downloaded content {manifest.ContentVersion} is older than current {currentFloor}; use explicit rollback instead.");

                stagingRoot = Path.Combine(
                    _contentRoot,
                    "Staging",
                    $"{manifest.ContentVersion}-{Guid.NewGuid():N}");
                Directory.CreateDirectory(stagingRoot);
                File.WriteAllBytes(Path.Combine(stagingRoot, "manifest.json"), manifestBytes);

                for (int i = 0; i < manifest.Files.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ContentPackageFile file = manifest.Files[i];
                    byte[] bytes = await _transport.DownloadAsync(
                        new Uri(normalizedBaseUri, file.Path),
                        file.Size,
                        cancellationToken).ConfigureAwait(false);
                    string destination = Path.Combine(
                        stagingRoot,
                        file.Path.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(destination) ?? stagingRoot);
                    File.WriteAllBytes(destination, bytes);
                }

                ContentPackageSnapshot snapshot = _reader.Read(stagingRoot, _clientVersion, true);
                if (!_signatureVerifier.Verify(snapshot, out string signatureFailure))
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(signatureFailure)
                            ? "Content signature verification failed."
                            : signatureFailure);
                ValidatePreflight(snapshot);

                string packageRoot = InstallOrReuse(snapshot, stagingRoot);
                stagingRoot = null;
                bool alreadyCurrent = _pointerStore.TryReadCurrent(out SemanticVersion currentVersion) &&
                                      currentVersion == manifest.ContentVersion;
                if (!alreadyCurrent) _pointerStore.Activate(manifest.ContentVersion);

                string message = alreadyCurrent
                    ? $"Content {manifest.ContentVersion} is already selected and valid."
                    : $"Content {manifest.ContentVersion} is installed at '{packageRoot}' and will activate after restart.";
                _logger.Log(GameLogLevel.Info, message);
                return new ContentUpdateResult(
                    alreadyCurrent ? ContentUpdateStatus.AlreadyCurrent : ContentUpdateStatus.ActivatedForNextLaunch,
                    manifest.ContentVersion,
                    message);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                               exception is DecoderFallbackException || exception is FormatException ||
                                               exception is InvalidOperationException || exception is ContentPackageLoadException ||
                                               exception is ContentPackageDownloadException)
            {
                string message = $"Content update was rejected without changing the active pointer: {exception.Message}";
                _logger.Log(GameLogLevel.Warning, message);
                return new ContentUpdateResult(ContentUpdateStatus.Rejected, default, message);
            }
            finally
            {
                TryDeleteStaging(stagingRoot);
            }
        }

        public ContentUpdateResult RollbackToPrevious()
        {
            try
            {
                if (!_pointerStore.TryReadPrevious(out SemanticVersion previousVersion))
                    throw new InvalidOperationException("No valid previous content pointer is available.");
                string packageRoot = GetPackageRoot(previousVersion);
                ContentPackageSnapshot snapshot = _reader.Read(packageRoot, _clientVersion, true);
                if (!_signatureVerifier.Verify(snapshot, out string failureReason))
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(failureReason) ? "Previous content signature is invalid." : failureReason);
                ValidatePreflight(snapshot);
                _pointerStore.Activate(previousVersion);
                string message = $"Content {previousVersion} will be restored after restart.";
                _logger.Log(GameLogLevel.Info, message);
                return new ContentUpdateResult(ContentUpdateStatus.RolledBackForNextLaunch, previousVersion, message);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                               exception is FormatException || exception is InvalidOperationException ||
                                               exception is ContentPackageLoadException)
            {
                string message = $"Content rollback was rejected without changing the active pointer: {exception.Message}";
                _logger.Log(GameLogLevel.Warning, message);
                return new ContentUpdateResult(ContentUpdateStatus.Rejected, default, message);
            }
        }

        private string InstallOrReuse(ContentPackageSnapshot snapshot, string stagingRoot)
        {
            string packageRoot = GetPackageRoot(snapshot.Manifest.ContentVersion);
            if (Directory.Exists(packageRoot))
            {
                try
                {
                    ContentPackageSnapshot existing = _reader.Read(packageRoot, _clientVersion, true);
                    if (!_signatureVerifier.Verify(existing, out _)) throw new InvalidOperationException("Existing package signature is invalid.");
                    if (!SameImmutablePackage(existing.Manifest, snapshot.Manifest))
                        throw new InvalidOperationException(
                            $"Content version {snapshot.Manifest.ContentVersion} already exists with different signed metadata.");
                    TryDeleteStaging(stagingRoot);
                    return packageRoot;
                }
                catch (ContentPackageLoadException)
                {
                    QuarantineExisting(packageRoot, snapshot.Manifest.ContentVersion);
                }
                catch (InvalidOperationException exception) when (exception.Message == "Existing package signature is invalid.")
                {
                    QuarantineExisting(packageRoot, snapshot.Manifest.ContentVersion);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(packageRoot) ?? _contentRoot);
            Directory.Move(stagingRoot, packageRoot);
            return packageRoot;
        }

        private void QuarantineExisting(string packageRoot, SemanticVersion version)
        {
            string rejectedRoot = Path.Combine(_contentRoot, "Rejected");
            Directory.CreateDirectory(rejectedRoot);
            string quarantine = Path.Combine(rejectedRoot, $"{version}-{Guid.NewGuid():N}");
            Directory.Move(packageRoot, quarantine);
            _logger.Log(GameLogLevel.Warning, $"Invalid installed content was preserved at '{quarantine}'.");
        }

        private string GetPackageRoot(SemanticVersion version) =>
            Path.Combine(_contentRoot, "Packages", version.ToString());

        private void ValidatePreflight(ContentPackageSnapshot snapshot)
        {
            if (_preflight == null) return;
            if (!_preflight.Validate(snapshot, out string failureReason))
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(failureReason)
                        ? "Content package preflight failed."
                        : failureReason);
        }

        private static Uri NormalizeBaseUri(Uri packageBaseUri)
        {
            if (packageBaseUri == null || !packageBaseUri.IsAbsoluteUri)
                throw new ContentPackageDownloadException("Content package base URI must be absolute.");
            var builder = new UriBuilder(packageBaseUri) { Query = string.Empty, Fragment = string.Empty };
            if (!builder.Path.EndsWith("/", StringComparison.Ordinal)) builder.Path += "/";
            return builder.Uri;
        }

        private static bool SameImmutablePackage(ContentPackageManifest left, ContentPackageManifest right)
        {
            byte[] leftPayload = ContentPackageSignaturePayload.Build(left);
            byte[] rightPayload = ContentPackageSignaturePayload.Build(right);
            if (leftPayload.Length != rightPayload.Length ||
                !string.Equals(left.Signature, right.Signature, StringComparison.Ordinal))
                return false;
            for (int i = 0; i < leftPayload.Length; i++)
            {
                if (leftPayload[i] != rightPayload[i]) return false;
            }

            return true;
        }

        private void TryDeleteStaging(string stagingRoot)
        {
            if (string.IsNullOrEmpty(stagingRoot) || !Directory.Exists(stagingRoot)) return;
            try
            {
                string stagingParent = Path.GetFullPath(Path.Combine(_contentRoot, "Staging"))
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                string candidate = Path.GetFullPath(stagingRoot);
                if (!candidate.StartsWith(stagingParent, StringComparison.OrdinalIgnoreCase)) return;
                Directory.Delete(candidate, true);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                _logger.Log(GameLogLevel.Warning, $"Staging cleanup failed for '{stagingRoot}'.", exception);
            }
        }
    }
}
