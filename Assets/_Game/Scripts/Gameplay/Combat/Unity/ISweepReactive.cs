using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>Optional offline presentation reaction for ordinary enemies hit by the wide sweep.</summary>
    public interface ISweepReactive
    {
        void ApplySweepImpulse(Vector3 sourcePosition, float distance);
    }
}
