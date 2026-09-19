using UnityEngine;

namespace Emberfall.Gameplay.Combat.Unity
{
    /// <summary>Unity spatial adapter for the domain's front-arc defense fact.</summary>
    public static class DefenseArcUtility
    {
        public static bool IsThreatInFrontArc(
            Transform defender,
            Vector3 threatPosition,
            float fullArcDegrees = 150f)
        {
            if (defender == null) return false;
            Vector3 toThreat = Vector3.ProjectOnPlane(threatPosition - defender.position, Vector3.up);
            return toThreat.sqrMagnitude > 0.001f &&
                   Vector3.Angle(defender.forward, toThreat) <= fullArcDegrees * 0.5f;
        }
    }
}
