using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    public enum CombatImpactStyle
    {
        Steel,
        Guard,
        Ember
    }

    public readonly struct CombatImpactPresentationEvent
    {
        public CombatImpactPresentationEvent(Vector3 position, CombatImpactStyle style)
        {
            Position = position;
            Style = style;
        }

        public Vector3 Position { get; }
        public CombatImpactStyle Style { get; }
    }

}
