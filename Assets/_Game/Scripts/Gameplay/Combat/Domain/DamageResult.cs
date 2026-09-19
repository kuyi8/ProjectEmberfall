namespace Emberfall.Gameplay.Combat.Domain
{
    public readonly struct DamageResult
    {
        public DamageResult(
            bool accepted,
            bool invulnerable,
            float appliedDamage,
            bool killed,
            bool blocked = false,
            bool guardBroken = false,
            bool defended = false,
            bool perfectGuard = false,
            bool staggered = false,
            float postureDamageApplied = 0f,
            float counterPostureDamage = 0f,
            bool perfectDodge = false)
        {
            Accepted = accepted;
            Invulnerable = invulnerable;
            AppliedDamage = appliedDamage;
            Killed = killed;
            Blocked = blocked;
            GuardBroken = guardBroken;
            Defended = defended;
            PerfectGuard = perfectGuard;
            Staggered = staggered;
            PostureDamageApplied = postureDamageApplied;
            CounterPostureDamage = counterPostureDamage;
            PerfectDodge = perfectDodge;
        }

        public bool Accepted { get; }
        public bool Invulnerable { get; }
        public float AppliedDamage { get; }
        public bool Killed { get; }
        public bool Blocked { get; }
        public bool GuardBroken { get; }
        public bool Defended { get; }
        public bool PerfectGuard { get; }
        public bool Staggered { get; }
        public float PostureDamageApplied { get; }
        public float CounterPostureDamage { get; }
        public bool PerfectDodge { get; }

        public static DamageResult Ignored => new DamageResult(false, false, 0f, false);
        public static DamageResult Evaded => new DamageResult(false, true, 0f, false);
    }
}
