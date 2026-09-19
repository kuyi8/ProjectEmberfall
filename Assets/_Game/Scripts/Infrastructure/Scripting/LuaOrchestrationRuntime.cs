using System;
using System.Collections.Generic;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using XLua;

namespace Emberfall.Infrastructure.Scripting
{
    public readonly struct LuaQuestConditionContext
    {
        public LuaQuestConditionContext(ContentId questId, bool forestEnemiesDefeated, bool sealActivated)
        {
            QuestId = questId;
            ForestEnemiesDefeated = forestEnemiesDefeated;
            SealActivated = sealActivated;
        }

        public ContentId QuestId { get; }
        public bool ForestEnemiesDefeated { get; }
        public bool SealActivated { get; }
    }

    public readonly struct LuaQuestConditionResult
    {
        public LuaQuestConditionResult(bool eligible, ContentId reasonId)
        {
            Eligible = eligible;
            ReasonId = reasonId;
        }

        public bool Eligible { get; }
        public ContentId ReasonId { get; }
    }

    public enum LuaEncounterAction
    {
        None = 0,
        QueueWave = 1
    }

    public readonly struct LuaEncounterContext
    {
        public LuaEncounterContext(ContentId encounterId, int defeatedCount, int playerCount, bool gateOpen)
        {
            if (defeatedCount < 0) throw new ArgumentOutOfRangeException(nameof(defeatedCount));
            if (playerCount < 1) throw new ArgumentOutOfRangeException(nameof(playerCount));
            EncounterId = encounterId;
            DefeatedCount = defeatedCount;
            PlayerCount = playerCount;
            GateOpen = gateOpen;
        }

        public ContentId EncounterId { get; }
        public int DefeatedCount { get; }
        public int PlayerCount { get; }
        public bool GateOpen { get; }
    }

    public readonly struct LuaEncounterResult
    {
        public LuaEncounterResult(
            LuaEncounterAction action,
            ContentId waveId,
            ContentId presentationId)
        {
            Action = action;
            WaveId = waveId;
            PresentationId = presentationId;
        }

        public LuaEncounterAction Action { get; }
        public ContentId WaveId { get; }
        public ContentId PresentationId { get; }
    }

    /// <summary>
    /// Executes two small, signed orchestration scripts in a table-only environment.
    /// No C# object, delegate, Unity object or global Lua library is exposed to package code.
    /// </summary>
    public sealed class LuaOrchestrationRuntime : IDisposable
    {
        public const string QuestConditionPath = "Scripts/quest_forest_condition.lua";
        public const string EncounterOrchestrationPath = "Scripts/encounter_forest_orchestration.lua";
        public const int MaximumScriptBytes = 32 * 1024;

        private static readonly HashSet<string> ForbiddenIdentifiers = new HashSet<string>(StringComparer.Ordinal)
        {
            "CS", "_G", "xlua", "require", "package", "io", "os", "debug", "dofile", "loadfile",
            "load", "loadstring", "collectgarbage", "getmetatable", "setmetatable", "rawget", "rawset",
            "rawequal", "rawlen", "coroutine", "while", "repeat", "for", "function", "goto"
        };
        private readonly LuaEnv _lua = new LuaEnv();
        private bool _disposed;

        public LuaQuestConditionResult EvaluateQuestCondition(
            ContentPackageSnapshot snapshot,
            LuaQuestConditionContext context)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            ThrowIfDisposed();
            byte[] script = GetApprovedScript(snapshot, QuestConditionPath);

            using LuaTable sandbox = _lua.NewTable();
            using LuaTable emberfall = _lua.NewTable();
            using LuaTable quest = _lua.NewTable();
            using LuaTable encounter = _lua.NewTable();
            using LuaTable world = _lua.NewTable();
            using LuaTable presentation = _lua.NewTable();

