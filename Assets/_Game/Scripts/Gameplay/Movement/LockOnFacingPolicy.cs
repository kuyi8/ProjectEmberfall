using Emberfall.Gameplay.Combat.Domain;

namespace Emberfall.Gameplay.Movement
{
    /// <summary>
    /// Keeps target tracking in the camera during locomotion, but restores target-facing for attacks.
    /// This avoids sideways foot sliding without giving animation ownership of attack direction.
    /// </summary>
    public static class LockOnFacingPolicy
    {
        public static bool ShouldFaceTarget(CombatState state)
        {
            switch (state)
            {
                case CombatState.LightAttack1:
                case CombatState.LightAttack2:
                case CombatState.LightAttack3:
                case CombatState.HeavyCharge:
                case CombatState.HeavyAttack:
                case CombatState.RangedAttack:
                case CombatState.Guard:
                    return true;
                default:
                    return false;
            }
        }
    }
}
