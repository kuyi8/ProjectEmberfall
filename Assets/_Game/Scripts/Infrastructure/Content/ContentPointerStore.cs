using System;
using System.IO;
using System.Text;
using Emberfall.Core.Content;

namespace Emberfall.Infrastructure.Content
{
    public sealed class ContentPointerStore
    {
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private readonly string _contentRoot;

        public ContentPointerStore(string contentRoot)
        {
            _contentRoot = string.IsNullOrWhiteSpace(contentRoot)
                ? throw new ArgumentException("Content root is required.", nameof(contentRoot))
                : contentRoot;
        }

        public bool TryReadCurrent(out SemanticVersion version) => TryRead("current", out version);
        public bool TryReadPrevious(out SemanticVersion version) => TryRead("previous", out version);

        public void Activate(SemanticVersion version)
        {
            Directory.CreateDirectory(_contentRoot);
            string currentPath = Path.Combine(_contentRoot, "current");
            string previousPath = Path.Combine(_contentRoot, "previous");
            string temporaryPath = Path.Combine(_contentRoot, $"current.{Guid.NewGuid():N}.tmp");
            try
            {
                byte[] bytes = StrictUtf8.GetBytes(version.ToString());
                using (var stream = new FileStream(
                           temporaryPath,
                           FileMode.CreateNew,
                           FileAccess.Write,
                           FileShare.None,
                           4096,
                           FileOptions.WriteThrough))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }

                if (File.Exists(currentPath))
                    File.Replace(temporaryPath, currentPath, previousPath, true);
                else
                    File.Move(temporaryPath, currentPath);
            }
            finally
            {
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
        }

        private bool TryRead(string fileName, out SemanticVersion version)
        {
            version = default;
            string path = Path.Combine(_contentRoot, fileName);
            if (!File.Exists(path)) return false;
            try
            {
                string text = StrictUtf8.GetString(File.ReadAllBytes(path));
                return string.Equals(text, text.Trim(), StringComparison.Ordinal) &&
                       SemanticVersion.TryParse(text, out version);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                                               exception is DecoderFallbackException)
            {
                return false;
            }
        }
    }
}
