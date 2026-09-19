using Emberfall.Gameplay.Combat.Unity;

namespace Emberfall.Gameplay.Interaction
{
    public readonly struct InteractionContext
    {
        public InteractionContext(PlayerCombatActor actor)
        {
            Actor = actor;
        }

        public PlayerCombatActor Actor { get; }
    }
}
