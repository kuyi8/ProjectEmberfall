using Emberfall.Application.Flow;
using UnityEngine;

namespace Emberfall.UI
{
    public enum AshEncounterBanner { Approach, GuardPass, Return }

    /// <summary>Decorative record of a saved clear. Never enables enemies, rewards or interactions.</summary>
    public sealed class M2EncounterBannerPresenter : MonoBehaviour
    {
        [SerializeField] private M2RouteFlowController _flow;
        [SerializeField] private AshEncounterBanner _encounter;
        [SerializeField] private Renderer[] _renderers = System.Array.Empty<Renderer>();
        private bool _applied;
        private bool _shown;

        public AshEncounterBanner Encounter => _encounter;
        public bool IsBannerVisible => _shown;

        public void Configure(M2RouteFlowController flow, AshEncounterBanner encounter, Renderer[] renderers)
        {
            _flow = flow; _encounter = encounter; _renderers = renderers; _applied = false;
            SetVisible(false);
        }

        private void OnEnable() => Refresh();
        private void OnDisable() => SetVisible(false);
        // All encounter Update callbacks publish the real flag before this read. The independent
        // presentation root survives C's actor-root deactivation; it is NOT another clear owner.
        private void LateUpdate() => Refresh();
        private void Refresh()
        {
            bool visible = _flow != null && _flow.isActiveAndEnabled && _flow.IsInitialized && (_encounter switch
            {
                AshEncounterBanner.Approach => _flow.AshApproachCleared,
                AshEncounterBanner.GuardPass => _flow.AshGuardPassCleared,
                AshEncounterBanner.Return => _flow.AshReturnCleared,
                _ => false
            });
            SetVisible(visible);
        }

        private void SetVisible(bool visible)
        {
            if (_applied && visible == _shown) return;
            foreach (Renderer item in _renderers) if (item != null) item.enabled = visible;
            _shown = visible; _applied = true;
        }
    }
}
