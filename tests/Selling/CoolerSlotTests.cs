using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Fleet;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class CoolerSlotTests
{
    private static readonly DemandTables Tables = ShippedData.Tables;
    private const string Small = "ba:itemname_drinksfridge", Large = "ba:itemname_largedrinksfridge";
    private static CargoLine Line(string item, int amount = 1, bool nested = false) => new(item, amount, nested);

    [Theory]
    [InlineData("cart", 2, Small, false)]
    [InlineData("cart", 2, Large, true)]
    [InlineData("truck", 4, Small, true)]
    [InlineData("truck", 4, Large, false)]
    public void FullKitchenAcceptsOneFridgeAndItsMenuRegardlessOfCargoOrder(string kind, int slots, string fridge, bool nested)
    {
        var kitchen = new[] { Line("ba:itemname_hotdoggrill", slots) };
        Assert.Equal(1, Storage.Check(Tables, kind, kitchen, fridge, 2, nested).Allowed);
        var cargo = kitchen.Append(Line(fridge, nested: nested)).ToArray();
        foreach (var lines in new[] { cargo, cargo.Reverse().ToArray() })
        {
            var menu = Menu.FromCargo(Tables, kind, lines);
            Assert.Equal(MenuProblem.None, menu.Problem);
            Assert.Contains("ba:itemname_sodacan", menu.Menu.Items);
            Assert.Equal(0, Storage.Check(Tables, kind, lines, fridge, 1, nested).Allowed);
            Assert.Equal(0, Storage.Check(Tables, kind, lines, "ba:itemname_slushimachine", 1, false).Allowed);
        }
    }

    [Fact]
    public void ExtraFridgesStillUseGeneralSlotsIncludingAStack()
    {
        Assert.Equal(3, Storage.Check(Tables, "cart", new CargoLine[0], Small, 9, true).Allowed);
        var cargo = new[] { Line(Small), Line(Large), Line("ba:itemname_hotdoggrill") };
        Assert.Equal(MenuProblem.None, Menu.FromCargo(Tables, "cart", cargo).Problem);
        Assert.Equal(160, Storage.Check(Tables, "cart", cargo, "ba:itemname_sodacan", 999, false).Allowed);
        Assert.Equal(MenuProblem.TooMuchEquipment,
            Menu.FromCargo(Tables, "cart", cargo.Append(Line("ba:itemname_industrialcoffeemachine"))).Problem);
    }

    [Theory]
    [InlineData("cart", 80)]
    [InlineData("truck", 160)]
    public void CoolerCapacityAndStorageMultiplierAreUnchanged(string kind, int cans)
    {
        var cargo = new[] { Line(Small), Line("ba:itemname_hotdoggrill") };
        Assert.Equal(cans, Storage.Check(Tables, kind, cargo, "ba:itemname_sodacan", 999, false).Allowed);
        var withStorage = cargo.Append(Line("foodtrucks:itemname_storagecabinet")).ToArray();
        Assert.Equal(MenuProblem.None, Menu.FromCargo(Tables, kind, withStorage).Problem);
        Assert.Equal(cans * 2, Storage.Check(Tables, kind, withStorage, "ba:itemname_sodacan", 999, false).Allowed);
        Assert.Equal(LoadRefusal.NoPiece, Storage.Check(Tables, kind, cargo.Skip(1), "ba:itemname_sodacan", 1, false).Refusal);
    }

    [Fact]
    public void ReservedPlacementWinsAndOtherPiecesKeepTheirPlaces()
    {
        var placed = EquipmentLayout.Place(new[] { "cooler", "hotdoggrill", "storage" }, 2, 1,
            p => p, new[] { "cooler", "hotdoggrill" }).ToDictionary(p => p.Slot, p => p.Piece);
        Assert.Equal("cooler", placed[2]);
        Assert.Equal("hotdoggrill", placed[1]);
        Assert.Equal("storage", placed[0]);
        var duplicates = EquipmentLayout.Place(new[] { "cooler", "cooler", "storage" }, 2, 1, p => p);
        Assert.Equal(3, duplicates.Count);
        Assert.Equal(2, duplicates.Count(p => p.Piece == "cooler"));
    }

    [Fact]
    public void LegacyDataWithoutReservationRetainsItsSlotLimit()
    {
        var economy = JObject.Parse(ShippedData.Economy);
        ((JObject)economy["vehicles"]!["cart"]!).Remove("coolerSlots");
        var tables = DemandTables.Parse(ShippedData.Demand, economy.ToString(), ShippedData.Zones);
        Assert.Equal(0, tables.Vehicle("cart")!.CoolerSlots);
        Assert.Equal(MenuProblem.TooMuchEquipment,
            Menu.FromEquipment(tables, "cart", new[] { "hotdoggrill", "cooler", "storage" }).Problem);
    }

    [Theory]
    [InlineData("cart", 2)]
    [InlineData("truck", 4)]
    public void ShippedDrawingReservesCoolerAndNeverUsesItForCooking(string kind, int slots)
    {
        var json = System.IO.File.ReadAllText(System.IO.Path.Combine(System.AppContext.BaseDirectory, "data", "vehicle-diagrams.json"));
        var diagram = VehicleDiagrams.Parse(json).For(kind)!;
        Assert.Equal(slots + 1, diagram.Places.Count);
        var pieces = Enumerable.Repeat("hotdoggrill", slots).Prepend("cooler").ToArray();
        var filled = VehicleDiagrams.Fill(diagram, pieces, p => p);
        Assert.Equal("cooler", filled.Single(p => p.Place.Name == EquipmentLayout.CoolerPlace).Piece);
        Assert.Equal(slots, filled.Count(p => p.Piece == "hotdoggrill"));
        Assert.DoesNotContain(VehicleDiagrams.Fill(diagram, pieces.Skip(1), p => p),
            p => p.Place.Name == EquipmentLayout.CoolerPlace);
        var cooler = diagram.Places.Single(p => p.Name == EquipmentLayout.CoolerPlace);
        Assert.NotNull(cooler.LayerFor(Small));
        Assert.NotNull(cooler.LayerFor(Large));
        Assert.Null(cooler.LayerFor("ba:itemname_hotdoggrill"));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void InvalidReservationIsRejected(int slots)
    {
        var economy = JObject.Parse(ShippedData.Economy);
        economy["vehicles"]!["cart"]!["coolerSlots"] = slots;
        Assert.Throws<DemandDataException>(() => DemandTables.Parse(ShippedData.Demand, economy.ToString(), ShippedData.Zones));
    }
}
