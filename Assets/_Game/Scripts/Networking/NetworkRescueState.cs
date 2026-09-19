using System;
using System.Collections.Generic;

namespace Emberfall.Networking
{
    public enum NetworkRescueTickResult
    {
        None = 0,
        Cancelled = 1,
        Completed = 2
    }

    /// <summary>
    /// Pure server-side co-op rescue rules. Unity supplies connection, distance and time facts;
    /// this model owns downed membership, one active rescue and its completion/cancellation.
    /// </summary>
    public sealed class NetworkRescueState
    {
        public const float RequiredSeconds = 3f;
        public const float IntentGraceSeconds = 0.85f;

        private readonly HashSet<ulong> _connected = new HashSet<ulong>();
        private readonly HashSet<ulong> _downed = new HashSet<ulong>();
        private float _intentGraceRemaining;

        public bool IsRescueActive { get; private set; }
        public ulong ActiveRescuerId { get; private set; } = ulong.MaxValue;
        public ulong ActiveTargetId { get; private set; } = ulong.MaxValue;
        public float ProgressSeconds { get; private set; }
        public float ProgressNormalized => Math.Min(1f, ProgressSeconds / RequiredSeconds);
        public int ConnectedCount => _connected.Count;
        public int DownedCount => _downed.Count;
        public bool PartyDefeated => _connected.Count > 0 && _downed.Count == _connected.Count;

        public bool Register(ulong clientId) => _connected.Add(clientId);

        public bool Unregister(ulong clientId)
        {
            bool changed = _connected.Remove(clientId);
            _downed.Remove(clientId);
            if (IsRescueActive && (ActiveRescuerId == clientId || ActiveTargetId == clientId))
                ClearActive();
            return changed;
        }

        public bool IsConnected(ulong clientId) => _connected.Contains(clientId);
        public bool IsDowned(ulong clientId) => _downed.Contains(clientId);

        public bool TryDown(ulong clientId, out string reason)
        {
            if (!_connected.Contains(clientId))
            {
                reason = "player-not-connected";
                return false;
            }
            if (!_downed.Add(clientId))
            {
                reason = "already-downed";
                return false;
            }

            if (IsRescueActive && ActiveRescuerId == clientId) ClearActive();
            reason = string.Empty;
            return true;
        }

        public bool TryStartOrRefresh(ulong rescuerId, ulong targetId, bool inRange, out string reason)
        {
            if (!_connected.Contains(rescuerId) || !_connected.Contains(targetId))
            {
                reason = "player-not-connected";
                return false;
            }
            if (rescuerId == targetId)
            {
                reason = "cannot-rescue-self";
                return false;
            }
            if (_downed.Contains(rescuerId))
            {
                reason = "rescuer-downed";
                return false;
            }
            if (!_downed.Contains(targetId))
            {
                reason = "target-not-downed";
                return false;
            }
            if (!inRange)
            {
                reason = "rescue-out-of-range";
                return false;
            }

            if (!IsRescueActive || ActiveRescuerId != rescuerId || ActiveTargetId != targetId)
            {
                IsRescueActive = true;
                ActiveRescuerId = rescuerId;
                ActiveTargetId = targetId;
                ProgressSeconds = 0f;
            }

            _intentGraceRemaining = IntentGraceSeconds;
            reason = string.Empty;
            return true;
        }

        public bool TryCancel(ulong rescuerId)
        {
            if (!IsRescueActive || ActiveRescuerId != rescuerId) return false;
            ClearActive();
            return true;
        }

        public NetworkRescueTickResult Tick(
            float deltaTime,
            bool pairStillConnected,
            bool pairStillInRange,
            out ulong rescuerId,
            out ulong targetId)
        {
            if (deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            rescuerId = ActiveRescuerId;
            targetId = ActiveTargetId;
            if (!IsRescueActive) return NetworkRescueTickResult.None;

            _intentGraceRemaining -= deltaTime;
            bool valid = pairStillConnected && pairStillInRange && _intentGraceRemaining > 0f &&
                         _connected.Contains(ActiveRescuerId) && _connected.Contains(ActiveTargetId) &&
                         !_downed.Contains(ActiveRescuerId) && _downed.Contains(ActiveTargetId);
            if (!valid)
            {
                ClearActive();
                return NetworkRescueTickResult.Cancelled;
            }

            ProgressSeconds += deltaTime;
            if (ProgressSeconds < RequiredSeconds) return NetworkRescueTickResult.None;

            _downed.Remove(ActiveTargetId);
            ClearActive();
            return NetworkRescueTickResult.Completed;
        }

        public bool TryRecoverSolo(out ulong clientId)
        {
            clientId = ulong.MaxValue;
            if (_connected.Count != 1 || _downed.Count != 1) return false;
            foreach (ulong candidate in _connected)
            {
                if (!_downed.Remove(candidate)) return false;
                clientId = candidate;
                ClearActive();
                return true;
            }
            return false;
        }

        public bool MarkRevived(ulong clientId)
        {
            bool changed = _downed.Remove(clientId);
            if (IsRescueActive && ActiveTargetId == clientId) ClearActive();
            return changed;
        }

        private void ClearActive()
        {
            IsRescueActive = false;
            ActiveRescuerId = ulong.MaxValue;
            ActiveTargetId = ulong.MaxValue;
            ProgressSeconds = 0f;
            _intentGraceRemaining = 0f;
        }
    }
}
