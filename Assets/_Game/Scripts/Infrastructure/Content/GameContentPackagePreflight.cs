using System;
using System.Collections.Generic;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Infrastructure.Scripting;
using UnityEngine;

namespace Emberfall.Infrastructure.Content
{
    /// <summary>
    /// Validates the complete game-facing content contract before a package can become session authority.
    /// It intentionally reads only schema and stable-ID relationships; gameplay loaders remain the owners
    /// of concrete enemy and quest runtime models.
    /// </summary>
    public sealed class GameContentPackagePreflight : IContentPackagePreflight
    {
        private static readonly ContentId ForestQuestId = new ContentId("quest:forest-seal");
        private static readonly string[] RequiredEnemyIds =
        {
            "enemy:fogwalker",
            "enemy:rune-priest",
            "enemy:ruin-guard",
            "enemy:ruin-guard-scorched",
            "boss:ember-warden"
        };
        private static readonly string[] RequiredQuestIds =
        {
            "quest:emberfall.main",
            "quest:forest-seal"
        };

        public bool Validate(ContentPackageSnapshot snapshot, out string failureReason)
        {
            try
            {
                if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
                EnsureExpectedJsonFiles(snapshot);

                EnemyDocument enemies = Parse<EnemyDocument>(
                    snapshot.GetText(RuntimeContentPaths.Enemies), "enemy");
                QuestDocument quests = Parse<QuestDocument>(
                    snapshot.GetText(RuntimeContentPaths.Quests), "quest");
                TextDocument texts = Parse<TextDocument>(
                    snapshot.GetText(RuntimeContentPaths.SimplifiedChineseTexts), "localized text");

                if (enemies.schemaVersion != 1 || quests.schemaVersion != 1 || texts.schemaVersion != 1)
                    throw new NotSupportedException("JSON schema version is missing or unsupported.");

                var textIds = ValidateTexts(texts);
                var enemyIds = new HashSet<string>(StringComparer.Ordinal);
                ValidateRecords(enemies.enemies, "enemy", enemyIds, textIds);
                ValidateRecords(enemies.rangedEnemies, "ranged enemy", enemyIds, textIds);
                ValidateRecords(enemies.shieldEnemies, "shield enemy", enemyIds, textIds);
                ValidateRecords(enemies.wardens, "warden", enemyIds, textIds);
                RequireIds(enemyIds, RequiredEnemyIds, "enemy");

                var questIds = new HashSet<string>(StringComparer.Ordinal);
                if (quests.quests == null || quests.quests.Length == 0)
                    throw new FormatException("Quest JSON must contain quest records.");
                for (int i = 0; i < quests.quests.Length; i++)
                {
                    QuestRecord record = quests.quests[i] ?? throw new FormatException("Quest records cannot be null.");
                    ContentId id = RequireId(record.id, "quest", "Quest id");
                    ContentId title = RequireId(record.titleTextId, "text", "Quest titleTextId");
                    if (!questIds.Add(id.Value)) throw new FormatException($"Duplicate quest ID '{id}'.");
                    if (!textIds.Contains(title.Value))
                        throw new FormatException($"Quest '{id}' references missing text ID '{title}'.");
                    if (record.requiredSealCount <= 0)
                        throw new FormatException($"Quest '{id}' requires a positive seal count.");
                }
                RequireIds(questIds, RequiredQuestIds, "quest");

                using var lua = new LuaOrchestrationRuntime();
                if (!lua.ValidatePackage(snapshot, out string luaFailure))
                    throw new FormatException(luaFailure);

                LuaQuestConditionResult ready = lua.EvaluateQuestCondition(
                    snapshot,
                    new LuaQuestConditionContext(ForestQuestId, true, false));
                LuaQuestConditionResult blocked = lua.EvaluateQuestCondition(
                    snapshot,
                    new LuaQuestConditionContext(ForestQuestId, false, false));
                if (!ready.Eligible || blocked.Eligible)
                    throw new FormatException("Quest Lua does not preserve the required ready/blocked contract.");
                if (!textIds.Contains(ready.ReasonId.Value) || !textIds.Contains(blocked.ReasonId.Value))
                    throw new FormatException("Quest Lua references a missing localized reason ID.");

                LuaEncounterResult opening = lua.EvaluateEncounter(
                    snapshot,
                    new LuaEncounterContext(new ContentId("encounter:forest-seal"), 0, 1, false));
                if (opening.Action != LuaEncounterAction.QueueWave ||
                    opening.WaveId != new ContentId("wave:forest.guard-pair") ||
                    opening.PresentationId != new ContentId("presentation:forest.encounter-start"))
                    throw new FormatException("Encounter Lua does not queue the allow-listed forest opening wave.");

                failureReason = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
                                               exception is KeyNotFoundException || exception is NotSupportedException ||
                                               exception is FormatException)
            {
                failureReason = $"Game content preflight failed: {exception.Message}";
                return false;
            }
        }

