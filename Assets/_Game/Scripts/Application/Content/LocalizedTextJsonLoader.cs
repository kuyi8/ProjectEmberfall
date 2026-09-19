using System;
using System.Collections.Generic;
using Emberfall.Core.Identifiers;
using UnityEngine;

namespace Emberfall.Application.Content
{
    public static class LocalizedTextJsonLoader
    {
        public const int SupportedSchemaVersion = 1;

        public static LocalizedTextCatalog Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Localized text JSON cannot be empty.", nameof(json));
            }

            TextDocument document;
            try
            {
                document = JsonUtility.FromJson<TextDocument>(json);
            }
            catch (Exception exception)
            {
                throw new FormatException("Localized text JSON is malformed.", exception);
            }

            if (document == null || document.schemaVersion != SupportedSchemaVersion)
            {
                throw new NotSupportedException("Localized text schema version is not supported.");
            }

            if (document.entries == null || document.entries.Length == 0)
            {
                throw new FormatException("Localized text JSON must contain entries.");
            }

            var entries = new KeyValuePair<ContentId, string>[document.entries.Length];
            for (int i = 0; i < document.entries.Length; i++)
            {
                TextRecord record = document.entries[i] ?? throw new FormatException("Localized text records cannot be null.");
                var id = new ContentId(record.id);
                if (!id.Value.StartsWith("text:", StringComparison.Ordinal) || string.IsNullOrWhiteSpace(record.value))
                {
                    throw new FormatException("Localized text records require a text ID and non-empty value.");
                }

                entries[i] = new KeyValuePair<ContentId, string>(id, record.value);
            }

            return new LocalizedTextCatalog(entries);
        }

        [Serializable]
        private sealed class TextDocument
        {
            public int schemaVersion;
            public TextRecord[] entries;
        }

        [Serializable]
        private sealed class TextRecord
        {
            public string id;
            public string value;
        }
    }
}
