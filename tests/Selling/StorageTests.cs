using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Selling;
using FoodTrucks.Core.Tests.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Selling;

// Storage per piece (Peter, 10 October 2026): each fitted piece holds unitsPerPiece of its own foods,
// a storage piece doubles every piece, and Restock splits a shared piece evenly.
public class StorageTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string HotDogGrill = "ba:itemname_hotdoggrill";
    private const string Grill = "ba:itemname_industrialgrill";
    private const string Burger = "ba:itemname_burger";
    private const string Kabob = "ba:itemname_kabob";
    private const string Concessions = "ba:itemname_concessionsdisplaycase";
    private const string Popcorn = "ba:itemname_popcorn";
    private const string CottonCandy = "ba:itemname_cottoncandy";
    private const string Showcase = "ba:itemname_bakeryshowcase";
    private const string Croissant = "ba:itemname_croissant";
    private const string Bread = "bakeryxx:itemname_bread";
    private const string Storage = "foodtrucks:itemname_storagecabinet";
    private const string Fryer = "ba:itemname_industrialfryermachine";

    private static DemandTables Tables => ShippedData.Tables;
    private static CargoLine L(string item, int amount, bool nested = false) => new(item, amount, nested);

    [Theory]
    [InlineData("cart", false, 40)]
    [InlineData("cart", true, 80)]
    [InlineData("truck", false, 80)]
    [InlineData("truck", true, 160)]
    public void EachPieceHoldsItsOwnFoodAndTheStoragePieceDoublesIt(string vehicle, bool storage, int capacity)
    {
        var cargo = new List<CargoLine> { L(HotDogGrill, 1), L(Grill, 1) };
        if (storage) cargo.Add(L(Storage, 1));

        var view = Core.Selling.Storage.Of(Tables, vehicle, cargo)!;

        Assert.Equal(new[] { "hotdoggrill", "grill" }, view.Pools.Select(p => p.EquipmentId));
        Assert.All(view.Pools, p => Assert.Equal(capacity, p.Capacity));
        Assert.Equal(capacity, view.PerPiece);
        Assert.Equal(capacity, CargoRules.AllowedUnits(Tables, vehicle, cargo, HotDog, 500, false));
        Assert.Equal(capacity, CargoRules.AllowedUnits(Tables, vehicle, cargo.Append(L(HotDog, capacity)), Burger, 500, false));
    }

    [Fact]
    public void TwoPiecesOfOneKindHoldTwiceAsMuch()
    {
        var view = Core.Selling.Storage.Of(Tables, "truck", new[] { L(HotDogGrill, 2), L(HotDog, 100) })!;

        var pool = Assert.Single(view.Pools);
        Assert.Equal(2, pool.Pieces);
        Assert.Equal(160, pool.Capacity);
        Assert.Equal(60, pool.Room);
    }

    [Fact]
    public void ASecondStoragePieceAddsNothingAndIsRefused()
    {
        var one = new[] { L(HotDogGrill, 1), L(Storage, 1) };
        var check = Core.Selling.Storage.Check(Tables, "truck", one, Storage, 1, false);
        Assert.Equal(0, check.Allowed);
        Assert.Equal(LoadRefusal.SecondStorage, check.Refusal);

        var stack = Core.Selling.Storage.Check(Tables, "truck", new[] { L(HotDogGrill, 1) }, Storage, 2, false);
        Assert.Equal(1, stack.Allowed);
        Assert.Equal(LoadRefusal.SecondStorage, stack.Refusal);

        // A save that somehow holds two still counts once.
        var two = Core.Selling.Storage.Of(Tables, "truck", new[] { L(HotDogGrill, 1), L(Storage, 2) })!;
        Assert.Equal(2, two.StoragePieces);
        Assert.Equal(160, Assert.Single(two.Pools).Capacity);
    }

    [Fact]
    public void TheStoragePieceTakesASlotAndServesNoFood()
    {
        var cargo = new[] { L(HotDogGrill, 1), L(Storage, 1) };

        var check = Core.Selling.Storage.Check(Tables, "cart", cargo, Grill, 1, false);
        Assert.Equal(0, check.Allowed);
        Assert.Equal(LoadRefusal.EquipmentSlots, check.Refusal);
        Assert.Equal(new[] { HotDog }, Menu.FromCargo(Tables, "cart", cargo).Menu.Items);
        Assert.True(Tables.EquipmentForGameItem(Storage)!.IsStorage);
        Assert.Empty(Tables.Equipment("storage")!.Products);
    }

    [Fact]
    public void FoodWithoutItsPieceIsRefusedWithTheReason()
    {
        var check = Core.Selling.Storage.Check(Tables, "cart", new[] { L(HotDogGrill, 1) }, Burger, 80, false);

        Assert.Equal(0, check.Allowed);
        Assert.Equal(LoadRefusal.NoPiece, check.Refusal);
        Assert.Equal("grill", check.EquipmentId);
    }

    [Fact]
    public void AFullPieceNamesItselfAndItsCapacity()
    {
        var check = Core.Selling.Storage.Check(Tables, "cart", new[] { L(HotDogGrill, 1), L(HotDog, 30) }, HotDog, 500, false);

        Assert.Equal(10, check.Allowed);
        Assert.Equal(LoadRefusal.PieceFull, check.Refusal);
        Assert.Equal("hotdoggrill", check.EquipmentId);
        Assert.Equal(40, check.Capacity);
    }

    [Fact]
    public void StockAboveTheLimitStaysAndOnlyBlocksNewLoads()
    {
        // An old save with 80 hot dogs on a cart, or a storage piece taken out.
        var cargo = new[] { L(HotDogGrill, 1), L(HotDog, 80) };
        var view = Core.Selling.Storage.Of(Tables, "cart", cargo)!;

        Assert.Equal(80, view.Pools[0].Load);
        Assert.Equal(0, view.Pools[0].Room);
        Assert.Equal(0, CargoRules.AllowedUnits(Tables, "cart", cargo, HotDog, 1, false));
        Assert.Empty(Core.Selling.Storage.Restock(Tables, "cart", cargo, new Dictionary<string, int> { [HotDog] = 500 }).Load);
        Assert.Equal(80, CargoRules.FoodUnits(Tables, cargo));
    }

    [Fact]
    public void FoodWhosePieceWasRemovedIsUnheldButKept()
    {
        var view = Core.Selling.Storage.Of(Tables, "cart", new[] { L(Grill, 1), L(HotDog, 25), L(Burger, 5) })!;

        Assert.Equal(new Dictionary<string, int> { [HotDog] = 25 }, view.Unheld);
        Assert.Equal(5, view.Pools[0].Units[Burger]);
    }

    [Fact]
    public void RestockFillsEachPieceAndLeavesFoodsWithoutOne()
    {
        var cargo = new[] { L(HotDogGrill, 1), L(HotDog, 12) };
        var plan = Core.Selling.Storage.Restock(Tables, "cart", cargo,
            new Dictionary<string, int> { [HotDog] = 500, [Burger] = 80, [Fryer] = 1 });

        Assert.Equal(new Dictionary<string, int> { [HotDog] = 28 }, plan.Load);
        Assert.Equal(new[] { Burger }, plan.NotHeld);
    }

    [Fact]
    public void RestockSplitsASharedPieceEvenly()
    {
        var plan = Core.Selling.Storage.Restock(Tables, "cart", new[] { L(Grill, 1) },
            new Dictionary<string, int> { [Burger] = 80, [Kabob] = 80 });

        Assert.Equal(20, plan.Load[Burger]);
        Assert.Equal(20, plan.Load[Kabob]);
    }

    [Fact]
    public void RestockTopsUpFromTheOtherFoodWhenOneRunsShort()
    {
        var plan = Core.Selling.Storage.Restock(Tables, "truck", new[] { L(Grill, 1) },
            new Dictionary<string, int> { [Burger] = 5, [Kabob] = 500 });

        Assert.Equal(5, plan.Load[Burger]);
        Assert.Equal(75, plan.Load[Kabob]);
    }

    [Fact]
    public void RestockLevelsWhatIsAlreadyLoaded()
    {
        var cargo = new[] { L(Grill, 1), L(Burger, 30), L(Kabob, 2) };
        var plan = Core.Selling.Storage.Restock(Tables, "truck", cargo,
            new Dictionary<string, int> { [Burger] = 500, [Kabob] = 500 });

        // 80 room in all, 48 free: kabobs come up to the burgers first, then both share the rest.
        Assert.Equal(40, 30 + plan.Load[Burger]);
        Assert.Equal(40, 2 + plan.Load[Kabob]);
    }

    [Fact]
    public void RestockSplitsByStockWeight()
    {
        // A cart's display case: 40 units, half for croissants (0.5 each) and half for bread (1.0 each).
        var plan = Core.Selling.Storage.Restock(Tables, "cart", new[] { L(Showcase, 1) },
            new Dictionary<string, int> { [Croissant] = 500, [Bread] = 500 });

        Assert.Equal(40, plan.Load[Croissant]);
        Assert.Equal(20, plan.Load[Bread]);

        var odd = Core.Selling.Storage.Restock(Tables, "cart", new[] { L(Concessions, 1), L(Popcorn, 1) },
            new Dictionary<string, int> { [Popcorn] = 500, [CottonCandy] = 500 });
        Assert.Equal(160, 1 + odd.Load[Popcorn] + odd.Load[CottonCandy]);
        Assert.InRange(odd.Load[CottonCandy] - (1 + odd.Load[Popcorn]), 0, 1);
    }

    [Fact]
    public void RestockNeverOverfillsAndMatchesTheLoadCheck()
    {
        var random = new Random(7);
        var foods = new[] { Burger, Kabob, Popcorn, CottonCandy, Croissant, Bread, HotDog };
        for (var round = 0; round < 300; round++)
        {
            var cargo = new List<CargoLine> { L(Grill, 1), L(Concessions, 1), L(Showcase, 1) };
            if (random.Next(2) == 0) cargo.Add(L(Storage, 1));
            foreach (var food in foods) cargo.Add(L(food, random.Next(0, 60)));
            var available = foods.ToDictionary(f => f, _ => random.Next(0, 200));
            var vehicle = random.Next(2) == 0 ? "cart" : "truck";

            var plan = Core.Selling.Storage.Restock(Tables, vehicle, cargo, available);

            foreach (var pair in plan.Load)
            {
                Assert.InRange(pair.Value, 1, available[pair.Key]);
                Assert.Equal(pair.Value, CargoRules.AllowedUnits(Tables, vehicle, cargo, pair.Key, pair.Value, false));
                cargo.Add(L(pair.Key, pair.Value));
            }
            var after = Core.Selling.Storage.Of(Tables, vehicle, cargo)!;
            var before = Core.Selling.Storage.Of(Tables, vehicle, cargo.Take(cargo.Count - plan.Load.Count))!;
            for (var i = 0; i < after.Pools.Count; i++)
            {
                Assert.True(after.Pools[i].Load <= Math.Max(after.Pools[i].Capacity, before.Pools[i].Load));
                // Every food still on offer would no longer fit (or the pool was already over its room).
                var pool = after.Pools[i];
                var stillFits = pool.Piece.Products.Where(p => available.TryGetValue(p, out var n) && (plan.Load.TryGetValue(p, out var t) ? t : 0) < n)
                    .Any(p => pool.Load + Tables.Product(p)!.StockWeight <= pool.Capacity);
                Assert.False(stillFits && before.Pools[i].Load < before.Pools[i].Capacity);
            }
            Assert.Equal(available[HotDog] > 0, plan.NotHeld.Contains(HotDog));
        }
    }

    [Fact]
    public void LargestCategoryCapacityAddsThePiecesOfOneCategory()
    {
        // Hot dogs and burgers are both fast food: two pieces' room.
        Assert.Equal(160, Core.Selling.Storage.LargestCategoryCapacity(Tables, "truck", new[] { L(HotDogGrill, 1), L(Grill, 1) }));
        Assert.Equal(320, Core.Selling.Storage.LargestCategoryCapacity(Tables, "truck", new[] { L(HotDogGrill, 1), L(Grill, 1), L(Storage, 1) }));
        Assert.Equal(0, Core.Selling.Storage.LargestCategoryCapacity(Tables, "truck", new[] { L(Storage, 1) }));
        Assert.Equal(0, Core.Selling.Storage.LargestCategoryCapacity(Tables, "boat", new[] { L(Grill, 1) }));
    }

    [Theory]
    [InlineData("equipment.storage.products", "[\"ba:itemname_hotdog\"]", "must be empty")]
    [InlineData("equipment.storage.storageFactor", "1", "above 1")]
    [InlineData("equipment.cooler.products", "[]", "at least one product")]
    [InlineData("equipment.cooler.products", "[\"ba:itemname_hotdog\"]", "already serves")]
    [InlineData("vehicles.cart.unitsPerPiece", "0", "unitsPerPiece")]
    public void TheLoaderRejectsBadStorageData(string path, string json, string message)
    {
        var economy = JObject.Parse(ShippedData.Economy);
        economy.SelectToken(path)!.Replace(JToken.Parse(json));

        var error = Assert.ThrowsAny<Exception>(() => DemandTables.Parse(ShippedData.Demand, economy.ToString(), ShippedData.Zones));
        Assert.Contains(message, error.Message);
    }
}