        private static void EnsureExpectedJsonFiles(ContentPackageSnapshot snapshot)
        {
            var expected = new HashSet<string>(StringComparer.Ordinal)
            {
                RuntimeContentPaths.Enemies,
                RuntimeContentPaths.Quests,
                RuntimeContentPaths.SimplifiedChineseTexts
            };
            for (int i = 0; i < snapshot.Manifest.Files.Count; i++)
            {
                ContentPackageFile file = snapshot.Manifest.Files[i];
                if (file.Kind == ContentFileKind.Json && !expected.Remove(file.Path))
                    throw new FormatException($"JSON entry '{file.Path}' is not part of schema 1.");
            }
            if (expected.Count != 0) throw new FormatException("The package is missing a required JSON entry.");
        }

        private static HashSet<string> ValidateTexts(TextDocument document)
        {
            if (document.entries == null || document.entries.Length == 0)
                throw new FormatException("Localized text JSON must contain entries.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < document.entries.Length; i++)
            {
                TextRecord record = document.entries[i] ?? throw new FormatException("Localized text records cannot be null.");
                ContentId id = RequireId(record.id, "text", "Localized text id");
                if (string.IsNullOrWhiteSpace(record.value))
                    throw new FormatException($"Localized text '{id}' is empty.");
                if (!ids.Add(id.Value)) throw new FormatException($"Duplicate localized text ID '{id}'.");
            }
            return ids;
        }

        private static void ValidateRecords(
            EnemyRecord[] records,
            string label,
            HashSet<string> ids,
            HashSet<string> textIds)
        {
            if (records == null || records.Length == 0)
                throw new FormatException($"Enemy JSON must contain {label} records.");
            for (int i = 0; i < records.Length; i++)
            {
                EnemyRecord record = records[i] ?? throw new FormatException($"{label} records cannot be null.");
                ContentId id = RequireAnyCategory(record.id, new[] { "enemy", "boss" }, $"{label} id");
                ContentId name = RequireId(record.displayNameTextId, "text", $"{label} displayNameTextId");
                if (!ids.Add(id.Value)) throw new FormatException($"Duplicate enemy ID '{id}'.");
                if (!textIds.Contains(name.Value))
                    throw new FormatException($"Enemy '{id}' references missing text ID '{name}'.");
            }
        }

        private static void RequireIds(HashSet<string> actual, string[] required, string label)
        {
            for (int i = 0; i < required.Length; i++)
            {
                if (!actual.Contains(required[i]))
                    throw new FormatException($"Required {label} stable ID '{required[i]}' is missing.");
            }
        }

        private static ContentId RequireId(string value, string category, string label)
        {
            if (!ContentId.TryCreate(value, out ContentId id) ||
                !id.Value.StartsWith(category + ":", StringComparison.Ordinal))
                throw new FormatException($"{label} must be a valid '{category}:' stable ID.");
            return id;
        }

        private static ContentId RequireAnyCategory(string value, string[] categories, string label)
        {
            if (!ContentId.TryCreate(value, out ContentId id))
                throw new FormatException($"{label} is not a valid stable ID.");
            for (int i = 0; i < categories.Length; i++)
            {
                if (id.Value.StartsWith(categories[i] + ":", StringComparison.Ordinal)) return id;
            }
            throw new FormatException($"{label} has an unsupported category.");
        }

        private static T Parse<T>(string json, string label) where T : class
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException($"{label} JSON is empty.");
            try
            {
                return JsonUtility.FromJson<T>(json) ?? throw new FormatException($"{label} JSON is malformed.");
            }
            catch (ArgumentException exception)
            {
                throw new FormatException($"{label} JSON is malformed.", exception);
            }
        }

        [Serializable]
        private sealed class EnemyDocument
        {
            public int schemaVersion;
            public EnemyRecord[] enemies;
            public EnemyRecord[] rangedEnemies;
            public EnemyRecord[] shieldEnemies;
            public EnemyRecord[] wardens;
        }

        [Serializable]
        private sealed class EnemyRecord
        {
            public string id;
            public string displayNameTextId;
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
