using System;

namespace FoodTrucks.Core.Selling
{
    public readonly struct SalesTickResult
    {
        public int Sales { get; }
        public double Progress { get; }
        public bool SoldOut { get; }
        public bool Skipped { get; }

        internal SalesTickResult(int sales, double progress, bool soldOut, bool skipped)
        {
            Sales = sales;
            Progress = progress;
            SoldOut = soldOut;
            Skipped = skipped;
        }
    }

    public static class SalesTick
    {
        public const int MaxCatchUpMinutes = 120;

        public static SalesTickResult Advance(double progress, int elapsedMinutes, double salesPerHour, int stockUnits)
        {
            progress = ClampProgress(progress);
            var skipped = elapsedMinutes > MaxCatchUpMinutes;
            if (stockUnits <= 0)
                return new SalesTickResult(0, 0, true, skipped);
            if (elapsedMinutes <= 0 || skipped || salesPerHour <= 0 ||
                double.IsNaN(salesPerHour) || double.IsInfinity(salesPerHour))
                return new SalesTickResult(0, progress, false, skipped);

            var total = progress + elapsedMinutes * salesPerHour / 60;
            var wholeSales = Math.Floor(total);
            // Compare before converting: even a finite rate can overflow the double total.
            if (wholeSales >= stockUnits)
                return new SalesTickResult(stockUnits, 0, true, false);

            return new SalesTickResult((int)wholeSales, ClampProgress(total - wholeSales), false, false);
        }

        private static double ClampProgress(double progress) =>
            double.IsNaN(progress) || double.IsInfinity(progress) || progress < 0 || progress >= 1 ? 0 : progress;
    }
}
