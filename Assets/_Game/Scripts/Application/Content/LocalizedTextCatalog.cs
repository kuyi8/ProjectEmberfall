using System;
using System.Collections.Generic;
using Emberfall.Core.Identifiers;

namespace Emberfall.Application.Content
{
    public sealed class LocalizedTextCatalog
    {
        private readonly Dictionary<ContentId, string> _entries;

        public LocalizedTextCatalog(IEnumerable<KeyValuePair<ContentId, string>> entries)
        {
            if (entries == null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            _entries = new Dictionary<ContentId, string>();
            foreach (KeyValuePair<ContentId, string> entry in entries)
            {
                if (entry.Key.IsEmpty || !entry.Key.Value.StartsWith("text:", StringComparison.Ordinal) ||
                    string.IsNullOrWhiteSpace(entry.Value) || !_entries.TryAdd(entry.Key, entry.Value))
                {
                    throw new ArgumentException("Text entries require unique text IDs and non-empty values.", nameof(entries));
                }
            }

            if (_entries.Count == 0)
            {
                throw new ArgumentException("Text catalog cannot be empty.", nameof(entries));
            }
        }

        public int Count => _entries.Count;

        public bool TryResolve(ContentId textId, out string value) => _entries.TryGetValue(textId, out value);

        public string Resolve(ContentId textId)
        {
            if (!_entries.TryGetValue(textId, out string value))
            {
                throw new KeyNotFoundException($"Localized text '{textId}' is not registered.");
            }

            return value;
        }
    }
}
