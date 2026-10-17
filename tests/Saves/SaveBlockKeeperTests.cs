using FoodTrucks.Core.Saves;
using Xunit;

namespace FoodTrucks.Core.Tests.Saves;

public class SaveBlockKeeperTests
{
    private const string FromFile = "{\"schemaVersion\":1,\"saveCount\":47}";
    private const string Newer = "{\"schemaVersion\":1,\"saveCount\":48}";

    [Fact]
    public void APresentBlockGoesOutUnchanged()
    {
        var keeper = new SaveBlockKeeper();
        var game = new object();
        keeper.Loaded(game, FromFile);
        Assert.Equal(SaveBlockOutcome.Present, keeper.BeforeSave(game, FromFile, out var restore));
        Assert.Null(restore);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void AMissingOrEmptyBlockGetsTheLoadedOneBack(string? current)
    {
        var keeper = new SaveBlockKeeper();
        var game = new object();
        keeper.Loaded(game, FromFile);
        Assert.Equal(SaveBlockOutcome.Restored, keeper.BeforeSave(game, current, out var restore));
        Assert.Equal(FromFile, restore);
    }

    [Fact]
    public void TheLastWrittenBlockIsTheOneKept()
    {
        var keeper = new SaveBlockKeeper();
        var game = new object();
        keeper.Loaded(game, FromFile);
        keeper.Written(game, Newer);
        Assert.Equal(SaveBlockOutcome.Restored, keeper.BeforeSave(game, null, out var restore));
        Assert.Equal(Newer, restore);
    }

    [Fact]
    public void AnEmptyWriteNeverReplacesARealBlock()
    {
        var keeper = new SaveBlockKeeper();
        var game = new object();
        keeper.Loaded(game, FromFile);
        keeper.Written(game, "");
        keeper.Written(game, null);
        Assert.Equal(FromFile, keeper.BlockFor(game));
    }

    [Fact]
    public void ABlockIsNeverCarriedIntoAnotherGame()
    {
        var keeper = new SaveBlockKeeper();
        var loaded = new object();
        var other = new object();
        keeper.Loaded(loaded, FromFile);
        Assert.Equal(SaveBlockOutcome.None, keeper.BeforeSave(other, null, out var restore));
        Assert.Null(restore);
        Assert.Null(keeper.BlockFor(other));
        Assert.Null(keeper.BlockFor(null));
    }

    [Fact]
    public void ASaveThatNeverHadTheModHasNothingToKeep()
    {
        var keeper = new SaveBlockKeeper();
        var game = new object();
        keeper.Loaded(game, null);
        Assert.Equal(SaveBlockOutcome.None, keeper.BeforeSave(game, null, out _));
    }

    [Fact]
    public void ALaterLoadReplacesTheKnownGame()
    {
        var keeper = new SaveBlockKeeper();
        var first = new object();
        var second = new object();
        keeper.Loaded(first, FromFile);
        keeper.Loaded(second, null);
        Assert.Null(keeper.BlockFor(first));
        Assert.Equal(SaveBlockOutcome.None, keeper.BeforeSave(second, null, out _));
    }

    [Fact]
    public void DescribeNamesTheSaveCountForTheLog()
    {
        Assert.Equal("none", SaveBlockKeeper.Describe(null));
        Assert.StartsWith("save count 47, written by (none)", SaveBlockKeeper.Describe(FromFile));
        Assert.StartsWith("unreadable", SaveBlockKeeper.Describe("{not json"));
    }
}
