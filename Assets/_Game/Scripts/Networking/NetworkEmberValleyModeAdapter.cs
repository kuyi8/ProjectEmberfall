using System;
using Unity.Netcode;
using UnityEngine;

namespace Emberfall.Networking
{
    /// <summary>
    /// Isolates the offline route actors only when Ember Valley is loaded by an active NGO
    /// session. Ordinary offline scene loading leaves every authored object untouched.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    [DisallowMultipleComponent]
    public sealed class NetworkEmberValleyModeAdapter : MonoBehaviour
    {
        [SerializeField] private GameObject[] _offlineActorRoots = Array.Empty<GameObject>();
        [SerializeField] private Behaviour[] _offlineBehaviours = Array.Empty<Behaviour>();

        public bool IsConfigured => _offlineActorRoots != null && _offlineActorRoots.Length > 0 &&
                                    _offlineBehaviours != null && _offlineBehaviours.Length > 0;
        public bool NetworkModeActive { get; private set; }
        public bool ContainsOfflineBehaviour<T>() where T : Behaviour
        {
            if (_offlineBehaviours == null) return false;
            for (int i = 0; i < _offlineBehaviours.Length; i++)
            {
                if (_offlineBehaviours[i] is T) return true;
            }

            return false;
        }

        public void Configure(GameObject[] offlineActorRoots, Behaviour[] offlineBehaviours)
        {
            _offlineActorRoots = offlineActorRoots ?? Array.Empty<GameObject>();
            _offlineBehaviours = offlineBehaviours ?? Array.Empty<Behaviour>();
        }

        private void Awake()
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return;

            NetworkModeActive = true;
            for (int i = 0; i < _offlineBehaviours.Length; i++)
            {
                Behaviour behaviour = _offlineBehaviours[i];
                if (behaviour != null && behaviour != this) behaviour.enabled = false;
            }

            for (int i = 0; i < _offlineActorRoots.Length; i++)
            {
                GameObject actorRoot = _offlineActorRoots[i];
                if (actorRoot != null && actorRoot != gameObject) actorRoot.SetActive(false);
            }

            Debug.Log(
                $"[M5_EMBER_VALLEY_NETWORK_MODE] offlineRoots={_offlineActorRoots.Length} " +
                $"offlineBehaviours={_offlineBehaviours.Length}");
        }
    }
}
