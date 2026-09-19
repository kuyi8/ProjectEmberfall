using System;

namespace Emberfall.Gameplay.Combat.Domain
{
    public enum TacticalPostureBurstState
    {
        Ready,
        Telegraph,
        Spent
    }

    /// <summary>One-use-per-attempt neutral posture burst with an authoritative warning fuse.</summary>
    public sealed class TacticalPostureBurstModel
    {
        private readonly float _telegraphDuration;

        public TacticalPostureBurstModel(float telegraphDuration)
        {
            if (telegraphDuration <= 0f || float.IsNaN(telegraphDuration) || float.IsInfinity(telegraphDuration))
            {
                throw new ArgumentOutOfRangeException(nameof(telegraphDuration));
            }
            _telegraphDuration = telegraphDuration;
        }

        public TacticalPostureBurstState State { get; private set; } = TacticalPostureBurstState.Ready;
        public float Elapsed { get; private set; }
        public float Normalized => State == TacticalPostureBurstState.Ready
            ? 0f
            : Math.Min(1f, Elapsed / _telegraphDuration);
        public bool IsReady => State == TacticalPostureBurstState.Ready;
        public bool IsTelegraphing => State == TacticalPostureBurstState.Telegraph;
        public bool IsSpent => State == TacticalPostureBurstState.Spent;

        public bool TryArm()
        {
            if (!IsReady) return false;
            State = TacticalPostureBurstState.Telegraph;
            Elapsed = 0f;
            return true;
        }

        public bool Tick(float deltaTime)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }
            if (!IsTelegraphing) return false;
            Elapsed += deltaTime;
            if (Elapsed < _telegraphDuration) return false;
            State = TacticalPostureBurstState.Spent;
            return true;
        }

        public void Reset()
        {
            State = TacticalPostureBurstState.Ready;
            Elapsed = 0f;
        }
    }
}
