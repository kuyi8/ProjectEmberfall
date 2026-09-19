using System;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Emberfall.Networking
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NetworkManager), typeof(UnityTransport))]
    public sealed class DirectSessionService : MonoBehaviour, ISessionService
    {
        private const int MaxPlayers = 2;
        private NetworkManager _networkManager;
        private UnityTransport _transport;
        private SessionSnapshot _snapshot;
        private SessionCompatibility _compatibility;
        private bool _callbacksRegistered;
        private bool _isShuttingDown;
        private bool _offlineRequested;
        private bool _gameStarted;

        public SessionSnapshot Snapshot => _snapshot;
        public SessionCompatibility Compatibility => _compatibility;
        public event Action<SessionSnapshot> Changed;

        private void Awake()
        {
            _networkManager = GetComponent<NetworkManager>();
            _transport = GetComponent<UnityTransport>();
            _networkManager.NetworkConfig ??= new NetworkConfig();
            _compatibility = SessionRuntime.CreateCurrentCompatibility();
            _snapshot = OfflineSnapshot("单人离线模式已就绪。联机服务尚未启动。");

            _networkManager.NetworkConfig.NetworkTransport = _transport;
            _networkManager.NetworkConfig.ConnectionApproval = true;
            _networkManager.NetworkConfig.PlayerPrefab = null;
            RegisterCallbacks();
            SessionRuntime.Install(this);
        }

        public bool StartOffline()
        {
            _isShuttingDown = true;
            _offlineRequested = true;
            _gameStarted = false;
            StopNetwork();
            SetSnapshot(OfflineSnapshot("已切换到单人离线模式。"));
            _isShuttingDown = false;
            return true;
        }

        public bool StartHost(ushort port)
        {
            if (!IsValidPort(port)) return Fail(SessionMode.Host, string.Empty, port, "端口必须在 1024–65535 之间。");

            StopNetwork();
            _offlineRequested = false;
            _gameStarted = false;
            RefreshCompatibility();
            _transport.SetConnectionData("127.0.0.1", port, "0.0.0.0");
            PrepareNetworkConfig();
            SetSnapshot(new SessionSnapshot(
                SessionMode.Host,
                SessionConnectionState.Starting,
                "0.0.0.0",
                port,
                0,
                "正在创建 Direct/LAN 主机……"));

            if (!_networkManager.StartHost())
                return Fail(SessionMode.Host, "0.0.0.0", port, "主机启动失败，请检查端口是否被占用。");

            SetSnapshot(new SessionSnapshot(
                SessionMode.Host,
                SessionConnectionState.Listening,
                "0.0.0.0",
                port,
                CurrentPlayerCount(),
                $"主机正在监听端口 {port}；等待第 2 名玩家加入。"));
            return true;
        }

        public bool StartClient(string address, ushort port)
        {
            address = address?.Trim();
            if (string.IsNullOrWhiteSpace(address))
                return Fail(SessionMode.Client, string.Empty, port, "请输入主机 IP 地址。");
            if (!IsValidPort(port))
                return Fail(SessionMode.Client, address, port, "端口必须在 1024–65535 之间。");

            StopNetwork();
            _offlineRequested = false;
            _gameStarted = false;
            RefreshCompatibility();
            _transport.SetConnectionData(address, port);
            PrepareNetworkConfig();
            SetSnapshot(new SessionSnapshot(
                SessionMode.Client,
                SessionConnectionState.Starting,
                address,
                port,
                0,
                $"正在连接 {address}:{port}……"));

            if (!_networkManager.StartClient())
                return Fail(SessionMode.Client, address, port, "客户端启动失败，请检查地址与网络配置。");
            return true;
        }

        public void Shutdown()
        {
            _isShuttingDown = true;
            _offlineRequested = true;
            StopNetwork();
            SetSnapshot(OfflineSnapshot("联机会话已结束。"));
            _isShuttingDown = false;
        }

        public bool TryStartNetworkGym()
        {
            return TryStartNetworkScene(
                "91_NetworkGym",
                "Network Gym",
                "正在同步加载 Network Gym；进入后双方按 E Ready。",
                "[M5_NETWORK_GYM_LOAD_STARTED]");
        }

        public bool TryStartNetworkEmberValley()
        {
            return TryStartNetworkScene(
                "10_EmberValley",
                "共享余烬山谷",
                "正在同步加载余烬山谷森林封印段；进入后双方按 E Ready。",
                "[M5_EMBER_VALLEY_LOAD_STARTED]");
        }

        private bool TryStartNetworkScene(
            string sceneName,
            string displayName,
            string loadingMessage,
            string logMarker)
        {
            if (_networkManager == null || !_networkManager.IsHost)
                return Fail(_snapshot.Mode, _snapshot.Address, _snapshot.Port, $"只有主机可以开始{displayName}。");
            if (_gameStarted)
                return false;
            if (CurrentPlayerCount() != MaxPlayers)
                return Fail(SessionMode.Host, _snapshot.Address, _snapshot.Port, "需要两名玩家均通过握手后才能开始。");

            SceneEventProgressStatus status = _networkManager.SceneManager.LoadScene(sceneName, LoadSceneMode.Single);
            if (status != SceneEventProgressStatus.Started)
                return Fail(SessionMode.Host, _snapshot.Address, _snapshot.Port, $"{displayName}场景同步启动失败：{status}。");

            _gameStarted = true;
            SetSnapshot(new SessionSnapshot(
                SessionMode.Host,
                SessionConnectionState.Connected,
                _snapshot.Address,
                _snapshot.Port,
                CurrentPlayerCount(),
                loadingMessage));
            Debug.Log($"{logMarker} players=2 scene={sceneName}");
            return true;
        }

        private void PrepareNetworkConfig()
        {
            _networkManager.NetworkConfig.ConnectionApproval = true;
            _networkManager.NetworkConfig.ConnectionData = SessionCompatibilityCodec.Encode(_compatibility);
        }

        private void RefreshCompatibility() => _compatibility = SessionRuntime.CreateCurrentCompatibility();

        private void RegisterCallbacks()
        {
            if (_callbacksRegistered) return;
            _networkManager.ConnectionApprovalCallback += ApproveConnection;
            _networkManager.OnClientConnectedCallback += HandleClientConnected;
            _networkManager.OnClientDisconnectCallback += HandleClientDisconnected;
            _callbacksRegistered = true;
        }

        private void UnregisterCallbacks()
        {
            if (!_callbacksRegistered || _networkManager == null) return;
            _networkManager.ConnectionApprovalCallback -= ApproveConnection;
            _networkManager.OnClientConnectedCallback -= HandleClientConnected;
            _networkManager.OnClientDisconnectCallback -= HandleClientDisconnected;
            _callbacksRegistered = false;
        }

        private void ApproveConnection(
            NetworkManager.ConnectionApprovalRequest request,
            NetworkManager.ConnectionApprovalResponse response)
        {
            response.CreatePlayerObject = false;
            response.Pending = false;

            if (_gameStarted)
            {
                response.Approved = false;
                response.Reason = "本局已经开始，不支持中途加入。";
                Debug.LogWarning("[M5_HANDSHAKE_REJECTED] game-already-started");
                return;
            }

            if (_networkManager.ConnectedClientsIds.Count >= MaxPlayers)
            {
                response.Approved = false;
                response.Reason = "房间已满（最多 2 人）。";
                Debug.LogWarning("[M5_HANDSHAKE_REJECTED] room-full");
                return;
            }

            if (!SessionCompatibilityCodec.TryDecode(request.Payload, out SessionCompatibility candidate, out string reason) ||
                !SessionCompatibilityCodec.IsCompatible(_compatibility, candidate, out reason))
            {
                response.Approved = false;
                response.Reason = reason;
                Debug.LogWarning($"[M5_HANDSHAKE_REJECTED] {reason}");
                return;
            }

            response.Approved = true;
            response.Reason = string.Empty;
            Debug.Log($"[M5_HANDSHAKE_ACCEPTED] clientId={request.ClientNetworkId} compatibility={candidate}");
        }

        private void HandleClientConnected(ulong clientId)
        {
            if (_networkManager.IsHost)
            {
                int count = CurrentPlayerCount();
                string message = count >= MaxPlayers
                    ? "两名玩家均已通过版本握手；主机可开始 Network Gym。"
                    : "主机已就绪；等待第 2 名玩家加入。";
                SetSnapshot(new SessionSnapshot(
                    SessionMode.Host,
                    SessionConnectionState.Listening,
                    _snapshot.Address,
                    _snapshot.Port,
                    count,
                    message));
                return;
            }

            if (_networkManager.IsClient && clientId == _networkManager.LocalClientId)
            {
                SetSnapshot(new SessionSnapshot(
                    SessionMode.Client,
                    SessionConnectionState.Connected,
                    _snapshot.Address,
                    _snapshot.Port,
                    2,
                    "已通过 client/schema/contentVersion 握手并连接主机。"));
            }
        }

        private void HandleClientDisconnected(ulong clientId)
        {
            if (_isShuttingDown || _offlineRequested) return;

            if (_networkManager != null && _networkManager.IsServer)
            {
                if (clientId != NetworkManager.ServerClientId)
                {
                    SetSnapshot(new SessionSnapshot(
                        SessionMode.Host,
                        _gameStarted ? SessionConnectionState.Connected : SessionConnectionState.Listening,
                        _snapshot.Address,
                        _snapshot.Port,
                        CurrentPlayerCount(),
                        _gameStarted
                            ? "客户端已离开；主机可继续 Network Gym，本局不再接受中途加入。"
                            : "客户端已离开；主机仍可继续等待连接。"));
                }
                return;
            }

            if (_snapshot.Mode == SessionMode.Client)
            {
                string reason = string.IsNullOrWhiteSpace(_networkManager?.DisconnectReason)
                    ? "连接已断开或主机不可达。"
                    : _networkManager.DisconnectReason;
                SetSnapshot(new SessionSnapshot(
                    SessionMode.Client,
                    SessionConnectionState.Failed,
                    _snapshot.Address,
                    _snapshot.Port,
                    0,
                    reason));
            }
        }

        private int CurrentPlayerCount() =>
            _networkManager != null && _networkManager.IsServer
                ? _networkManager.ConnectedClientsIds.Count
                : (_networkManager != null && _networkManager.IsConnectedClient ? 1 : 0);

        private bool Fail(SessionMode mode, string address, ushort port, string message)
        {
            SetSnapshot(new SessionSnapshot(mode, SessionConnectionState.Failed, address, port, 0, message));
            return false;
        }

        private void StopNetwork()
        {
            if (_networkManager != null && _networkManager.IsListening) _networkManager.Shutdown();
        }

        private static bool IsValidPort(ushort port) => port >= 1024;

        private static SessionSnapshot OfflineSnapshot(string message) =>
            new SessionSnapshot(SessionMode.Offline, SessionConnectionState.Offline, string.Empty, 0, 1, message);

        private void SetSnapshot(SessionSnapshot value)
        {
            _snapshot = value;
            Debug.Log(
                $"[M5_SESSION_STATE] mode={value.Mode} state={value.State} " +
                $"players={value.ConnectedPlayers}/{MaxPlayers} endpoint={value.Address}:{value.Port} message={value.Message}");
            Changed?.Invoke(value);
        }

        private void OnDestroy()
        {
            _isShuttingDown = true;
            UnregisterCallbacks();
            StopNetwork();
            SessionRuntime.Uninstall(this);
        }

        private void OnApplicationQuit() => _isShuttingDown = true;
    }
}
