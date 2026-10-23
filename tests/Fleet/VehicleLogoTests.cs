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
