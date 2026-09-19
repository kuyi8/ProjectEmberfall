using System;
using System.Collections.Generic;
using System.Text;

namespace Emberfall.Core.Content
{
    /// <summary>Immutable bytes from one completely validated package.</summary>
    public sealed class ContentPackageSnapshot
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly Dictionary<string, byte[]> _files;

        public ContentPackageSnapshot(
            ContentPackageManifest manifest,
            IReadOnlyDictionary<string, byte[]> files)
        {
            Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            if (files == null) throw new ArgumentNullException(nameof(files));
            _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            foreach (KeyValuePair<string, byte[]> pair in files)
            {
                if (pair.Value == null) throw new ArgumentException("Content bytes cannot be null.", nameof(files));
                _files.Add(pair.Key, (byte[])pair.Value.Clone());
            }
        }

        public ContentPackageManifest Manifest { get; }

        public byte[] GetBytes(string path)
        {
            if (!_files.TryGetValue(path, out byte[] bytes))
                throw new KeyNotFoundException($"Content file '{path}' is not present.");
            return (byte[])bytes.Clone();
        }

        public string GetText(string path) => StrictUtf8.GetString(GetBytes(path));
    }
}
