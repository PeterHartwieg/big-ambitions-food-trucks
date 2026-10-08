using FoodTrucks.Core.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class StreetFoodMapTests
{
    private static readonly AreaClassifier classifier = new(ShippedData.Tables);

    [Theory]
    [InlineData("ba:buildingtype_office", null, StreetFoodMap.Offices)]
    [InlineData("ba:buildingtype_office", "ba:businesstype_lawfirm", StreetFoodMap.Offices)]
    [InlineData("ba:buildingtype_warehouse", null, StreetFoodMap.Factories)]
    [InlineData("ba:buildingtype_warehouse", "ba:businesstype_factory", StreetFoodMap.Factories)]
    [InlineData("ba:buildingtype_retail", "ba:businesstype_nightclub", StreetFoodMap.Nightlife)]
    [InlineData("ba:buildingtype_theater", null, StreetFoodMap.Shows)]
    [InlineData("ba:buildingtype_cinema", "ba:businesstype_cinema", StreetFoodMap.Shows)]
    public void EachFilterHighlightsTheBuildingsThatBringItsCrowd(string building, string? business, string filter)
    {
        Assert.Equal(new[] { filter }, StreetFoodMap.FiltersFor(classifier, building, business));
    }

    [Fact]
    public void AShopInACinemaBuildingIsNoShow()
    {
        Assert.False(StreetFoodMap.Highlights(classifier, StreetFoodMap.Shows, "ba:buildingtype_cinema", "ba:businesstype_giftshop"));
        Assert.True(StreetFoodMap.Highlights(classifier, StreetFoodMap.Shows, "ba:buildingtype_cinema", "ba:businesstype_empty"));
    }

    [Fact]
    public void HomesAndShopsAreNotOnTheMap()
    {
        Assert.Empty(StreetFoodMap.FiltersFor(classifier, "ba:buildingtype_residential", null));
        Assert.Empty(StreetFoodMap.FiltersFor(classifier, "ba:buildingtype_retail", "ba:businesstype_giftshop"));
    }
}
