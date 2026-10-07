using System;
using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class SalesLedgerTests
{
    [Fact]
    public void RecordAddsToTheSameDay()
    {
        var state = new SaveState();
        SalesLedger.Record(state, 5, 1000);
        SalesLedger.Record(state, 5, 250);
        SalesLedger.Record(state, 6, 99);

        Assert.Equal(2, state.DailySales.Count);
        Assert.Equal(1250, SalesLedger.SumCents(state, 5, 5));
    }

    [Fact]
    public void SumIsInclusiveAndIgnoresOtherDays()
    {
        var state = new SaveState();
        for (var day = 1; day <= 10; day++) SalesLedger.Record(state, day, 100);

        Assert.Equal(300, SalesLedger.SumCents(state, 3, 5));
        Assert.Equal(0, SalesLedger.SumCents(state, 11, 20));
        Assert.Equal(0, SalesLedger.SumCents(null, 1, 10));
    }

    [Fact]
    public void NegativeRevenueIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SalesLedger.Record(new SaveState(), 1, -1));
    }

    [Fact]
    public void LedgerSurvivesARoundTrip()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        SalesLedger.Record(state, 42, 123456);

        var loaded = codec.Load(codec.Serialize(state)).State!;

        Assert.Equal(123456, SalesLedger.SumCents(loaded, 42, 42));
    }
}
