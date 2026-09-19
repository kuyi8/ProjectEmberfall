using System;

namespace Emberfall.Gameplay.Movement
{
    /// <summary>
    /// Deterministic planar speed response for the local character motor.
    /// Direction follows accepted input immediately; animation and root motion never own displacement.
    /// </summary>
    public static class PlanarMovementResponse
    {
        private const float Epsilon = 0.0001f;

        public static PlanarMovementStep Step(
            float currentX,
            float currentZ,
            float inputX,
            float inputZ,
            float maximumSpeed,
            float acceleration,
            float deceleration,
            float deltaTime)
        {
            float safeDeltaTime = Math.Max(0f, deltaTime);
            float currentSpeed = Magnitude(currentX, currentZ);
            float rawInputMagnitude = Magnitude(inputX, inputZ);
            float inputMagnitude = Math.Min(1f, rawInputMagnitude);

            if (inputMagnitude <= Epsilon || maximumSpeed <= 0f)
            {
                float stoppedSpeed = MoveTowards(
                    currentSpeed,
                    0f,
                    Math.Max(0f, deceleration) * safeDeltaTime);
                if (currentSpeed <= Epsilon || stoppedSpeed <= Epsilon)
                {
                    return new PlanarMovementStep(0f, 0f, false);
                }

                float retainedScale = stoppedSpeed / currentSpeed;
                return new PlanarMovementStep(
                    currentX * retainedScale,
                    currentZ * retainedScale,
                    false);
            }

            float directionX = inputX / rawInputMagnitude;
            float directionZ = inputZ / rawInputMagnitude;
            bool reversed = IsReversal(currentX, currentZ, directionX, directionZ);
            if (reversed)
            {
                currentSpeed = 0f;
            }

            float targetSpeed = Math.Max(0f, maximumSpeed) * inputMagnitude;
            float response = targetSpeed >= currentSpeed
                ? Math.Max(0f, acceleration)
                : Math.Max(0f, deceleration);
            float nextSpeed = MoveTowards(currentSpeed, targetSpeed, response * safeDeltaTime);
            return new PlanarMovementStep(directionX * nextSpeed, directionZ * nextSpeed, reversed);
        }

        public static bool IsReversal(float currentX, float currentZ, float inputX, float inputZ)
        {
            float currentMagnitude = Magnitude(currentX, currentZ);
            float inputMagnitude = Magnitude(inputX, inputZ);
            if (currentMagnitude <= Epsilon || inputMagnitude <= Epsilon)
            {
                return false;
            }

            float normalizedDot = ((currentX * inputX) + (currentZ * inputZ)) /
                                  (currentMagnitude * inputMagnitude);
            return normalizedDot < -0.05f;
        }

        private static float Magnitude(float x, float z) =>
            (float)Math.Sqrt((x * x) + (z * z));

        private static float MoveTowards(float current, float target, float maximumDelta)
        {
            if (Math.Abs(target - current) <= maximumDelta)
            {
                return target;
            }

            return current + (Math.Sign(target - current) * maximumDelta);
        }
    }

    public readonly struct PlanarMovementStep
    {
        public PlanarMovementStep(float x, float z, bool reversed)
        {
            X = x;
            Z = z;
            Reversed = reversed;
        }

        public float X { get; }
        public float Z { get; }
        public bool Reversed { get; }
        public float Speed => (float)Math.Sqrt((X * X) + (Z * Z));
    }
}
