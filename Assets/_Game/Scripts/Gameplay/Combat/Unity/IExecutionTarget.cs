using Emberfall.Gameplay.Combat.Domain;

namespace Emberfall.Gameplay.Combat.Unity
{
    public interface IExecutionTarget
    {
        CombatTarget CombatTarget { get; }
        ExecutionTargetKind ExecutionKind { get; }
        bool IsExecutionEligible { get; }
        float ExecutionDamage { get; }
        bool TryClaimExecution();
        void HoldForExecution(float seconds);
    }
}
