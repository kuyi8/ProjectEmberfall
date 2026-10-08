using System;
using Emberfall.Core.Content;
using Emberfall.Core.Identifiers;

namespace Emberfall.AI.Domain
{
    /// <summary>Immutable JSON-owned tuning; the two-minion limit is a rule, not a wave modifier.</summary>
    public sealed class SummonerEnemyDefinition : IContentDefinition
    {
        public const int MaximumLivingSummons = 2;

        public SummonerEnemyDefinition(RangedEnemyDefinition combat, ContentId minionId,
            float summonWindup, float summonRecovery, float summonCooldown,
            float interruptCooldown, float initialSummonDelay, float minionHealth)
        {
            Combat = combat ?? throw new ArgumentNullException(nameof(combat));
            if (minionId.IsEmpty || !minionId.Value.StartsWith("enemy:", StringComparison.Ordinal) ||
                !Positive(summonWindup) || !Positive(summonRecovery) || !Positive(summonCooldown) ||
                !Positive(interruptCooldown) || !NonNegative(initialSummonDelay) || !Positive(minionHealth) ||
                combat.MaximumPosture >= 80f)
                throw new FormatException("Invalid summoner timing, minion reference or posture (<80).");
            MinionId = minionId;
            SummonWindup = summonWindup;
            SummonRecovery = summonRecovery;
            SummonCooldown = summonCooldown;
            InterruptCooldown = interruptCooldown;
            InitialSummonDelay = initialSummonDelay;
            MinionHealth = minionHealth;
        }

        public ContentId Id => Combat.Id;
        public ContentId DisplayNameTextId => Combat.DisplayNameTextId;
        public RangedEnemyDefinition Combat { get; }
        public ContentId MinionId { get; }
        public float SummonWindup { get; }
        public float SummonRecovery { get; }
        public float SummonCooldown { get; }
        public float InterruptCooldown { get; }
        public float InitialSummonDelay { get; }
        public float MinionHealth { get; }

        private static bool Positive(float value) => NonNegative(value) && value > 0f;
        private static bool NonNegative(float value) => value >= 0f && !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
