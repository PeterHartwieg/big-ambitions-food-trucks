using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

public class StockTransferTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string HotDogGrill = "ba:itemname_hotdoggrill";
    private const string Grill = "ba:itemname_industrialgrill";
    private const string Burger = "ba:itemname_burger";
    private const string Kabob = "ba:itemname_kabob";
    private const string Storage = "foodtrucks:itemname_storagecabinet";
    private const string Coffee = "ba:itemname_cupofcoffee";
    private const string Machine = "ba:itemname_industrialcoffeemachine";

    private static DemandTables Tables => ShippedData.Tables;
    private static CargoLine L(string item, int amount) => new(item, amount, false);
    private static Dictionary<string, int> D(params (string Item, int Units)[] pairs) => pairs.ToDictionary(p => p.Item, p => p.Units);

    [Fact]
    public void TheListOffersTakesForStockHereAndReturnsForWhatIsOnBoard()
    {
        var cargo = new[] { L(HotDogGrill, 1), L(HotDog, 30) };

        var choices = StockTransfer.Choices(Tables, "truck", cargo, D((HotDog, 1200), (Burger, 500), (Grill, 2), ("ba:itemname_chair", 4)),
            D((HotDog, 30), (HotDogGrill, 1), ("ba:itemname_chair", 1)));

        Assert.Equal(new[]
        {
            (TransferKind.Take, Burger), (TransferKind.Take, "ba:itemname_chair"), (TransferKind.Take, HotDog), (TransferKind.Take, Grill),
            (TransferKind.Return, HotDog), (TransferKind.Return, HotDogGrill)
        }, choices.Select(c => (c.Kind, c.Item)));
        var hotDog = choices.Single(c => c.Kind == TransferKind.Take && c.Item == HotDog);
        Assert.Equal(1200, hotDog.Units);
        Assert.Equal(50, hotDog.Most);
        Assert.Equal(LoadRefusal.PieceFull, hotDog.Refusal);
        var burger = choices.Single(c => c.Item == Burger);
        Assert.Equal(0, burger.Most);
        Assert.Equal(LoadRefusal.NoPiece, burger.Refusal);
        var grill = choices.Single(c => c.Item == Grill);
        Assert.True(grill.IsPiece);
        Assert.Equal(2, grill.Units);
        Assert.Equal(1, grill.Most);
        Assert.Equal(30, choices.Single(c => c.Kind == TransferKind.Return && c.Item == HotDog).Most);
    }

    [Fact]
    public void ATakeLoadsOnlyWhatTheSlotsAndPiecesAllow()
    {
        var cart = new[] { L(HotDogGrill, 1), L(Storage, 1) };
        Assert.Equal(LoadRefusal.EquipmentSlots, StockTransfer.Take(Tables, "cart", cart, Grill, 1).Refusal);
        Assert.Equal(LoadRefusal.SecondStorage, StockTransfer.Take(Tables, "truck", cart, Storage, 1).Refusal);
        var check = StockTransfer.Take(Tables, "cart", cart, HotDog, 100);
        Assert.Equal(80, check.Allowed);
        Assert.Equal(LoadRefusal.PieceFull, check.Refusal);
        Assert.Equal(LoadRefusal.None, StockTransfer.Take(Tables, "cart", cart, HotDog, 20).Refusal);
    }

    [Fact]
    public void FillUpLoadsFoodButNeverEquipment()
    {
        var plan = StockTransfer.FillUp(Tables, "truck", new[] { L(HotDogGrill, 1) }, D((HotDog, 500), (Grill, 1), (Burger, 500)));

        Assert.Equal(D((HotDog, 80)), plan.Load);
        Assert.Equal(new[] { Burger }, plan.NotHeld);
    }

    [Fact]
    public void APieceLeavingWithNoOtherOfItsKindSendsAllItsFoodBack()
    {
        var cargo = new[] { L(Grill, 1), L(Burger, 30), L(Kabob, 20), L(HotDogGrill, 1), L(HotDog, 50) };

        Assert.Equal(D((Burger, 30), (Kabob, 20)), StockTransfer.FoodLeavingWith(Tables, "truck", cargo, Grill));
    }

    [Fact]
    public void OneOfTwoPiecesLeavingSendsBackOnlyTheExcessFromTheFullestFood()
    {
        var cargo = new[] { L(Grill, 2), L(Burger, 80), L(Kabob, 40) };

        var back = StockTransfer.FoodLeavingWith(Tables, "truck", cargo, Grill);

        Assert.Equal(40, back.Values.Sum());
        Assert.Equal(D((Burger, 40)), back);
        Assert.Empty(StockTransfer.FoodLeavingWith(Tables, "truck", new[] { L(Grill, 2), L(Burger, 60) }, Grill));
    }

    [Fact]
    public void ExtraStorageLeavingHalvesEveryPiece()
    {
        var cargo = new[] { L(Storage, 1), L(HotDogGrill, 1), L(HotDog, 150), L(Machine, 1), L(Coffee, 400) };

        var back = StockTransfer.FoodLeavingWith(Tables, "truck", cargo, Storage);

        Assert.Equal(D((HotDog, 70), (Coffee, 80)), back);
    }

    [Fact]
    public void TheLastPieceLeavingTakesAllItsFoodEvenOverItsRoom()
    {
        var cargo = new[] { L(HotDogGrill, 1), L(HotDog, 100), L(Grill, 1), L(Burger, 90) };

        Assert.Equal(D((HotDog, 100)), StockTransfer.FoodLeavingWith(Tables, "cart", cargo, HotDogGrill));
        Assert.Empty(StockTransfer.FoodLeavingWith(Tables, "cart", cargo, HotDog));
    }
}
