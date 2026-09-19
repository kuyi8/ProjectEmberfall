using Emberfall.Gameplay.Combat.Domain;

namespace Emberfall.Networking
{
    /// <summary>Server-side replay and command-scope gate for owner combat intents.</summary>
    public sealed class NetworkCombatIntentValidator
    {
        public uint LastAcceptedSequence { get; private set; }

        public bool TryAccept(uint sequence, CombatCommand command, out string reason)
        {
            if (command != CombatCommand.LightAttack &&
                command != CombatCommand.HeavyPressed &&
                command != CombatCommand.HeavyReleased &&
                command != CombatCommand.RangedAttack &&
                command != CombatCommand.Dodge &&
                command != CombatCommand.Heal &&
                command != CombatCommand.GuardPressed &&
                command != CombatCommand.GuardReleased)
            {
                reason = "unsupported-command";
                return false;
            }

            if (sequence == 0 || sequence <= LastAcceptedSequence)
            {
                reason = "stale-sequence";
                return false;
            }

            LastAcceptedSequence = sequence;
            reason = string.Empty;
            return true;
        }

        public static bool TryNormalizeDodgeDirection(
            float directionX,
            float directionZ,
            out float normalizedX,
            out float normalizedZ,
            out string reason)
        {
            normalizedX = 0f;
            normalizedZ = 0f;
            if (!IsFinite(directionX) || !IsFinite(directionZ))
            {
                reason = "non-finite-dodge-direction";
                return false;
            }

            float magnitudeSquared = (directionX * directionX) + (directionZ * directionZ);
            if (magnitudeSquared > 1.21f)
            {
                reason = "dodge-direction-out-of-range";
                return false;
            }

            if (magnitudeSquared > 0.0001f)
            {
                float inverseMagnitude = 1f / (float)System.Math.Sqrt(magnitudeSquared);
                normalizedX = directionX * inverseMagnitude;
                normalizedZ = directionZ * inverseMagnitude;
            }

            reason = string.Empty;
            return true;
        }

        public static bool TryNormalizeAimDirection(
            float directionX,
            float directionZ,
            out float normalizedX,
            out float normalizedZ,
            out string reason)
        {
            normalizedX = 0f;
            normalizedZ = 0f;
            if (!IsFinite(directionX) || !IsFinite(directionZ))
            {
                reason = "non-finite-aim-direction";
                return false;
            }

            float magnitudeSquared = (directionX * directionX) + (directionZ * directionZ);
            if (magnitudeSquared < 0.25f || magnitudeSquared > 1.21f)
            {
                reason = "aim-direction-out-of-range";
                return false;
            }

            float inverseMagnitude = 1f / (float)System.Math.Sqrt(magnitudeSquared);
            normalizedX = directionX * inverseMagnitude;
            normalizedZ = directionZ * inverseMagnitude;
            reason = string.Empty;
            return true;
        }

        public void Reset() => LastAcceptedSequence = 0;

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
