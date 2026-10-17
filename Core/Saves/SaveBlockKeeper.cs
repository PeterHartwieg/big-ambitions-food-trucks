namespace FoodTrucks.Core.Saves
{
    public enum SaveBlockOutcome
    {
        // The game instance holds a block; the save takes it as it is.
        Present,
        // The block was missing or empty; the one last read or written for this game goes back in.
        Restored,
        // Nothing to keep: no block now and none known for this game (a save that never had the mod).
        None
    }

    // The mod's block in a game's mod data, as last read from its save file or written by the mod. A save written while
    // the mod has no session on that game keeps the block: an empty or missing block never goes out over real data.
    // The game instance is only compared by reference; Core never looks inside it.
    public sealed class SaveBlockKeeper
    {
        private object? game;

        // The block last read or written for the known game; null when there is none.
        public string? Block { get; private set; }

        // A load read this block (null or empty when the file had none) for this game instance.
        public void Loaded(object game, string? block)
        {
            this.game = game;
            Block = IsBlank(block) ? null : block;
        }

        // The mod wrote this block into the game instance. An empty block is never remembered over a real one.
        public void Written(object game, string? block)
        {
            if (IsBlank(block)) return;
            this.game = game;
            Block = block;
        }

        // The block known for this game instance, or null for another game or none.
        public string? BlockFor(object? game) =>
            game != null && ReferenceEquals(game, this.game) ? Block : null;

        // Before the game writes this instance: what its mod data should hold. A present block is never replaced, so a
        // newer block the mod wrote, or one the mod can't read, goes out unchanged.
        public SaveBlockOutcome BeforeSave(object game, string? current, out string? restore)
        {
            restore = null;
            if (!IsBlank(current)) return SaveBlockOutcome.Present;
            var known = BlockFor(game);
            if (known == null) return SaveBlockOutcome.None;
            restore = known;
            return SaveBlockOutcome.Restored;
        }

        // For log lines: "save count 47, written by 0.10.0", "none" or "unreadable".
        public static string Describe(string? block)
        {
            if (IsBlank(block)) return "none";
            try
            {
                var document = Newtonsoft.Json.Linq.JObject.Parse(block!);
                var count = document["saveCount"]?.ToString() ?? "?";
                var by = document["writtenByModVersion"]?.ToString();
                return $"save count {count}, written by {(string.IsNullOrEmpty(by) ? "(none)" : by)}, {block!.Length} characters";
            }
            catch (Newtonsoft.Json.JsonException)
            {
                return $"unreadable, {block!.Length} characters";
            }
        }

        private static bool IsBlank(string? block) => string.IsNullOrWhiteSpace(block);
    }
}
