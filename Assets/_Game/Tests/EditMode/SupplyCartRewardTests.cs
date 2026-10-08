using System;
using System.Text;
using Emberfall.Application.Flow;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Infrastructure.Content;
using Emberfall.Infrastructure.Saves;
using Emberfall.Quests.Data;
using Emberfall.Quests.Domain;
using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class SupplyCartRewardTests
    {
        [Test] public void FinalHeavyPostureIsMultipliedOnceAndOtherAttackTagsAreUntouched()
        {
            var model = new CombatStateMachine(CombatTuning.CreateDefault());
            Assert.That(model.HeavyPostureMultiplier, Is.EqualTo(1));
            Assert.That(model.ApplySupplyCartHeavyPostureReward(1.15f), Is.True);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                Assert.That(model.ApplySupplyCartHeavyPostureReward(1.15f), Is.True);
                Assert.That(model.ResolveRewardedPostureDamage(AttackTag.Heavy, 60), Is.EqualTo(69).Within(.0001f));
                Assert.That(model.ResolveRewardedPostureDamage(AttackTag.Heavy, 85), Is.EqualTo(97.75f).Within(.0001f), "Charge/rune bonuses precede the single multiplication.");
                foreach (AttackTag tag in Enum.GetValues(typeof(AttackTag)))
                    if (tag != AttackTag.Heavy) Assert.That(model.ResolveRewardedPostureDamage(tag, 60), Is.EqualTo(60));
            }
            Assert.That(model.ApplySupplyCartHeavyPostureReward(1.25f), Is.False, "A later grant must not stack or replace the run reward.");
            model.Reset(); Assert.That(model.HeavyPostureMultiplier, Is.EqualTo(1.15f));
            Assert.That(new CombatStateMachine(CombatTuning.CreateDefault()).HeavyPostureMultiplier, Is.EqualTo(1));
        }

        [TestCase(0f)] [TestCase(.9f)] [TestCase(1f)] [TestCase(1.51f)]
        [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)] [TestCase(float.NegativeInfinity)]
        public void InvalidRuntimeGrantIsRejectedWithoutChangingTheModel(float value)
        {
            var model = new CombatStateMachine(CombatTuning.CreateDefault());
            Assert.That(model.ApplySupplyCartHeavyPostureReward(value), Is.False);
            Assert.That(model.HeavyPostureMultiplier, Is.EqualTo(1));
        }

        [Test] public void SchemaOneClaimIsIndependentUniqueAndMissingMeansUnclaimed()
        {
            var state = new RouteEnrichmentState();
            Assert.That(state.TryClaimSupplyCart(), Is.True); Assert.That(state.TryClaimSupplyCart(), Is.False);
            state.TryRecordAshApproachCleared(); state.TryRecordAshGuardPassCleared(); state.TryRecordAshReturnCleared();
            var dto = RouteEnrichmentProgressV1.FromSnapshot(state.CaptureSnapshot());
            var restored = RouteEnrichmentState.Restore(JsonUtility.FromJson<RouteEnrichmentProgressV1>(JsonUtility.ToJson(dto)).ToSnapshot());
            Assert.That(restored.SupplyCartClaimed, Is.True); Assert.That(restored.TryClaimSupplyCart(), Is.False);
            Assert.That(restored.WatchtowerDiscovered, Is.False); Assert.That(restored.RiskRewardClaimed, Is.False);
            var legacy = JsonUtility.FromJson<RouteEnrichmentProgressV1>("{\"ashGuardPassCleared\":true}");
            Assert.That(RouteEnrichmentState.Restore(legacy.ToSnapshot()).SupplyCartClaimed, Is.False);
        }

        [Test] public void AuthoredAndLegacyJsonUseTheSameDomainNormalization()
        {
            var files = M4ContentTestFactory.LoadAuthoredFiles();
            string json = Encoding.UTF8.GetString(files[RuntimeContentPaths.Quests]);
            var id = new ContentId("quest:emberfall.main");
            Assert.That(QuestDefinitionJsonLoader.Load(json).GetRequired(id).SupplyCartHeavyPostureMultiplier, Is.EqualTo(1.15f));
            string missing = json.Replace(",\n      \"supplyCartHeavyPostureMultiplier\": 1.15", "");
            Assert.That(missing, Is.Not.EqualTo(json), "The missing-field test must actually remove the new field.");
            Assert.That(QuestDefinitionJsonLoader.Load(missing).GetRequired(id).SupplyCartHeavyPostureMultiplier, Is.EqualTo(1));
            Assert.That(QuestDefinition.NormalizeSupplyCartMultiplier(0), Is.EqualTo(1));
            Assert.That(QuestDefinition.NormalizeSupplyCartMultiplier(1), Is.EqualTo(1));
        }

        [TestCase(-1f)] [TestCase(.5f)] [TestCase(1.51f)] [TestCase(float.NaN)] [TestCase(float.PositiveInfinity)]
        public void InvalidAuthoredMultiplierFailsDomainValidation(float value) =>
            Assert.Throws<FormatException>(() => QuestDefinition.NormalizeSupplyCartMultiplier(value));

        [TestCase("0.5")] [TestCase("1.51")] [TestCase("-1")]
        public void InvalidJsonRewardIsRejectedBeforePackageSelection(string value)
        {
            var files = M4ContentTestFactory.LoadAuthoredFiles();
            M4ContentTestFactory.ReplaceText(files, RuntimeContentPaths.Quests, "\"supplyCartHeavyPostureMultiplier\": 1.15", "\"supplyCartHeavyPostureMultiplier\": " + value);
            Assert.That(new GameContentPackagePreflight().Validate(M4ContentTestFactory.CreateSnapshot(new SemanticVersion(0, 8, 12), files), out string reason), Is.False);
            Assert.That(reason, Does.Contain("multiplier"));
            Assert.Throws<FormatException>(() => QuestDefinitionJsonLoader.Load(Encoding.UTF8.GetString(files[RuntimeContentPaths.Quests])));
        }

        [TestCase(false, true, RouteHighlightState.Ready)]
        [TestCase(false, false, RouteHighlightState.Locked)]
        [TestCase(true, false, RouteHighlightState.Completed)]
        public void CartHighlightDoesNotBorrowOtherChoiceOrWatchtowerState(bool claimed, bool available, RouteHighlightState expected) =>
            Assert.That(RouteHighlightStateResolver.ResolveRouteInteraction(RouteEnrichmentInteractionKind.SupplyCart, true,
                EmberValleyRouteChoice.Risk, available, AshReinforcementChoice.Together, claimed), Is.EqualTo(expected));

        [TestCase(960)] [TestCase(1280)]
        public void PermanentRewardRowGrowsPlayerFrameWithoutBossOverlap(int width)
        {
            var before = PresentationHudLayout.Resolve(width, true, 64);
            var after = PresentationHudLayout.Resolve(width, true, 64, true);
            Assert.That(before.Player.height, Is.EqualTo(170)); Assert.That(after.Player.height, Is.EqualTo(196));
            Assert.That(after.Player.Overlaps(after.Boss), Is.False); Assert.That(after.Quest.Overlaps(after.Boss), Is.False);
            Assert.That(after.Player.y + 186, Is.LessThanOrEqualTo(after.Player.yMax - 10));
        }
    }
}
