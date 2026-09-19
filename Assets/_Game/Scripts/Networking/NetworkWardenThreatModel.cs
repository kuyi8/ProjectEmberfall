using System;
using System.Collections.Generic;

namespace Emberfall.Networking
{
    public readonly struct NetworkWardenTargetCandidate
    {
        public NetworkWardenTargetCandidate(ulong clientId, float distance)
        {
            ClientId = clientId;
            Distance = distance;
        }

        public ulong ClientId { get; }
        public float Distance { get; }
    }

    /// <summary>
    /// Server-only Warden target selection. Recent damage creates urgency, distance keeps the
    /// encounter readable, and a continuous-lock bonus prevents rapid target ping-pong.
    /// </summary>
    public sealed class NetworkWardenThreatModel
    {
        private const double RecentDamageSeconds = 6d;
        private const float DamageWeight = 1.6f;
        private const float DistanceWeight = 2.4f;
        private const float LockBonusPerSecond = 0.35f;
        private const float MaximumLockBonus = 2.1f;
        private const float SwitchHysteresis = 0.75f;

        private readonly Dictionary<ulong, DamageFact> _damage = new Dictionary<ulong, DamageFact>();
        private ulong _currentTarget = ulong.MaxValue;
        private double _lockedSince;

        public ulong CurrentTarget => _currentTarget;

        public void RecordDamage(ulong clientId, float amount, double serverTime)
        {
            if (amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount)) return;
            _damage[clientId] = new DamageFact(amount, serverTime);
        }

        public ulong SelectTarget(
            IReadOnlyList<NetworkWardenTargetCandidate> candidates,
            double serverTime)
        {
            if (candidates == null || candidates.Count == 0)
            {
                _currentTarget = ulong.MaxValue;
                _lockedSince = serverTime;
                return _currentTarget;
            }

            ulong bestId = candidates[0].ClientId;
            float bestScore = Score(candidates[0], serverTime);
            float currentScore = float.MinValue;
            bool currentIsAvailable = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                NetworkWardenTargetCandidate candidate = candidates[i];
                float score = Score(candidate, serverTime);
                if (candidate.ClientId == _currentTarget)
                {
                    currentIsAvailable = true;
                    currentScore = score + Math.Min(
                        MaximumLockBonus,
                        (float)Math.Max(0d, serverTime - _lockedSince) * LockBonusPerSecond);
                }
                if (score > bestScore)
                {
                    bestScore = score;
                    bestId = candidate.ClientId;
                }
            }

            if (currentIsAvailable && bestId != _currentTarget && bestScore < currentScore + SwitchHysteresis)
                return _currentTarget;

            if (bestId != _currentTarget)
            {
                _currentTarget = bestId;
                _lockedSince = serverTime;
            }
            return _currentTarget;
        }

        public void Reset(double serverTime = 0d)
        {
            _damage.Clear();
            _currentTarget = ulong.MaxValue;
            _lockedSince = serverTime;
        }

        private float Score(NetworkWardenTargetCandidate candidate, double serverTime)
        {
            float distanceScore = DistanceWeight / Math.Max(1f, candidate.Distance);
            if (!_damage.TryGetValue(candidate.ClientId, out DamageFact fact) ||
                serverTime - fact.ServerTime > RecentDamageSeconds)
                return distanceScore;
            float recency = 1f - (float)((serverTime - fact.ServerTime) / RecentDamageSeconds);
            return distanceScore + fact.Amount * DamageWeight * Math.Max(0f, recency);
        }

        private readonly struct DamageFact
        {
            public DamageFact(float amount, double serverTime)
            {
                Amount = amount;
                ServerTime = serverTime;
            }

            public float Amount { get; }
            public double ServerTime { get; }
        }
    }
}
