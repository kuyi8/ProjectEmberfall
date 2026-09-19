using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Quests.Domain;

namespace Emberfall.Application.Flow
{
    public sealed class PacingTelemetryRecorder
    {
        public const string LogPrefix = "[M5C_PACING]";

        private readonly Func<double> _clock;
        private readonly Action<string> _sink;
        private readonly HashSet<string> _uniqueEvents = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _activeEncounters = new HashSet<string>(StringComparer.Ordinal);
        private readonly double _origin;
        private double _lastElapsed;
        private int _index;

        public PacingTelemetryRecorder(Func<double> clock, Action<string> sink, string runId = null)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            RunId = SanitizeToken(string.IsNullOrWhiteSpace(runId)
                ? Guid.NewGuid().ToString("N").Substring(0, 12)
                : runId);
            _origin = _clock();
        }

        public string RunId { get; }
        public int EncounterAttemptCount { get; private set; }
        public int ActiveEncounterCount => _activeEncounters.Count;

        public bool RecordMilestone(
            string segment,
            string eventName,
            MainQuestStage stage,
            int deaths,
            bool recordOnce = true)
        {
            string key = $"{segment}:{eventName}";
            if (recordOnce && !_uniqueEvents.Add(key))
            {
                return false;
            }

            Emit(segment, eventName, stage, deaths);
            return true;
        }

        public bool EnterEncounter(string segment, MainQuestStage stage, int deaths)
        {
            string safeSegment = SanitizeToken(segment);
            if (!_activeEncounters.Add(safeSegment))
            {
                return false;
            }

            EncounterAttemptCount++;
            Emit(safeSegment, "enter", stage, deaths);
            return true;
        }

        public bool EndEncounter(string segment, string eventName, MainQuestStage stage, int deaths)
        {
            string safeSegment = SanitizeToken(segment);
            if (!_activeEncounters.Remove(safeSegment))
            {
                return false;
            }

            Emit(safeSegment, eventName, stage, deaths);
            return true;
        }

        public void RecordDeath(MainQuestStage stage, int deaths)
        {
            Emit("player", "died", stage, deaths);
        }

        public void RecordCombatProgress(CombatProgressKind kind, MainQuestStage stage, int deaths)
        {
            Emit(
                "combat-progress",
                kind == CombatProgressKind.DamageDealt ? "dealt" : "received",
                stage,
                deaths);
        }

        private void Emit(string segment, string eventName, MainQuestStage stage, int deaths)
        {
            double elapsed = Math.Max(_lastElapsed, _clock() - _origin);
            _lastElapsed = elapsed;
            _index++;

            var line = new StringBuilder(192);
            line.Append(LogPrefix);
            line.Append(" run=").Append(RunId);
            line.Append(" index=").Append(_index.ToString(CultureInfo.InvariantCulture));
            line.Append(" segment=").Append(SanitizeToken(segment));
            line.Append(" event=").Append(SanitizeToken(eventName));
            line.Append(" mono=").Append(elapsed.ToString("0.000", CultureInfo.InvariantCulture));
            line.Append(" stage=").Append(SanitizeToken(stage.ToString()));
            line.Append(" encounters=").Append(EncounterAttemptCount.ToString(CultureInfo.InvariantCulture));
            line.Append(" deaths=").Append(Math.Max(0, deaths).ToString(CultureInfo.InvariantCulture));
            line.Append(" combatActive=").Append(ActiveEncounterCount > 0 ? "true" : "false");
            _sink(line.ToString());
        }

        private static string SanitizeToken(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }

            var result = new StringBuilder(value.Length);
            foreach (char character in value.Trim())
            {
                result.Append(char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.'
                    ? character
                    : '_');
            }

            return result.ToString();
        }
    }
}
