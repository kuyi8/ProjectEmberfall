using System.Globalization;
using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Domain;
using Emberfall.Gameplay.Interaction;
using UnityEngine;

namespace Emberfall.UI
{
    /// <summary>Read-only explanations of existing choices, never a second interaction authority.</summary>
    public readonly struct RouteChoicePreview
    {
        private RouteChoicePreview(string title, string left, string leftBody, string right, string rightBody, int selected)
        { Title=title; Left=left; LeftBody=leftBody; Right=right; RightBody=rightBody; SelectedIndex=selected; }
        public string Title { get; }
        public string Left { get; }
        public string LeftBody { get; }
        public string Right { get; }
        public string RightBody { get; }
        public int SelectedIndex { get; }
        public const string Note = "每组二选一；靠近另一标记可切换 E 目标";

        public static bool TryRead(IInteractable candidate, int supplyQuota, int riskQuota, out RouteChoicePreview preview)
        {
            preview=default;
            if (candidate == null || !candidate.IsAvailable) return false;
            if (candidate is ForestTemplateInteractable forest) return Describe(forest.Role, out preview);
            if (candidate is RouteEnrichmentInteractable route) return Describe(route.Kind, supplyQuota, riskQuota, out preview);
            return false;
        }

        public static bool Describe(ForestTemplateInteractionRole role, out RouteChoicePreview preview)
        {
            preview=default;
            if (role != ForestTemplateInteractionRole.EmberRune && role != ForestTemplateInteractionRole.GuardRune) return false;
            // Quantities come from the same domain rules used by PlayerCombatActor.
            preview=new RouteChoicePreview("选择符文祝福", "烈印 · 蓄力重击",
                $"蓄满重击命中时\n追加 {Number(RuneBlessingRules.EmberHeavyBonusDamage)} 点伤害\n追加 {Number(RuneBlessingRules.EmberHeavyBonusPostureDamage)} 点架势伤害",
                "守印 · 精准反击",
                $"精准防御或精准闪避后\n下一次轻击追加 {Number(RuneBlessingRules.GuardCounterBonusDamage)} 伤害\n普通翻滚不会触发",
                role == ForestTemplateInteractionRole.EmberRune ? 0 : 1);
            return true;
        }

        public static bool Describe(RouteEnrichmentInteractionKind kind, int supplyQuota, int riskQuota, out RouteChoicePreview preview)
        {
            preview=default;
            if (kind == RouteEnrichmentInteractionKind.StagedReinforcement || kind == RouteEnrichmentInteractionKind.TogetherReinforcement)
            {
                preview=new RouteChoicePreview("选择增援安排", "守住窄口 · 分批",
                    "先迎战 2 名敌人\n盾卫倒下后补 1 近战\n总 3 人 · 窄口阵地", "引到空地 · 同时",
                    "3 名敌人同时在场\n保留宽场地绕行\n总 3 人 · 人数不减少",
                    kind == RouteEnrichmentInteractionKind.StagedReinforcement ? 0 : 1);
                return true;
            }
            if (kind != RouteEnrichmentInteractionKind.SupplyRoute && kind != RouteEnrichmentInteractionKind.RiskRoute || supplyQuota < 1 || riskQuota < 1) return false;
            // These are real authored ATTACK quotas, not enemy population or a difficulty score.
            preview=new RouteChoicePreview("选择圣所前的路线", "补给路线",
                $"同时近战进攻名额：{supplyQuota}\n敌人总数不变\n不追加险径清场奖励", "险径路线",
                $"同时近战进攻名额：{riskQuota}\n清场后药剂上限 +1\n敌人总数不变",
                kind == RouteEnrichmentInteractionKind.SupplyRoute ? 0 : 1);
            return true;
        }
        private static string Number(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);
    }

    public readonly struct RouteChoicePreviewLayout
    {
        public const float Width=480, Height=230;
        private RouteChoicePreviewLayout(Rect panel) { Panel=panel; }
        public Rect Panel { get; }
        public Rect Title => new Rect(Panel.x+14,Panel.y+10,452,24);
        public Rect Left => new Rect(Panel.x+14,Panel.y+44,220,150);
        public Rect Right => new Rect(Panel.x+246,Panel.y+44,220,150);
        public Rect Note => new Rect(Panel.x+14,Panel.y+204,452,20);
        public static Rect Heading(Rect card) => new Rect(card.x+12,card.y+8,196,24);
        public static Rect Badge(Rect card) => new Rect(card.x+12,card.y+35,196,20);
        public static Rect Body(Rect card) => new Rect(card.x+12,card.y+63,196,78);

        public static bool TryResolve(float width, float height, float topBottom, Rect[] obstacles, int count, out RouteChoicePreviewLayout layout)
        {
            layout=default;
            float top=Mathf.Max(20,topBottom+16), bottom=height-Height-212;
            if (bottom < top || width < Width+48) return false;
            // Nine bounded screen-space placements. Reject crowded layouts instead of
            // covering danger/target information or silently shrinking the Chinese font.
            for (int row=0;row<3;row++)
                for (int column=0;column<3;column++)
                {
                    float y=row==0?top:row==1?(top+bottom)*.5f:bottom;
                    float x=column==0?(width-Width)*.5f:column==1?24:width-Width-24;
                    var panel=new Rect(x,y,Width,Height); bool clear=true;
                    for (int i=0;i<count;i++)
                    {
                        Rect r=obstacles[i]; r=new Rect(r.x-8,r.y-8,r.width+16,r.height+16);
                        if (panel.Overlaps(r)) { clear=false;break; }
                    }
                    if (clear) { layout=new RouteChoicePreviewLayout(panel);return true; }
                }
            return false;
        }
    }
}
