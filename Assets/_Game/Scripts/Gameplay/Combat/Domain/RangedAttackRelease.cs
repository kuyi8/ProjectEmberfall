namespace Emberfall.Gameplay.Combat.Domain
{
    /// <summary>Immutable authoritative snapshot emitted when a committed ranged action releases.</summary>
    public readonly struct RangedAttackRelease
    {
        public RangedAttackRelease(
            int attackSequence,
            float damage,
            float postureDamage,
            float projectileSpeed,
            float maximumDistance)
        {
            AttackSequence = attackSequence;
            Damage = damage;
            PostureDamage = postureDamage;
            ProjectileSpeed = projectileSpeed;
            MaximumDistance = maximumDistance;
        }

        public int AttackSequence { get; }
        public float Damage { get; }
        public float PostureDamage { get; }
        public float ProjectileSpeed { get; }
        public float MaximumDistance { get; }
    }
}
