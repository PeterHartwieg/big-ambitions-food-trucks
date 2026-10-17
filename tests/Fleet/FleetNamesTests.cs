using System.Collections.Generic;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class FleetNamesTests
{
    [Fact]
    public void ANewVehicleNeverRenumbersTheOthers()
    {
        var numbers = new List<VehicleNumber>();
        Assert.True(FleetNames.Assign(numbers, new[] { ("m", "cart"), ("x", "cart") }));
        Assert.Equal(1, FleetNames.NumberOf(numbers, "m", "cart"));
        Assert.Equal(2, FleetNames.NumberOf(numbers, "x", "cart"));

        // "c" sorts first by ID but comes last, so it takes the next number.
        Assert.True(FleetNames.Assign(numbers, new[] { ("m", "cart"), ("x", "cart"), ("c", "cart") }));
        Assert.Equal(1, FleetNames.NumberOf(numbers, "m", "cart"));
        Assert.Equal(2, FleetNames.NumberOf(numbers, "x", "cart"));
        Assert.Equal(3, FleetNames.NumberOf(numbers, "c", "cart"));
        Assert.False(FleetNames.Assign(numbers, new[] { ("m", "cart"), ("x", "cart"), ("c", "cart") }));
    }

    [Fact]
    public void AVehicleMissingForAMomentComesBackUnderItsNumberAndKindsNumberApart()
    {
        var numbers = new List<VehicleNumber>();
        FleetNames.Assign(numbers, new[] { ("a", "cart"), ("b", "cart") });
        // "a" is gone (or not listed yet): a new cart doesn't take its number.
        FleetNames.Assign(numbers, new[] { ("b", "cart"), ("t", "truck"), ("z", "cart") });
        Assert.Equal(2, FleetNames.NumberOf(numbers, "b", "cart"));
        Assert.Equal(3, FleetNames.NumberOf(numbers, "z", "cart"));
        Assert.Equal(1, FleetNames.NumberOf(numbers, "t", "truck"));
        FleetNames.Assign(numbers, new[] { ("a", "cart"), ("b", "cart"), ("z", "cart") });
        Assert.Equal(1, FleetNames.NumberOf(numbers, "a", "cart"));
    }

    [Fact]
    public void ASaveWithBadNumbersLoadsWithoutThem()
    {
        var repaired = FleetNames.Repaired(new List<VehicleNumber>
        {
            null!, new() { Id = "", Kind = "cart", Number = 1 }, new() { Id = "a", Kind = "cart", Number = 0 },
            new() { Id = "a", Kind = "cart", Number = 1 }, new() { Id = "a", Kind = "cart", Number = 2 }, new() { Id = "b", Kind = "cart", Number = 1 },
            new() { Id = "b", Kind = "cart", Number = 3 }
        });
        Assert.Equal(2, repaired.Count);
        Assert.Equal(1, FleetNames.NumberOf(repaired, "a", "cart"));
        Assert.Equal(3, FleetNames.NumberOf(repaired, "b", "cart"));
        Assert.Empty(FleetNames.Repaired(null));
    }

    [Theory]
    [InlineData("[{\"id\":\"a\",\"kind\":\"cart\",\"number\":\"bad\"},{\"id\":\"b\",\"kind\":\"cart\",\"number\":2}]")]
    [InlineData("[{\"id\":\"a\",\"kind\":\"cart\",\"number\":2147483648},{\"id\":\"b\",\"kind\":\"cart\",\"number\":2}]")]
    [InlineData("[{\"Id\":\"a\",\"kind\":\"cart\",\"Number\":\"bad\"},{\"id\":\"b\",\"kind\":\"cart\",\"number\":2}]")]
    [InlineData("[{\"id\":\"a\",\"kind\":\"cart\",\"number\":99999999999999999999999},{\"id\":\"b\",\"kind\":\"cart\",\"number\":2}]")]
    [InlineData("[5,null,{\"id\":{\"x\":1},\"kind\":\"cart\",\"number\":1},{\"id\":\"b\",\"kind\":\"cart\",\"number\":2}]")]
    public void UnreadableNumbersNeverRejectTheSave(string numbers)
    {
        foreach (var spelling in new[] { "vehicleNumbers", "VehicleNumbers", "VEHICLENUMBERS" })
        {
            var spelled = new SaveCodec().Load("{\"schemaVersion\":2,\"" + spelling + "\":" + numbers + "}");
            Assert.Equal(LoadStatus.Loaded, spelled.Status);
            Assert.Equal("b", Assert.Single(spelled.State!.VehicleNumbers).Id);
        }
        var result = new SaveCodec().Load("{\"schemaVersion\":2,\"vehicleNumbers\":" + numbers + "}");
        Assert.Equal(LoadStatus.Loaded, result.Status);
        var only = Assert.Single(result.State!.VehicleNumbers);
        Assert.Equal("b", only.Id);
        Assert.Equal(LoadStatus.Loaded, new SaveCodec().Load("{\"schemaVersion\":2,\"vehicleNumbers\":{\"a\":1}}").Status);
        Assert.Equal(LoadStatus.Loaded, new SaveCodec().Load("{\"schemaVersion\":2,\"vehicleNumbers\":[{\"Number\":\"bad\"},{\"ID\":\"b\",\"Kind\":\"cart\",\"NUMBER\":2}]}").Status);
    }

    [Fact]
    public void TheNumberShowsOnlyWithMoreThanOneOfAKind()
    {
        Assert.Equal("Food cart", FleetNames.Name("Food cart", 2, 1));
        Assert.Equal("Food cart 2", FleetNames.Name("Food cart", 2, 3));
        Assert.Equal("Food cart", FleetNames.Name("Food cart", 0, 3));
    }
}
