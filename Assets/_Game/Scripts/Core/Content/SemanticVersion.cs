using System;
using System.Globalization;

namespace Emberfall.Core.Content
{
    /// <summary>Strict numeric major.minor.patch version used by client and content compatibility checks.</summary>
    public readonly struct SemanticVersion : IComparable<SemanticVersion>, IEquatable<SemanticVersion>
    {
        public SemanticVersion(int major, int minor, int patch)
        {
            if (major < 0) throw new ArgumentOutOfRangeException(nameof(major));
            if (minor < 0) throw new ArgumentOutOfRangeException(nameof(minor));
            if (patch < 0) throw new ArgumentOutOfRangeException(nameof(patch));
            Major = major;
            Minor = minor;
            Patch = patch;
        }

        public int Major { get; }
        public int Minor { get; }
        public int Patch { get; }

        public static SemanticVersion Parse(string value)
        {
            if (!TryParse(value, out SemanticVersion version))
            {
                throw new FormatException("Version must use non-negative major.minor.patch numbers.");
            }

            return version;
        }

        public static bool TryParse(string value, out SemanticVersion version)
        {
            version = default;
            if (string.IsNullOrWhiteSpace(value) || !string.Equals(value, value.Trim(), StringComparison.Ordinal))
            {
                return false;
            }

            string[] parts = value.Split('.');
            if (parts.Length != 3 ||
                !TryParsePart(parts[0], out int major) ||
                !TryParsePart(parts[1], out int minor) ||
                !TryParsePart(parts[2], out int patch))
            {
                return false;
            }

            version = new SemanticVersion(major, minor, patch);
            return true;
        }

        public int CompareTo(SemanticVersion other)
        {
            int major = Major.CompareTo(other.Major);
            if (major != 0) return major;
            int minor = Minor.CompareTo(other.Minor);
            return minor != 0 ? minor : Patch.CompareTo(other.Patch);
        }

        public bool Equals(SemanticVersion other) =>
            Major == other.Major && Minor == other.Minor && Patch == other.Patch;

        public override bool Equals(object obj) => obj is SemanticVersion other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Major;
                hash = (hash * 397) ^ Minor;
                return (hash * 397) ^ Patch;
            }
        }

        public override string ToString() => string.Format(
            CultureInfo.InvariantCulture,
            "{0}.{1}.{2}",
            Major,
            Minor,
            Patch);

        public static bool operator <(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) < 0;
        public static bool operator >(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) > 0;
        public static bool operator <=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) <= 0;
        public static bool operator >=(SemanticVersion left, SemanticVersion right) => left.CompareTo(right) >= 0;
        public static bool operator ==(SemanticVersion left, SemanticVersion right) => left.Equals(right);
        public static bool operator !=(SemanticVersion left, SemanticVersion right) => !left.Equals(right);

        private static bool TryParsePart(string value, out int result)
        {
            result = 0;
            if (string.IsNullOrEmpty(value)) return false;
            for (int i = 0; i < value.Length; i++)
            {
                if (value[i] < '0' || value[i] > '9') return false;
            }

            return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
        }
    }
}
