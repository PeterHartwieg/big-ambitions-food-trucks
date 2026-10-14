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
            long deliveryFeeCents = 25000, double bayRadiusMetres = 15, int foodBoxesPerWeek = 10,
            IReadOnlyDictionary<string, int>? foodBoxesPerWeekByItem = null)
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
            if (deliveryFeeCents < 0) throw new ArgumentOutOfRangeException(nameof(deliveryFeeCents));
            if (double.IsNaN(bayRadiusMetres) || double.IsInfinity(bayRadiusMetres) || bayRadiusMetres <= 0)
                throw new ArgumentOutOfRangeException(nameof(bayRadiusMetres));
            if (foodBoxesPerWeek < 0) throw new ArgumentOutOfRangeException(nameof(foodBoxesPerWeek));
            var limits = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var pair in foodBoxesPerWeekByItem ?? new Dictionary<string, int>())
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value < 0)
                    throw new ArgumentException("Weekly food box limits must name items with amounts not negative.", nameof(foodBoxesPerWeekByItem));
                limits.Add(pair.Key, pair.Value);
            }
            RentPerMonthCents = new ReadOnlyDictionary<string, long>(rents);
            OvernightFineCents = overnightFineCents;
            NightCheckMinute = nightCheckMinute;
            DeliveryMinute = deliveryMinute;
            DeliveryFeeCents = deliveryFeeCents;
            BayRadiusMetres = bayRadiusMetres;
            FoodBoxesPerWeek = foodBoxesPerWeek;
            FoodBoxesPerWeekByItem = new ReadOnlyDictionary<string, int>(limits);
        }

        public IReadOnlyDictionary<string, long> RentPerMonthCents { get; }
        public long OvernightFineCents { get; }
        public int NightCheckMinute { get; }
        public int DeliveryMinute { get; }
        // One flat fee per delivery: everything a commissary delivers from phone orders on one morning together, whatever
        // its size and however many orders, carts or trucks it is for (Peter, 14 October 2026). The game charges a flat
        // fee per delivery too: a wholesale contract's deliveryFee per delivery, whatever its size, and $250 for a
        // furniture delivery [code, 1.0: DeliveryContract.TotalPricePerDelivery, FurnitureDeliveryContractSettings].
        public long DeliveryFeeCents { get; }
        public double BayRadiusMetres { get; }
        // Boxes of one food delivered from phone orders in a game week, at all commissaries together, one-off and standing
        // orders together, by delivery day (Peter, 14 October 2026: one limit for the whole fleet, so a growing fleet builds
        // its own supply chain). The default, and overrides by game item. Equipment has no limit (CommissarySupply).
        public int FoodBoxesPerWeek { get; }
        public IReadOnlyDictionary<string, int> FoodBoxesPerWeekByItem { get; }
        public int FoodBoxLimit(string itemName) => itemName != null && FoodBoxesPerWeekByItem.TryGetValue(itemName, out var limit) ? limit : FoodBoxesPerWeek;

        // Round each day's rent once, to whole cents; halves round away from zero.
        public long DailyRentCents(string kind) => checked((long)Math.Round(RentPerMonthCents[kind] * 12m / 365m, 0, MidpointRounding.AwayFromZero));

        internal static CommissaryRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "rentPerMonthCents", "overnightFineCents", "nightCheckMinute", "deliveryMinute", "deliveryFeeCents", "bayRadiusMetres", "foodBoxesPerWeek");
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
            var boxes = r.Object(obj, "foodBoxesPerWeek");
            r.OnlyKeys(boxes, "default", "items");
            var byItem = new Dictionary<string, int>(StringComparer.Ordinal);
            var items = r.OptionalObject(boxes, "items");
            if (items != null)
                foreach (var entry in r.Entries(items))
                {
                    if (string.IsNullOrWhiteSpace(entry.Name)) throw r.Fail(entry.Path, "must not be blank");
                    byItem.Add(entry.Name, r.Integer(items, entry.Name, 0, 100000));
                }
            return new CommissaryRules(amounts, Cents(r, obj, "overnightFineCents"),
                r.Integer(obj, "nightCheckMinute", 0, 1439), r.Integer(obj, "deliveryMinute", 0, 1439),
                Cents(r, obj, "deliveryFeeCents"), radius, r.Integer(boxes, "default", 0, 100000), byItem);
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
