using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Emberfall.Core.Content;

namespace Emberfall.Infrastructure.Content
{
    public sealed class ContentPackageLoadException : Exception
    {
        public ContentPackageLoadException(ContentPackageValidationCode code, string message)
            : base(message)
        {
            Code = code;
        }

        public ContentPackageLoadException(ContentPackageValidationCode code, string message, Exception innerException)
            : base(message, innerException)
        {
            Code = code;
        }

        public ContentPackageValidationCode Code { get; }
    }

    public sealed class DirectoryContentPackageReader
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public ContentPackageSnapshot Read(
            string packageRoot,
            SemanticVersion clientVersion,
            bool requireSignature)
        {
            if (string.IsNullOrWhiteSpace(packageRoot)) throw new ArgumentException("Package root is required.", nameof(packageRoot));
            string manifestPath = Path.Combine(packageRoot, "manifest.json");
            if (!File.Exists(manifestPath))
                throw new ContentPackageLoadException(ContentPackageValidationCode.MissingManifest, "Content manifest is missing.");

            ContentPackageManifest manifest;
            try
            {
                manifest = ContentPackageManifestJson.Deserialize(StrictUtf8.GetString(File.ReadAllBytes(manifestPath)));
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                               exception is DecoderFallbackException || exception is FormatException)
            {
                throw new ContentPackageLoadException(
                    ContentPackageValidationCode.MissingManifest,
                    "Content manifest could not be read.",
                    exception);
            }

            ContentPackageValidationResult manifestResult = ContentPackageValidator.ValidateManifest(
                manifest,
                clientVersion,
                requireSignature);
            ThrowIfInvalid(manifestResult);

            var payload = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            string fullRoot = Path.GetFullPath(packageRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string[] files = Directory.GetFiles(fullRoot, "*", SearchOption.AllDirectories);
            for (int i = 0; i < files.Length; i++)
            {
                string fullPath = Path.GetFullPath(files[i]);
                string relativePath = fullPath.Substring(fullRoot.Length + 1).Replace('\\', '/');
                if (string.Equals(relativePath, "manifest.json", StringComparison.Ordinal) ||
                    relativePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(relativePath, ".DS_Store", StringComparison.OrdinalIgnoreCase))
                    continue;

                var fileInfo = new FileInfo(fullPath);
                if (fileInfo.Length <= 0 || fileInfo.Length > ContentPackageValidator.MaximumFileSize)
                    throw new ContentPackageLoadException(
                        ContentPackageValidationCode.InvalidFileSize,
                        $"Content size is invalid for '{relativePath}'.");
                payload.Add(relativePath, File.ReadAllBytes(fullPath));
            }

            ContentPackageValidationResult payloadResult = ContentPackageValidator.ValidatePayload(manifest, payload);
            ThrowIfInvalid(payloadResult);
            return new ContentPackageSnapshot(manifest, payload);
        }

        private static void ThrowIfInvalid(ContentPackageValidationResult result)
        {
            if (!result.IsValid) throw new ContentPackageLoadException(result.Code, result.Message);
        }
    }
}
