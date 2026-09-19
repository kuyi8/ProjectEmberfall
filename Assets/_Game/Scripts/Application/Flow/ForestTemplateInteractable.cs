using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Interaction;
using Emberfall.Quests.Domain;
using UnityEngine;

namespace Emberfall.Application.Flow
{
    public enum ForestTemplateInteractionRole
    {
        SigilPickup = 0,
        SupplyCache = 1,
        EmberRune = 2,
        GuardRune = 3
    }

    public sealed class ForestTemplateInteractable : InteractableBehaviour
    {
        [SerializeField] private ForestSealTemplateCoordinator _coordinator;
        [SerializeField] private ForestTemplateInteractionRole _role;
        [SerializeField] private Transform _animatedMarker;
        private Vector3 _markerBaseScale;

        public ForestTemplateInteractionRole Role => _role;

        public override ContentId PromptTextId => _role switch
        {
            ForestTemplateInteractionRole.SigilPickup => new ContentId("text:interaction.claim-forest-sigil"),
            ForestTemplateInteractionRole.SupplyCache => new ContentId("text:interaction.use-forest-supply"),
            ForestTemplateInteractionRole.EmberRune => new ContentId("text:interaction.choose-ember-rune"),
            _ => new ContentId("text:interaction.choose-guard-rune")
        };

        public override bool IsAvailable => _coordinator != null && _coordinator.CanInteract(_role);

        public void Configure(
            ForestSealTemplateCoordinator coordinator,
            ForestTemplateInteractionRole role,
            Transform animatedMarker = null)
        {
            _coordinator = coordinator;
            _role = role;
            _animatedMarker = animatedMarker;
            _markerBaseScale = _animatedMarker != null ? _animatedMarker.localScale : Vector3.one;
        }

        private void Awake()
        {
            if (_animatedMarker != null)
            {
                _markerBaseScale = _animatedMarker.localScale;
            }
        }

        private void Update()
        {
            if (_animatedMarker != null && IsAvailable)
            {
                _animatedMarker.Rotate(0f, 55f * Time.deltaTime, 0f, Space.World);
                float pulse = 1f + (Mathf.Sin(Time.time * 3.4f) * 0.08f);
                _animatedMarker.localScale = _markerBaseScale * pulse;
            }
        }

        public override bool TryInteract(InteractionContext context) =>
            context.Actor != null && IsAvailable && _coordinator.TryInteract(_role);
    }
}
