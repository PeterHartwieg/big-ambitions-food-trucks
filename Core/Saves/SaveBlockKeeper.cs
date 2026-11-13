namespace FoodTrucks.Core.Saves
{
    public enum SaveBlockOutcome
    {
        Present,
        Restored,
        None
    }

    public sealed class SaveBlockKeeper
    {
        private object? game;

        public string? Block { get; private set; }

        public void Loaded(object game, string? block)
        {
            this.game = game;
            Block = IsBlank(block) ? null : block;
        }

        public void Written(object game, string? block)
        {
            if (IsBlank(block)) return;
            this.game = game;
            Block = block;
        }

        public string? BlockFor(object? game) =>
            game != null && ReferenceEquals(game, this.game) ? Block : null;

        public SaveBlockOutcome BeforeSave(object game, string? current, out string? restore)
        {
            restore = null;
            if (!IsBlank(current)) return SaveBlockOutcome.Present;
            var known = BlockFor(game);
            if (known == null) return SaveBlockOutcome.None;
            restore = known;
            return SaveBlockOutcome.Restored;
        }

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
