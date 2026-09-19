using Emberfall.Core.Identifiers;
using UnityEngine;

namespace Emberfall.Gameplay.Interaction
{
    public sealed class CheckpointInteractable : InteractableBehaviour
    {
        [SerializeField] private string _checkpointId = "checkpoint:prototype";
        [SerializeField] private string _promptTextId = "text:interaction.activate-checkpoint";
        [SerializeField] private Transform _spawnPoint;

        public override ContentId PromptTextId => new ContentId(_promptTextId);
        public override bool IsAvailable => isActiveAndEnabled;
        public ContentId CheckpointId => new ContentId(_checkpointId);

        public void Configure(string checkpointId, Transform spawnPoint)
        {
            _checkpointId = checkpointId;
            _spawnPoint = spawnPoint;
        }

        public override bool TryInteract(InteractionContext context)
        {
            if (context.Actor == null || !IsAvailable)
            {
                return false;
            }

            Transform point = _spawnPoint != null ? _spawnPoint : transform;
            return context.Actor.ActivateCheckpoint(CheckpointId, point.position, point.rotation);
        }
    }
}
