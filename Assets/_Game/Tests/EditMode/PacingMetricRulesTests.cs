using System.Collections.Generic;
using Emberfall.Application.Flow;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class PacingMetricRulesTests
    {
        [Test]
        public void PlayerFacingBeatCount_MergesOnlyFiveDeclaredCausalPairs()
        {
            var events = new List<PacingMetricEvent>();
            for (int index = 0; index < 20; index++)
                events.Add(new PacingMetricEvent($"independent-{index}:completed", index + 1d));
            events.Add(new PacingMetricEvent("old-watchtower:discovered", 24.896d));
            events.Add(new PacingMetricEvent("watchtower-flask:upgraded", 24.896d));
            events.Add(new PacingMetricEvent("forest-encounter:cleared", 42.310d));
            events.Add(new PacingMetricEvent("forest-progress:completed", 42.310d));
            events.Add(new PacingMetricEvent("bridge-encounter:cleared", 49.678d));
            events.Add(new PacingMetricEvent("bridge-condition:completed", 49.692d));
            events.Add(new PacingMetricEvent("sanctum-gate:opened", 215.070d));
            events.Add(new PacingMetricEvent("sanctum-load:entered", 215.070d));
            events.Add(new PacingMetricEvent("pre-sanctum-encounter:cleared", 274.987d));
            events.Add(new PacingMetricEvent("route-risk-reward:claimed", 274.987d));

            Assert.That(PacingMetricRules.BuildBeatKeys(events), Has.Count.EqualTo(30));
            Assert.That(PacingMetricRules.BuildPlayerFacingBeatKeys(events), Has.Count.EqualTo(25));
        }

        [Test]
        public void PlayerFacingBeatCount_DoesNotMergeIndependentSimultaneousActions()
        {
            var events = new[]
            {
                new PacingMetricEvent("choice:first", 1d),
                new PacingMetricEvent("branch:discovered", 1d)
            };

            Assert.That(PacingMetricRules.BuildPlayerFacingBeatKeys(events), Has.Count.EqualTo(2));
        }

        [Test]
        public void PlayerFacingBeatCount_DoesNotMergeDeclaredPairOutsideTolerance()
        {
            var events = new[]
            {
                new PacingMetricEvent("bridge-encounter:cleared", 10d),
                new PacingMetricEvent("bridge-condition:completed", 10.051d)
            };

            Assert.That(PacingMetricRules.BuildPlayerFacingBeatKeys(events), Has.Count.EqualTo(2));
        }

        [Test]
        public void EncounterIntervals_ReportOverlapUnlessPairIsWhitelisted()
        {
            var intervals = new[]
            {
                new EncounterInterval("forest-encounter", 1d, 8d),
                new EncounterInterval("bridge-encounter", 7d, 12d),
                new EncounterInterval("courtyard-encounter", 13d, 18d)
            };

            Assert.That(PacingMetricRules.FindEncounterOverlaps(intervals), Has.Count.EqualTo(1));
            var whitelist = new HashSet<string>
            {
                PacingMetricRules.PairKey("forest-encounter", "bridge-encounter")
            };
            Assert.That(PacingMetricRules.FindEncounterOverlaps(intervals, whitelist), Is.Empty);
        }
    }
}
