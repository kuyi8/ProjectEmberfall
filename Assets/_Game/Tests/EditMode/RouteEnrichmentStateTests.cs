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
        public void ReinforcementChoice_IsUniqueAndOptionalSchemaOneRoundTrips()
        {
            var state = new RouteEnrichmentState();
            Assert.That(state.TryChooseReinforcement(AshReinforcementChoice.None), Is.False);
            Assert.That(state.TryChooseReinforcement((AshReinforcementChoice)99), Is.False);
            Assert.That(state.TryChooseReinforcement(AshReinforcementChoice.Staged), Is.True);
            Assert.That(state.TryChooseReinforcement(AshReinforcementChoice.Together), Is.False);
            var dto = Emberfall.Infrastructure.Saves.RouteEnrichmentProgressV1.FromSnapshot(state.CaptureSnapshot());
            Assert.That(RouteEnrichmentState.Restore(dto.ToSnapshot()).ReinforcementChoice, Is.EqualTo(AshReinforcementChoice.Staged));
            var legacy = UnityEngine.JsonUtility.FromJson<Emberfall.Infrastructure.Saves.RouteEnrichmentProgressV1>("{\"ashGuardPassCleared\":true,\"routeChoice\":0}");
            var restored = RouteEnrichmentState.Restore(legacy.ToSnapshot());
            Assert.That(restored.ReinforcementChoice, Is.EqualTo(AshReinforcementChoice.None));
            Assert.That(restored.TryChooseReinforcement(AshReinforcementChoice.Together), Is.False);
            Assert.Throws<System.FormatException>(() => new RouteEnrichmentSnapshot(false, EmberValleyRouteChoice.None,
                reinforcementChoice: (AshReinforcementChoice)42));
        }

        [Test]
        public void NoneCannotBeCommittedAsAChoice()
        {
            var state = new RouteEnrichmentState();
            Assert.That(state.TryChooseRoute(EmberValleyRouteChoice.None), Is.False);
            Assert.That(state.RouteChoice, Is.EqualTo(EmberValleyRouteChoice.None));
        }

        [Test]
        public void AshApproach_ClearIsUniqueAndSurvivesOptionalV1SaveRoundTrip()
        {
            var state = RouteEnrichmentState.Restore(null);
            Assert.That(state.AshApproachCleared, Is.False);
            Assert.That(state.TryRecordAshApproachCleared(), Is.True);
            Assert.That(state.TryRecordAshApproachCleared(), Is.False);
            var payload = Emberfall.Infrastructure.Saves.RouteEnrichmentProgressV1.FromSnapshot(state.CaptureSnapshot());
            var restored = RouteEnrichmentState.Restore(payload.ToSnapshot());
            Assert.That(restored.AshApproachCleared, Is.True);
            Assert.That(restored.WatchtowerDiscovered, Is.False);
            Assert.That(restored.RouteChoice, Is.EqualTo(EmberValleyRouteChoice.None));
            // Missing optional field in an old schema-1 payload defaults to never entered.
            var legacy = UnityEngine.JsonUtility.FromJson<Emberfall.Infrastructure.Saves.RouteEnrichmentProgressV1>("{\"watchtowerDiscovered\":true,\"routeChoice\":0}");
            Assert.That(RouteEnrichmentState.Restore(legacy.ToSnapshot()).AshApproachCleared, Is.False);
        }

        [Test]
        public void AshGuardPass_ClearIsIndependentUniqueAndOldSaveCompatible()
        {
            var state = new RouteEnrichmentState();
            Assert.That(state.TryRecordAshGuardPassCleared(), Is.True);
            Assert.That(state.TryRecordAshGuardPassCleared(), Is.False);
            var dto = Emberfall.Infrastructure.Saves.RouteEnrichmentProgressV1.FromSnapshot(state.CaptureSnapshot());
            var restored = RouteEnrichmentState.Restore(dto.ToSnapshot());
            Assert.That(restored.AshGuardPassCleared, Is.True);
            Assert.That(restored.AshApproachCleared, Is.False, "Bypassing A must not forge an A clear when B is defeated.");
            var legacy = UnityEngine.JsonUtility.FromJson<Emberfall.Infrastructure.Saves.RouteEnrichmentProgressV1>("{\"ashApproachCleared\":true,\"routeChoice\":0}");
            var old = RouteEnrichmentState.Restore(legacy.ToSnapshot());
            Assert.That(old.AshApproachCleared, Is.True); Assert.That(old.AshGuardPassCleared, Is.False);
        }

        [Test]
        public void AshReturn_ClearIsIndependentAndOptionalV1RoundTrips()
        {
            var state = new RouteEnrichmentState();
            Assert.That(state.TryRecordAshReturnCleared(), Is.True);
            Assert.That(state.TryRecordAshReturnCleared(), Is.False);
            var dto = Emberfall.Infrastructure.Saves.RouteEnrichmentProgressV1.FromSnapshot(state.CaptureSnapshot());
            var restored = RouteEnrichmentState.Restore(dto.ToSnapshot());
            Assert.That(restored.AshReturnCleared, Is.True);
            Assert.That(restored.AshApproachCleared, Is.False); Assert.That(restored.AshGuardPassCleared, Is.False);
            var legacy = UnityEngine.JsonUtility.FromJson<Emberfall.Infrastructure.Saves.RouteEnrichmentProgressV1>("{\"ashGuardPassCleared\":true,\"routeChoice\":0}");
            var old = RouteEnrichmentState.Restore(legacy.ToSnapshot());
            Assert.That(old.AshGuardPassCleared, Is.True); Assert.That(old.AshReturnCleared, Is.False);
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
