using Emberfall.Gameplay.Combat.Unity;
using UnityEngine;

namespace Emberfall.AI.Unity
{
    /// <summary>Perception only. All non-player CombatTarget bodies are transparent,
    /// including other encounters and summons. Projectile collision policy is separate.</summary>
    internal static class EnemyLineOfSight
    {
        public static bool HasContact(CombatTarget observer, PlayerCombatActor target, RaycastHit[] hits)
        {
            return HasContactFrom(observer.AimPoint.position, target, hits);
        }

        public static bool HasContactFrom(Vector3 origin, PlayerCombatActor target, RaycastHit[] hits)
        {
            Vector3 direction = target.AimPoint.position - origin;
            float distance = direction.magnitude;
            if (distance <= Mathf.Epsilon) return true;
            int count = Physics.RaycastNonAlloc(origin, direction / distance, hits, distance, ~0,
                QueryTriggerInteraction.Ignore);
            // A full unordered buffer may omit a wall. Refuse rather than guess through it.
            if (count == hits.Length) return false;
            float nearest = float.PositiveInfinity;
            Collider blocker = null;
            for (int i = 0; i < count; i++)
            {
                var combatant = hits[i].collider.GetComponentInParent<CombatTarget>();
                if (combatant != null && !(combatant is PlayerCombatActor)) continue;
                if (hits[i].distance >= nearest) continue;
                nearest = hits[i].distance;
                blocker = hits[i].collider;
            }
            return blocker == null || blocker.GetComponentInParent<PlayerCombatActor>() == target;
        }
    }
}
