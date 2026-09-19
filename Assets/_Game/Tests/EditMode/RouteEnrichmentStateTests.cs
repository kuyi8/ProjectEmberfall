using Emberfall.Quests.Domain;
using NUnit.Framework;

namespace Emberfall.Tests.EditMode
{
    public sealed class RouteEnrichmentStateTests
    {
        [Test]
        public void DiscoverAndChoose_AreUniqueAndRoundTrip()
        {
            var state = new RouteEnrichmentState();

            Assert.That(state.TryDiscoverWatchtower(), Is.True);
            Assert.That(state.TryDiscoverWatchtower(), Is.False);
            Assert.That(state.TryChooseRoute(EmberValleyRouteChoice.Risk), Is.True);
            Assert.That(state.TryChooseRoute(EmberValleyRouteChoice.Supply), Is.False);

            RouteEnrichmentState restored = RouteEnrichmentState.Restore(state.CaptureSnapshot());
            Assert.That(restored.WatchtowerDiscovered, Is.True);
            Assert.That(restored.RouteChoice, Is.EqualTo(EmberValleyRouteChoice.Risk));
            Assert.That(restored.TryRecordPreSanctumCleared(), Is.True);
            Assert.That(restored.TryClaimRiskReward(), Is.True);
            Assert.That(restored.TryClaimRiskReward(), Is.False);

            restored = RouteEnrichmentState.Restore(restored.CaptureSnapshot());
            Assert.That(restored.PreSanctumEncounterCleared, Is.True);
            Assert.That(restored.RiskRewardClaimed, Is.True);
        }

        [Test]
        public void NoneCannotBeCommittedAsAChoice()
        {
            var state = new RouteEnrichmentState();
            Assert.That(state.TryChooseRoute(EmberValleyRouteChoice.None), Is.False);
            Assert.That(state.RouteChoice, Is.EqualTo(EmberValleyRouteChoice.None));
        }

        [Test]
        public void RiskReward_RequiresRiskChoiceAndPreSanctumClear()
        {
            var state = new RouteEnrichmentState();
            Assert.That(state.TryClaimRiskReward(), Is.False);
            Assert.That(state.TryChooseRoute(EmberValleyRouteChoice.Risk), Is.True);
            Assert.That(state.TryClaimRiskReward(), Is.False);
            Assert.That(state.TryRecordPreSanctumCleared(), Is.True);
            Assert.That(state.TryClaimRiskReward(), Is.True);
        }
    }
}
