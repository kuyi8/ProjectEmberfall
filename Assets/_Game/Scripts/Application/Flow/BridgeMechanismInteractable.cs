using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Interaction;
using UnityEngine;

namespace Emberfall.Application.Flow
{
    public sealed class BridgeMechanismInteractable : InteractableBehaviour
    {
        [SerializeField] private M2RouteFlowController _flow;
        [SerializeField] private string _stableId = "bridge-mechanism:A";
        [SerializeField] private Renderer _indicator;
        [SerializeField] private M2QuestHighlightPresenter _highlight;

        private RouteHighlightState? _lastState;
        private MaterialPropertyBlock _propertyBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public string StableId => _stableId;
        public override ContentId PromptTextId => new ContentId(
            StableId == "bridge-mechanism:B"
                ? _flow != null && !_flow.BridgeEncounterCleared
                    ? "text:interaction.bridge-mechanism-b-locked"
                    : "text:interaction.bridge-mechanism-b"
                : "text:interaction.bridge-mechanism-a");
        public RouteHighlightState HighlightState => ResolveHighlightState();
        public bool UsesPropertyBlock => _indicator == null || _indicator.HasPropertyBlock();

        public override bool IsAvailable =>
            _flow != null && _flow.CanAttemptBridgeMechanism(StableId);

        public void Configure(
            M2RouteFlowController flow,
            string stableId,
            Renderer indicator,
            M2QuestHighlightPresenter highlight = null)
        {
            _flow = flow;
            _stableId = stableId;
            _indicator = indicator;
            _highlight = highlight;
            RefreshPresentation(true);
        }

        private void Update() => RefreshPresentation(false);

        public override bool TryInteract(InteractionContext context)
        {
            if (!IsAvailable || context.Actor == null)
            {
                return false;
            }

            bool succeeded = _flow.TryActivateBridgeMechanism(StableId);
            RefreshPresentation(true);
            return succeeded;
        }

        private void RefreshPresentation(bool force)
        {
            RouteHighlightState state = ResolveHighlightState();
            if (!force && _lastState == state) return;
            _lastState = state;
            _highlight?.SetState(state);
            if (_indicator == null) return;

            Color color = state switch
            {
                RouteHighlightState.Ready => new Color(1f, 0.52f, 0.08f),
                RouteHighlightState.Completed => new Color(0.12f, 0.38f, 0.34f),
                _ => new Color(0.28f, 0.31f, 0.34f)
            };
            _propertyBlock ??= new MaterialPropertyBlock();
            _indicator.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            _indicator.SetPropertyBlock(_propertyBlock);
        }

        private RouteHighlightState ResolveHighlightState() =>
            RouteHighlightStateResolver.ResolveBridgeMechanism(
                StableId,
                _flow?.BridgeEncounterCleared == true,
                _flow?.BridgeMechanismAActivated == true,
                _flow?.BridgeMechanismBActivated == true,
                _flow?.CanActivateBridgeMechanism(StableId) == true);
    }
}
