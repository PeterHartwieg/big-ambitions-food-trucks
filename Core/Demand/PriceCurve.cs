using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Demand
{
    public sealed class PriceCurve
    {
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

        public static double Ratio(double price, double referencePrice)
        {
            if (!IsFinite(price) || !IsFinite(referencePrice) || price <= 0 || referencePrice <= 0) return 1.0;
            return price / referencePrice;
        }

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
