using System;

namespace Emberfall.Quests.Domain
{
    public enum EmberValleyRouteChoice
    {
        None = 0,
        Supply = 1,
        Risk = 2
    }

    public sealed class RouteEnrichmentSnapshot
    {
        public RouteEnrichmentSnapshot(
            bool watchtowerDiscovered,
            EmberValleyRouteChoice routeChoice,
            bool preSanctumEncounterCleared = false,
            bool riskRewardClaimed = false)
        {
            WatchtowerDiscovered = watchtowerDiscovered;
            RouteChoice = routeChoice;
            PreSanctumEncounterCleared = preSanctumEncounterCleared;
            RiskRewardClaimed = riskRewardClaimed;
            Validate();
        }

        public bool WatchtowerDiscovered { get; }
        public EmberValleyRouteChoice RouteChoice { get; }
        public bool PreSanctumEncounterCleared { get; }
        public bool RiskRewardClaimed { get; }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(EmberValleyRouteChoice), RouteChoice))
            {
                throw new FormatException("Route enrichment contains an unknown choice.");
            }

            if (RiskRewardClaimed &&
                (RouteChoice != EmberValleyRouteChoice.Risk || !PreSanctumEncounterCleared))
            {
                throw new FormatException("Route risk reward was claimed without its route and encounter requirements.");
            }
        }
    }

    /// <summary>Persisted, presentation-independent state for the optional branch and route choice.</summary>
    public sealed class RouteEnrichmentState
    {
        private RouteEnrichmentState(RouteEnrichmentSnapshot snapshot)
        {
            snapshot.Validate();
            WatchtowerDiscovered = snapshot.WatchtowerDiscovered;
            RouteChoice = snapshot.RouteChoice;
            PreSanctumEncounterCleared = snapshot.PreSanctumEncounterCleared;
            RiskRewardClaimed = snapshot.RiskRewardClaimed;
        }

        public RouteEnrichmentState()
        {
        }

        public bool WatchtowerDiscovered { get; private set; }
        public EmberValleyRouteChoice RouteChoice { get; private set; }
        public bool PreSanctumEncounterCleared { get; private set; }
        public bool RiskRewardClaimed { get; private set; }

        public static RouteEnrichmentState Restore(RouteEnrichmentSnapshot snapshot) =>
            snapshot == null ? new RouteEnrichmentState() : new RouteEnrichmentState(snapshot);

        public bool TryDiscoverWatchtower()
        {
            if (WatchtowerDiscovered) return false;
            WatchtowerDiscovered = true;
            return true;
        }

        public bool TryChooseRoute(EmberValleyRouteChoice choice)
        {
            if (RouteChoice != EmberValleyRouteChoice.None ||
                choice == EmberValleyRouteChoice.None ||
                !Enum.IsDefined(typeof(EmberValleyRouteChoice), choice))
            {
                return false;
            }

            RouteChoice = choice;
            return true;
        }

        public bool TryRecordPreSanctumCleared()
        {
            if (PreSanctumEncounterCleared) return false;
            PreSanctumEncounterCleared = true;
            return true;
        }

        public bool TryClaimRiskReward()
        {
            if (RiskRewardClaimed || !PreSanctumEncounterCleared ||
                RouteChoice != EmberValleyRouteChoice.Risk)
            {
                return false;
            }

            RiskRewardClaimed = true;
            return true;
        }

        public RouteEnrichmentSnapshot CaptureSnapshot() =>
            new RouteEnrichmentSnapshot(
                WatchtowerDiscovered,
                RouteChoice,
                PreSanctumEncounterCleared,
                RiskRewardClaimed);
    }
}
