#nullable enable
using System;
using System.Linq;
using BigAmbitions.Items;
using Buildings.Office.Headquarters;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Selling;
using Extensions;
using Helpers;

namespace FoodTrucks
{
    internal static class CartPricing
    {
        public static long PriceCents(VehicleState? vehicle, string itemName, string? neighbourhood)
        {
            long? custom = null;
            if (vehicle?.CustomPriceCents != null && vehicle.CustomPriceCents.TryGetValue(itemName, out var c)) custom = c;
            return PriceChoice.Choose(custom, ManagerCents(itemName, neighbourhood), DefaultCents(itemName));
        }

        public static long DefaultCents(string itemName)
        {
            var item = ItemsGetter.GetByName(itemName, suppressError: true);
            return item == null ? 0 : (long)Math.Round(item.DefaultMarketPrice * CartTuning.PriceMultiplier * 100.0);
        }

        public static long CeilingCents(string itemName, string? neighbourhood)
        {
            if (string.IsNullOrEmpty(neighbourhood) || !Known(neighbourhood!)) return 0;
            try { return PricingManagerHelper.GetHighestAcceptableCents(itemName, neighbourhood); }
            catch (Exception ex)
            {
                Log.Error($"Price ceiling for {itemName} in {neighbourhood}: {ex.Message}");
                return 0;
            }
        }

        public static long? ManagerCents(string itemName, string? neighbourhood)
        {
            if (string.IsNullOrEmpty(neighbourhood) || !Known(neighbourhood!)) return null;
            try
            {
                var plan = PricingManagerHelper.GetPlanCoveringNeighborhood(neighbourhood);
                if (plan == null) return null;
                if (plan.IsManuallyPriced(itemName) && plan.TryGetUniformPrice(itemName, out var manual) && manual > 0)
                    return (long)Math.Round(manual * 100.0);
                var cached = plan.cachedSuggestions?.FirstOrDefault(p => p != null && p.itemName == itemName && p.suggestedMax > 0);
                if (cached != null) return (long)Math.Round(cached.suggestedMax * 100.0);
                var skill = Math.Max(0f, Math.Min(1f, plan.PricingManagerSkillValue / 100f));
                var jitter = RngHelper.StableValue01(plan.id + plan.assignedEmployeeId + itemName);
                var (_, max) = PricingManagerHelper.ComputeSuggestion(itemName, neighbourhood, skill, jitter);
                return max > 0 ? (long?)Math.Round(max * 100.0) : null;
            }
            catch (Exception ex)
            {
                Log.Error($"Pricing manager price for {itemName} in {neighbourhood}: {ex.Message}");
                return null;
            }
        }

        private static bool Known(string neighbourhood) =>
            AI.Citizens.CitizenHelper.averagePriceIndicesInNeighborhoods.ContainsKey(neighbourhood);
    }
}
