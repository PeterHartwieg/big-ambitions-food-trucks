using System;
using System.Linq;
using FoodTrucks.Core.Fleet;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class VehicleDiagramsTests
{
    private const string Json = @"{
        ""truck"": { ""image"": ""VehicleDiagram_Truck.png"", ""iconSize"": 0.18,
                     ""places"": [ { ""x"": 0.2, ""y"": 0.5 }, { ""x"": 0.4, ""y"": 0.5, ""size"": 0.25 } ] },
        ""cart"": { ""image"": ""VehicleDiagram_Cart.png"", ""places"": [ { ""x"": 0.5, ""y"": 0.3, ""iconSize"": 0.3 } ] }
    }";

    [Fact]
    public void KindsImagesAndPlacesAreRead()
    {
        var diagrams = VehicleDiagrams.Parse(Json);

        var truck = diagrams.For("truck")!;
        Assert.Equal("VehicleDiagram_Truck.png", truck.Image);
        Assert.Equal(new[] { 0.18, 0.25 }, truck.Places.Select(p => p.Size));
        Assert.Equal((0.4, 0.5), (truck.Places[1].X, truck.Places[1].Y));
        Assert.Equal(0.3, diagrams.For("cart")!.Places.Single().Size);
        Assert.Null(diagrams.For("van"));
    }

    [Fact]
    public void TheShippedFileReadsWithTheTruckAndCartPlaces()
    {
        var json = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "data", "vehicle-diagrams.json"));
        var diagrams = VehicleDiagrams.Parse(json);

        Assert.Equal("VehicleDiagram_Truck.png", diagrams.For("truck")!.Image);
        Assert.Equal(4, diagrams.For("truck")!.Places.Count);
        Assert.Equal(2, diagrams.For("cart")!.Places.Count);
        Assert.All(diagrams.For("truck")!.Places, p => Assert.InRange(p.Size, 0.01, 0.5));
    }

    [Theory]
    [InlineData(@"{ ""schemaVersion"": 2, ""truck"": { ""image"": ""a.png"", ""places"": [] } }")]
    [InlineData(@"{ ""truck"": { ""places"": [] } }")]
    [InlineData(@"{ ""truck"": { ""image"": ""a.png"" } }")]
    [InlineData(@"{ ""truck"": { ""image"": ""a.png"", ""places"": [ { ""x"": 1.5, ""y"": 0.2 } ] } }")]
    [InlineData(@"{ ""truck"": { ""image"": ""a.png"", ""places"": [ { ""y"": 0.2 } ] } }")]
    public void ABadFileFailsWithAClearMessage(string json) =>
        Assert.Contains("vehicle-diagrams.json", Assert.Throws<FormatException>(() => VehicleDiagrams.Parse(json)).Message);

    [Fact]
    public void PiecesFillThePlacesInOrderAndExtraPiecesAreLeftOut()
    {
        var truck = VehicleDiagrams.Parse(Json).For("truck")!;
        var filled = VehicleDiagrams.Fill(truck, new[] { "griddle", "fryer", "steamer" });
        Assert.Equal(new[] { "griddle", "fryer" }, filled.Select(f => f.Piece));
        Assert.Same(truck.Places[0], filled[0].Place);
    }
}

public class RouteMapTests
{
    [Fact]
    public void StopsKeepTheirProportionsNorthUpAndInsideTheMargin()
    {
        var map = RouteMap.Layout(new[] { (0.0, 0.0), (400.0, 0.0), (400.0, 200.0) }, null, runsDaily: true, aspect: 2, margin: 0.1);

        Assert.All(map.Stops, p => Assert.InRange(p.U, 0.1 - 1e-9, 0.9 + 1e-9));
        Assert.All(map.Stops, p => Assert.InRange(p.V, 0.1 - 1e-9, 0.9 + 1e-9));
        // East is right, north is up, and 400 m east spans twice the panel fraction of 200 m north once aspect is counted.
        Assert.True(map.Stops[1].U > map.Stops[0].U);
        Assert.True(map.Stops[2].V > map.Stops[1].V);
        var eastPanels = (map.Stops[1].U - map.Stops[0].U) * 2;
        var northPanels = map.Stops[2].V - map.Stops[1].V;
        Assert.Equal(2.0, eastPanels / northPanels, 6);
        Assert.True(map.Loops);
    }

    [Fact]
    public void OneStopIsCentredAndTheVehicleIsOnTheMap()
    {
        var map = RouteMap.Layout(new[] { (100.0, 100.0) }, (130.0, 100.0), runsDaily: true, aspect: 1);

        Assert.False(map.Loops);
        Assert.InRange(map.Vehicle!.Value.U, 0.5, 1);
        Assert.InRange(map.Stops[0].U, 0, 0.5);
    }

    [Fact]
    public void AShortRouteShowsAtLeast250MetresAndStreetsAreCulledToTheMap()
    {
        var map = RouteMap.Layout(new[] { (0.0, 0.0), (30.0, 0.0) }, null, runsDaily: true, aspect: 2);

        Assert.Equal(250, map.HeightMetres, 6);
        Assert.Equal(500, map.WidthMetres, 6);
        Assert.Equal(map.Stops[1], map.Place(30, 0));
        Assert.True(map.Shows(-300, 0, 300, 0));
        Assert.False(map.Shows(400, 0, 500, 10));
        Assert.False(map.Shows(0, 200, 10, 300));
    }

    [Fact]
    public void NoStopsGiveAnEmptyMap() =>
        Assert.Empty(RouteMap.Layout(Array.Empty<(double, double)>(), null, true, 2).Stops);

    [Fact]
    public void ARouteThatDoesNotRunDailyDoesNotLoop() =>
        Assert.False(RouteMap.Layout(new[] { (0.0, 0.0), (10.0, 0.0) }, null, runsDaily: false, aspect: 2).Loops);
}
