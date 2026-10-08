using System;
using System.Collections.Generic;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;

namespace FoodTrucks.Core.Economics
{
    // The game's difficulty settings that move money, from SaveGameManager.Current.gameVariables
    // [code, unverified in game]. Defaults are the code's defaults; the shipped presets are asset data.
    public sealed class GameVariables
    {
        // Scales wholesale and import prices only (ItemHelper.GetWholesalePrice). Range 0.7 to 1.3.
        public double MarketPriceMultiplier { get; set; } = 1.0;
        // Scales every hourly wage (EmployeeHelper.CalculateHourlyWageForSkill). Range 0.5 to 1.3.
        public double WageMultiplier { get; set; } = 1.0;
        // The base of a shop's customer multiplier (CustomerEntriesCalculator.GetPromotionMultiplier). Range 0.1 to 1.0.
        public double BaseCustomerPromotion { get; set; } = 0.5;

        public static GameVariables Default => new GameVariables();
    }

    // The cheapest small fast food shop a player can set up: the yardstick for the mod's prices and
    // calibration targets. The game side should fill it from the live game (the cheapest retail
    // rent, item and wage data); the values in rules.json are guesses until then.
    public sealed class ReferenceShop
    {
        public long DailyRentCents { get; set; }
        // The deposit is this many days of rent (BuildingTypeData.daysToCalculateDeposit, 30 in code).
        public int DepositDays { get; set; } = 30;
        // Register, counter, grill, fridge: what the shop needs before it can open.
        public long EquipmentCents { get; set; }
        // The building's customer capacity per hour (BuildingSizeData.customerCapacities).
        public double BuildingCustomersPerHour { get; set; }
        // What the shop's register serves per hour (Item.addedCustomersPerHour).
        public double RegisterCustomersPerHour { get; set; }
        // 0 to 100; with no marketing a shop's promotion is its traffic index.
        public double TrafficIndex { get; set; } = 50;
        public int HoursOpen { get; set; } = 12;
        // The fast food business type's hourly factor, averaged over the open hours.
        public double AverageHourFactor { get; set; } = 1.0;
        public double ItemsPerCustomer { get; set; } = 1.0;
        public long ItemPriceCents { get; set; }
        // Before the difficulty's market multiplier.
        public long ItemWholesaleCents { get; set; }
        // SkillData.baseHourlyWage of the cashier skill.
        public long BaseHourlyWageCents { get; set; }
        public double WorkerSkill { get; set; } = 20;
        public int Workers { get; set; } = 1;
        // Units bought before opening, at wholesale.
        public int OpeningStockUnits { get; set; }
        // When set, the game's own estimate replaces the formula: the shop's weekly profit from
        // EstimatedWeeklyIncomeHelper.GetEstimatedWeeklyIncome divided by 7, difficulty already in it.
        public long? MeasuredDailyProfitCents { get; set; }

        public ReferenceShop Copy() => (ReferenceShop)MemberwiseClone();
    }

    public sealed class EconomyRules
    {
        public EconomyRules(ReferenceShop shop, IReadOnlyDictionary<string, double> priceMultiples, long priceRoundingCents,
            double litresPerKm, long fuelCentsPerLitre, double midGameBusinessShops)
        {
            Shop = shop ?? throw new ArgumentNullException(nameof(shop));
            PriceMultiples = priceMultiples ?? throw new ArgumentNullException(nameof(priceMultiples));
            PriceRoundingCents = priceRoundingCents;
            LitresPerKm = litresPerKm;
            FuelCentsPerLitre = fuelCentsPerLitre;
            MidGameBusinessShops = midGameBusinessShops;
        }

        // The assumed reference shop, used until the game side measures one.
        public ReferenceShop Shop { get; }
        // Price of each priced thing (vehicles, licence, permit, equipment) as a multiple of the
        // reference shop's setup cost.
        public IReadOnlyDictionary<string, double> PriceMultiples { get; }
        public long PriceRoundingCents { get; }
        public double LitresPerKm { get; }
        // The game charges $1.30 a litre everywhere (GasStationOverlay.PricePerLiter) [code, unverified in game].
        public long FuelCentsPerLitre { get; }
        // A mid-game business earns this many reference shops; a fleet of four trucks stays below it.
        public double MidGameBusinessShops { get; }

        internal static EconomyRules Read(DataReader r, JObject obj)
        {
            r.OnlyKeys(obj, "referenceShop", "prices", "fuel", "midGameBusinessShops");
            var shopObj = r.Object(obj, "referenceShop");
            r.OnlyKeys(shopObj, "dailyRentCents", "depositDays", "equipmentCents", "buildingCustomersPerHour", "registerCustomersPerHour",
                "trafficIndex", "hoursOpen", "averageHourFactor", "itemsPerCustomer", "itemPriceCents", "itemWholesaleCents",
                "baseHourlyWageCents", "workerSkill", "workers", "openingStockUnits");
            var shop = new ReferenceShop
            {
                DailyRentCents = Cents(r, shopObj, "dailyRentCents"),
                DepositDays = r.Integer(shopObj, "depositDays", 0, 365),
                EquipmentCents = Cents(r, shopObj, "equipmentCents"),
                BuildingCustomersPerHour = r.Number(shopObj, "buildingCustomersPerHour", 0, 10000),
                RegisterCustomersPerHour = r.Number(shopObj, "registerCustomersPerHour", 0, 10000),
                TrafficIndex = r.Number(shopObj, "trafficIndex", 0, 100),
                HoursOpen = r.Integer(shopObj, "hoursOpen", 1, 24),
                AverageHourFactor = r.Number(shopObj, "averageHourFactor", 0, 10),
                ItemsPerCustomer = r.Number(shopObj, "itemsPerCustomer", 0, 100),
                ItemPriceCents = Cents(r, shopObj, "itemPriceCents"),
                ItemWholesaleCents = Cents(r, shopObj, "itemWholesaleCents"),
                BaseHourlyWageCents = Cents(r, shopObj, "baseHourlyWageCents"),
                WorkerSkill = r.Number(shopObj, "workerSkill", 0, 100),
                Workers = r.Integer(shopObj, "workers", 0, 100),
                OpeningStockUnits = r.Integer(shopObj, "openingStockUnits", 0, 100000)
            };

            var pricesObj = r.Object(obj, "prices");
            r.OnlyKeys(pricesObj, "roundToCents", "multiples");
            var multiples = new Dictionary<string, double>(StringComparer.Ordinal);
            var multiplesObj = r.Object(pricesObj, "multiples");
            foreach (var entry in r.Entries(multiplesObj))
                multiples.Add(entry.Name, r.Number(entry.Value, DataReader.PathOf(multiplesObj, entry.Name), 0, 1000));

            var fuel = r.Object(obj, "fuel");
            r.OnlyKeys(fuel, "litresPerKm", "centsPerLitre");
            return new EconomyRules(shop, multiples, r.Integer(pricesObj, "roundToCents", 1, 1000000),
                r.Number(fuel, "litresPerKm", 0, 10), r.Integer(fuel, "centsPerLitre", 0, 100000),
                r.Number(obj, "midGameBusinessShops", 1, 1000));
        }

        private static long Cents(DataReader r, JObject obj, string name) => r.Integer(obj, name, 0, int.MaxValue);
    }
}
