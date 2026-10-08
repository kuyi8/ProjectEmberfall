using Emberfall.Application.Flow;
using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;

namespace Emberfall.UI
{
    /// <summary>Passive projection of existing saved journey facts; no new counters or cached truth.</summary>
    public readonly struct RouteCompletionPresentation
    {
        private RouteCompletionPresentation(string choices, string rewards, string encounters, string journey)
        { Choices = choices; Rewards = rewards; Encounters = encounters; Journey = journey; }

        public string Choices { get; }
        public string Rewards { get; }
        public string Encounters { get; }
        public string Journey { get; }
        public const string ScopeNote = "以下回顾来自当前存档，包含继续游戏前的记录；可选遭遇不影响主线交付。";

        public static RouteCompletionPresentation Read(M2RouteFlowController flow, PlayerCombatActor player)
            => new RouteCompletionPresentation(
                $"路线抉择：{flow.RouteChoiceStatus}\n\n增援抉择：{flow.ReinforcementChoiceStatus}",
                $"废弃补给车：{flow.SupplyCartStatus}\n" +
                $"险径奖励：{(flow.RiskRouteRewardClaimed ? "已取得（药剂上限 +1）" : "未取得")}\n" +
                $"旧瞭望塔：{(flow.WatchtowerDiscovered ? "已发现" : "未发现")}",
                DescribeEncounters(flow.AshApproachCleared, flow.AshGuardPassCleared, flow.AshReturnCleared),
                DescribeJourney(flow.SessionElapsedSeconds, flow.DeathCount, player != null ? player.ActiveCheckpointId.Value : null));

        public static string DescribeEncounters(bool approach, bool guardPass, bool returning)
            => $"林地前哨 · 唤骸与近战：{ClearStatus(approach)}\n" +
               $"窄口阵地 · 唤骸与盾卫：{ClearStatus(guardPass)}\n" +
               $"回程伏击 · 唤骸与祭司：{ClearStatus(returning)}";

        private static string ClearStatus(bool cleared) => cleared ? "已清场" : "尚未清场（可选）";

        public static string DescribeJourney(float seconds, int deaths, string checkpoint)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            string checkpointName = checkpoint == "checkpoint:courtyard" ? "圣所前庭"
                : checkpoint == "checkpoint:camp" ? "林地营地"
                : string.IsNullOrEmpty(checkpoint) ? "未记录休整点" : "未知休整点";
            // SessionElapsedSeconds is restored from SaveGameV1.elapsedSeconds. It also includes
            // paused pre-completion time; deliberately do not call it active play time or this run.
            return $"存档累计用时：{total / 60:00}:{total % 60:00}\n" +
                   $"存档累计倒地：{deaths} 次\n最近休整点：{checkpointName}";
        }
    }

    public readonly struct RouteCompletionLayout
    {
        private RouteCompletionLayout(float width, float height)
        {
            Panel = new Rect((width - 980f) * .5f, (height - 630f) * .5f, 980f, 630f);
            Title = new Rect(Panel.x + 30, Panel.y + 28, 920, 36);
            Subtitle = new Rect(Panel.x + 30, Panel.y + 70, 920, 24);
            Choices = new Rect(Panel.x + 30, Panel.y + 116, 450, 174);
            Rewards = new Rect(Panel.x + 500, Panel.y + 116, 450, 174);
            Encounters = new Rect(Panel.x + 30, Panel.y + 306, 450, 174);
            Journey = new Rect(Panel.x + 500, Panel.y + 306, 450, 174);
            Scope = new Rect(Panel.x + 30, Panel.y + 498, 920, 28);
            Return = new Rect(Panel.x + 30, Panel.y + 550, 920, 48);
        }
        public Rect Panel { get; }
        public Rect Title { get; }
        public Rect Subtitle { get; }
        public Rect Choices { get; }
        public Rect Rewards { get; }
        public Rect Encounters { get; }
        public Rect Journey { get; }
        public Rect Scope { get; }
        public Rect Return { get; }
        public static Rect Body(Rect card) => new Rect(card.x + 18, card.y + 54, card.width - 36, card.height - 68);
        public static Rect Heading(Rect card) => new Rect(card.x + 18, card.y + 14, card.width - 36, 24);
        public static RouteCompletionLayout Resolve(float logicalWidth, float logicalHeight)
            => new RouteCompletionLayout(logicalWidth, logicalHeight);
    }
}
