namespace Emberfall.Networking
{
    /// <summary>Server-side replay gate for a generic owner interaction request.</summary>
    public sealed class NetworkInteractionIntentValidator
    {
        public uint LastAcceptedSequence { get; private set; }

        public bool TryAccept(uint sequence, out string reason)
        {
            if (sequence == 0 || sequence <= LastAcceptedSequence)
            {
                reason = "stale-sequence";
                return false;
            }

            LastAcceptedSequence = sequence;
            reason = string.Empty;
            return true;
        }
    }
}
