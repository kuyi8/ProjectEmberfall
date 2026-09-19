using Emberfall.AI.Unity;
using Emberfall.Quests.Domain;
using UnityEngine;

namespace Emberfall.Application.Flow
{
    /// <summary>Applies the persisted route choice to an existing encounter composition.</summary>
    public sealed class RouteChoiceEncounterModifier : MonoBehaviour
    {
        [SerializeField] private M2RouteFlowController _flow;
        [SerializeField] private CombatEncounterCoordinator _encounter;
        [SerializeField, Min(1)] private int _supplyRouteMeleeQuota = 1;
        [SerializeField, Min(1)] private int _riskRouteMeleeQuota = 2;

        private EmberValleyRouteChoice _appliedChoice = (EmberValleyRouteChoice)(-1);

        public void Configure(M2RouteFlowController flow, CombatEncounterCoordinator encounter)
        {
            _flow = flow;
            _encounter = encounter;
        }

        private void Update()
        {
            if (_flow == null || _encounter == null || !_flow.IsInitialized ||
                _appliedChoice == _flow.RouteChoice)
            {
                return;
            }

            _appliedChoice = _flow.RouteChoice;
            _encounter.SetMaximumConcurrentMeleeAttackers(
                _appliedChoice == EmberValleyRouteChoice.Risk
                    ? _riskRouteMeleeQuota
                    : _supplyRouteMeleeQuota);
        }
    }
}