            quest.Set("questId", context.QuestId.Value);
            quest.Set("forestEnemiesDefeated", context.ForestEnemiesDefeated);
            quest.Set("sealActivated", context.SealActivated);
            BuildSandbox(sandbox, emberfall, quest, encounter, world, presentation);

            LuaTable result = ExecuteForSingleTable(_lua, sandbox, script, QuestConditionPath);
            try
            {
                result.Get("eligible", out bool eligible);
                result.Get("reasonId", out string reasonText);
                ContentId reasonId = RequireCategory(reasonText, "text", "Quest reasonId");
                return new LuaQuestConditionResult(eligible, reasonId);
            }
            finally
            {
                result.Dispose();
            }
        }

        public LuaEncounterResult EvaluateEncounter(
            ContentPackageSnapshot snapshot,
            LuaEncounterContext context)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            ThrowIfDisposed();
            byte[] script = GetApprovedScript(snapshot, EncounterOrchestrationPath);

            using LuaTable sandbox = _lua.NewTable();
            using LuaTable emberfall = _lua.NewTable();
            using LuaTable quest = _lua.NewTable();
            using LuaTable encounter = _lua.NewTable();
            using LuaTable world = _lua.NewTable();
            using LuaTable presentation = _lua.NewTable();

            encounter.Set("encounterId", context.EncounterId.Value);
            encounter.Set("defeatedCount", context.DefeatedCount);
            encounter.Set("playerCount", context.PlayerCount);
            world.Set("gateOpen", context.GateOpen);
            presentation.Set("allowCue", true);
            BuildSandbox(sandbox, emberfall, quest, encounter, world, presentation);

