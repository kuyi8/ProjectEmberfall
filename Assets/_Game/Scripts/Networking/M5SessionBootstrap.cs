using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Emberfall.Networking
{
    internal static class M5SessionBootstrap
    {
        private const string RuntimeObjectName = "Emberfall_DirectSessionRuntime";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
            if (Object.FindObjectOfType<DirectSessionService>() != null) return;

            var root = new GameObject(RuntimeObjectName);
            root.SetActive(false);
            NetworkManager manager = root.AddComponent<NetworkManager>();
            UnityTransport transport = root.AddComponent<UnityTransport>();
            manager.NetworkConfig = new NetworkConfig
            {
                NetworkTransport = transport,
                ConnectionApproval = true,
                PlayerPrefab = null,
                EnableSceneManagement = true
            };
            GameObject networkPlayer = Resources.Load<GameObject>("Networking/P_M5_NetworkGymPlayer");
            if (networkPlayer != null)
                manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = networkPlayer });
            else
                Debug.LogWarning("[M5_NETWORK_GYM] Network player prefab is not available; run the M5 setup.");
            GameObject networkEnemy = Resources.Load<GameObject>("Networking/P_M5_NetworkGymEnemy");
            if (networkEnemy != null)
                manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = networkEnemy });
            else
                Debug.LogWarning("[M5_NETWORK_GYM] Network enemy prefab is not available; run the M5 setup.");
            RegisterOptionalPrefab(manager, "Networking/P_M5_NetworkRunePriest");
            RegisterOptionalPrefab(manager, "Networking/P_M5_NetworkRuinGuard");
            RegisterOptionalPrefab(manager, "Networking/P_M5_NetworkWarden");
            GameObject networkWorld = Resources.Load<GameObject>("Networking/P_M5_NetworkGymWorldObjective");
            if (networkWorld != null)
                manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = networkWorld });
            else
                Debug.LogWarning("[M5_NETWORK_GYM] Network world prefab is not available; run the M5 setup.");
            root.AddComponent<DirectSessionService>();
            root.AddComponent<SessionCommandLineDriver>();
            Object.DontDestroyOnLoad(root);
            root.SetActive(true);
        }

        private static void RegisterOptionalPrefab(NetworkManager manager, string resourcePath)
        {
            GameObject prefab = Resources.Load<GameObject>(resourcePath);
            if (prefab != null)
                manager.NetworkConfig.Prefabs.Add(new NetworkPrefab { Prefab = prefab });
            else
                Debug.LogWarning($"[M5_NETWORK_GYM] Optional network prefab is unavailable: {resourcePath}");
        }
    }
}
