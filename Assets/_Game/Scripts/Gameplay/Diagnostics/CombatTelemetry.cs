namespace Emberfall.Gameplay.Diagnostics
{
    /// <summary>
    /// Read-only encounter data for Development/recording overlays. Implementations expose
    /// authoritative state, but consumers cannot submit commands or mutate combat.
    /// </summary>
    public readonly struct EncounterTelemetrySnapshot
    {
        public EncounterTelemetrySnapshot(
            string encounterLabel,
            string attackerLabel,
            string supportLeftLabel,
            string supportRightLabel,
            float relevance)
        {
            EncounterLabel = encounterLabel ?? string.Empty;
            AttackerLabel = attackerLabel ?? string.Empty;
            SupportLeftLabel = supportLeftLabel ?? string.Empty;
            SupportRightLabel = supportRightLabel ?? string.Empty;
            Relevance = relevance;
        }

        public string EncounterLabel { get; }
        public string AttackerLabel { get; }
        public string SupportLeftLabel { get; }
        public string SupportRightLabel { get; }
        public float Relevance { get; }
    }

    public interface IEncounterTelemetrySource
    {
        bool TryCaptureEncounterTelemetry(out EncounterTelemetrySnapshot snapshot);
    }

    public enum TacticalTelemetryState
    {
        Ready,
        Telegraph,
        Spent
    }

    public readonly struct TacticalTelemetrySnapshot
    {
        public TacticalTelemetrySnapshot(string label, TacticalTelemetryState state)
        {
            Label = label ?? string.Empty;
            State = state;
        }

        public string Label { get; }
        public TacticalTelemetryState State { get; }
    }

    public interface ITacticalTelemetrySource
    {
        bool TryCaptureTacticalTelemetry(out TacticalTelemetrySnapshot snapshot);
    }
}
