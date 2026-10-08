using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class AreaClassifierTests
{
    private static readonly ZonePoint[] Square =
    {
        new(0, 0), new(10, 0), new(10, 10), new(0, 10)
    };

    [Fact]
    public void OfficeBlockGivesOffice()
    {
        var mix = new AreaClassifier(ShippedData.Tables).Classify(new[]
        {
            Place("office", 2), Place("office", 10), Place("office", 20)
        }, 5, 5);

        Assert.Single(mix.Shares);
        Assert.Equal(Presence(2.68), mix.PresenceOf("office"), 12);
    }

    [Fact]
    public void BusinessTypeBeatsBuildingType()
    {
        var mix = new AreaClassifier(ShippedData.Tables).FromPlaces(new[]
        {
            new NearbyPlace("ba:buildingtype_retail", "ba:businesstype_nightclub", 5)
        });

        Assert.Single(mix.Shares);
        Assert.InRange(mix.PresenceOf("nightlife"), double.Epsilon, 1);
        Assert.Equal(0, mix.PresenceOf("retail"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("ba:businesstype_empty")]
    [InlineData("ba:businesstype_unknown")]
    public void MissingBusinessRuleFallsThroughToBuilding(string? businessType)
    {
        var mix = new AreaClassifier(ShippedData.Tables).FromPlaces(new[]
        {
            new NearbyPlace("ba:buildingtype_office", businessType, 0)
        });

        Assert.Equal(Presence(1), mix.PresenceOf("office"), 12);
    }

    [Theory]
    [InlineData(50.001)]
    [InlineData(-1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void InvalidOrDistantPlacesAreIgnored(double distance)
    {
        var mix = new AreaClassifier(ShippedData.Tables).FromPlaces(new[]
        {
            Place("office", 0), Place("retail", distance)
        });

        Assert.Single(mix.Shares);
        Assert.Equal(Presence(1), mix.PresenceOf("office"), 12);
    }

    [Fact]
    public void NothingNearbyUsesFallback()
    {
        var classifier = new AreaClassifier(ShippedData.Tables);
        var empty = classifier.FromPlaces(Array.Empty<NearbyPlace>());
        var ignored = classifier.FromPlaces(new[] { null!, Place("unknown", 0), Place("office", 51) });

        Assert.Single(empty.Shares);
        Assert.Single(ignored.Shares);
        Assert.Equal(1.0, empty.ShareOf(ShippedData.Tables.Classifier.FallbackArea));
        Assert.Equal(1.0, ignored.ShareOf(ShippedData.Tables.Classifier.FallbackArea));
    }

    [Fact]
    public void KeepsAllIndependentAreasDespiteMaxAreas()
    {
        var mix = new AreaClassifier(ShippedData.Tables).FromPlaces(new[]
        {
            Place("office", 0), Place("office", 0), Place("office", 0),
            Place("retail", 0), Place("retail", 0), Place("residential", 0)
        });

        Assert.Equal(3, mix.Presences.Count);
        Assert.Equal(Presence(3), mix.PresenceOf("office"), 12);
        Assert.Equal(Presence(2), mix.PresenceOf("retail"), 12);
        Assert.Equal(Presence(1), mix.PresenceOf("residential"), 12);
    }

    [Fact]
    public void TiesUseOrdinalAreaNames()
    {
        var classifier = new AreaClassifier(ShippedData.Tables);
        var places = new[] { Place("retail", 0), Place("office", 0), Place("warehouse", 0) };

        var mix = classifier.FromPlaces(places);
        Assert.Equal(new[] { "industrial", "office", "retail" }, mix.Shares.Keys.ToArray());
        Assert.Equal(Presence(1), mix.PresenceOf("industrial"), 12);
        Assert.Equal(Presence(1), mix.PresenceOf("office"), 12);
        Assert.Equal(mix.Shares.ToArray(), classifier.FromPlaces(places.Reverse()).Shares.ToArray());
    }

    [Theory]
    [InlineData(0, 1.0)]
    [InlineData(25, 0.75)]
    [InlineData(50, 0.5)]
    public void NearerBuildingsCountMoreWithLinearFalloff(double retailDistance, double retailVotes)
    {
        var mix = new AreaClassifier(ShippedData.Tables).FromPlaces(new[]
        {
            Place("office", 0), Place("retail", retailDistance)
        });

        Assert.Equal(Presence(1), mix.PresenceOf("office"), 12);
        Assert.Equal(Presence(retailVotes), mix.PresenceOf("retail"), 12);
    }

    [Fact]
    public void RuleWeightsApplyToEveryArea()
    {
        var mix = new AreaClassifier(ShippedData.Tables).FromPlaces(new[]
        {
            Place("office", 0),
            new NearbyPlace("ba:buildingtype_retail", "ba:businesstype_importexport", 0)
        });

        Assert.Equal(Presence(1.5), mix.PresenceOf("office"), 12);
        Assert.Equal(Presence(0.5), mix.PresenceOf("industrial"), 12);
        Assert.Equal(0, mix.ShareOf("retail"));
    }

    [Fact]
    public void SummationIsIndependentOfPlaceOrder()
    {
        var classifier = new AreaClassifier(ShippedData.Tables);
        var places = Enumerable.Range(0, 200).SelectMany(i => new[]
        {
            Place("office", i % 51), Place("retail", (i * 7) % 51)
        }).ToArray();
        var expected = classifier.FromPlaces(places).Shares.ToArray();

        Assert.Equal(expected, classifier.FromPlaces(places.Reverse()).Shares.ToArray());
        Assert.Equal(expected, classifier.FromPlaces(places.OrderBy(p => p.DistanceMetres)).Shares.ToArray());
    }

    [Fact]
    public void ZeroVotesAtAnEdgeUseFallback()
    {
        var demand = JObject.Parse(ShippedData.Demand);
        demand["classifier"]!["edgeWeight"] = 0;
        var tables = DemandTables.Parse(demand.ToString(), ShippedData.Economy, ShippedData.Zones);

        var mix = new AreaClassifier(tables).FromPlaces(new[] { Place("office", tables.Classifier.RadiusMetres) });

        Assert.Equal(1.0, mix.ShareOf(tables.Classifier.FallbackArea));
    }

    [Fact]
    public void ZoneOverridesAnOfficeBlock()
    {
        var classifier = WithZones(Zone("park", "override", new JObject { ["waterfront"] = 0.75, ["retail"] = 0.25 }));
        var mix = classifier.Classify(new[] { Place("office", 0), Place("office", 10) }, 5, 5);

        Assert.Equal(0, mix.ShareOf("office"));
        Assert.Equal(0.75, mix.ShareOf("waterfront"));
        Assert.Equal(0.25, mix.ShareOf("retail"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.25)]
    [InlineData(1)]
    public void AddZoneSetsPresenceAndIgnoresLegacyShare(double share)
    {
        var classifier = WithZones(Zone("park", "add", new JObject { ["waterfront"] = 1 }, share));
        var mix = classifier.ApplyZones(AreaMix.Single("office"), 5, 5);

        Assert.Equal(1, mix.PresenceOf("office"), 12);
        Assert.Equal(1, mix.PresenceOf("waterfront"), 12);
    }

    [Fact]
    public void AddZonesRetainAllCrowdsWithoutDilution()
    {
        var classifier = WithZones(
            Zone("park", "add", new JObject { ["waterfront"] = 1 }, 0.5),
            Zone("shops", "add", new JObject { ["retail"] = 1 }, 0.5));
        var mix = classifier.ApplyZones(AreaMix.Single("office"), 5, 5);

        Assert.Equal(3, mix.Shares.Count);
        Assert.Equal(1, mix.PresenceOf("office"));
        Assert.Equal(1, mix.PresenceOf("waterfront"));
        Assert.Equal(1, mix.PresenceOf("retail"));
    }

    [Fact]
    public void AddZonesRaisePresenceToTheMaximumAndCanOmitLegacyShare()
    {
        var classifier = WithZones(
            Zone("park", "add", new JObject { ["waterfront"] = 0.6, ["office"] = 0.3 }),
            Zone("more-park", "add", new JObject { ["waterfront"] = 0.4, ["office"] = 0.8 }));
        var mix = classifier.ApplyZones(AreaMix.FromPresences(new Dictionary<string, double>
        {
            ["office"] = 0.7, ["nightlife"] = 0.9
        }), 5, 5);

        Assert.Equal(0.8, mix.PresenceOf("office"));
        Assert.Equal(0.6, mix.PresenceOf("waterfront"));
        Assert.Equal(0.9, mix.PresenceOf("nightlife"));
    }

    [Fact]
    public void FirstOverrideWinsAndLaterZonesAreIgnored()
    {
        var classifier = WithZones(
            Zone("before", "add", new JObject { ["nightlife"] = 1 }, 0.5),
            Zone("first", "override", new JObject { ["waterfront"] = 1 }),
            Zone("second", "override", new JObject { ["retail"] = 1 }),
            Zone("after", "add", new JObject { ["industrial"] = 1 }, 0.5));
        var mix = classifier.ApplyZones(AreaMix.Single("office"), 5, 5);

        Assert.Single(mix.Shares);
        Assert.Equal(1.0, mix.ShareOf("waterfront"));
    }

    [Fact]
    public void NonMatchingOverrideDoesNotBlockAnAddZone()
    {
        var distant = Zone("distant", "override", new JObject { ["retail"] = 1 });
        distant["polygon"] = JArray.Parse("[[20,20],[30,20],[30,30],[20,30]]");
        var classifier = WithZones(distant, Zone("park", "add", new JObject { ["waterfront"] = 1 }, 0.5));

        var mix = classifier.ApplyZones(AreaMix.Single("office"), 5, 5);

        Assert.Equal(1, mix.PresenceOf("office"));
        Assert.Equal(1, mix.PresenceOf("waterfront"));
    }

    [Theory]
    [InlineData(11, 5)]
    [InlineData(-1, 5)]
    [InlineData(double.NaN, 5)]
    [InlineData(5, double.NaN)]
    [InlineData(double.PositiveInfinity, 5)]
    [InlineData(5, double.NegativeInfinity)]
    public void OutsideOrNonFinitePointIsUnaffected(double x, double z)
    {
        var classifier = WithZones(
            Zone("park", "add", new JObject { ["waterfront"] = 1 }, 0.5),
            Zone("shops", "override", new JObject { ["retail"] = 1 }));
        var automatic = AreaMix.Single("office");

        Assert.Same(automatic, classifier.ApplyZones(automatic, x, z));
        Assert.False(AreaClassifier.Contains(Square, x, z));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 10)]
    [InlineData(0, 5)]
    [InlineData(5, 0)]
    [InlineData(10, 5)]
    [InlineData(5, 10)]
    [InlineData(5, 5)]
    public void EdgesVerticesAndInteriorCountAsInside(double x, double z)
    {
        Assert.True(AreaClassifier.Contains(Square, x, z));
        Assert.True(AreaClassifier.Contains(Square.Reverse().ToArray(), x, z));
        var classifier = WithZones(Zone("park", "override", new JObject { ["waterfront"] = 1 }));
        Assert.Equal(1.0, classifier.ApplyZones(AreaMix.Single("office"), x, z).ShareOf("waterfront"));
    }

    [Theory]
    [InlineData(1, 3, true)]
    [InlineData(3, 1, true)]
    [InlineData(3, 3, false)]
    [InlineData(2, 3, true)]
    [InlineData(2, 2, true)]
    [InlineData(-1, 1, false)]
    public void NonConvexPolygonWorks(double x, double z, bool expected)
    {
        var polygon = new[]
        {
            new ZonePoint(0, 0), new ZonePoint(4, 0), new ZonePoint(4, 2),
            new ZonePoint(2, 2), new ZonePoint(2, 4), new ZonePoint(0, 4)
        };

        Assert.Equal(expected, AreaClassifier.Contains(polygon, x, z));
        Assert.Equal(expected, AreaClassifier.Contains(polygon.Reverse().ToArray(), x, z));
    }

    [Fact]
    public void DiagonalEdgesAndRepeatedClosingVertexWork()
    {
        var triangle = new[] { new ZonePoint(0, 0), new ZonePoint(10, 10), new ZonePoint(10, 0), new ZonePoint(0, 0) };

        Assert.True(AreaClassifier.Contains(triangle, 5, 5));
        Assert.False(AreaClassifier.Contains(triangle, 4, 5));
        Assert.True(AreaClassifier.Contains(triangle, 6, 5));
    }

    private static double Presence(double votes) => 1 - Math.Exp(-votes / ShippedData.Tables.Classifier.Saturation);

    private static NearbyPlace Place(string building, double distance) =>
        new("ba:buildingtype_" + building, null, distance);

    private static AreaClassifier WithZones(params JObject[] zones)
    {
        var json = JObject.Parse(ShippedData.Zones);
        json["zones"] = new JArray(zones);
        return new AreaClassifier(DemandTables.Parse(ShippedData.Demand, ShippedData.Economy, json.ToString()));
    }

    private static JObject Zone(string id, string mode, JObject areas, double? share = null)
    {
        var zone = new JObject
        {
            ["id"] = id, ["mode"] = mode, ["areas"] = areas,
            ["polygon"] = new JArray(Square.Select(point => new JArray(point.X, point.Z)))
        };
        if (share.HasValue) zone["share"] = share.Value;
        return zone;
    }
}
