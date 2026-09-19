using Emberfall.Core.Identifiers;
using UnityEngine;

namespace Emberfall.Gameplay.Interaction
{
    public interface IInteractable
    {
        Transform InteractionTransform { get; }
        ContentId PromptTextId { get; }
        bool IsAvailable { get; }
        bool TryInteract(InteractionContext context);
    }
}
