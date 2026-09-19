using System;

namespace Emberfall.Core.Identifiers
{
    /// <summary>
    /// Stable, serialization-friendly identifier used across authored data and runtime state.
    /// Format: category:name, using lowercase ASCII letters, digits, '.', '_' or '-'.
    /// </summary>
    public readonly struct ContentId : IEquatable<ContentId>
    {
        private const char Separator = ':';

        public ContentId(string value)
        {
            if (!IsValid(value))
            {
                throw new ArgumentException(
                    "Content ID must use the format 'category:name' with lowercase ASCII characters.",
                    nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public static bool TryCreate(string value, out ContentId contentId)
        {
            if (!IsValid(value))
            {
                contentId = default;
                return false;
            }

            contentId = new ContentId(value);
            return true;
        }

        public static bool IsValid(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            int separatorIndex = value.IndexOf(Separator);
            if (separatorIndex <= 0 || separatorIndex != value.LastIndexOf(Separator) || separatorIndex == value.Length - 1)
            {
                return false;
            }

            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                if (character == Separator)
                {
                    continue;
                }

                bool isLowercaseLetter = character >= 'a' && character <= 'z';
                bool isDigit = character >= '0' && character <= '9';
                bool isPunctuation = character == '.' || character == '_' || character == '-';
                if (!isLowercaseLetter && !isDigit && !isPunctuation)
                {
                    return false;
                }
            }

            return true;
        }

        public bool Equals(ContentId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);

        public override bool Equals(object obj) => obj is ContentId other && Equals(other);

        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);

        public override string ToString() => Value ?? string.Empty;

        public static bool operator ==(ContentId left, ContentId right) => left.Equals(right);

        public static bool operator !=(ContentId left, ContentId right) => !left.Equals(right);
    }
}

