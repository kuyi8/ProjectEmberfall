using System;
using Emberfall.Gameplay.Combat.Domain;

namespace Emberfall.Networking
{
    /// <summary>Pure server-side planar range and facing check for authoritative melee hits.</summary>
    public static class NetworkCombatSpatialValidator
    {
        /// <summary>
        /// Uses the same authored player melee sector as offline combat. The server supplies
        /// all positions and facing; clients never submit a target or hit collection.
        /// </summary>
        public static bool IsValidPlayerMeleeHit(
            float sourceX,
            float sourceZ,
            float forwardX,
            float forwardZ,
            float targetX,
            float targetZ) =>
            MeleeSectorRules.Contains(
                sourceX,
                sourceZ,
                forwardX,
                forwardZ,
                targetX,
                targetZ,
                MeleeSectorRules.DefaultRadius,
                MeleeSectorRules.DefaultFullAngleDegrees);

        public static bool IsValidMeleeHit(
            float sourceX,
            float sourceZ,
            float forwardX,
            float forwardZ,
            float targetX,
            float targetZ,
            float maximumRange,
            float minimumFacingDot)
        {
            if (!IsFinite(sourceX) || !IsFinite(sourceZ) ||
                !IsFinite(forwardX) || !IsFinite(forwardZ) ||
                !IsFinite(targetX) || !IsFinite(targetZ) ||
                !IsFinite(maximumRange) || maximumRange <= 0f ||
                !IsFinite(minimumFacingDot) || minimumFacingDot < -1f || minimumFacingDot > 1f)
            {
                return false;
            }

            float offsetX = targetX - sourceX;
            float offsetZ = targetZ - sourceZ;
            float distanceSquared = (offsetX * offsetX) + (offsetZ * offsetZ);
            if (distanceSquared > maximumRange * maximumRange) return false;
            if (distanceSquared <= 0.0001f) return true;

            float forwardLength = (float)Math.Sqrt((forwardX * forwardX) + (forwardZ * forwardZ));
            if (forwardLength <= 0.0001f) return false;
            float offsetLength = (float)Math.Sqrt(distanceSquared);
            float dot = ((forwardX / forwardLength) * (offsetX / offsetLength)) +
                        ((forwardZ / forwardLength) * (offsetZ / offsetLength));
            return dot >= minimumFacingDot;
        }

        public static bool IsThreatInFrontArc(
            float defenderX,
            float defenderZ,
            float defenderForwardX,
            float defenderForwardZ,
            float threatX,
            float threatZ,
            float minimumFacingDot = 0f)
        {
            if (!IsFinite(defenderX) || !IsFinite(defenderZ) ||
                !IsFinite(defenderForwardX) || !IsFinite(defenderForwardZ) ||
                !IsFinite(threatX) || !IsFinite(threatZ) ||
                !IsFinite(minimumFacingDot) || minimumFacingDot < -1f || minimumFacingDot > 1f)
            {
                return false;
            }

            float offsetX = threatX - defenderX;
            float offsetZ = threatZ - defenderZ;
            float offsetLengthSquared = (offsetX * offsetX) + (offsetZ * offsetZ);
            if (offsetLengthSquared <= 0.0001f) return true;

            float forwardLengthSquared =
                (defenderForwardX * defenderForwardX) + (defenderForwardZ * defenderForwardZ);
            if (forwardLengthSquared <= 0.0001f) return false;

            float inverseForward = 1f / (float)Math.Sqrt(forwardLengthSquared);
            float inverseOffset = 1f / (float)Math.Sqrt(offsetLengthSquared);
            float dot = (defenderForwardX * inverseForward * offsetX * inverseOffset) +
                        (defenderForwardZ * inverseForward * offsetZ * inverseOffset);
            return dot >= minimumFacingDot;
        }

        public static bool IsAimWithinFacingArc(
            float forwardX,
            float forwardZ,
            float aimX,
            float aimZ,
            float minimumFacingDot)
        {
            if (!IsFinite(forwardX) || !IsFinite(forwardZ) ||
                !IsFinite(aimX) || !IsFinite(aimZ) ||
                !IsFinite(minimumFacingDot) || minimumFacingDot < -1f || minimumFacingDot > 1f)
            {
                return false;
            }

            float forwardLengthSquared = (forwardX * forwardX) + (forwardZ * forwardZ);
            float aimLengthSquared = (aimX * aimX) + (aimZ * aimZ);
            if (forwardLengthSquared <= 0.0001f || aimLengthSquared <= 0.0001f) return false;

            float inverseForward = 1f / (float)Math.Sqrt(forwardLengthSquared);
            float inverseAim = 1f / (float)Math.Sqrt(aimLengthSquared);
            float dot = (forwardX * inverseForward * aimX * inverseAim) +
                        (forwardZ * inverseForward * aimZ * inverseAim);
            return dot >= minimumFacingDot;
        }

        /// <summary>
        /// Tests whether a planar target point intersects a finite projectile sweep.
        /// Physics occlusion is deliberately supplied by the Unity server adapter.
        /// </summary>
        public static bool TryEvaluateProjectileSweep(
            float sourceX,
            float sourceZ,
            float directionX,
            float directionZ,
            float targetX,
            float targetZ,
            float maximumDistance,
            float hitRadius,
            out float distanceAlongSweep)
        {
            distanceAlongSweep = 0f;
            if (!IsFinite(sourceX) || !IsFinite(sourceZ) ||
                !IsFinite(directionX) || !IsFinite(directionZ) ||
                !IsFinite(targetX) || !IsFinite(targetZ) ||
                !IsFinite(maximumDistance) || maximumDistance <= 0f ||
                !IsFinite(hitRadius) || hitRadius <= 0f)
            {
                return false;
            }

            float directionLengthSquared =
                (directionX * directionX) + (directionZ * directionZ);
            if (directionLengthSquared <= 0.0001f) return false;

            float inverseDirection = 1f / (float)Math.Sqrt(directionLengthSquared);
            float normalizedX = directionX * inverseDirection;
            float normalizedZ = directionZ * inverseDirection;
            float offsetX = targetX - sourceX;
            float offsetZ = targetZ - sourceZ;
            float projection = (offsetX * normalizedX) + (offsetZ * normalizedZ);
            if (projection < 0f || projection > maximumDistance) return false;

            float closestX = sourceX + (normalizedX * projection);
            float closestZ = sourceZ + (normalizedZ * projection);
            float perpendicularX = targetX - closestX;
            float perpendicularZ = targetZ - closestZ;
            if ((perpendicularX * perpendicularX) + (perpendicularZ * perpendicularZ) >
                hitRadius * hitRadius)
            {
                return false;
            }

            distanceAlongSweep = projection;
            return true;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
