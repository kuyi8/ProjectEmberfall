using Emberfall.Quests.Domain;

namespace Emberfall.Application.Flow
{
    public static class RouteHighlightStateResolver
    {
        public static RouteHighlightState ResolveBridgeMechanism(
            string stableId,
            bool bridgeEncounterCleared,
            bool mechanismAActivated,
            bool mechanismBActivated,
            bool canActivate)
        {
            if (stableId == "bridge-mechanism:A")
            {
                if (mechanismAActivated) return RouteHighlightState.Completed;
                return canActivate ? RouteHighlightState.Ready : RouteHighlightState.Locked;
            }

            if (stableId == "bridge-mechanism:B")
            {
                if (mechanismBActivated) return RouteHighlightState.Completed;
                return bridgeEncounterCleared && canActivate
                    ? RouteHighlightState.Ready
                    : RouteHighlightState.Locked;
            }

            return RouteHighlightState.Locked;
        }

        public static RouteHighlightState ResolveRouteInteraction(
            RouteEnrichmentInteractionKind kind,
            bool watchtowerDiscovered,
            EmberValleyRouteChoice routeChoice,
            bool isAvailable,
            AshReinforcementChoice reinforcementChoice = AshReinforcementChoice.None,
            bool supplyCartClaimed = false)
        {
            bool completed = kind switch
            {
                RouteEnrichmentInteractionKind.Watchtower => watchtowerDiscovered,
                RouteEnrichmentInteractionKind.SupplyCart => supplyCartClaimed,
                RouteEnrichmentInteractionKind.StagedReinforcement or RouteEnrichmentInteractionKind.TogetherReinforcement =>
                    reinforcementChoice != AshReinforcementChoice.None,
                _ => routeChoice != EmberValleyRouteChoice.None
            };
            if (completed) return RouteHighlightState.Completed;
            return isAvailable ? RouteHighlightState.Ready : RouteHighlightState.Locked;
        }
    }
}
