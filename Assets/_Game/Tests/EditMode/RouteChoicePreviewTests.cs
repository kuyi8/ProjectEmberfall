using System;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.UI;
using NUnit.Framework;
using UnityEngine;

namespace Emberfall.Tests.EditMode
{
    public sealed class RouteChoicePreviewTests
    {
        [TestCase(ForestTemplateInteractionRole.EmberRune,0)]
        [TestCase(ForestTemplateInteractionRole.GuardRune,1)]
        public void RuneFactsComeFromActualRulesAndHaveExactlyOneHighlightedChoice(ForestTemplateInteractionRole role,int selected)
        {
            Assert.That(RouteChoicePreview.Describe(role,out var p),Is.True);
            Assert.That(p.SelectedIndex,Is.EqualTo(selected));
            Assert.That(p.LeftBody,Does.Contain(RuneBlessingRules.EmberHeavyBonusDamage.ToString("0.#"))
                .And.Contain(RuneBlessingRules.EmberHeavyBonusPostureDamage.ToString("0.#")).And.Contain("蓄满"));
            Assert.That(p.RightBody,Does.Contain("精准防御或精准闪避").And.Contain("普通翻滚不会触发"));
            Assert.That(p.RightBody,Does.Contain(RuneBlessingRules.GuardCounterBonusDamage.ToString("0.#")));
        }

        [TestCase(RouteEnrichmentInteractionKind.SupplyRoute,0)]
        [TestCase(RouteEnrichmentInteractionKind.RiskRoute,1)]
        [TestCase(RouteEnrichmentInteractionKind.StagedReinforcement,0)]
        [TestCase(RouteEnrichmentInteractionKind.TogetherReinforcement,1)]
        public void QuotasNeverPretendToBePopulationAndStagedDoesNotDeleteAnEnemy(RouteEnrichmentInteractionKind kind,int selected)
        {
            Assert.That(RouteChoicePreview.Describe(kind,2,3,out var p),Is.True);
            Assert.That(p.SelectedIndex,Is.EqualTo(selected));
            if (kind == RouteEnrichmentInteractionKind.SupplyRoute || kind == RouteEnrichmentInteractionKind.RiskRoute)
            {
                Assert.That(p.LeftBody,Does.Contain("进攻名额：2").And.Contain("敌人总数不变"));
                Assert.That(p.RightBody,Does.Contain("进攻名额：3").And.Contain("清场后药剂上限 +1"));
            }
            else
            {
                Assert.That(p.LeftBody,Does.Contain("2 名").And.Contain("盾卫倒下后补 1").And.Contain("总 3 人"));
                Assert.That(p.RightBody,Does.Contain("3 名敌人同时").And.Contain("总 3 人"));
            }
            Assert.That(p.LeftBody+p.RightBody,Does.Not.Contain("简单").And.Not.Contain("困难"));
        }

        [Test]
        public void OtherInteractionsUnknownQuotasAndUnavailableActualCandidatesDoNotInventCards()
        {
            foreach (ForestTemplateInteractionRole role in Enum.GetValues(typeof(ForestTemplateInteractionRole)))
                Assert.That(RouteChoicePreview.Describe(role,out _),Is.EqualTo(role == ForestTemplateInteractionRole.EmberRune || role == ForestTemplateInteractionRole.GuardRune));
            foreach (RouteEnrichmentInteractionKind kind in Enum.GetValues(typeof(RouteEnrichmentInteractionKind)))
                Assert.That(RouteChoicePreview.Describe(kind,1,2,out _),Is.EqualTo(kind == RouteEnrichmentInteractionKind.SupplyRoute || kind == RouteEnrichmentInteractionKind.RiskRoute || kind == RouteEnrichmentInteractionKind.StagedReinforcement || kind == RouteEnrichmentInteractionKind.TogetherReinforcement));
            Assert.That(RouteChoicePreview.Describe(RouteEnrichmentInteractionKind.RiskRoute,0,2,out _),Is.False);
            var obj=new GameObject("UnavailablePreviewOnly");
            try { Assert.That(RouteChoicePreview.TryRead(obj.AddComponent<RouteEnrichmentInteractable>(),1,2,out _),Is.False); }
            finally { UnityEngine.Object.DestroyImmediate(obj); }
            Assert.That(RouteChoicePreview.TryRead(null,1,2,out _),Is.False);
        }

        [TestCase(960,540)]
        [TestCase(1280,800)]
        [TestCase(800,600)]
        [TestCase(2560,1440)]
        public void ActualF1F2FootprintsAndAllCardContentFitWithoutShrinkingFonts(int width,int height)
        {
            float scale=EmberfallGuiTheme.Scale(width,height),w=width/scale,h=height/scale;
            var top=PresentationHudLayout.Resolve(w,true,50,true);var bottom=PresentationBottomHudLayout.Resolve(w,h);
            var pixelOverlay=InputTelemetryOverlay.ResolvePanel(width,height,true);
            var overlay=new Rect(pixelOverlay.x/scale,pixelOverlay.y/scale,pixelOverlay.width/scale,pixelOverlay.height/scale);
            var obstacles=new[]{top.Player,top.Quest,bottom.Guide,overlay};
            Assert.That(RouteChoicePreviewLayout.TryResolve(w,h,Mathf.Max(top.Player.yMax,top.Quest.yMax),obstacles,obstacles.Length,out var l),Is.True);
            foreach (var r in obstacles) Assert.That(l.Panel.Overlaps(r),Is.False);
            foreach (var r in new[]{l.Panel,l.Title,l.Left,l.Right,l.Note,RouteChoicePreviewLayout.Body(l.Left),RouteChoicePreviewLayout.Body(l.Right)})
            { Assert.That(new Rect(0,0,w,h).Contains(r.min),Is.True);Assert.That(new Rect(0,0,w,h).Contains(r.max),Is.True); }
            Assert.That(l.Left.Overlaps(l.Right),Is.False);Assert.That(l.Panel.yMax,Is.LessThan(bottom.Feedback.yMin));
        }

        [Test]
        public void CrowdedWorldPlatesRejectThePreviewRatherThanCoveringCombatInformation()
        {
            var obstacles=new[]{new Rect(0,200,1280,400)};
            Assert.That(RouteChoicePreviewLayout.TryResolve(1280,800,194,obstacles,1,out _),Is.False);
        }
    }
}
