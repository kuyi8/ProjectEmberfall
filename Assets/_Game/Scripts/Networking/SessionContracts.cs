using System;
using Emberfall.Core.Content;

namespace Emberfall.Networking
{
    public enum SessionMode
    {
        Offline = 0,
        Host = 1,
        Client = 2
    }

    public enum SessionConnectionState
    {
        Offline = 0,
        Starting = 1,
        Listening = 2,
        Connected = 3,
        Failed = 4
    }

    public readonly struct SessionCompatibility : IEquatable<SessionCompatibility>
    {
        public const int CurrentProtocolVersion = 1;

        public SessionCompatibility(
            int protocolVersion,
            SemanticVersion clientVersion,
            int contentSchemaVersion,
            SemanticVersion contentVersion)
        {
            if (protocolVersion <= 0) throw new ArgumentOutOfRangeException(nameof(protocolVersion));
            if (contentSchemaVersion <= 0) throw new ArgumentOutOfRangeException(nameof(contentSchemaVersion));

            ProtocolVersion = protocolVersion;
            ClientVersion = clientVersion;
            ContentSchemaVersion = contentSchemaVersion;
            ContentVersion = contentVersion;
        }

        public int ProtocolVersion { get; }
        public SemanticVersion ClientVersion { get; }
        public int ContentSchemaVersion { get; }
        public SemanticVersion ContentVersion { get; }

        public bool Equals(SessionCompatibility other) =>
            ProtocolVersion == other.ProtocolVersion &&
            ClientVersion == other.ClientVersion &&
            ContentSchemaVersion == other.ContentSchemaVersion &&
            ContentVersion == other.ContentVersion;

        public override bool Equals(object obj) => obj is SessionCompatibility other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = ProtocolVersion;
                hash = (hash * 397) ^ ClientVersion.GetHashCode();
                hash = (hash * 397) ^ ContentSchemaVersion;
                return (hash * 397) ^ ContentVersion.GetHashCode();
            }
        }

        public override string ToString() =>
            $"protocol={ProtocolVersion};client={ClientVersion};schema={ContentSchemaVersion};content={ContentVersion}";
    }

    public readonly struct SessionSnapshot
    {
        public SessionSnapshot(
            SessionMode mode,
            SessionConnectionState state,
            string address,
            ushort port,
            int connectedPlayers,
            string message)
        {
            Mode = mode;
            State = state;
            Address = address ?? string.Empty;
            Port = port;
            ConnectedPlayers = connectedPlayers;
            Message = message ?? string.Empty;
        }

        public SessionMode Mode { get; }
        public SessionConnectionState State { get; }
        public string Address { get; }
        public ushort Port { get; }
        public int ConnectedPlayers { get; }
        public string Message { get; }
    }

    public interface ISessionService
    {
        SessionSnapshot Snapshot { get; }
        SessionCompatibility Compatibility { get; }
        event Action<SessionSnapshot> Changed;

        bool StartOffline();
        bool StartHost(ushort port);
        bool StartClient(string address, ushort port);
        bool TryStartNetworkGym();
        bool TryStartNetworkEmberValley();
        void Shutdown();
    }
}
