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
    public void ZeroRevenueCreatesNoEntry()
    {
        var state = new SaveState();

        SalesLedger.Record(state, 1, 0);

        Assert.Empty(state.DailySales);
        SalesLedger.Record(state, 2, long.MaxValue);
        SalesLedger.Record(state, 2, 0);
        SalesLedger.Record(state, 3, 0);
        Assert.Single(state.DailySales);
        Assert.Equal(long.MaxValue, state.DailySales[0].RevenueCents);
    }

    [Fact]
    public void RecordOverflowThrowsWithoutChangingRevenue()
    {
        var state = new SaveState();
        SalesLedger.Record(state, 1, long.MaxValue);

        Assert.Throws<OverflowException>(() => SalesLedger.Record(state, 1, 1));

        Assert.Single(state.DailySales);
        Assert.Equal(long.MaxValue, state.DailySales[0].RevenueCents);
    }

    [Fact]
    public void SumOverflowThrows()
    {
        var state = new SaveState();
        SalesLedger.Record(state, 1, long.MaxValue);
        SalesLedger.Record(state, 2, 1);

        Assert.Equal(long.MaxValue, SalesLedger.SumCents(state, 1, 1));
        Assert.Throws<OverflowException>(() => SalesLedger.SumCents(state, 1, 2));
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
