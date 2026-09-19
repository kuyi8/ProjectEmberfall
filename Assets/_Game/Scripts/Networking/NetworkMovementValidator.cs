using System;

namespace Emberfall.Networking
{
    public enum NetworkMovementValidationStatus
    {
        Accepted = 0,
        Corrected = 1,
        Ignored = 2
    }

    public readonly struct NetworkMovementValidationResult
    {
        public NetworkMovementValidationResult(
            NetworkMovementValidationStatus status,
            float acceptedX,
            float acceptedZ,
            float observedSpeed,
            string reason)
        {
            Status = status;
            AcceptedX = acceptedX;
            AcceptedZ = acceptedZ;
            ObservedSpeed = observedSpeed;
            Reason = reason ?? string.Empty;
        }

        public NetworkMovementValidationStatus Status { get; }
        public float AcceptedX { get; }
        public float AcceptedZ { get; }
        public float ObservedSpeed { get; }
        public string Reason { get; }
    }

    /// <summary>
    /// Server-side planar movement guard. It uses server time, never client-provided delta time,
    /// and limits long gaps so packet stalls cannot become a teleport budget.
    /// </summary>
    public sealed class NetworkMovementValidator
    {
        private readonly float _maximumSpeed;
        private readonly float _positionTolerance;
        private readonly double _maxElapsedSeconds;
        private float _acceptedX;
        private float _acceptedZ;
        private double _lastServerTime;
        private uint _lastSequence;
        private bool _initialized;

        public NetworkMovementValidator(float maxSpeed, float positionTolerance, double maxElapsedSeconds)
        {
            if (!IsFinite(maxSpeed) || maxSpeed <= 0f) throw new ArgumentOutOfRangeException(nameof(maxSpeed));
            if (!IsFinite(positionTolerance) || positionTolerance < 0f) throw new ArgumentOutOfRangeException(nameof(positionTolerance));
            if (double.IsNaN(maxElapsedSeconds) || double.IsInfinity(maxElapsedSeconds) || maxElapsedSeconds <= 0d)
                throw new ArgumentOutOfRangeException(nameof(maxElapsedSeconds));

            _maximumSpeed = maxSpeed;
            _positionTolerance = positionTolerance;
            _maxElapsedSeconds = maxElapsedSeconds;
        }

        public void Reset(float x, float z, double serverTime, uint sequence = 0)
        {
            if (!IsFinite(x) || !IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(x));
            if (double.IsNaN(serverTime) || double.IsInfinity(serverTime)) throw new ArgumentOutOfRangeException(nameof(serverTime));

            _acceptedX = x;
            _acceptedZ = z;
            _lastServerTime = serverTime;
            _lastSequence = sequence;
            _initialized = true;
        }

        public NetworkMovementValidationResult Validate(float candidateX, float candidateZ, double serverTime, uint sequence)
        {
            return Validate(candidateX, candidateZ, serverTime, sequence, _maximumSpeed);
        }

        public NetworkMovementValidationResult Validate(
            float candidateX,
            float candidateZ,
            double serverTime,
            uint sequence,
            float allowedSpeed)
        {
            if (!_initialized) throw new InvalidOperationException("Reset must be called before validation.");
            if (!IsFinite(candidateX) || !IsFinite(candidateZ) || double.IsNaN(serverTime) || double.IsInfinity(serverTime))
                return Ignore("non-finite-sample");
            if (!IsFinite(allowedSpeed) || allowedSpeed <= 0f || allowedSpeed > _maximumSpeed)
                return Ignore("invalid-speed-budget");
            if (sequence <= _lastSequence)
                return Ignore("stale-sequence");

            double elapsed = serverTime - _lastServerTime;
            if (elapsed <= 0d)
                return Ignore("non-positive-server-time");

            float dx = candidateX - _acceptedX;
            float dz = candidateZ - _acceptedZ;
            float distance = (float)Math.Sqrt((dx * dx) + (dz * dz));
            float observedSpeed = distance / (float)elapsed;
            float budget = (allowedSpeed * (float)Math.Min(elapsed, _maxElapsedSeconds)) + _positionTolerance;

            _lastSequence = sequence;
            _lastServerTime = serverTime;
            if (distance <= budget || distance <= float.Epsilon)
            {
                _acceptedX = candidateX;
                _acceptedZ = candidateZ;
                return new NetworkMovementValidationResult(
                    NetworkMovementValidationStatus.Accepted,
                    _acceptedX,
                    _acceptedZ,
                    observedSpeed,
                    string.Empty);
            }

            float scale = budget / distance;
            _acceptedX += dx * scale;
            _acceptedZ += dz * scale;
            return new NetworkMovementValidationResult(
                NetworkMovementValidationStatus.Corrected,
                _acceptedX,
                _acceptedZ,
                observedSpeed,
                "speed-or-position-budget-exceeded");
        }

        private NetworkMovementValidationResult Ignore(string reason) =>
            new NetworkMovementValidationResult(
                NetworkMovementValidationStatus.Ignored,
                _acceptedX,
                _acceptedZ,
                0f,
                reason);

        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
