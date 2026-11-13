using System;
using System.Globalization;

namespace FoodTrucks.Core.Selling
{
    public static class PriceChoice
    {
        public const long MinCents = 1;
        public const long MaxCents = 1_000_000;

        public static long Choose(long? customCents, long? managerCents, long defaultCents) =>
            customCents > 0 ? customCents.Value : managerCents > 0 ? managerCents.Value : defaultCents > 0 ? defaultCents : 0;

        public static bool TryParseCents(string? text, out long cents)
        {
            cents = 0;
            if (text == null) return false;
            var trimmed = text.Trim().TrimStart('$').Trim().Replace(',', '.');
            if (trimmed.Length == 0) return false;
            if (!decimal.TryParse(trimmed, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var dollars)) return false;
            if (dollars > 1_000_000_000m) return false;
            var rounded = (long)Math.Round(dollars * 100m, MidpointRounding.AwayFromZero);
            if (rounded < MinCents || rounded > MaxCents) return false;
            cents = rounded;
            return true;
        }
    }
}
