using System;
using System.Collections;
using System.Globalization;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Emberfall.Networking
{
    /// <summary>Opt-in executable harness for repeatable two-instance Direct/LAN smoke tests.</summary>
    [DisallowMultipleComponent]
    public sealed class SessionCommandLineDriver : MonoBehaviour
    {
        private const string RolePrefix = "-emberfall-network-role=";
        private const string AddressPrefix = "-emberfall-network-address=";
        private const string PortPrefix = "-emberfall-network-port=";
        private const string ExitPrefix = "-emberfall-network-exit-after=";
        private const string ExitOnResultPrefix = "-emberfall-network-exit-on-result=";
        private const string EnterGymPrefix = "-emberfall-network-enter-gym=";
        private const string EnterMainWorldPrefix = "-emberfall-network-enter-main-world=";
        private const string DelayPrefix = "-emberfall-network-delay-ms=";
        private const string JitterPrefix = "-emberfall-network-jitter-ms=";
        private const string PacketLossPrefix = "-emberfall-network-loss-percent=";

        private IEnumerator Start()
        {
            yield return null;

            string role = ReadArgument(RolePrefix);
            if (string.IsNullOrWhiteSpace(role)) yield break;

            string address = ReadArgument(AddressPrefix) ?? "127.0.0.1";
            ushort port = 7777;
            string portText = ReadArgument(PortPrefix);
            if (!string.IsNullOrWhiteSpace(portText) && !ushort.TryParse(portText, out port))
            {
                Debug.LogError("[M5_COMMAND_LINE] invalid network port");
                yield break;
            }

            if (!TryReadInteger(DelayPrefix, out int delayMilliseconds, out string simulationReason) ||
                !TryReadInteger(JitterPrefix, out int jitterMilliseconds, out simulationReason) ||
                !TryReadInteger(PacketLossPrefix, out int packetLossPercent, out simulationReason) ||
                !NetworkSimulationProfile.TryCreate(
                    delayMilliseconds,
                    jitterMilliseconds,
                    packetLossPercent,
                    out NetworkSimulationProfile simulation,
                    out simulationReason))
            {
                Debug.LogError($"[M5_NETWORK_CONDITION_REJECTED] reason={simulationReason}");
                yield break;
            }

            if (!TryApplyNetworkSimulation(simulation, out simulationReason))
            {
                Debug.LogError($"[M5_NETWORK_CONDITION_REJECTED] reason={simulationReason}");
                yield break;
            }
            Debug.Log(
                $"[M5_NETWORK_CONDITION] role={role} {simulation} development={Debug.isDebugBuild}");

            ISessionService session = SessionRuntime.Current;
            bool started;
            if (string.Equals(role, "host", StringComparison.OrdinalIgnoreCase))
                started = session.StartHost(port);
            else if (string.Equals(role, "client", StringComparison.OrdinalIgnoreCase))
                started = session.StartClient(address, port);
            else
            {
                Debug.LogError($"[M5_COMMAND_LINE] unsupported role '{role}'");
                yield break;
            }

            Debug.Log($"[M5_COMMAND_LINE] role={role} started={started} compatibility={session.Compatibility}");

            bool enterGym = ReadFlag(EnterGymPrefix);
            bool enterMainWorld = ReadFlag(EnterMainWorldPrefix);
            if (enterGym && enterMainWorld)
            {
                Debug.LogError("[M5_COMMAND_LINE] choose only one network destination");
                started = false;
            }

            if (started && string.Equals(role, "host", StringComparison.OrdinalIgnoreCase) &&
                (enterGym || enterMainWorld))
            {
                float timeoutAt = Time.realtimeSinceStartup + 20f;
                while (session.Snapshot.ConnectedPlayers < 2 && Time.realtimeSinceStartup < timeoutAt)
                    yield return null;

                bool sceneStarted = session.Snapshot.ConnectedPlayers == 2 &&
                                    (enterMainWorld
                                        ? session.TryStartNetworkEmberValley()
                                        : session.TryStartNetworkGym());
                Debug.Log($"[M5_COMMAND_LINE] network-scene-started={sceneStarted} " +
                          $"destination={(enterMainWorld ? "ember-valley" : "network-gym")}");
                started &= sceneStarted;
            }

            if (started && ReadFlag(ExitOnResultPrefix))
            {
                float resultTimeoutAt = Time.realtimeSinceStartup + 90f;
                NetworkGymWorldObjective resultSource = null;
                while (Time.realtimeSinceStartup < resultTimeoutAt)
                {
                    resultSource = NetworkGymSceneController.Find()?.WorldObjective;
                    if (resultSource?.ResultPublished == true) break;
                    yield return null;
                }

                if (resultSource?.ResultPublished != true)
                {
                    Debug.LogError("[M5_COMMAND_LINE] exit-on-result timed out");
                    session.Shutdown();
                    yield return null;
                    Application.Quit(2);
                    yield break;
                }

                yield return new WaitForSecondsRealtime(1.25f);
                Debug.Log(
                    $"[M5_NETWORK_CONDITION_RESULT] role={role} {simulation} rttMs={ReadCurrentRtt(role)}");
                Debug.Log("[M5_COMMAND_LINE] exit-on-result=true");
                session.Shutdown();
                yield return null;
                Application.Quit(0);
                yield break;
            }

            string exitText = ReadArgument(ExitPrefix);
            if (float.TryParse(exitText, NumberStyles.Float, CultureInfo.InvariantCulture, out float seconds) && seconds > 0f)
            {
                yield return new WaitForSecondsRealtime(seconds);
                Debug.Log(
                    $"[M5_NETWORK_CONDITION_RESULT] role={role} {simulation} rttMs={ReadCurrentRtt(role)}");
                Debug.Log($"[M5_COMMAND_LINE] exit-after={seconds.ToString(CultureInfo.InvariantCulture)}");
                session.Shutdown();
                yield return null;
                Application.Quit(started ? 0 : 2);
            }
        }

        private static string ReadArgument(string prefix)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return argument.Substring(prefix.Length).Trim();
            }
            return null;
        }

        private static bool ReadFlag(string prefix)
        {
            string value = ReadArgument(prefix);
            return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
                       string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryReadInteger(string prefix, out int value, out string reason)
        {
            value = 0;
            string text = ReadArgument(prefix);
            if (string.IsNullOrWhiteSpace(text))
            {
                reason = string.Empty;
                return true;
            }
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                reason = string.Empty;
                return true;
            }

            reason = $"{prefix} requires an integer";
            return false;
        }

        private static bool TryApplyNetworkSimulation(NetworkSimulationProfile profile, out string reason)
        {
            if (!profile.IsEnabled)
            {
                reason = string.Empty;
                return true;
            }
            if (!Debug.isDebugBuild)
            {
                reason = "artificial conditions require a Development Build";
                return false;
            }

            UnityTransport transport = NetworkManager.Singleton != null
                ? NetworkManager.Singleton.GetComponent<UnityTransport>()
                : FindObjectOfType<UnityTransport>();
            if (transport == null)
            {
                reason = "UnityTransport is unavailable before connect";
                return false;
            }

            transport.SetDebugSimulatorParameters(
                profile.DelayMilliseconds,
                profile.JitterMilliseconds,
                profile.PacketLossPercent);
            reason = string.Empty;
            return true;
        }

        private static long ReadCurrentRtt(string role)
        {
            NetworkManager manager = NetworkManager.Singleton;
            if (manager == null || !manager.IsListening) return -1;
            UnityTransport transport = manager.GetComponent<UnityTransport>();
            if (transport == null) return -1;

            ulong peerId = NetworkManager.ServerClientId;
            if (string.Equals(role, "host", StringComparison.OrdinalIgnoreCase))
            {
                peerId = ulong.MaxValue;
                foreach (ulong clientId in manager.ConnectedClientsIds)
                {
                    if (clientId == NetworkManager.ServerClientId) continue;
                    peerId = clientId;
                    break;
                }
                if (peerId == ulong.MaxValue) return -1;
            }

            return (long)transport.GetCurrentRtt(peerId);
        }
    }
}
