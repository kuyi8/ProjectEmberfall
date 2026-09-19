using System;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Quests.Domain;
using UnityEngine;

namespace Emberfall.Quests.Data
{
    public static class QuestDefinitionJsonLoader
    {
        public const int SupportedSchemaVersion = 1;

        public static ContentRegistry<QuestDefinition> Load(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new ArgumentException("Quest JSON cannot be empty.", nameof(json));
            }

            QuestDocument document;
            try
            {
                document = JsonUtility.FromJson<QuestDocument>(json);
            }
            catch (Exception exception)
            {
                throw new FormatException("Quest JSON is malformed.", exception);
            }

            if (document == null || document.schemaVersion != SupportedSchemaVersion)
            {
                throw new NotSupportedException("Quest JSON schema version is not supported.");
            }

            if (document.quests == null || document.quests.Length == 0)
            {
                throw new FormatException("Quest JSON must contain at least one definition.");
            }

            var definitions = new QuestDefinition[document.quests.Length];
            for (int i = 0; i < document.quests.Length; i++)
            {
                QuestRecord record = document.quests[i] ?? throw new FormatException("Quest records cannot be null.");
                var questId = new ContentId(record.id);
                var titleTextId = new ContentId(record.titleTextId);
                if (!questId.Value.StartsWith("quest:", StringComparison.Ordinal))
                {
                    throw new FormatException($"Quest ID '{questId}' must use the quest category.");
                }

                if (!titleTextId.Value.StartsWith("text:", StringComparison.Ordinal))
                {
                    throw new FormatException($"Title ID '{titleTextId}' must use the text category.");
                }

                if (record.requiredSealCount <= 0)
                {
                    throw new FormatException("Required seal count must be greater than zero.");
                }

                definitions[i] = new QuestDefinition(questId, titleTextId, record.requiredSealCount);
            }

            return new ContentRegistry<QuestDefinition>(definitions);
        }

        [Serializable]
        private sealed class QuestDocument
        {
            public int schemaVersion;
            public QuestRecord[] quests;
        }

        [Serializable]
        private sealed class QuestRecord
        {
            public string id;
            public string titleTextId;
            public int requiredSealCount;
        }
    }
}
