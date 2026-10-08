using System;
using System.Globalization;

namespace FoodTrucks.Core.Selling
{
    public static class PriceChoice
    {
        // The range a custom price may take; SaveCodec rejects anything outside it.
        public const long MinCents = 1;
        public const long MaxCents = 1_000_000;

        // A valid player price wins over the district's pricing manager, then the default.
        public static long Choose(long? customCents, long? managerCents, long defaultCents) =>
            customCents > 0 ? customCents.Value : managerCents > 0 ? managerCents.Value : defaultCents > 0 ? defaultCents : 0;

        // Parses what the player typed as dollars ("12.50", "12,50", "$12"). Rounds to cents first, then
        // checks the range, so nothing that rounds to 0 cents ever reaches the save.
        public static bool TryParseCents(string? text, out long cents)
        {
            cents = 0;
            if (text == null) return false;
            var trimmed = text.Trim().TrimStart('$').Trim().Replace(',', '.');
            if (trimmed.Length == 0) return false;
            if (!decimal.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var dollars)) return false;
            // Decimal holds far more than the range, so round to cents first and check the range after.
            if (dollars > 1_000_000_000m) return false;
            var rounded = (long)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero);
            if (rounded < MinCents || rounded > MaxCents) return false;
            cents = rounded;
            return true;
        }
    }
}
