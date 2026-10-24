using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Fleet;

public class VehicleLogoTests
{
    [Fact]
    public void PerVehicleDesignsRoundTripAndOldVehiclesStayUnbranded()
    {
        var state = new SaveState();
        var logo = new VehicleLogo("Corner Kitchen", "burger", "Rubik", "FFFFFFFF", "FF8800FF", "000000FF");
        StreetSales.GetOrAdd(state, "truck-a").Logo = logo.Save();
        StreetSales.GetOrAdd(state, "truck-b");
        var codec = new SaveCodec();
        var loaded = codec.Load(codec.Serialize(state)).State!;
        Assert.Equal("Corner Kitchen", VehicleLogo.Read(loaded.Vehicles[0].Logo)!.Name);
        Assert.Null(loaded.Vehicles[1].Logo);
        Assert.Null(codec.Load("{\"schemaVersion\":2,\"vehicles\":[{\"vehicleId\":\"old\"}]}").State!.Vehicles[0].Logo);
    }

    [Fact]
    public void TwoDesignsRemainIndependentAcrossRepeatedReloadRemovalAndMissingIcon()
    {
        var state = new SaveState();
        StreetSales.GetOrAdd(state, "truck-a").Logo = new VehicleLogo("Alpha", "missing-custom-icon.png", "Rubik", "FFFFFFFF", "FF8800FF", "000000FF").Save();
        StreetSales.GetOrAdd(state, "truck-b").Logo = new VehicleLogo("Beta", "burger", "Rubik", "000000FF", "FFFFFFFF", "FF8800FF").Save();
        var expected = StreetSales.Find(state, "truck-b")!.Logo!.DeepClone();
        var codec = new SaveCodec();
        for (var i = 0; i < 50; i++)
        {
            state = codec.Load(codec.Serialize(state)).State!;
            Assert.Equal(2, state.Vehicles.Count);
            Assert.True(JToken.DeepEquals(expected, StreetSales.Find(state, "truck-b")!.Logo));
            var first = StreetSales.Find(state, "truck-a")!;
            Assert.Equal("missing-custom-icon.png", VehicleLogo.Read(first.Logo)!.Shape);
            first.Logo = null;
            var removed = codec.Load(codec.Serialize(state)).State!;
            Assert.Null(StreetSales.Find(removed, "truck-a")!.Logo);
            Assert.True(JToken.DeepEquals(expected, StreetSales.Find(removed, "truck-b")!.Logo));
            first.Logo = new VehicleLogo("Alpha", "missing-custom-icon.png", "Rubik", "FFFFFFFF", "FF8800FF", "000000FF").Save();
        }
    }

    [Theory]
    [InlineData("{\"version\":999,\"future\":true}")]
    [InlineData("{\"version\":1,\"name\":42}")]
    [InlineData("[1,2,3]")]
    public void UnknownCosmeticsStayIntactWithoutMakingTheSaveCorrupt(string json)
    {
        var state = new SaveState();
        StreetSales.GetOrAdd(state, "truck").Logo = JToken.Parse(json);
        var codec = new SaveCodec();
        var loaded = codec.Load(codec.Serialize(state));
        Assert.NotNull(loaded.State);
        var token = loaded.State!.Vehicles[0].Logo;
        Assert.True(JToken.DeepEquals(JToken.Parse(json), token));
        Assert.Null(VehicleLogo.Read(token));
        Assert.False(VehicleLogo.CanEdit(token));
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("..\\outside")]
    [InlineData("C:outside")]
    public void ShapeCannotEscapeNativeIconFolder(string shape) =>
        Assert.Throws<System.ArgumentException>(() => new VehicleLogo("Kitchen", shape, "Rubik", "FFFFFFFF", "FFFFFFFF", "FFFFFFFF"));

    [Theory]
    [InlineData("")]
    [InlineData("<size=500>Kitchen</size>")]
    [InlineData("Kitchen\nSecond line")]
    public void NameIsPlainBoundedText(string name) => Assert.False(VehicleLogo.ValidName(name));
}
