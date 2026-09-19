using UnityEngine;

namespace Emberfall.Gameplay.Interaction
{
    public abstract class InteractableBehaviour : MonoBehaviour, IInteractable
    {
        public virtual Transform InteractionTransform => transform;
        public abstract Emberfall.Core.Identifiers.ContentId PromptTextId { get; }
        public abstract bool IsAvailable { get; }
        public abstract bool TryInteract(InteractionContext context);
    }
}
