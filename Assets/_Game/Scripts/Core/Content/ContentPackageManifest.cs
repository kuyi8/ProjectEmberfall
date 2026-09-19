using System;
using System.Collections.Generic;
using Emberfall.Core.Identifiers;

namespace Emberfall.Core.Content
{
    public enum ContentFileKind
    {
        Json = 0,
        Lua = 1
    }

    public sealed class ContentPackageFile
    {
        public ContentPackageFile(string path, ContentFileKind kind, long size, string sha256)
        {
            Path = path;
            Kind = kind;
            Size = size;
            Sha256 = sha256;
        }

        public string Path { get; }
        public ContentFileKind Kind { get; }
        public long Size { get; }
        public string Sha256 { get; }
    }

    public sealed class ContentPackageManifest
    {
        public ContentPackageManifest(
            int schemaVersion,
            ContentId packageId,
            SemanticVersion contentVersion,
            SemanticVersion minClientVersion,
            SemanticVersion maxClientVersion,
            string signatureAlgorithm,
            string signature,
            IEnumerable<ContentPackageFile> files)
        {
            SchemaVersion = schemaVersion;
            PackageId = packageId;
            ContentVersion = contentVersion;
            MinClientVersion = minClientVersion;
            MaxClientVersion = maxClientVersion;
            SignatureAlgorithm = signatureAlgorithm ?? string.Empty;
            Signature = signature ?? string.Empty;
            if (files == null) throw new ArgumentNullException(nameof(files));
            Files = new List<ContentPackageFile>(files).AsReadOnly();
        }

        public int SchemaVersion { get; }
        public ContentId PackageId { get; }
        public SemanticVersion ContentVersion { get; }
        public SemanticVersion MinClientVersion { get; }
        public SemanticVersion MaxClientVersion { get; }
        public string SignatureAlgorithm { get; }
        public string Signature { get; }
        public IReadOnlyList<ContentPackageFile> Files { get; }
    }
}
