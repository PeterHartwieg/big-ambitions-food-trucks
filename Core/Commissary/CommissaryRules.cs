using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Commissary
{
    public sealed class CommissaryRules
    {
        public CommissaryRules(IReadOnlyDictionary<string, long>? rentPerMonthCents = null,
            long overnightFineCents = 25000, int nightCheckMinute = 240, int deliveryMinute = 480,
            double deliveryFeeShare = 0.10, double bayRadiusMetres = 15)
        {
            var rents = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var pair in rentPerMonthCents ?? new Dictionary<string, long> { ["cart"] = 30000, ["truck"] = 125000 })
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0)
                    throw new ArgumentException("Rent must name vehicle kinds with amounts not negative.", nameof(rentPerMonthCents));
                rents.Add(pair.Key, pair.Value);
            }
            if (rents.Count == 0) throw new ArgumentException("Rent must contain at least one vehicle kind.", nameof(rentPerMonthCents));
            if (overnightFineCents < 0) throw new ArgumentOutOfRangeException(nameof(overnightFineCents));
            if (nightCheckMinute < 0 || nightCheckMinute > 1439) throw new ArgumentOutOfRangeException(nameof(nightCheckMinute));
            if (deliveryMinute < 0 || deliveryMinute > 1439) throw new ArgumentOutOfRangeException(nameof(deliveryMinute));
            if (double.IsNaN(deliveryFeeShare) || deliveryFeeShare < 0 || deliveryFeeShare > 1)
                throw new ArgumentOutOfRangeException(nameof(deliveryFeeShare));
            if (double.IsNaN(bayRadiusMetres) || double.IsInfinity(bayRadiusMetres) || bayRadiusMetres <= 0)
                throw new ArgumentOutOfRangeException(nameof(bayRadiusMetres));
            RentPerMonthCents = new ReadOnlyDictionary<string, long>(rents);
            OvernightFineCents = overnightFineCents;
            NightCheckMinute = nightCheckMinute;
            DeliveryMinute = deliveryMinute;
            DeliveryFeeShare = deliveryFeeShare;
            BayRadiusMetres = bayRadiusMetres;
        }

        public IReadOnlyDictionary<string, long> RentPerMonthCents { get; }
        public long OvernightFineCents { get; }
        public int NightCheckMinute { get; }
        public int DeliveryMinute { get; }
        public double DeliveryFeeShare { get; }
        public double BayRadiusMetres { get; }

        // Round each day's rent and each order's fee once, to whole cents; halves round away from zero.
        public long DailyRentCents(string kind) => checked((long)Math.Round(RentPerMonthCents[kind] * 12m / 365m, 0, MidpointRounding.AwayFromZero));
        public long FeeCents(long goodsCents)
        {
            if (goodsCents < 0) throw new ArgumentOutOfRangeException(nameof(goodsCents));
            return checked((long)Math.Round(goodsCents * (decimal)DeliveryFeeShare, 0, MidpointRounding.AwayFromZero));
        }

        internal static CommissaryRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "rentPerMonthCents", "overnightFineCents", "nightCheckMinute", "deliveryMinute", "deliveryFeeShare", "bayRadiusMetres");
            var rents = r.Object(obj, "rentPerMonthCents");
            var amounts = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (var entry in r.Entries(rents))
            {
                if (string.IsNullOrWhiteSpace(entry.Name)) throw r.Fail(entry.Path, "must not be blank");
                amounts.Add(entry.Name, Cents(r, rents, entry.Name));
            }
            if (amounts.Count == 0) throw r.Fail(rents.Path, "must not be empty");
            var radius = r.Number(obj, "bayRadiusMetres", 0, double.MaxValue);
            if (radius == 0) throw r.Fail(DataReader.PathOf(obj, "bayRadiusMetres"), "must be above 0");
            return new CommissaryRules(amounts, Cents(r, obj, "overnightFineCents"),
                r.Integer(obj, "nightCheckMinute", 0, 1439), r.Integer(obj, "deliveryMinute", 0, 1439),
                r.Number(obj, "deliveryFeeShare", 0, 1), radius);
        }

        private static long Cents(DataReader r, JObject obj, string name)
        {
            var token = r.Required(obj, name);
            if (token.Type != JTokenType.Integer || !(((JValue)token).Value is long value) || value < 0)
                throw r.Fail(DataReader.PathOf(obj, name), "must be whole cents between 0 and Int64.MaxValue");
            return value;
        }
    }
}
