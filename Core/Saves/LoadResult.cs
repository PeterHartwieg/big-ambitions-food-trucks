namespace FoodTrucks.Core.Saves
{
    public enum LoadStatus
    {
        Fresh,
        Loaded,
        Migrated,
        NewerVersion,
        Corrupt
    }

    public sealed class LoadResult
    {
        public LoadStatus Status { get; }
        public SaveState? State { get; }
        public string? RawJson { get; }
        public string? Error { get; }

        internal LoadResult(LoadStatus status, SaveState? state = null, string? rawJson = null, string? error = null)
        {
            Status = status;
            State = state;
            RawJson = rawJson;
            Error = error;
        }
    }
}
