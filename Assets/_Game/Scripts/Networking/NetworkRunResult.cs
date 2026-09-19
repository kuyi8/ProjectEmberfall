using System;

namespace Emberfall.Networking
{
    public enum NetworkRunGrade
    {
        C = 0,
        B = 1,
        A = 2,
        S = 3
    }

    public readonly struct NetworkRunResultSummary
    {
        internal NetworkRunResultSummary(
            int sequence,
            int elapsedSeconds,
            int downedCount,
            int secretsFound,
            int initialPlayerCount,
            int connectedPlayerCount,
            bool teammateLeft,
            NetworkRunGrade grade)
        {
            Sequence = sequence;
            ElapsedSeconds = elapsedSeconds;
            DownedCount = downedCount;
            SecretsFound = secretsFound;
            InitialPlayerCount = initialPlayerCount;
            ConnectedPlayerCount = connectedPlayerCount;
            TeammateLeft = teammateLeft;
            Grade = grade;
        }

        public int Sequence { get; }
        public int ElapsedSeconds { get; }
        public int DownedCount { get; }
        public int SecretsFound { get; }
        public int InitialPlayerCount { get; }
        public int ConnectedPlayerCount { get; }
        public bool TeammateLeft { get; }
        public NetworkRunGrade Grade { get; }
        public bool IsValid => Sequence > 0;
    }

    public static class NetworkRunResultEvaluator
    {
        public static NetworkRunResultSummary Create(
            int sequence,
            float elapsedSeconds,
            int downedCount,
            int secretsFound,
            int initialPlayerCount,
            int connectedPlayerCount,
            bool teammateLeft)
        {
            if (sequence <= 0) throw new ArgumentOutOfRangeException(nameof(sequence));
            if (float.IsNaN(elapsedSeconds) || float.IsInfinity(elapsedSeconds) || elapsedSeconds < 0f)
                throw new ArgumentOutOfRangeException(nameof(elapsedSeconds));
            if (downedCount < 0) throw new ArgumentOutOfRangeException(nameof(downedCount));
            if (secretsFound < 0) throw new ArgumentOutOfRangeException(nameof(secretsFound));
            if (initialPlayerCount < 1 || initialPlayerCount > 2)
                throw new ArgumentOutOfRangeException(nameof(initialPlayerCount));
            if (connectedPlayerCount < 1 || connectedPlayerCount > initialPlayerCount)
                throw new ArgumentOutOfRangeException(nameof(connectedPlayerCount));

            int roundedSeconds = Math.Max(0, (int)Math.Round(elapsedSeconds));
            NetworkRunGrade grade = EvaluateGrade(roundedSeconds, downedCount);
            return new NetworkRunResultSummary(
                sequence,
                roundedSeconds,
                downedCount,
                secretsFound,
                initialPlayerCount,
                connectedPlayerCount,
                teammateLeft || connectedPlayerCount < initialPlayerCount,
                grade);
        }

        private static NetworkRunGrade EvaluateGrade(int elapsedSeconds, int downedCount)
        {
            if (elapsedSeconds <= 8 * 60 && downedCount == 0) return NetworkRunGrade.S;
            if (elapsedSeconds <= 12 * 60 && downedCount <= 1) return NetworkRunGrade.A;
            if (elapsedSeconds <= 20 * 60 && downedCount <= 3) return NetworkRunGrade.B;
            return NetworkRunGrade.C;
        }
    }
}