            LuaTable result = ExecuteForSingleTable(_lua, sandbox, script, EncounterOrchestrationPath);
            try
            {
                result.Get("action", out string actionText);
                if (string.Equals(actionText, "none", StringComparison.Ordinal))
                    return new LuaEncounterResult(LuaEncounterAction.None, default, default);
                if (!string.Equals(actionText, "queue_wave", StringComparison.Ordinal))
                    throw new FormatException($"Encounter action '{actionText}' is not allow-listed.");

                result.Get("waveId", out string waveText);
                result.Get("presentationId", out string presentationText);
                return new LuaEncounterResult(
                    LuaEncounterAction.QueueWave,
                    RequireCategory(waveText, "wave", "Encounter waveId"),
                    RequireCategory(presentationText, "presentation", "Encounter presentationId"));
            }
            finally
            {
                result.Dispose();
            }
        }

        public bool ValidatePackage(ContentPackageSnapshot snapshot, out string failureReason)
        {
            try
            {
                EnsureOnlyApprovedEntryPoints(snapshot);
                EvaluateQuestCondition(
                    snapshot,
                    new LuaQuestConditionContext(new ContentId("quest:forest-seal"), true, false));
                EvaluateQuestCondition(
                    snapshot,
                    new LuaQuestConditionContext(new ContentId("quest:forest-seal"), false, false));
                EvaluateEncounter(
                    snapshot,
                    new LuaEncounterContext(new ContentId("encounter:forest-seal"), 0, 1, false));
                EvaluateEncounter(
                    snapshot,
                    new LuaEncounterContext(new ContentId("encounter:forest-seal"), 2, 1, true));
                failureReason = string.Empty;
                return true;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidOperationException ||
                                               exception is KeyNotFoundException || exception is FormatException ||
                                               exception is InvalidCastException || exception is LuaException)
            {
                failureReason = $"Lua package preflight failed: {exception.Message}";
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lua.Dispose();
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(LuaOrchestrationRuntime));
        }

        private static void BuildSandbox(
            LuaTable sandbox,
            LuaTable emberfall,
            LuaTable quest,
            LuaTable encounter,
            LuaTable world,
            LuaTable presentation)
        {
            emberfall.Set("Quest", quest);
            emberfall.Set("Encounter", encounter);
            emberfall.Set("World", world);
            emberfall.Set("Presentation", presentation);
            sandbox.Set("Emberfall", emberfall);
        }

        private static LuaTable ExecuteForSingleTable(
            LuaEnv lua,
            LuaTable sandbox,
            byte[] script,
            string path)
        {
            object[] values = lua.DoString(script, path, sandbox);
            if (values == null || values.Length != 1 || !(values[0] is LuaTable table))
                throw new FormatException($"Lua entry point '{path}' must return exactly one table.");
            return table;
        }

        private static byte[] GetApprovedScript(ContentPackageSnapshot snapshot, string path)
        {
            byte[] bytes = snapshot.GetBytes(path);
            if (bytes.Length == 0 || bytes.Length > MaximumScriptBytes)
                throw new FormatException($"Lua entry point '{path}' exceeds the allowed size.");
            ValidateIdentifiers(bytes, path);
            return bytes;
        }

        private static void EnsureOnlyApprovedEntryPoints(ContentPackageSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            int luaCount = 0;
            for (int i = 0; i < snapshot.Manifest.Files.Count; i++)
            {
                ContentPackageFile file = snapshot.Manifest.Files[i];
                if (file.Kind != ContentFileKind.Lua) continue;
                luaCount++;
                if (!string.Equals(file.Path, QuestConditionPath, StringComparison.Ordinal) &&
                    !string.Equals(file.Path, EncounterOrchestrationPath, StringComparison.Ordinal))
                    throw new FormatException($"Lua entry point '{file.Path}' is not allow-listed.");
            }

            if (luaCount != 2)
                throw new FormatException("Content package must contain exactly the two approved Lua entry points.");
        }

        private static ContentId RequireCategory(string value, string category, string label)
        {
            if (!ContentId.TryCreate(value, out ContentId id) ||
                !id.Value.StartsWith(category + ":", StringComparison.Ordinal))
                throw new FormatException($"{label} must be a valid '{category}:' content ID.");
            return id;
        }

        private static void ValidateIdentifiers(byte[] bytes, string path)
        {
            string text;
            try
            {
                text = new System.Text.UTF8Encoding(false, true).GetString(bytes);
            }
            catch (System.Text.DecoderFallbackException exception)
            {
                throw new FormatException($"Lua entry point '{path}' is not strict UTF-8.", exception);
            }

            foreach (string identifier in EnumerateCodeIdentifiers(text))
            {
                if (ForbiddenIdentifiers.Contains(identifier))
                    throw new FormatException($"Lua entry point '{path}' uses forbidden identifier '{identifier}'.");
            }
        }

        private static IEnumerable<string> EnumerateCodeIdentifiers(string source)
        {
            for (int index = 0; index < source.Length;)
            {
                char current = source[index];
                if (current == '-' && index + 1 < source.Length && source[index + 1] == '-')
                {
                    index += 2;
                    if (index + 1 < source.Length && source[index] == '[' && source[index + 1] == '[')
                    {
                        index += 2;
                        int close = source.IndexOf("]]", index, StringComparison.Ordinal);
                        index = close < 0 ? source.Length : close + 2;
                    }
                    else
                    {
                        int newline = source.IndexOf('\n', index);
                        index = newline < 0 ? source.Length : newline + 1;
                    }
                    continue;
                }

                if (current == '\'' || current == '"')
                {
                    char quote = current;
                    index++;
                    while (index < source.Length)
                    {
                        if (source[index] == '\\') index += Math.Min(2, source.Length - index);
                        else if (source[index++] == quote) break;
                    }
                    continue;
                }

                if ((current >= 'a' && current <= 'z') || (current >= 'A' && current <= 'Z') || current == '_')
                {
                    int start = index++;
                    while (index < source.Length)
                    {
                        char character = source[index];
                        if (!((character >= 'a' && character <= 'z') ||
                              (character >= 'A' && character <= 'Z') ||
                              (character >= '0' && character <= '9') || character == '_')) break;
                        index++;
                    }
                    yield return source.Substring(start, index - start);
                    continue;
                }

                index++;
            }
        }
    }
}
