using System;
using System.Collections.Generic;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using UnityEngine;

namespace Emberfall.Infrastructure.Content
{
    public static class ContentPackageManifestJson
    {
        public static ContentPackageManifest Deserialize(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new FormatException("Content manifest JSON is empty.");

            ManifestDto dto;
            try
            {
                dto = JsonUtility.FromJson<ManifestDto>(json);
            }
            catch (ArgumentException exception)
            {
                throw new FormatException("Content manifest JSON is malformed.", exception);
            }

            if (dto == null) throw new FormatException("Content manifest JSON is malformed.");
            if (!ContentId.TryCreate(dto.packageId, out ContentId packageId))
                throw new FormatException("Content manifest packageId is invalid.");
            if (!SemanticVersion.TryParse(dto.contentVersion, out SemanticVersion contentVersion) ||
                !SemanticVersion.TryParse(dto.minClientVersion, out SemanticVersion minClientVersion) ||
                !SemanticVersion.TryParse(dto.maxClientVersion, out SemanticVersion maxClientVersion))
                throw new FormatException("Content manifest contains an invalid semantic version.");
            if (dto.files == null) throw new FormatException("Content manifest files are missing.");

            var files = new List<ContentPackageFile>(dto.files.Length);
            for (int i = 0; i < dto.files.Length; i++)
            {
                FileDto file = dto.files[i];
                if (file == null || !TryParseKind(file.kind, out ContentFileKind kind))
                    throw new FormatException($"Content manifest file kind is invalid at index {i}.");
                files.Add(new ContentPackageFile(file.path, kind, file.size, file.sha256));
            }

            return new ContentPackageManifest(
                dto.schemaVersion,
                packageId,
                contentVersion,
                minClientVersion,
                maxClientVersion,
                dto.signatureAlgorithm,
                dto.signature,
                files);
        }

        public static string Serialize(ContentPackageManifest manifest, bool prettyPrint = true)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            var dto = new ManifestDto
            {
                schemaVersion = manifest.SchemaVersion,
                packageId = manifest.PackageId.Value,
                contentVersion = manifest.ContentVersion.ToString(),
                minClientVersion = manifest.MinClientVersion.ToString(),
                maxClientVersion = manifest.MaxClientVersion.ToString(),
                signatureAlgorithm = manifest.SignatureAlgorithm,
                signature = manifest.Signature,
                files = new FileDto[manifest.Files.Count]
            };

            for (int i = 0; i < manifest.Files.Count; i++)
            {
                ContentPackageFile file = manifest.Files[i];
                dto.files[i] = new FileDto
                {
                    path = file.Path,
                    kind = file.Kind == ContentFileKind.Json ? "json" : "lua",
                    size = file.Size,
                    sha256 = file.Sha256
                };
            }

            return JsonUtility.ToJson(dto, prettyPrint);
        }

        private static bool TryParseKind(string value, out ContentFileKind kind)
        {
            if (string.Equals(value, "json", StringComparison.Ordinal))
            {
                kind = ContentFileKind.Json;
                return true;
            }

            if (string.Equals(value, "lua", StringComparison.Ordinal))
            {
                kind = ContentFileKind.Lua;
                return true;
            }

            kind = default;
            return false;
        }

        [Serializable]
        private sealed class ManifestDto
        {
            public int schemaVersion;
            public string packageId;
            public string contentVersion;
            public string minClientVersion;
            public string maxClientVersion;
            public string signatureAlgorithm;
            public string signature;
            public FileDto[] files;
        }

        [Serializable]
        private sealed class FileDto
        {
            public string path;
            public string kind;
            public long size;
            public string sha256;
        }
    }
}
