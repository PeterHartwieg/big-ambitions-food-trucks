using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Catering;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Rules;
using FoodTrucks.Core.Tests.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Catering;

public class CateringRulesTests
{
    private static CateringKind Kind((int, int)? start = null, (int, int)? duration = null, (int, int)? guests = null,
        IReadOnlyDictionary<string, double>? categories = null, IEnumerable<DayOfWeek>? days = null, double weight = 1) =>
        new("office", new[] { "office" }, start ?? (690, 750), duration ?? (60, 90), guests ?? (40, 120),
            categories ?? new Dictionary<string, double> { ["fastfood"] = 1 }, weight, days);

    [Fact]
    public void DefaultsMatchTheBrief()
    {
        var rules = new CateringRules();
        Assert.Equal(new[] { "truck" }, rules.VehicleTypes);
        Assert.Equal(0.5, rules.OffersPerDayAtMaturity);
        Assert.Equal(2, rules.MaxOffersPerDay);
        Assert.Equal(500, rules.MaturityServed);
        Assert.Equal((2, 5), rules.LeadDays);
        Assert.Equal(1, rules.ReplyDays);
        Assert.Equal(10, rules.GraceMinutes);
        Assert.Equal(60, rules.ArrivalWindowMinutes);
        Assert.Equal(1, rules.UnitsPerGuest);
        Assert.Equal(1.25, rules.FeeMarkup);
        Assert.Equal(1, rules.ReputationStart);
        Assert.Equal(0.1, rules.ReputationMin);
        Assert.Equal(0.05, rules.ReputationSuccess);
        Assert.Equal(0.25, rules.ReputationFailure);
        Assert.Collection(rules.Kinds,
            k => { Assert.Equal("office", k.Id); Assert.Equal((690, 750), k.Start); Assert.Equal((60, 90), k.DurationMinutes); Assert.Equal((40, 120), k.Guests); Assert.Equal(5, k.Days.Count); Assert.Equal(0.3, k.Categories["coffee"]); Assert.Equal(1, k.Weight); },
            k => { Assert.Equal("party", k.Id); Assert.Equal((1080, 1200), k.Start); Assert.Equal((90, 150), k.DurationMinutes); Assert.Equal((40, 100), k.Guests); Assert.Equal(7, k.Days.Count); Assert.Equal(0.8, k.Weight); },
            k => { Assert.Equal("event", k.Id); Assert.Equal((720, 960), k.Start); Assert.Equal((120, 180), k.DurationMinutes); Assert.Equal((100, 200), k.Guests); Assert.Equal(new[] { DayOfWeek.Sunday, DayOfWeek.Friday, DayOfWeek.Saturday }, k.Days); Assert.Equal(0.5, k.Weight); });
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ReputationValuesMustBeFiniteAndBetweenZeroAndOne(double value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(reputationStart: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(reputationMin: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(reputationSuccess: value));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(reputationFailure: value));
    }

    [Fact]
    public void InvalidRangesAndNonpositiveWeightsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Kind(start: (800, 700)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kind(start: (0, 1440)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kind(duration: (0, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kind(duration: (90, 60)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kind(guests: (0, 10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kind(guests: (1, 1001)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kind(guests: (100, 40)));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kind(weight: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kind(weight: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kind(categories: new Dictionary<string, double> { ["food"] = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Kind(categories: new Dictionary<string, double> { ["food"] = double.PositiveInfinity }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(leadDays: (5, 2)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(reputationStart: 0.2, reputationMin: 0.3));
    }

    [Fact]
    public void EmptyListsDuplicateKindsAndUnknownWeekdaysAreRejected()
    {
        Assert.Throws<ArgumentException>(() => new CateringRules(vehicleTypes: Array.Empty<string>()));
        Assert.Throws<ArgumentException>(() => new CateringRules(kinds: Array.Empty<CateringKind>()));
        Assert.Throws<ArgumentException>(() => new CateringRules(kinds: new[] { Kind(), Kind() }));
        Assert.Throws<ArgumentException>(() => new CateringRules(kinds: new CateringKind[] { null! }));
        Assert.Throws<ArgumentException>(() => Kind(categories: new Dictionary<string, double>()));
        Assert.Throws<ArgumentException>(() => Kind(days: Array.Empty<DayOfWeek>()));
        Assert.Throws<ArgumentException>(() => Kind(days: new[] { (DayOfWeek)7 }));
    }

    [Fact]
    public void InvalidScalarRulesAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(offersPerDayAtMaturity: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(offersPerDayAtMaturity: double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(maxOffersPerDay: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(maturityServed: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(replyDays: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(graceMinutes: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(arrivalWindowMinutes: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(arrivalWindowMinutes: 1441));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(unitsPerGuest: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CateringRules(feeMarkup: -1));
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("1441")]
    [InlineData("60.5")]
    public void InvalidArrivalWindowNamesItsJsonPath(string value)
    {
        var root = JObject.Parse(ShippedData.RulesJson);
        root["catering"]!["arrivalWindowMinutes"] = JToken.Parse(value);
        var error = Assert.Throws<DemandDataException>(() => RuleTables.Parse(root.ToString()));
        Assert.Contains("catering.arrivalWindowMinutes", error.Message);
    }

    [Fact]
    public void RulesCopyCollectionsAndExposeReadOnlyViews()
    {
        var areas = new List<string> { "office" };
        var days = new List<DayOfWeek> { DayOfWeek.Monday };
        var categories = new Dictionary<string, double> { ["fastfood"] = 1 };
        var kind = new CateringKind("office", areas, (690, 750), (60, 90), (40, 120), categories, days: days);
        var kinds = new List<CateringKind> { kind };
        var vehicles = new List<string> { "truck" };
        var rules = new CateringRules(vehicleTypes: vehicles, kinds: kinds);
        areas.Clear(); days.Clear(); categories.Clear(); kinds.Clear(); vehicles.Clear();
        Assert.Single(rules.Kinds);
        Assert.Single(rules.VehicleTypes);
        Assert.Single(kind.Areas);
        Assert.Single(kind.Days);
        Assert.Single(kind.Categories);
        Assert.Throws<NotSupportedException>(() => ((IList<CateringKind>)rules.Kinds).Clear());
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, double>)kind.Categories).Clear());
    }

    [Fact]
    public void CustomStartingReputationCanInitializeStateAndBoundaryValuesAreAllowed()
    {
        var rules = new CateringRules(offersPerDayAtMaturity: 0, maxOffersPerDay: 0, leadDays: (0, 0), replyDays: 0,
            graceMinutes: 0, feeMarkup: 0, reputationStart: 0.4, reputationMin: 0, reputationSuccess: 0, reputationFailure: 1,
            kinds: new[] { Kind(start: (0, 1439), guests: (1, 1000)) });
        var state = CateringState.New(rules, 123);
        Assert.Equal(0.4, state.BookingReputation);
        Assert.Equal(123, state.RandomState);
        Assert.Equal(7, rules.Kinds.Single().Days.Count);
        Assert.Equal(0, new CateringRules(arrivalWindowMinutes: 0).ArrivalWindowMinutes);
        Assert.Equal(1440, new CateringRules(arrivalWindowMinutes: 1440).ArrivalWindowMinutes);
    }
}

public class CateringSaveTests
{
    [Fact]
    public void TheShippedRulesLoad()
    {
        var rules = FoodTrucks.Core.Tests.Demand.ShippedData.Rules.Catering;

        Assert.Equal(3, rules.Kinds.Count);
        Assert.Contains("truck", rules.VehicleTypes);
    }

    [Fact]
    public void OffersAndGigsSurviveASave()
    {
        var rules = FoodTrucks.Core.Tests.Demand.ShippedData.Rules.Catering;
        var save = new FoodTrucks.Core.Saves.SaveState { Catering = CateringState.New(rules, 5) };
        var context = new OfferContext
        {
            Day = 1, EligibleVehicles = 1, LifetimeServed = 10000,
            Places = new[] { new CateringPlace { PlaceId = "a", Area = "office" }, new CateringPlace { PlaceId = "b", Area = "residential" } },
            ReferencePriceCents = new System.Collections.Generic.Dictionary<string, long> { ["fastfood"] = 1165, ["pizza"] = 1400 }
        };
        for (var day = 1; day < 20 && save.Catering.Offers.Count == 0; day++)
        {
            context.Day = day;
            CateringOffers.ForDay(rules, save.Catering, context);
        }
        Assert.NotEmpty(save.Catering.Offers);
        var codec = new FoodTrucks.Core.Saves.SaveCodec();

        var loaded = codec.Load(codec.Serialize(save)).State!.Catering;

        Assert.Equal(save.Catering.Offers[0].FeeCents, loaded.Offers[0].FeeCents);
        Assert.Equal(save.Catering.RandomState, loaded.RandomState);
        Assert.Equal(save.Catering.LastOfferDay, loaded.LastOfferDay);
    }
}
