using System;

namespace Emberfall.Core.Content
{
    public enum ContentPackageSource
    {
        Builtin = 0,
        Patch = 1,
        FallbackToBuiltin = 2
    }

    /// <summary>Immutable package choice and diagnostics fixed for one process lifetime.</summary>
    public sealed class ContentPackageSelection
    {
        public ContentPackageSelection(
            ContentPackageSnapshot snapshot,
            ContentPackageSource source,
            string fallbackReason = "")
        {
            Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            Source = source;
            FallbackReason = fallbackReason ?? string.Empty;
        }

        public ContentPackageSnapshot Snapshot { get; }
        public ContentPackageSource Source { get; }
        public string FallbackReason { get; }
    }
}
