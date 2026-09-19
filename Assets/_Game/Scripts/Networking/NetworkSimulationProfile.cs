using System;

namespace Emberfall.Networking
{
    /// <summary>Validated command-line profile for UTP's pre-connect development simulator.</summary>
    public readonly struct NetworkSimulationProfile : IEquatable<NetworkSimulationProfile>
    {
        public const int MaximumDelayMilliseconds = 1000;
        public const int MaximumJitterMilliseconds = 250;
        public const int MaximumPacketLossPercent = 25;

        private NetworkSimulationProfile(int delayMilliseconds, int jitterMilliseconds, int packetLossPercent)
        {
            DelayMilliseconds = delayMilliseconds;
            JitterMilliseconds = jitterMilliseconds;
            PacketLossPercent = packetLossPercent;
        }

        public int DelayMilliseconds { get; }
        public int JitterMilliseconds { get; }
        public int PacketLossPercent { get; }
        public bool IsEnabled => DelayMilliseconds > 0 || JitterMilliseconds > 0 || PacketLossPercent > 0;

        public static bool TryCreate(
            int delayMilliseconds,
            int jitterMilliseconds,
            int packetLossPercent,
            out NetworkSimulationProfile profile,
            out string reason)
        {
            profile = default;
            if (delayMilliseconds < 0 || delayMilliseconds > MaximumDelayMilliseconds)
            {
                reason = $"delay must be 0-{MaximumDelayMilliseconds} ms";
                return false;
            }
            if (jitterMilliseconds < 0 || jitterMilliseconds > MaximumJitterMilliseconds)
            {
                reason = $"jitter must be 0-{MaximumJitterMilliseconds} ms";
                return false;
            }
            if (packetLossPercent < 0 || packetLossPercent > MaximumPacketLossPercent)
            {
                reason = $"packet loss must be 0-{MaximumPacketLossPercent} percent";
                return false;
            }

            profile = new NetworkSimulationProfile(delayMilliseconds, jitterMilliseconds, packetLossPercent);
            reason = string.Empty;
            return true;
        }

        public bool Equals(NetworkSimulationProfile other) =>
            DelayMilliseconds == other.DelayMilliseconds &&
            JitterMilliseconds == other.JitterMilliseconds &&
            PacketLossPercent == other.PacketLossPercent;

        public override bool Equals(object obj) => obj is NetworkSimulationProfile other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(DelayMilliseconds, JitterMilliseconds, PacketLossPercent);
        public override string ToString() =>
            $"delay={DelayMilliseconds};jitter={JitterMilliseconds};loss={PacketLossPercent}";
    }
}
