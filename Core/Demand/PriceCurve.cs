using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Demand
{
    // The mod's own price-to-conversion curve: the share of would-be customers who still buy at a price,
    // given as the ratio of the vehicle's price to the game's reference price for the item.
    // Straight lines between the points from economy.json, flat beyond the first and last point.
    // Calibration: a game shop customer buys up to max(class index, neighbourhood average index) times
    // the reference price, with class indices 1.2, 1.4 and 1.7 and the average weighted by the
    // neighbourhood's class mix (CitizenHelper.MaxAcceptableRelativePrice; read in the decompile,
    // unverified in game). The curve smooths that staircase for a neighbourhood whose average index is
    // AssumedPriceIndex; NeighbourhoodRatio moves other neighbourhoods onto it.
    public sealed class PriceCurve
    {
        // The working-class index, the lowest a neighbourhood's average can be.
        public const double LowestPriceIndex = 1.2;

        private readonly double[] ratios;
        private readonly double[] shares;

        public PriceCurve(IReadOnlyList<double> ratios, IReadOnlyList<double> shares, double assumedPriceIndex = 1.35)
        {
            if (double.IsNaN(assumedPriceIndex) || double.IsInfinity(assumedPriceIndex) || assumedPriceIndex < LowestPriceIndex)
                throw new ArgumentOutOfRangeException(nameof(assumedPriceIndex));
            if (ratios == null) throw new ArgumentNullException(nameof(ratios));
            if (shares == null) throw new ArgumentNullException(nameof(shares));
            if (ratios.Count < 2 || ratios.Count != shares.Count)
                throw new ArgumentException("A price curve needs at least two points, each with a ratio and a share.");
            for (var i = 0; i < ratios.Count; i++)
            {
                if (!IsFinite(ratios[i]) || ratios[i] < 0 || !IsFinite(shares[i]) || shares[i] < 0)
                    throw new ArgumentException("Price curve points must be finite and not negative.");
                if (i > 0 && ratios[i] <= ratios[i - 1])
                    throw new ArgumentException("Price curve ratios must rise from point to point.");
                if (i > 0 && shares[i] > shares[i - 1])
                    throw new ArgumentException("A higher price must never bring more buyers.");
            }
            if (assumedPriceIndex >= ratios[ratios.Count - 1])
                throw new ArgumentException("The curve's last point must lie above its assumed price index.");
            AssumedPriceIndex = assumedPriceIndex;
            this.ratios = ratios.ToArray();
            this.shares = shares.ToArray();
        }

        public double AssumedPriceIndex { get; }

        // The share of customers who buy at price / reference price. A bad ratio (no reference price,
        // NaN) is treated as the reference price itself.
        public double Conversion(double priceRatio)
        {
            if (!IsFinite(priceRatio) || priceRatio < 0) priceRatio = 1.0;
            if (priceRatio <= ratios[0]) return shares[0];
            var last = ratios.Length - 1;
            if (priceRatio >= ratios[last]) return shares[last];
            var i = 1;
            while (ratios[i] < priceRatio) i++;
            var t = (priceRatio - ratios[i - 1]) / (ratios[i] - ratios[i - 1]);
            return shares[i - 1] + t * (shares[i] - shares[i - 1]);
        }

        // Price over reference, or 1.0 when either is missing.
        public static double Ratio(double price, double referencePrice)
        {
            if (!IsFinite(price) || !IsFinite(referencePrice) || price <= 0 || referencePrice <= 0) return 1.0;
            return price / referencePrice;
        }

        // Price over reference, moved onto the curve for a neighbourhood whose average price index differs
        // from AssumedPriceIndex. At or below the reference price nothing changes, so the undercut bonus
        // stays where it is. From the reference to the neighbourhood's index (where every game customer
        // still buys) maps onto 1 to AssumedPriceIndex; from there to the curve's last point maps onto
        // AssumedPriceIndex to that point, so nobody buys beyond it anywhere. A missing index leaves the plain ratio.
        public double NeighbourhoodRatio(double price, double referencePrice, double neighbourhoodPriceIndex)
        {
            var ratio = Ratio(price, referencePrice);
            if (!IsFinite(neighbourhoodPriceIndex) || neighbourhoodPriceIndex <= 0 || ratio <= 1.0) return ratio;
            var index = Math.Max(LowestPriceIndex, neighbourhoodPriceIndex);
            var end = ratios[ratios.Length - 1];
            if (ratio <= index) return 1.0 + (ratio - 1.0) * (AssumedPriceIndex - 1.0) / (index - 1.0);
            if (index >= end || ratio >= end) return Math.Max(ratio, end);
            return AssumedPriceIndex + (ratio - index) * (end - AssumedPriceIndex) / (end - index);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
