using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Saves;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class CommissarySaveTests
{
    private static readonly CommissaryRules Rules = new();
    private static readonly SaveCodec Codec = new();

    [Fact]
    public void OldSavesReadAnEmptyCommissaryAndZeroLedgerExpenses()
    {
        var load = Codec.Load("{\"schemaVersion\":2,\"dailySales\":[{\"day\":3,\"byVehicle\":[{\"vehicleId\":\"cart\"}]}]}");
        Assert.Equal(LoadStatus.Loaded, load.Status);
        var state = load.State!;
        Assert.Empty(state.Commissary.Unlocked);
        Assert.Empty(state.Commissary.Spaces);
        Assert.Empty(state.Commissary.Orders);
        Assert.Empty(state.Commissary.Stock);
        Assert.Empty(state.Commissary.Fines);
        Assert.Empty(state.Commissary.Charges);
        Assert.Null(state.Commissary.RentPaidThroughDay);
        Assert.Null(state.Commissary.NightCheckedThroughDay);
        Assert.Equal(0, state.DailySales.Single().DeliveryFeesCents);
        Assert.Equal(0, state.DailySales.Single().ByVehicle.Single().CommissaryRentCents);
        Assert.Equal(0, state.DailySales.Single().ByVehicle.Single().OvernightFineCents);
    }

    [Fact]
    public void CompleteStateAndFailedPaymentsRoundTripAndDoNotRetriggerCharges()
    {
        var state = new SaveState { Commissary = CommissariesTests.Rented(3) };
        var rent = Assert.Single(Commissaries.RentDue(state.Commissary, Rules, 3));
        state.Commissary.Charges.Add(rent);
        var fine = Assert.Single(Commissaries.NightCheck(state.Commissary, Rules, 3, 240, new[] { new CommissaryVehicle { VehicleId = "cart" } }));
        state.Commissary.Fines.Add(fine);
        Commissaries.Order(state.Commissary, Rules, "a", "taco", 10, 7, (3, 500));
        Commissaries.Deliver(state.Commissary, Rules, 4, 480, CommissariesTests.Paid);
        var pending = Commissaries.Order(state.Commissary, Rules, "a", "griddle", 1, 5000, (4, 481));
        var loaded = Codec.Load(Codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var c = loaded.State!.Commissary;
        Assert.Equal(new[] { "a" }, c.Unlocked);
        Assert.Equal(3, c.Spaces.Single().SinceDay);
        Assert.Equal(3, c.Spaces.Single().RentPaidThroughDay);
        Assert.Equal(3, c.RentPaidThroughDay);
        Assert.Equal(3, c.NightCheckedThroughDay);
        Assert.Equal(pending.TotalCents, c.Orders.Single().TotalCents);
        Assert.Equal((10, 7L), (c.Stock["a"].Single().Units, c.Stock["a"].Single().UnitPriceCents));
        Assert.Equal(986, c.Charges.Single().Cents);
        Assert.Equal(25000, c.Fines.Single().Cents);
        Assert.Empty(Commissaries.RentDue(c, Rules, 3));
        Assert.Empty(Commissaries.NightCheck(c, Rules, 3, 1439, new[] { new CommissaryVehicle { VehicleId = "cart" } }));
        Assert.Equal(pending.Id + 1, Commissaries.Order(c, Rules, "a", "taco", 1, 1, (4, 500)).Id);
    }

    [Fact]
    public void AnOrderChargeFromBeforePayOnDeliveryIsSetAsideNotCollected()
    {
        var state = new SaveState { Commissary = CommissariesTests.Rented() };
        var order = Commissaries.Order(state.Commissary, Rules, "a", "taco", 10, 100, (1, 1));
        var rent = Assert.Single(Commissaries.RentDue(state.Commissary, Rules, 1));
        state.Commissary.Charges.Add(rent);
        var json = JObject.Parse(Codec.Serialize(state));
        ((JArray)json["commissary"]!["charges"]!).Add(JObject.Parse(
            "{\"vehicleId\":\"\",\"commissaryId\":\"a\",\"day\":1,\"cents\":1100,\"orderId\":1,\"feeCents\":100}"));
        json["commissary"]!["orders"]![0]!["awaitingPayment"] = true;
        var loaded = Codec.Load(json.ToString());
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var c = loaded.State!.Commissary;
        Assert.Equal(rent.VehicleId, Assert.Single(c.Charges).VehicleId);
        Assert.Single((JArray)c.ExtensionData["_discardedCharges"]);
        Assert.Equal(order.Id, Assert.Single(c.Orders).Id);
        Assert.Single(Commissaries.Deliver(c, Rules, 2, 480, CommissariesTests.Paid).Delivered);
    }

    [Fact]
    public void UnknownFieldsOnEverySavedClassSurviveLoadingAndInventoryOperations()
    {
        var json = JObject.Parse("""
        {"schemaVersion":2,"commissary":{
          "futureState":{"x":1},"unlocked":["a"],
          "spaces":[{"vehicleId":"v","kind":"cart","commissaryId":"a","sinceDay":1,"futureSpace":2}],
          "orders":[{"id":1,"commissaryId":"a","itemName":"future-food","units":2,"unitPriceCents":3,"feeCents":1,"orderedDay":1,"deliverDay":2,"futureOrder":3}],
          "stock":{"a":[{"itemName":"future-machine","units":2,"unitPriceCents":4,"futureLine":4}]},
          "fines":[{"vehicleId":"v","day":1,"cents":1,"futureFine":5}],
          "charges":[{"vehicleId":"v","commissaryId":"a","day":1,"cents":1,"futureCharge":6}]
        }}
        """);
        var loaded = Codec.Load(json.ToString());
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var saved = JObject.Parse(Codec.Serialize(loaded.State!))["commissary"]!;
        Assert.Equal(json["commissary"]!["futureState"], saved["futureState"]);
        Assert.Equal(2, (int)saved["spaces"]![0]!["futureSpace"]!);
        Assert.Equal(3, (int)saved["orders"]![0]!["futureOrder"]!);
        Assert.Equal(4, (int)saved["stock"]!["a"]![0]!["futureLine"]!);
        Assert.Equal(5, (int)saved["fines"]![0]!["futureFine"]!);
        Assert.Equal(6, (int)saved["charges"]![0]!["futureCharge"]!);
        var state = loaded.State!;
        Commissaries.Deliver(state.Commissary, Rules, 2, 480, CommissariesTests.Paid);
        var taken = Commissaries.Restock(state.Commissary, "a", new Dictionary<string, int> { ["future-machine"] = 1 });
        Assert.Equal(4, (int)taken.Single().ExtensionData["futureLine"]);
        Commissaries.ReturnToStock(state.Commissary, "a", taken);
        saved = JObject.Parse(Codec.Serialize(state))["commissary"]!;
        Assert.Equal(4, (int)saved["stock"]!["a"]![0]!["futureLine"]!);
        Assert.Equal(3, (int)saved["stock"]!["a"]![1]!["orderMetadata"]!["futureOrder"]!);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void ReleasedVehicleRentCursorSurvivesSavingAndDoesNotChargeTwiceOrChargeTheGap(int rentAgainDay)
    {
        var state = new SaveState { Commissary = CommissariesTests.Rented(5) };
        // Even if payment fails, its debt already exists and must not be generated twice.
        state.Commissary.Charges.Add(Assert.Single(Commissaries.RentDue(state.Commissary, Rules, 5)));
        Assert.True(Commissaries.Release(state.Commissary, "cart"));
        var loaded = Codec.Load(Codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var c = loaded.State!.Commissary;
        Assert.Equal(5, c.ReleasedRentPaidThroughDay["cart"]);
        Commissaries.Unlock(c, "b");
        Commissaries.Rent(c, Rules, "cart", "cart", "b", rentAgainDay);
        Assert.Empty(c.ReleasedRentPaidThroughDay);
        var due = Commissaries.RentDue(c, Rules, rentAgainDay);
        if (rentAgainDay == 5) Assert.Empty(due);
        else Assert.Equal(8, Assert.Single(due).Day);
        Assert.Single(c.Charges);
        Assert.Equal(rentAgainDay + 1, Assert.Single(Commissaries.RentDue(c, Rules, rentAgainDay + 1)).Day);

        // A different vehicle rented after the fleet run still owes its first day.
        Commissaries.Rent(c, Rules, "new-cart", "cart", "b", rentAgainDay + 1);
        Assert.Equal("new-cart", Assert.Single(Commissaries.RentDue(c, Rules, rentAgainDay + 1)).VehicleId);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(2)]
    public void InconsistentSpaceCursorResetsToRentalStartRatherThanFleetCursor(int cursor)
    {
        var loaded = Codec.Load("{\"schemaVersion\":2,\"commissary\":{\"rentPaidThroughDay\":8," +
            "\"spaces\":[{\"vehicleId\":\"cart\",\"kind\":\"cart\",\"commissaryId\":\"a\",\"sinceDay\":5,\"rentPaidThroughDay\":" + cursor + "}]}}");
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var c = loaded.State!.Commissary;
        Assert.Equal(4, Assert.Single(c.Spaces).RentPaidThroughDay);
        Assert.Equal(new[] { 5, 6, 7, 8 }, Commissaries.RentDue(c, Rules, 8).Select(charge => charge.Day));
        Assert.Empty(Commissaries.RentDue(c, Rules, 8));
        Assert.Equal(9, Assert.Single(Commissaries.RentDue(c, Rules, 9)).Day);
    }

    [Fact]
    public void UnknownOrderBatchIdsCannotOverrideStockBatchIdsAcrossDeliveryRestockAndSaving()
    {
        var loaded = Codec.Load("""
        {"schemaVersion":2,"commissary":{
          "orders":[
            {"id":1,"commissaryId":"a","itemName":"taco","units":2,"unitPriceCents":10,"orderedDay":1,"deliverDay":2,"batchId":99,"orderMetadata":{"future":1}},
            {"id":2,"commissaryId":"a","itemName":"taco","units":3,"unitPriceCents":20,"orderedDay":1,"deliverDay":2,"batchId":99}]
        }}
        """);
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var state = loaded.State!;
        Assert.Equal(2, Commissaries.Deliver(state.Commissary, Rules, 2, 480, CommissariesTests.Paid).Delivered.Count);
        var taken = Commissaries.Restock(state.Commissary, "a", new Dictionary<string, int> { ["taco"] = 1 });
        Assert.Equal(99, (int)taken.Single().OrderMetadata["batchId"]);
        Commissaries.ReturnToStock(state.Commissary, "a", taken);
        var saved = JObject.Parse(Codec.Serialize(state));
        var lines = (JArray)saved["commissary"]!["stock"]!["a"]!;
        Assert.Equal(new long[] { 1, 2 }, lines.Select(line => (long)line["batchId"]!));
        Assert.All(lines, line => Assert.Equal(99, (int)line["orderMetadata"]!["batchId"]!));
        Assert.Equal(1, (int)lines[0]!["orderMetadata"]!["orderMetadata"]!["future"]!);
        loaded = Codec.Load(saved.ToString());
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal(new[] { (2, 10L), (3, 20L) }, loaded.State!.Commissary.Stock["a"].Select(line => (line.Units, line.UnitPriceCents)));
        Assert.Equal(LoadStatus.Loaded, Codec.Load(Codec.Serialize(loaded.State)).Status);
    }

    [Fact]
    public void LoadingNormalisesKnownBadEntriesAndArchivesTheirUnknownMetadata()
    {
        var loaded = Codec.Load("""
        {"schemaVersion":2,"commissary":{
          "unlocked":["a","a",""],
          "spaces":[
            {"vehicleId":"v","kind":"cart","commissaryId":"a","sinceDay":1,"futureOldSpace":8},
            {"vehicleId":"v","kind":"future-vehicle","commissaryId":"future-garage","sinceDay":2}],
          "orders":[
            {"id":1,"itemName":"taco","units":1,"unitPriceCents":1,"orderedDay":1,"deliverDay":2,"futureOrphan":9},
            {"id":2,"commissaryId":"a","itemName":"taco","units":-1,"orderedDay":1,"deliverDay":2},
            {"id":3,"commissaryId":"future-garage","itemName":"future-food","units":1,"unitPriceCents":1,"orderedDay":1,"deliverDay":2}],
          "stock":{"future-garage":[
            {"itemName":"taco","units":-1,"futureBadLine":10},
            {"itemName":"future-machine","units":2,"unitPriceCents":4}]}
        }}
        """);
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        var c = loaded.State!.Commissary;
        Assert.Equal(new[] { "a" }, c.Unlocked);
        Assert.Equal("future-vehicle", c.Spaces.Single().Kind);
        Assert.Equal("future-garage", c.Spaces.Single().CommissaryId);
        Assert.Equal("future-food", c.Orders.Single().ItemName);
        Assert.Equal("future-machine", c.Stock["future-garage"].Single().ItemName);
        Assert.Equal(4, c.NextOrderId);
        var saved = JObject.Parse(Codec.Serialize(loaded.State))["commissary"]!;
        Assert.Equal(8, (int)saved["_discardedSpaces"]![0]!["futureOldSpace"]!);
        Assert.Equal(9, (int)saved["_discardedOrders"]![0]!["futureOrphan"]!);
        Assert.Equal(10, (int)saved["_discardedStock"]![0]!["futureBadLine"]!);
        Assert.Equal(LoadStatus.Loaded, Codec.Load(Codec.Serialize(loaded.State)).Status);
        Assert.Empty(Commissaries.RentDue(c, Rules, 5)); // future kind retained, no invented rent
    }

    [Fact]
    public void FifoBatchOrderAndRentalDaySurviveSavingBeforeFirstProcessing()
    {
        var state = new SaveState { Commissary = CommissariesTests.Rented(3) };
        Commissaries.ReturnToStock(state.Commissary, "a", new[] { CommissariesTests.Line("taco", 2, 10), CommissariesTests.Line("taco", 2, 20) });
        var taken = Commissaries.Restock(state.Commissary, "a", new Dictionary<string, int> { ["taco"] = 3 });
        var loaded = Codec.Load(Codec.Serialize(state)).State!;
        Assert.Equal(3, Assert.Single(Commissaries.RentDue(loaded.Commissary, Rules, 3)).Day);
        Commissaries.ReturnToStock(loaded.Commissary, "a", taken.Reverse());
        Assert.Equal(new long[] { 10, 20 }, loaded.Commissary.Stock["a"].Select(l => l.UnitPriceCents));
    }

    [Fact]
    public void NewerSaveSchemaIsRetainedWithoutNormalisingIt()
    {
        var json = "{\"schemaVersion\":3,\"commissary\":{\"future\":true}}";
        var loaded = Codec.Load(json);
        Assert.Equal(LoadStatus.NewerVersion, loaded.Status);
        Assert.Equal(json, loaded.RawJson);
        Assert.Null(loaded.State);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"fines\":[{\"vehicleId\":\"v\",\"day\":1,\"cents\":-1}]}")]
    [InlineData("{\"charges\":[{\"vehicleId\":\"v\",\"commissaryId\":\"a\",\"day\":1,\"cents\":-1}]}")]
    [InlineData("{\"nightCheckedThroughDay\":-1}")]
    public void InvalidPaymentObligationsAndCursorsAreCorrupt(string commissary) =>
        Assert.Equal(LoadStatus.Corrupt, Codec.Load("{\"schemaVersion\":2,\"commissary\":" + commissary + "}").Status);
}
