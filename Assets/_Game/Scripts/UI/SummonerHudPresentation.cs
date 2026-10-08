using Emberfall.AI.Domain;
using UnityEngine;

namespace Emberfall.UI
{
    /// <summary>Read-only priority for one existing owner plate; never a target-selection rule.</summary>
    public readonly struct SummonerHudPresentation
    {
        public string TextId { get; }
        public Color Accent { get; }
        public bool ShowCastProgress { get; }
        public bool Emphasized { get; }

        private SummonerHudPresentation(string textId, Color accent, bool progress, bool emphasized)
        { TextId = textId; Accent = accent; ShowCastProgress = progress; Emphasized = emphasized; }

        public static SummonerHudPresentation Resolve(RangedEnemyState state, SummonerAttackKind attack,
            int livingSummons, bool recentInterrupt, bool selected)
        {
            bool summoning = attack == SummonerAttackKind.Summon;
            // A previous success must never hide the next real attack, even while its receipt is recent.
            if (state == RangedEnemyState.Windup)
                return new SummonerHudPresentation(summoning ? "text:summoner.windup" :
                    livingSummons >= SummonerEnemyDefinition.MaximumLivingSummons ? "text:summoner.projectile" :
                    "text:summoner.projectile-ready", new Color(.82f, .62f, 1f), summoning, true);
            if (state == RangedEnemyState.Release)
                return new SummonerHudPresentation(summoning ? "text:summoner.committed" :
                    "text:summoner.projectile-released", new Color(.82f, .62f, 1f), false, true);
            if (state == RangedEnemyState.Dead) return new SummonerHudPresentation(null, EmberfallGuiTheme.Line, false, false);
            if (recentInterrupt)
                return new SummonerHudPresentation("text:summoner.interrupted", EmberfallGuiTheme.Execution, false, true);
            string text = summoning && state == RangedEnemyState.Recovery ? "text:summoner.committed" :
                livingSummons > 0 || selected ? "text:summoner.idle" : null;
            return new SummonerHudPresentation(text, selected ? EmberfallGuiTheme.Interaction :
                EmberfallGuiTheme.Line, false, selected);
        }
    }
}
