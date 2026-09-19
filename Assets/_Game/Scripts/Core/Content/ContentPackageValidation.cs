using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace Emberfall.Core.Content
{
    public enum ContentPackageValidationCode
    {
        None = 0,
        MissingManifest,
        UnsupportedSchema,
        InvalidPackageId,
        InvalidVersionRange,
        IncompatibleClientVersion,
        MissingSignature,
        InvalidFileList,
        InvalidFilePath,
        DuplicateFilePath,
        InvalidFileKind,
        InvalidFileSize,
        InvalidHash,
        MissingFile,
        UnexpectedFile,
        SizeMismatch,
        HashMismatch
    }

    public readonly struct ContentPackageValidationResult
    {
        public ContentPackageValidationResult(ContentPackageValidationCode code, string message)
        {
            Code = code;
            Message = message ?? string.Empty;
        }

        public ContentPackageValidationCode Code { get; }
        public string Message { get; }
        public bool IsValid => Code == ContentPackageValidationCode.None;

        public static ContentPackageValidationResult Valid =>
            new ContentPackageValidationResult(ContentPackageValidationCode.None, string.Empty);
    }

    public static class ContentPackageValidator
    {
        public const int SupportedSchemaVersion = 1;
        public const int MaximumFileCount = 64;
        public const long MaximumFileSize = 2L * 1024L * 1024L;
        public const long MaximumPackageSize = 8L * 1024L * 1024L;

        public static ContentPackageValidationResult ValidateManifest(
            ContentPackageManifest manifest,
            SemanticVersion clientVersion,
            bool requireSignature)
        {
            if (manifest == null)
                return Fail(ContentPackageValidationCode.MissingManifest, "Content manifest is missing.");
            if (manifest.SchemaVersion != SupportedSchemaVersion)
                return Fail(ContentPackageValidationCode.UnsupportedSchema, "Content manifest schema is unsupported.");
            if (manifest.PackageId.IsEmpty ||
                !manifest.PackageId.Value.StartsWith("package:", StringComparison.Ordinal))
                return Fail(ContentPackageValidationCode.InvalidPackageId, "Package ID must use the package category.");
            if (manifest.MinClientVersion > manifest.MaxClientVersion)
                return Fail(ContentPackageValidationCode.InvalidVersionRange, "Client version range is inverted.");
            if (clientVersion < manifest.MinClientVersion || clientVersion > manifest.MaxClientVersion)
                return Fail(
                    ContentPackageValidationCode.IncompatibleClientVersion,
                    $"Client {clientVersion} is outside {manifest.MinClientVersion}..{manifest.MaxClientVersion}.");

            bool hasAlgorithm = !string.IsNullOrWhiteSpace(manifest.SignatureAlgorithm);
            bool hasSignature = !string.IsNullOrWhiteSpace(manifest.Signature);
            if (hasAlgorithm != hasSignature || (requireSignature && (!hasAlgorithm || !hasSignature)))
                return Fail(ContentPackageValidationCode.MissingSignature, "Signature metadata is incomplete.");
            if (requireSignature && string.Equals(manifest.SignatureAlgorithm, "builtin", StringComparison.OrdinalIgnoreCase))
                return Fail(ContentPackageValidationCode.MissingSignature, "Patch packages cannot use the builtin trust marker.");

            if (manifest.Files == null || manifest.Files.Count == 0 || manifest.Files.Count > MaximumFileCount)
                return Fail(ContentPackageValidationCode.InvalidFileList, "Content file count is outside the allowed range.");

            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long totalSize = 0;
            for (int i = 0; i < manifest.Files.Count; i++)
            {
                ContentPackageFile file = manifest.Files[i];
                if (file == null)
                    return Fail(ContentPackageValidationCode.InvalidFileList, "Content file entries cannot be null.");
                if (!IsSafeRelativePath(file.Path))
                    return Fail(ContentPackageValidationCode.InvalidFilePath, $"Unsafe content path '{file.Path}'.");
                if (!paths.Add(file.Path))
                    return Fail(ContentPackageValidationCode.DuplicateFilePath, $"Duplicate content path '{file.Path}'.");
                if (!KindMatchesPath(file.Kind, file.Path))
                    return Fail(ContentPackageValidationCode.InvalidFileKind, $"Content type does not match '{file.Path}'.");
                if (file.Size <= 0 || file.Size > MaximumFileSize)
                    return Fail(ContentPackageValidationCode.InvalidFileSize, $"Content size is invalid for '{file.Path}'.");
                if (!IsLowercaseSha256(file.Sha256))
                    return Fail(ContentPackageValidationCode.InvalidHash, $"SHA-256 is invalid for '{file.Path}'.");

                totalSize += file.Size;
                if (totalSize > MaximumPackageSize)
                    return Fail(ContentPackageValidationCode.InvalidFileSize, "Content package exceeds its size budget.");
            }

            return ContentPackageValidationResult.Valid;
        }

        public static ContentPackageValidationResult ValidatePayload(
            ContentPackageManifest manifest,
            IReadOnlyDictionary<string, byte[]> payload)
        {
            ContentPackageValidationResult manifestResult = ValidateManifest(
                manifest,
                manifest?.MinClientVersion ?? default,
                false);
            if (!manifestResult.IsValid) return manifestResult;
            if (payload == null)
                return Fail(ContentPackageValidationCode.MissingFile, "Content payload is missing.");

            var expected = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < manifest.Files.Count; i++) expected.Add(manifest.Files[i].Path);
            foreach (string actualPath in payload.Keys)
            {
                if (!expected.Contains(actualPath))
                    return Fail(ContentPackageValidationCode.UnexpectedFile, $"Undeclared content file '{actualPath}'.");
            }

            for (int i = 0; i < manifest.Files.Count; i++)
            {
                ContentPackageFile file = manifest.Files[i];
                if (!payload.TryGetValue(file.Path, out byte[] bytes) || bytes == null)
                    return Fail(ContentPackageValidationCode.MissingFile, $"Content file '{file.Path}' is missing.");
                if (bytes.LongLength != file.Size)
                    return Fail(ContentPackageValidationCode.SizeMismatch, $"Content size changed for '{file.Path}'.");
                string actualHash = ComputeSha256Hex(bytes);
                if (!string.Equals(actualHash, file.Sha256, StringComparison.Ordinal))
                    return Fail(ContentPackageValidationCode.HashMismatch, $"Content hash changed for '{file.Path}'.");
            }

            return ContentPackageValidationResult.Valid;
        }

        public static string ComputeSha256Hex(byte[] bytes)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            char[] text = new char[hash.Length * 2];
            const string digits = "0123456789abcdef";
            for (int i = 0; i < hash.Length; i++)
            {
                text[i * 2] = digits[hash[i] >> 4];
                text[i * 2 + 1] = digits[hash[i] & 0x0f];
            }

            return new string(text);
        }

        public static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ||
                path[0] == '/' || path[path.Length - 1] == '/' ||
                path.IndexOf('\\') >= 0 || path.IndexOf(':') >= 0)
                return false;

            string[] segments = path.Split('/');
            if (segments.Length < 2) return false;
            for (int i = 0; i < segments.Length; i++)
            {
                string segment = segments[i];
                if (string.IsNullOrEmpty(segment) || segment == "." || segment == "..") return false;
                for (int characterIndex = 0; characterIndex < segment.Length; characterIndex++)
                {
                    char character = segment[characterIndex];
                    bool valid = character >= 'a' && character <= 'z' ||
                                 character >= 'A' && character <= 'Z' ||
                                 character >= '0' && character <= '9' ||
                                 character == '.' || character == '_' || character == '-';
                    if (!valid) return false;
                }
            }

            return true;
        }

        private static bool KindMatchesPath(ContentFileKind kind, string path)
        {
            return kind == ContentFileKind.Json
                ? path.StartsWith("Data/", StringComparison.Ordinal) && path.EndsWith(".json", StringComparison.Ordinal)
                : kind == ContentFileKind.Lua &&
                  path.StartsWith("Scripts/", StringComparison.Ordinal) && path.EndsWith(".lua", StringComparison.Ordinal);
        }

        private static bool IsLowercaseSha256(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length != 64) return false;
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (!((character >= '0' && character <= '9') || (character >= 'a' && character <= 'f')))
                    return false;
            }

            return true;
        }

        private static ContentPackageValidationResult Fail(ContentPackageValidationCode code, string message) =>
            new ContentPackageValidationResult(code, message);
    }
}
