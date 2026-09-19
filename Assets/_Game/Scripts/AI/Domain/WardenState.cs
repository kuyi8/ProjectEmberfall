namespace Emberfall.AI.Domain
{
    public enum WardenState
    {
        Dormant = 0,
        Chase = 1,
        Windup = 2,
        Attack = 3,
        Recovery = 4,
        GuardBreak = 5,
        Return = 6,
        Dead = 7,
        PhaseTransition = 8
    }
}
