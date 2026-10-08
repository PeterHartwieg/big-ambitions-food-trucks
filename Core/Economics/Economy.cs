using System;

namespace FoodTrucks.Core.Economics
{
    // Money in the mod is set against the cheapest small fast food shop at the current difficulty
    // (design: Economy calibration). Formulas follow the game's own, read in the 1.0 decompile
    // [code, unverified in game]; the inputs are the game side's to read.
    public sealed class Economy
    {
        private readonly EconomyRules rules;

        public Economy(EconomyRules rules) => this.rules = rules ?? throw new ArgumentNullException(nameof(rules));

        public EconomyRules Rules => rules;

        // The reference shop's profit per day: margin on its customers, less rent and wages. Customers
        // per hour follow CustomerEntriesCalculatorRetail.GetCustomersByHour: the building's capacity
        // times (base promotion + 0.75 × traffic / 100) times the hour factor, capped by the shop's
        // customer capacity, the lower of the building's and the register's.
        // Wholesale stores add 5% (priceIndex 105). A measured profit from the game wins over all of it.
        public long ReferenceShopIncome(GameVariables difficulty, ReferenceShop? measured = null)
        {
            if (difficulty == null) throw new ArgumentNullException(nameof(difficulty));
            var shop = measured ?? rules.Shop;
            if (shop.MeasuredDailyProfitCents.HasValue) return shop.MeasuredDailyProfitCents.Value;

            var promotion = Factor(difficulty.BaseCustomerPromotion) + 0.75 * Clamp(shop.TrafficIndex, 0, 100) / 100;
            var capacity = Math.Min(shop.BuildingCustomersPerHour, shop.RegisterCustomersPerHour);
            var perHour = Math.Min(shop.BuildingCustomersPerHour * promotion * shop.AverageHourFactor, capacity);
            var customers = Math.Max(0, perHour) * shop.HoursOpen;
            var margin = shop.ItemPriceCents - WholesaleCents(shop.ItemWholesaleCents, difficulty);
            var wages = (double)shop.Workers * shop.HoursOpen * HourlyWageCents(shop.BaseHourlyWageCents, shop.WorkerSkill, difficulty);
            return (long)Math.Round(customers * shop.ItemsPerCustomer * margin - shop.DailyRentCents - wages);
        }

        // What opening the reference shop costs: the deposit, the equipment and the first stock.
        // Only the stock moves with difficulty; rent and equipment prices don't [code, unverified in game].
        public long ReferenceSetupCost(GameVariables difficulty, ReferenceShop? measured = null)
        {
            if (difficulty == null) throw new ArgumentNullException(nameof(difficulty));
            var shop = measured ?? rules.Shop;
            var stock = (long)Math.Round(shop.OpeningStockUnits * WholesaleCents(shop.ItemWholesaleCents, difficulty));
            return checked(shop.DailyRentCents * shop.DepositDays + shop.EquipmentCents + stock);
        }

        // A price set as a multiple of the setup cost, rounded to the rules' step (never below one step
        // for a multiple above 0). Unknown keys throw, so a typo can't make something free.
        public long Price(string key, GameVariables difficulty, ReferenceShop? measured = null)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (!rules.PriceMultiples.TryGetValue(key, out var multiple))
                throw new ArgumentException($"No price multiple for '{key}' in rules.json.", nameof(key));
            if (multiple <= 0) return 0;
            var raw = multiple * ReferenceSetupCost(difficulty, measured);
            var step = rules.PriceRoundingCents;
            return Math.Max(step, (long)Math.Round(raw / step) * step);
        }

        public bool HasPrice(string key) => key != null && rules.PriceMultiples.ContainsKey(key);

        // EmployeeHelper.CalculateHourlyWageForSkill: base × (skill^1.05 / 100 + 1) × the difficulty's
        // wage multiplier [code, unverified in game]. A job-board hire has skill 5 to 40.
        public static double HourlyWageCents(long baseHourlyWageCents, double skill, GameVariables difficulty)
        {
            if (difficulty == null) throw new ArgumentNullException(nameof(difficulty));
            var boost = Math.Pow(Clamp(skill, 0, 100), 1.05) / 100 + 1;
            return baseHourlyWageCents * boost * Factor(difficulty.WageMultiplier);
        }

        // The game pays hours worked at midnight; the mod does the same for its hires.
        public static long WageCents(long baseHourlyWageCents, double skill, double hours, GameVariables difficulty) =>
            (long)Math.Round(HourlyWageCents(baseHourlyWageCents, skill, difficulty) * Math.Max(0, Factor(hours)));

        // Fuel for a hire's simulated driving. A truck the player drives burns the game's own fuel.
        public long FuelCents(double metres) =>
            (long)Math.Round(Math.Max(0, Factor(metres)) / 1000 * rules.LitresPerKm * rules.FuelCentsPerLitre);

        // What the player pays a wholesaler per unit: the item's wholesale price × the market multiplier × 1.05.
        public static double WholesaleCents(long baseWholesaleCents, GameVariables difficulty) =>
            baseWholesaleCents * Factor(difficulty.MarketPriceMultiplier) * 1.05;

        private static double Factor(double value) => double.IsNaN(value) || double.IsInfinity(value) || value < 0 ? 0 : value;

        private static double Clamp(double value, double min, double max) =>
            double.IsNaN(value) ? min : Math.Max(min, Math.Min(max, value));
    }
}
