using System;

namespace Emberfall.Core.Content
{
    /// <summary>Single read-only content authority selected before gameplay starts.</summary>
    public static class ContentPackageRuntime
    {
        public static ContentPackageSelection Current { get; private set; }

        public static bool IsInitialized => Current != null;

        public static void Initialize(ContentPackageSelection selection)
        {
            if (selection == null) throw new ArgumentNullException(nameof(selection));
            if (Current != null) throw new InvalidOperationException("Content package selection is already initialized.");
            Current = selection;
        }

        public static string GetRequiredText(string path)
        {
            if (Current == null)
                throw new InvalidOperationException("Runtime content has not been selected by AppBootstrap.");
            return Current.Snapshot.GetText(path);
        }

#if UNITY_EDITOR
        public static void ResetForTests()
        {
            Current = null;
        }
#endif
    }
}
