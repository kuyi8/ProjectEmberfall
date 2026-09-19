using UnityEngine;

namespace Emberfall.Application.Flow
{
    /// <summary>Read-only route guidance between the bridge mechanisms.</summary>
    public sealed class BridgeMechanismGuidancePresenter : MonoBehaviour
    {
        [SerializeField] private M2RouteFlowController _flow;
        [SerializeField] private GameObject _visualRoot;

        public bool IsGuidanceVisible => _visualRoot != null && _visualRoot.activeSelf;
        public GameObject VisualRoot => _visualRoot;

        public void Configure(M2RouteFlowController flow, GameObject visualRoot)
        {
            _flow = flow;
            _visualRoot = visualRoot;
            Refresh();
        }

        private void Update() => Refresh();

        private void Refresh()
        {
            if (_visualRoot == null) return;
            bool visible = _flow != null && _flow.BridgeMechanismAActivated &&
                           !_flow.BridgeMechanismBActivated;
            if (_visualRoot.activeSelf != visible) _visualRoot.SetActive(visible);
        }
    }
}
