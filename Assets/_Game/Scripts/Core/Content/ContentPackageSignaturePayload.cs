using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Emberfall.Core.Content
{
    /// <summary>Builds the deterministic UTF-8 bytes signed by trusted content publishers.</summary>
    public static class ContentPackageSignaturePayload
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false, true);

        public static byte[] Build(ContentPackageManifest manifest)
        {
            if (manifest == null) throw new ArgumentNullException(nameof(manifest));
            var files = new List<ContentPackageFile>(manifest.Files);
            files.Sort((left, right) => string.Compare(left.Path, right.Path, StringComparison.Ordinal));

            var builder = new StringBuilder(512);
            builder.Append("emberfall-content-signature-v1\n");
            Append(builder, "schemaVersion", manifest.SchemaVersion.ToString(CultureInfo.InvariantCulture));
            Append(builder, "packageId", manifest.PackageId.Value);
            Append(builder, "contentVersion", manifest.ContentVersion.ToString());
            Append(builder, "minClientVersion", manifest.MinClientVersion.ToString());
            Append(builder, "maxClientVersion", manifest.MaxClientVersion.ToString());
            Append(builder, "signatureAlgorithm", manifest.SignatureAlgorithm);
            Append(builder, "fileCount", files.Count.ToString(CultureInfo.InvariantCulture));
            for (int i = 0; i < files.Count; i++)
            {
                ContentPackageFile file = files[i];
                Append(builder, $"file[{i}].path", file.Path);
                Append(builder, $"file[{i}].kind", file.Kind == ContentFileKind.Json ? "json" : "lua");
                Append(builder, $"file[{i}].size", file.Size.ToString(CultureInfo.InvariantCulture));
                Append(builder, $"file[{i}].sha256", file.Sha256);
            }

            return Utf8WithoutBom.GetBytes(builder.ToString());
        }

        private static void Append(StringBuilder builder, string name, string value)
        {
            value = value ?? string.Empty;
            builder.Append(name)
                .Append('=')
                .Append(value.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':')
                .Append(value)
                .Append('\n');
        }
    }
}
