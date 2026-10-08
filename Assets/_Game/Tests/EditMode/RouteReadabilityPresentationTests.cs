using Emberfall.Application.Content;
using Emberfall.Application.Flow;
using Emberfall.Quests.Domain;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class RouteReadabilityPresentationTests
    {
        private const string TextPath = "Assets/_Game/Data/M2/texts.zh-CN.v1.json";

        [Test]
        public void BridgeMechanismStates_AreExplicitAndPairwiseDistinct()
        {
            Assert.That(RouteHighlightStateResolver.ResolveBridgeMechanism(
                "bridge-mechanism:A", false, false, false, true), Is.EqualTo(RouteHighlightState.Ready));
            Assert.That(RouteHighlightStateResolver.ResolveBridgeMechanism(
                "bridge-mechanism:A", false, true, false, false), Is.EqualTo(RouteHighlightState.Completed));
            Assert.That(RouteHighlightStateResolver.ResolveBridgeMechanism(
                "bridge-mechanism:B", false, true, false, false), Is.EqualTo(RouteHighlightState.Locked));
            Assert.That(RouteHighlightStateResolver.ResolveBridgeMechanism(
                "bridge-mechanism:B", true, true, false, true), Is.EqualTo(RouteHighlightState.Ready));
            Assert.That(RouteHighlightStateResolver.ResolveBridgeMechanism(
                "bridge-mechanism:B", true, true, true, false), Is.EqualTo(RouteHighlightState.Completed));
        }

        [TestCase(RouteEnrichmentInteractionKind.Watchtower, false, EmberValleyRouteChoice.None, true, RouteHighlightState.Ready)]
        [TestCase(RouteEnrichmentInteractionKind.Watchtower, true, EmberValleyRouteChoice.None, false, RouteHighlightState.Completed)]
        [TestCase(RouteEnrichmentInteractionKind.SupplyRoute, false, EmberValleyRouteChoice.None, true, RouteHighlightState.Ready)]
        [TestCase(RouteEnrichmentInteractionKind.RiskRoute, false, EmberValleyRouteChoice.Risk, false, RouteHighlightState.Completed)]
        [TestCase(RouteEnrichmentInteractionKind.SupplyRoute, false, EmberValleyRouteChoice.Risk, false, RouteHighlightState.Completed)]
        public void RouteInteractionStates_UseExplicitCompletionFacts(
            RouteEnrichmentInteractionKind kind,
            bool watchtowerDiscovered,
            EmberValleyRouteChoice choice,
            bool isAvailable,
            RouteHighlightState expected)
        {
            Assert.That(RouteHighlightStateResolver.ResolveRouteInteraction(
                kind, watchtowerDiscovered, choice, isAvailable), Is.EqualTo(expected));
        }

        [Test]
        public void SealConditionChecklist_HasStableLocalizedSnapshots()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(TextPath);
            LocalizedTextCatalog catalog = LocalizedTextJsonLoader.Load(asset.text);

            Assert.That(Build(catalog, false, false, false, false), Is.EqualTo(
                "断桥封印：前置机关 ✗ · 桥上守卫 ✗ · 后置机关 ✗\n" +
                "庭院封印：灼痕守卫架势 ✗"));
            Assert.That(Build(catalog, true, false, false, false), Is.EqualTo(
                "断桥封印：前置机关 ✓ · 桥上守卫 ✗ · 后置机关 ✗\n" +
                "庭院封印：灼痕守卫架势 ✗"));
            Assert.That(Build(catalog, true, true, false, true), Is.EqualTo(
                "断桥封印：前置机关 ✓ · 桥上守卫 ✓ · 后置机关 ✗\n" +
                "庭院封印：灼痕守卫架势 ✓"));
            Assert.That(Build(catalog, true, true, true, true), Is.EqualTo(
                "断桥封印：前置机关 ✓ · 桥上守卫 ✓ · 后置机关 ✓\n" +
                "庭院封印：灼痕守卫架势 ✓"));
        }

        [Test]
        public void LockedAndReadyBridgePromptsExistInLocalizedCatalog()
        {
            TextAsset asset = AssetDatabase.LoadAssetAtPath<TextAsset>(TextPath);
            LocalizedTextCatalog catalog = LocalizedTextJsonLoader.Load(asset.text);
            Assert.That(catalog.Resolve(new Emberfall.Core.Identifiers.ContentId(
                "text:interaction.bridge-mechanism-b-locked")), Does.Contain("守卫压制"));
            Assert.That(catalog.Resolve(new Emberfall.Core.Identifiers.ContentId(
                "text:interaction.bridge-mechanism-b")), Is.EqualTo("启动断桥后置机关"));
        }

        [TestCase(RouteEnrichmentInteractionKind.StagedReinforcement, AshReinforcementChoice.None, true, RouteHighlightState.Ready)]
        [TestCase(RouteEnrichmentInteractionKind.TogetherReinforcement, AshReinforcementChoice.None, false, RouteHighlightState.Locked)]
        [TestCase(RouteEnrichmentInteractionKind.StagedReinforcement, AshReinforcementChoice.Staged, false, RouteHighlightState.Completed)]
        [TestCase(RouteEnrichmentInteractionKind.TogetherReinforcement, AshReinforcementChoice.Staged, false, RouteHighlightState.Completed)]
        [TestCase(RouteEnrichmentInteractionKind.StagedReinforcement, AshReinforcementChoice.Together, false, RouteHighlightState.Completed)]
        [TestCase(RouteEnrichmentInteractionKind.TogetherReinforcement, AshReinforcementChoice.Together, false, RouteHighlightState.Completed)]
        public void ReinforcementHighlight_UsesItsOwnChoiceNotTheOtherRouteChoice(
            RouteEnrichmentInteractionKind kind, AshReinforcementChoice choice, bool available, RouteHighlightState expected)
        {
            Assert.That(RouteHighlightStateResolver.ResolveRouteInteraction(kind, true, EmberValleyRouteChoice.Risk,
                available, choice), Is.EqualTo(expected));
        }

        private static string Build(
            LocalizedTextCatalog catalog,
            bool mechanismA,
            bool bridgeCleared,
            bool mechanismB,
            bool guardBroken) => SealConditionChecklistText.Build(
                catalog.Resolve,
                true,
                mechanismA,
                bridgeCleared,
                mechanismB,
                true,
                guardBroken);
    }
}
