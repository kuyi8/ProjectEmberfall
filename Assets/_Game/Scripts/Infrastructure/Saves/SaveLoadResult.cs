namespace Emberfall.Infrastructure.Saves
{
    public enum SaveLoadStatus
    {
        NewGame = 0,
        Loaded = 1,
        RecoveredCorrupt = 2
    }

    public readonly struct SaveLoadResult
    {
        public SaveLoadResult(SaveGameV1 save, SaveLoadStatus status)
        {
            Save = save;
            Status = status;
        }

        public SaveGameV1 Save { get; }
        public SaveLoadStatus Status { get; }
    }
}
