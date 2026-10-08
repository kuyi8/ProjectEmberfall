using Emberfall.AI.Domain;

namespace Emberfall.UI
{
    /// <summary>
    /// Stateless display of the currently observed Boss facts. No local progress clock,
    /// minimum display time, damage-window inference or network ordering policy.
    /// </summary>
    public static class WardenHudPresentation
    {
        public static string AttackName(WardenAttackKind attack) => attack switch
        {
            WardenAttackKind.SwordCombo => "二连斩",
            WardenAttackKind.ShieldBash => "盾击",
            WardenAttackKind.Charge => "直线冲锋",
            WardenAttackKind.RuneCleave => "符文劈斩",
            WardenAttackKind.DelayedBlast => "延迟爆破",
            _ => string.Empty
        };

        public static string CommittedAttackName(WardenState state, WardenAttackKind attack)
        {
            if (state != WardenState.Windup && state != WardenState.Attack && state != WardenState.Recovery)
                return string.Empty;
            return AttackName(attack);
        }

        public static string AppendCommittedAttack(string status, WardenState state, WardenAttackKind attack)
        {
            string name = CommittedAttackName(state, attack);
            return string.IsNullOrEmpty(name) ? status : status + " · " + name;
        }
    }
}
