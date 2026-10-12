using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xunit;
using Xunit.Abstractions;

namespace FoodTrucks.Core.Tests.Soak;

public sealed class SoakTests
{
    private readonly ITestOutputHelper output;
    public SoakTests(ITestOutputHelper output) => this.output = output;

    public static IEnumerable<object[]> Seeds()
    {
        foreach (var seed in new[] { 0, 1, 2, 3, 7, 11, 19, 23, 42, 71, 101, 314, 997, 2026, 8191,
                     12345, 65537, 104729, 8675309, int.MaxValue }) yield return new object[] { seed };
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    [Trait("Category", "Soak")]
    public void SixtyDays(int seed)
    {
        var watch = Stopwatch.StartNew();
        var world = new SoakWorld(seed);
        world.Run(60);
        output.WriteLine($"seed={seed}: {watch.Elapsed.TotalSeconds:F3}s; {world.Coverage}");
    }

    [LongSoakFact]
    [Trait("Category", "SoakLong")]
    public void TwoHundredSeedsFor365Days()
    {
        var watch = Stopwatch.StartNew();
        for (var seed = 0; seed < 200; seed++) new SoakWorld(seed).Run(365);
        output.WriteLine($"200 seeds × 365 days: {watch.Elapsed.TotalSeconds:F3}s");
    }
}

public sealed class LongSoakFactAttribute : FactAttribute
{
    public LongSoakFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("FT_SOAK_LONG") != "1") Skip = "Set FT_SOAK_LONG=1 to run 200 seeds × 365 days.";
    }
}
