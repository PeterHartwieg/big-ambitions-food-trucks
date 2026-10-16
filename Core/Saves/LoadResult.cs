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
        // What loading changed to keep the save loadable (a shift from an earlier build with no row left), for the log.
        public System.Collections.Generic.List<string> Notes { get; } = new System.Collections.Generic.List<string>();

        internal LoadResult(LoadStatus status, SaveState? state = null, string? rawJson = null, string? error = null)
        {
            Status = status;
            State = state;
            RawJson = rawJson;
            Error = error;
        }
    }
}
