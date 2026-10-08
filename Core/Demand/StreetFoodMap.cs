using System;
using System.Collections.Generic;
using System.Linq;

namespace FoodTrucks.Core.Demand
{
    // The "Street food" category on the game's city map: which buildings each filter highlights.
    // The filters follow the demand model's own rules (AreaClassifier), so a highlighted building is one
    // that brings that crowd to the spots around it (design: Player view).
    public static class StreetFoodMap
    {
        public const string Offices = "offices";
        public const string Factories = "factories";
        public const string Nightlife = "nightlife";
        public const string Shows = "shows";

        public static readonly IReadOnlyList<string> BuildingFilters = new[] { Offices, Factories, Nightlife, Shows };

        // Theatres and cinemas have their own filter; nightlife covers the rest of the night crowd.
        private static readonly HashSet<string> ShowTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ba:buildingtype_theater", "ba:buildingtype_cinema", "ba:businesstype_theater", "ba:businesstype_cinema"
        };

        public static bool Highlights(AreaClassifier classifier, string filter, string? buildingType, string? businessType)
        {
            if (classifier == null) throw new ArgumentNullException(nameof(classifier));
            var show = IsShow(buildingType, businessType);
            switch (filter)
            {
                case Offices: return classifier.Draws("office", buildingType, businessType);
                case Factories: return classifier.Draws("industrial", buildingType, businessType);
                case Nightlife: return !show && classifier.Draws("nightlife", buildingType, businessType);
                case Shows: return show;
                default: throw new ArgumentException($"Unknown map filter '{filter}'.", nameof(filter));
            }
        }

        // A cinema building with a shop in it is no show; a business decides before its building.
        private static bool IsShow(string? buildingType, string? businessType)
        {
            if (businessType != null && businessType != "ba:businesstype_empty") return ShowTypes.Contains(businessType);
            return buildingType != null && ShowTypes.Contains(buildingType);
        }

        public static IEnumerable<string> FiltersFor(AreaClassifier classifier, string? buildingType, string? businessType) =>
            BuildingFilters.Where(f => Highlights(classifier, f, buildingType, businessType));
    }
}
