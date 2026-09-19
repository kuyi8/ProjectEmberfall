using System;
using System.Collections.Generic;
using System.Linq;

namespace Emberfall.Application.Flow
{
    public readonly struct PacingMetricEvent
    {
        public PacingMetricEvent(string key, double monoSeconds)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            MonoSeconds = monoSeconds;
        }

        public string Key { get; }
        public double MonoSeconds { get; }
    }

    public readonly struct EncounterInterval
    {
        public EncounterInterval(string segment, double startSeconds, double endSeconds)
        {
            if (string.IsNullOrWhiteSpace(segment)) throw new ArgumentException("Segment is required.", nameof(segment));
            if (endSeconds < startSeconds) throw new ArgumentOutOfRangeException(nameof(endSeconds));
            Segment = segment;
            StartSeconds = startSeconds;
            EndSeconds = endSeconds;
        }

        public string Segment { get; }
        public double StartSeconds { get; }
        public double EndSeconds { get; }
    }

    public readonly struct EncounterIntervalOverlap
    {
        public EncounterIntervalOverlap(EncounterInterval first, EncounterInterval second)
        {
            First = first;
            Second = second;
            DurationSeconds = Math.Max(0d,
                Math.Min(first.EndSeconds, second.EndSeconds) -
                Math.Max(first.StartSeconds, second.StartSeconds));
        }

        public EncounterInterval First { get; }
        public EncounterInterval Second { get; }
        public double DurationSeconds { get; }
    }

    public static class PacingMetricRules
    {
        public const double CausalMergeToleranceSeconds = 0.05d;

        private static readonly string[] ExcludedBeatKeys =
        {
            "new-game:enter",
            "continue:enter",
            "result:published",
            "player:died"
        };

        private static readonly CausalPair[] CausalPairs =
        {
            new CausalPair("old-watchtower:discovered", "watchtower-flask:upgraded"),
            new CausalPair("forest-encounter:cleared", "forest-progress:completed"),
            new CausalPair("bridge-encounter:cleared", "bridge-condition:completed"),
            new CausalPair("sanctum-gate:opened", "sanctum-load:entered"),
            new CausalPair("pre-sanctum-encounter:cleared", "route-risk-reward:claimed")
        };

        public static IReadOnlyList<string> BuildBeatKeys(IEnumerable<PacingMetricEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (PacingMetricEvent item in events)
            {
                if (IsExcludedBeatKey(item.Key) || !seen.Add(item.Key)) continue;
                result.Add(item.Key);
            }

            return result;
        }

        public static IReadOnlyList<string> BuildPlayerFacingBeatKeys(IEnumerable<PacingMetricEvent> events)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            PacingMetricEvent[] firstEvents = events
                .Where(item => !IsExcludedBeatKey(item.Key))
                .GroupBy(item => item.Key, StringComparer.Ordinal)
                .Select(group => group.First())
                .ToArray();
            var result = new List<string>(firstEvents.Select(item => item.Key));
            var byKey = firstEvents.ToDictionary(item => item.Key, StringComparer.Ordinal);

            foreach (CausalPair pair in CausalPairs)
            {
                if (!byKey.TryGetValue(pair.PrimaryKey, out PacingMetricEvent primary) ||
                    !byKey.TryGetValue(pair.DerivedKey, out PacingMetricEvent derived) ||
                    Math.Abs(primary.MonoSeconds - derived.MonoSeconds) > CausalMergeToleranceSeconds)
                {
                    continue;
                }

                result.Remove(pair.DerivedKey);
            }

            return result;
        }

        public static IReadOnlyList<EncounterIntervalOverlap> FindEncounterOverlaps(
            IEnumerable<EncounterInterval> intervals,
            ISet<string> whitelistedPairs = null)
        {
            if (intervals == null) throw new ArgumentNullException(nameof(intervals));
            EncounterInterval[] ordered = intervals.OrderBy(item => item.StartSeconds).ToArray();
            var overlaps = new List<EncounterIntervalOverlap>();
            for (int firstIndex = 0; firstIndex < ordered.Length; firstIndex++)
            {
                for (int secondIndex = firstIndex + 1; secondIndex < ordered.Length; secondIndex++)
                {
                    EncounterInterval first = ordered[firstIndex];
                    EncounterInterval second = ordered[secondIndex];
                    if (second.StartSeconds >= first.EndSeconds) break;
                    if (first.Segment == second.Segment) continue;
                    if (whitelistedPairs != null && whitelistedPairs.Contains(PairKey(first.Segment, second.Segment)))
                        continue;
                    overlaps.Add(new EncounterIntervalOverlap(first, second));
                }
            }

            return overlaps;
        }

        public static string PairKey(string first, string second) =>
            string.CompareOrdinal(first, second) <= 0 ? $"{first}|{second}" : $"{second}|{first}";

        private static bool IsExcludedBeatKey(string key) =>
            string.IsNullOrWhiteSpace(key) ||
            ExcludedBeatKeys.Contains(key, StringComparer.Ordinal) ||
            key.EndsWith(":reset", StringComparison.Ordinal) ||
            key.StartsWith("combat-progress:", StringComparison.Ordinal);

        private readonly struct CausalPair
        {
            public CausalPair(string primaryKey, string derivedKey)
            {
                PrimaryKey = primaryKey;
                DerivedKey = derivedKey;
            }

            public string PrimaryKey { get; }
            public string DerivedKey { get; }
        }
    }
}
