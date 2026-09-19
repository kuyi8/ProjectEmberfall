using System;

namespace Emberfall.Gameplay.Combat.Domain
{
    public readonly struct DamageRequest
    {
        public DamageRequest(
            int sourceCombatantId,
            int attackSequence,
            float rawDamage,
            float postureDamage,
            AttackTag tag,
            bool isDefendable = true,
            bool isInDefenderFrontArc = true)
        {
            if (rawDamage <= 0f || postureDamage < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(rawDamage));
            }

            SourceCombatantId = sourceCombatantId;
            AttackSequence = attackSequence;
            RawDamage = rawDamage;
            PostureDamage = postureDamage;
            Tag = tag;
            IsDefendable = isDefendable;
            IsInDefenderFrontArc = isInDefenderFrontArc;
        }

        public int SourceCombatantId { get; }
        public int AttackSequence { get; }
        public float RawDamage { get; }
        public float PostureDamage { get; }
        public AttackTag Tag { get; }
        public bool IsDefendable { get; }
        public bool IsInDefenderFrontArc { get; }
    }
}
