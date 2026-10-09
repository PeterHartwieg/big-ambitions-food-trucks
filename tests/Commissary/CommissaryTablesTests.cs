using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FoodTrucks.Core.Commissary;
using FoodTrucks.Core.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Commissary;

public class CommissaryTablesTests
{
    private static string Json => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "commissaries.json"));

    internal static CommissaryTables Shipped => CommissaryTables.Parse(Json);

    [Fact]
    public void ShippedFileHasFourToSixGaragesInDifferentNeighbourhoods()
    {
        var tables = Shipped;
        Assert.InRange(tables.Sites.Count, 4, 6);
        Assert.Equal(tables.Sites.Count, tables.Sites.Select(s => s.Address).Distinct().Count());
        Assert.All(tables.Sites, s => Assert.StartsWith("foodtrucks:commissary_", s.Name));
        Assert.All(tables.Sites, s => Assert.Null(s.Bay));
    }

    [Theory]
    [InlineData("\"id\": \"industry-city\"", "\"id\": \"Industry City\"", "lower-case letters")]
    [InlineData("\"id\": \"garment-district\"", "\"id\": \"industry-city\"", "must be unique")]
    [InlineData("\"address\": \"ba:street_secondstreet#51\"", "\"address\": \"ba:street_secondstreet\"", "street#number")]
    [InlineData("\"address\": \"ba:street_secondstreet#51\"", "\"address\": \"ba:street_twentysecondstreet#6\"", "must be unique")]
    [InlineData("\"schemaVersion\": 1", "\"schemaVersion\": 2", "reads schema 1")]
    [InlineData("\"address\": \"ba:street_fifthstreet#12\"", "\"address\": \"ba:street_fifthstreet#12\", \"bay\": { \"x\": 1, \"y\": 2 }", "not a known field")]
    public void BadEntriesFailWithTheirPath(string from, string to, string message)
    {
        Assert.Contains(from, Json);
        var error = Assert.Throws<DemandDataException>(() => CommissaryTables.Parse(Json.Replace(from, to)));
        Assert.Contains(message, error.Message);
        Assert.Contains("commissaries.json", error.Message);
    }

    [Fact]
    public void ABayPointIsRead()
    {
        var tables = CommissaryTables.Parse(Json.Replace("\"address\": \"ba:street_fifthstreet#12\"",
            "\"address\": \"ba:street_fifthstreet#12\", \"bay\": { \"x\": 1225.5, \"z\": -922 }"));
        Assert.Equal((1225.5, -922.0), tables.Site("murray-hill")!.Bay);
    }

    [Fact]
    public void AtPicksTheNearestBayWithinTheRadius()
    {
        var tables = Shipped;
        var bays = new Dictionary<string, (double, double)[]>
        {
            ["industry-city"] = new[] { (0.0, 0.0) },
            ["garment-district"] = new[] { (20.0, 0.0), (10.0, 0.0) },
        };
        IEnumerable<(double X, double Z)>? Bays(CommissarySite s) => bays.TryGetValue(s.Id, out var b) ? b : null;
        Assert.Equal("industry-city", tables.At(Bays, 4, 0, 15)!.Id);
        Assert.Equal("garment-district", tables.At(Bays, 7, 0, 15)!.Id);
        Assert.Null(tables.At(Bays, 40, 0, 15));
        Assert.Equal(double.MaxValue, CommissaryTables.Distance(null, 0, 0));
    }
}
