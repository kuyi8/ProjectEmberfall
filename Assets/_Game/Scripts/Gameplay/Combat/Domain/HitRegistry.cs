using System.Collections.Generic;

namespace Emberfall.Gameplay.Combat.Domain
{
    /// <summary>Prevents a physics query from damaging one target more than once per attack sequence.</summary>
    public sealed class HitRegistry
    {
        private readonly HashSet<int> _targetIds = new HashSet<int>();

        public int AttackSequence { get; private set; } = -1;

        public bool TryRegister(int attackSequence, int targetId)
        {
            if (attackSequence != AttackSequence)
            {
                AttackSequence = attackSequence;
                _targetIds.Clear();
            }

            return _targetIds.Add(targetId);
        }

        public void Clear()
        {
            AttackSequence = -1;
            _targetIds.Clear();
        }
    }
}
