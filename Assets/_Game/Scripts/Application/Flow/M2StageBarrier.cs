using Emberfall.Quests.Domain;
using UnityEngine;

namespace Emberfall.Application.Flow
{
    public sealed class M2StageBarrier : MonoBehaviour
    {
        [SerializeField] private M2RouteFlowController _flow;
        [SerializeField] private MainQuestStage _openAtStage = MainQuestStage.ReturnToScout;
        [SerializeField] private GameObject _blocker;

        private bool? _lastOpen;

        public bool IsOpen => _flow != null && _flow.IsInitialized && _flow.Stage >= _openAtStage;

        public void Configure(M2RouteFlowController flow, MainQuestStage openAtStage, GameObject blocker)
        {
            _flow = flow;
            _openAtStage = openAtStage;
            _blocker = blocker;
        }

        private void Start() => Refresh(true);

        private void Update() => Refresh(false);

        private void Refresh(bool force)
        {
            bool open = IsOpen;
            if (!force && _lastOpen == open)
            {
                return;
            }

            _lastOpen = open;
            if (_blocker != null)
            {
                _blocker.SetActive(!open);
            }
        }
    }
}
