using System;

namespace Emberfall.Quests.Domain
{
    public enum EmberValleyRouteChoice
    {
        None = 0,
        Supply = 1,
        Risk = 2
    }

    public enum AshReinforcementChoice { None = 0, Staged = 1, Together = 2 }

    public sealed class RouteEnrichmentSnapshot
    {
        public RouteEnrichmentSnapshot(
            bool watchtowerDiscovered,
            EmberValleyRouteChoice routeChoice,
            bool preSanctumEncounterCleared = false,
            bool riskRewardClaimed = false,
            bool ashApproachCleared = false,
            bool ashGuardPassCleared = false,
            bool ashReturnCleared = false,
            AshReinforcementChoice reinforcementChoice = AshReinforcementChoice.None,
            bool supplyCartClaimed = false)
        {
            WatchtowerDiscovered = watchtowerDiscovered;
            RouteChoice = routeChoice;
            PreSanctumEncounterCleared = preSanctumEncounterCleared;
            RiskRewardClaimed = riskRewardClaimed;
            AshApproachCleared = ashApproachCleared;
            AshGuardPassCleared = ashGuardPassCleared;
            AshReturnCleared = ashReturnCleared;
            ReinforcementChoice = reinforcementChoice;
            SupplyCartClaimed = supplyCartClaimed;
            Validate();
        }

        public bool WatchtowerDiscovered { get; }
        public EmberValleyRouteChoice RouteChoice { get; }
        public bool PreSanctumEncounterCleared { get; }
        public bool RiskRewardClaimed { get; }
        public bool AshApproachCleared { get; }
        public bool AshGuardPassCleared { get; }
        public bool AshReturnCleared { get; }
        public AshReinforcementChoice ReinforcementChoice { get; }
        public bool SupplyCartClaimed { get; }

        public void Validate()
        {
            if (!Enum.IsDefined(typeof(EmberValleyRouteChoice), RouteChoice) ||
                !Enum.IsDefined(typeof(AshReinforcementChoice), ReinforcementChoice))
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
            AshApproachCleared = snapshot.AshApproachCleared;
            AshGuardPassCleared = snapshot.AshGuardPassCleared;
            AshReturnCleared = snapshot.AshReturnCleared;
            ReinforcementChoice = snapshot.ReinforcementChoice;
            SupplyCartClaimed = snapshot.SupplyCartClaimed;
        }

        public RouteEnrichmentState()
        {
        }

        public bool WatchtowerDiscovered { get; private set; }
        public EmberValleyRouteChoice RouteChoice { get; private set; }
        public bool PreSanctumEncounterCleared { get; private set; }
        public bool RiskRewardClaimed { get; private set; }
        public bool AshApproachCleared { get; private set; }
        public bool AshGuardPassCleared { get; private set; }
        public bool AshReturnCleared { get; private set; }
        public AshReinforcementChoice ReinforcementChoice { get; private set; }
        public bool SupplyCartClaimed { get; private set; }

        public bool TryClaimSupplyCart()
        {
            if (SupplyCartClaimed) return false;
            SupplyCartClaimed = true;
            return true;
        }

        public bool TryChooseReinforcement(AshReinforcementChoice choice)
        {
            if (ReinforcementChoice != AshReinforcementChoice.None || AshGuardPassCleared ||
                choice == AshReinforcementChoice.None || !Enum.IsDefined(typeof(AshReinforcementChoice), choice)) return false;
            ReinforcementChoice = choice;
            return true;
        }
        public bool TryRecordAshReturnCleared()
        {
            if (AshReturnCleared) return false;
            AshReturnCleared = true;
            return true;
        }
        public bool TryRecordAshGuardPassCleared()
        {
            if (AshGuardPassCleared) return false;
            AshGuardPassCleared = true;
            return true;
        }

        public bool TryRecordAshApproachCleared()
        {
            if (AshApproachCleared) return false;
            AshApproachCleared = true;
            return true;
        }

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
                RiskRewardClaimed,
                AshApproachCleared,
                AshGuardPassCleared,
                AshReturnCleared,
                ReinforcementChoice,
                SupplyCartClaimed);
    }
}
