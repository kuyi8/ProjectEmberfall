using Emberfall.Core.Identifiers;
using Emberfall.Gameplay.Interaction;
using Emberfall.Quests.Domain;
using UnityEngine;

namespace Emberfall.Application.Flow
{
    public enum RouteEnrichmentInteractionKind
    {
        Watchtower = 0,
        SupplyRoute = 1,
        RiskRoute = 2
    }

    public sealed class RouteEnrichmentInteractable : InteractableBehaviour
    {
        [SerializeField] private M2RouteFlowController _flow;
        [SerializeField] private RouteEnrichmentInteractionKind _kind;
        [SerializeField] private Renderer _indicator;
        [SerializeField] private M2QuestHighlightPresenter _highlight;

        private RouteHighlightState? _lastState;
        private MaterialPropertyBlock _propertyBlock;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        public RouteEnrichmentInteractionKind Kind => _kind;
        public RouteHighlightState HighlightState => ResolveHighlightState();
        public bool UsesPropertyBlock => _indicator == null || _indicator.HasPropertyBlock();

        public override ContentId PromptTextId => _kind switch
        {
            RouteEnrichmentInteractionKind.Watchtower => new ContentId("text:interaction.examine-watchtower"),
            RouteEnrichmentInteractionKind.SupplyRoute => new ContentId("text:interaction.choose-supply-route"),
            _ => new ContentId("text:interaction.choose-risk-route")
        };

        public override bool IsAvailable => _flow != null && (_kind == RouteEnrichmentInteractionKind.Watchtower
            ? _flow.CanDiscoverWatchtower()
            : _flow.CanChooseRoute());

        public void Configure(
            M2RouteFlowController flow,
            RouteEnrichmentInteractionKind kind,
            Renderer indicator,
            M2QuestHighlightPresenter highlight = null)
        {
            _flow = flow;
            _kind = kind;
            _indicator = indicator;
            _highlight = highlight;
            RefreshPresentation(true);
        }

        private void Update() => RefreshPresentation(false);

        private void RefreshPresentation(bool force)
        {
            RouteHighlightState state = ResolveHighlightState();
            if (!force && _lastState == state) return;
            _lastState = state;
            _highlight?.SetState(state);
            if (_indicator == null) return;

            Color color = state switch
            {
                RouteHighlightState.Ready => new Color(1f, 0.55f, 0.08f),
                RouteHighlightState.Completed => new Color(0.12f, 0.38f, 0.34f),
                _ => new Color(0.28f, 0.31f, 0.34f)
            };
            _propertyBlock ??= new MaterialPropertyBlock();
            _indicator.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorId, color);
            _propertyBlock.SetColor(ColorId, color);
            _indicator.SetPropertyBlock(_propertyBlock);
        }

        public override bool TryInteract(InteractionContext context)
        {
            if (!IsAvailable || context.Actor == null) return false;
            return _kind switch
            {
                RouteEnrichmentInteractionKind.Watchtower => _flow.TryDiscoverWatchtower(context.Actor),
                RouteEnrichmentInteractionKind.SupplyRoute =>
                    _flow.TryChooseRoute(EmberValleyRouteChoice.Supply),
                RouteEnrichmentInteractionKind.RiskRoute =>
                    _flow.TryChooseRoute(EmberValleyRouteChoice.Risk),
                _ => false
            };
        }

        private RouteHighlightState ResolveHighlightState() =>
            RouteHighlightStateResolver.ResolveRouteInteraction(
                _kind,
                _flow?.WatchtowerDiscovered == true,
                _flow?.RouteChoice ?? EmberValleyRouteChoice.None,
                IsAvailable);
    }
}
