namespace FoodTrucks.Core.Selling
{
    public static class CartTuning
    {
        // A one-person cart serves roughly 12 to 20 customers per in-game hour.
        public const double SalesPerHour = 12.0;

        // Multiplies the game's item price until Milestone 2 supplies the price curve.
        public const double PriceMultiplier = 1.0;
    }
}
