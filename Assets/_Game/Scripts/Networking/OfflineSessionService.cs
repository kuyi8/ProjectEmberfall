using System;

namespace Emberfall.Networking
{
    public sealed class OfflineSessionService : ISessionService
    {
        private SessionSnapshot _snapshot;

        public OfflineSessionService(SessionCompatibility compatibility)
        {
            Compatibility = compatibility;
            _snapshot = CreateOfflineSnapshot("单人离线模式已就绪。");
        }

        public SessionSnapshot Snapshot => _snapshot;
        public SessionCompatibility Compatibility { get; }
        public event Action<SessionSnapshot> Changed;

        public bool StartOffline()
        {
            SetSnapshot(CreateOfflineSnapshot("单人离线模式已就绪。"));
            return true;
        }

        public bool StartHost(ushort port)
        {
            SetSnapshot(CreateOfflineSnapshot("当前仅安装了离线会话服务，无法创建主机。"));
            return false;
        }

        public bool StartClient(string address, ushort port)
        {
            SetSnapshot(CreateOfflineSnapshot("当前仅安装了离线会话服务，无法加入主机。"));
            return false;
        }

        public bool TryStartNetworkGym()
        {
            SetSnapshot(CreateOfflineSnapshot("请先创建或加入双人 Direct/LAN 会话。"));
            return false;
        }

        public bool TryStartNetworkEmberValley()
        {
            SetSnapshot(CreateOfflineSnapshot("请先创建或加入双人 Direct/LAN 会话。"));
            return false;
        }

        public void Shutdown() => StartOffline();

        private static SessionSnapshot CreateOfflineSnapshot(string message) =>
            new SessionSnapshot(SessionMode.Offline, SessionConnectionState.Offline, string.Empty, 0, 1, message);

        private void SetSnapshot(SessionSnapshot value)
        {
            _snapshot = value;
            Changed?.Invoke(value);
        }
    }
}
