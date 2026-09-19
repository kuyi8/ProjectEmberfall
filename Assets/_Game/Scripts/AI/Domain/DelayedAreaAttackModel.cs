using System;

namespace Emberfall.AI.Domain
{
    /// <summary>Deterministic fuse for a delayed area attack. Physics resolution stays in the authority adapter.</summary>
    public sealed class DelayedAreaAttackModel
    {
        private readonly float _triggerDelay;

        public DelayedAreaAttackModel(float triggerDelay)
        {
            if (triggerDelay <= 0f || float.IsNaN(triggerDelay) || float.IsInfinity(triggerDelay))
            {
                throw new ArgumentOutOfRangeException(nameof(triggerDelay));
            }

            _triggerDelay = triggerDelay;
        }

        public float Elapsed { get; private set; }
        public float Normalized => Math.Min(1f, Elapsed / _triggerDelay);
        public bool IsReady => !IsResolved && Elapsed >= _triggerDelay;
        public bool IsResolved { get; private set; }

        public void Tick(float deltaTime)
        {
            if (deltaTime < 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (!IsResolved)
            {
                Elapsed += deltaTime;
            }
        }

        public bool Resolve()
        {
            if (!IsReady)
            {
                return false;
            }

            IsResolved = true;
            return true;
        }
    }
}
