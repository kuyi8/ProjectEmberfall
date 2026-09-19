using System;

namespace Emberfall.Gameplay.Combat.Domain
{
    public static class MeleeSectorRules
    {
        public const float DefaultRadius = 2.45f;
        public const float DefaultFullAngleDegrees = 170f;

        public static bool Contains(
            float originX,
            float originZ,
            float forwardX,
            float forwardZ,
            float targetX,
            float targetZ,
            float radius,
            float fullAngleDegrees)
        {
            if (!IsFinite(originX) || !IsFinite(originZ) ||
                !IsFinite(forwardX) || !IsFinite(forwardZ) ||
                !IsFinite(targetX) || !IsFinite(targetZ) ||
                !IsFinite(radius) || !IsFinite(fullAngleDegrees) ||
                radius <= 0f || fullAngleDegrees <= 0f || fullAngleDegrees > 360f)
                return false;

            float offsetX = targetX - originX;
            float offsetZ = targetZ - originZ;
            float distanceSquared = (offsetX * offsetX) + (offsetZ * offsetZ);
            if (distanceSquared > radius * radius) return false;
            if (distanceSquared <= 0.000001f) return true;

            float forwardMagnitude = (float)Math.Sqrt((forwardX * forwardX) + (forwardZ * forwardZ));
            if (forwardMagnitude <= 0.0001f) return false;
            float targetMagnitude = (float)Math.Sqrt(distanceSquared);
            float dot = ((forwardX * offsetX) + (forwardZ * offsetZ)) /
                        (forwardMagnitude * targetMagnitude);
            float minimumDot = (float)Math.Cos(fullAngleDegrees * 0.5f * Math.PI / 180d);
            return dot + 0.00001f >= minimumDot;
        }

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
