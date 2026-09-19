using System;
using System.Collections.Generic;
using Emberfall.Application.Content;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;

namespace Emberfall.UI
{
    /// <summary>
    /// Read-only adapter for co-op player-facing text. Runtime content remains the primary source;
    /// fallbacks only keep direct-scene editor tests and diagnostics readable before bootstrap.
    /// </summary>
    public static class NetworkLocalizedText
    {
        private static readonly Dictionary<string, string> Fallbacks = new Dictionary<string, string>
        {
            ["text:network.quest.syncing"] = "正在同步共享目标……",
            ["text:network.quest.activate-seal"] = "激活林地余烬封印",
            ["text:network.quest.enter-sanctum"] = "开启通往圣所的门扉",
            ["text:network.quest.defeat-warden"] = "击败余烬守墓人",
            ["text:network.quest.claim-reward"] = "领取余烬奖励",
            ["text:network.quest.return-scout"] = "返回营地向斥候汇报",
            ["text:network.quest.complete"] = "双人协作路线已完成",
            ["text:interaction.activate-forest-seal"] = "使用余烬符印激活雾林封印",
            ["text:interaction.enter-sanctum"] = "开启圣所门扉",
            ["text:interaction.finish-quest"] = "向斥候汇报",
            ["text:network.interaction.seal-activated"] = "封印已激活",
            ["text:network.interaction.sanctum-opened"] = "圣所门扉已开启",
            ["text:network.interaction.reward-claimed"] = "余烬奖励已取得",
            ["text:network.interaction.route-complete"] = "任务已交付",
            ["text:network.interaction.seal-too-far"] = "距离封印太远",
            ["text:network.interaction.gate-too-far"] = "距离圣所机关太远",
            ["text:network.interaction.reward-too-far"] = "距离余烬奖励太远",
            ["text:network.interaction.scout-too-far"] = "请靠近营地斥候",
            ["text:network.interaction.warden-active"] = "必须先击败余烬守墓人",
            ["text:network.interaction.already-claimed"] = "余烬奖励已被领取",
            ["text:network.interaction.world-syncing"] = "共享目标仍在同步",
            ["text:network.interaction.request-expired"] = "互动请求已过期，请重试",
            ["text:network.interaction.unavailable"] = "当前无法互动",
            ["text:network.warden.phase-one"] = "第一阶段",
            ["text:network.warden.phase-transition"] = "余烬重铸",
            ["text:network.warden.phase-two"] = "第二阶段",
            ["text:network.warden.state-windup"] = "正在蓄势",
            ["text:network.warden.state-attack"] = "攻击中",
            ["text:network.warden.state-recovery"] = "收招破绽",
            ["text:network.warden.state-guard-break"] = "架势崩解 · 全力输出",
            ["text:network.warden.state-transition"] = "重铸护甲 · 暂时无法受击",
            ["text:network.warden.state-alert"] = "保持警戒"
        };

        private static LocalizedTextCatalog _catalog;
        private static string _catalogVersion;

        public static string Resolve(string textId)
        {
            if (string.IsNullOrWhiteSpace(textId)) return string.Empty;
            EnsureCatalog();
            if (_catalog != null && _catalog.TryResolve(new ContentId(textId), out string value)) return value;
            return Fallbacks.TryGetValue(textId, out string fallback) ? fallback : textId;
        }

        private static void EnsureCatalog()
        {
            if (!ContentPackageRuntime.IsInitialized) return;
            string version = ContentPackageRuntime.Current.Snapshot.Manifest.ContentVersion.ToString();
            if (_catalog != null && string.Equals(_catalogVersion, version, StringComparison.Ordinal)) return;
            _catalog = LocalizedTextJsonLoader.Load(
                ContentPackageRuntime.GetRequiredText(RuntimeContentPaths.SimplifiedChineseTexts));
            _catalogVersion = version;
        }
    }
}
