using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Catering;
using FoodTrucks.Core.Common;
using Newtonsoft.Json;
using Xunit;

namespace FoodTrucks.Core.Tests.Catering;

public class CateringOffersTests
{
    private static OfferContext Context(int day = 1, int vehicles = 1, long served = 500) => new()
    {
        Day = day, EligibleVehicles = vehicles, LifetimeServed = served,
        Places = new[]
        {
            new CateringPlace { PlaceId = "office-b", Area = "office" },
            new CateringPlace { PlaceId = "office-a", Area = "office" },
            new CateringPlace { PlaceId = "home", Area = "residential" },
            new CateringPlace { PlaceId = "club", Area = "nightlife" },
            new CateringPlace { PlaceId = "water", Area = "waterfront" },
            new CateringPlace { PlaceId = "shop", Area = "retail" }
        },
        ReferencePriceCents = new Dictionary<string, long> { ["fastfood"] = 1165, ["pizza"] = 1200 }
    };

    private static List<CateringOffer> Generate(CateringRules rules, CateringState state, int days)
    {
        var offers = new List<CateringOffer>();
        for (var day = 1; day <= days; day++) offers.AddRange(CateringOffers.ForDay(rules, state, Context(day)));
        return offers;
    }

    [Theory]
    [InlineData(0, 500)]
    [InlineData(1, 0)]
    public void NoTruckOrNoCustomersMeansNoOffers(int vehicles, long served)
    {
        var state = CateringState.New(42);
        Assert.Empty(CateringOffers.ForDay(new CateringRules(), state, Context(vehicles: vehicles, served: served)));
        Assert.Empty(state.Offers);
        Assert.Equal(42, state.RandomState);
        Assert.Equal(1, state.LastOfferDay);
    }

    [Fact]
    public void MatureFleetHasASteadyFlowOverFourHundredDays()
    {
        var rules = new CateringRules();
        var count = Generate(rules, CateringState.New(42), 400).Count;
        Assert.InRange(count / 400.0, rules.OffersPerDayAtMaturity * 0.8, rules.OffersPerDayAtMaturity * 1.2);
    }

    [Theory]
    [InlineData(0, 500, 3)]
    [InlineData(1, 0, 3)]
    [InlineData(0, 0, 3)]
    [InlineData(1, 500, 0)]
    public void SkippingLocationsOnIneligibleDaysPreservesStateAndLaterOffers(int vehicles, long served, int maxOffers)
    {
        var rules = new CateringRules(maxOffersPerDay: maxOffers);
        Assert.False(CateringOffers.CanGenerateOffers(rules, vehicles, served));
        var original = CateringState.New(42);
        var optimized = CateringState.New(42);
        for (var day = 1; day <= 3; day++)
        {
            var input = Context(day, vehicles, served);
            var before = CateringOffers.ForDay(rules, original, input);
            input.Places = Array.Empty<CateringPlace>();
            var after = CateringOffers.ForDay(rules, optimized, input);
            Assert.Empty(before);
            Assert.Empty(after);
            Assert.Equal(JsonConvert.SerializeObject(original), JsonConvert.SerializeObject(optimized));
            Assert.Equal(day, optimized.LastOfferDay);
            Assert.Equal(42, optimized.RandomState);
        }
        // A fleet that qualifies later must receive the same seeded offers after the skipped days.
        rules = new CateringRules(offersPerDayAtMaturity: 60);
        Assert.True(CateringOffers.CanGenerateOffers(rules, 1, 500));
        var expected = CateringOffers.ForDay(rules, original, Context(4));
        var actual = CateringOffers.ForDay(rules, optimized, Context(4));
        Assert.NotEmpty(actual);
        Assert.Equal(JsonConvert.SerializeObject(expected), JsonConvert.SerializeObject(actual));
        Assert.Equal(JsonConvert.SerializeObject(original), JsonConvert.SerializeObject(optimized));
    }

    [Fact]
    public void SameSeedAndReloadGiveTheSameOffersRegardlessOfInputOrder()
    {
        var rules = new CateringRules();
        var first = CateringState.New(123);
        var second = CateringState.New(123);
        for (var day = 1; day <= 100; day++)
        {
            var input = Context(day);
            var a = CateringOffers.ForDay(rules, first, input);
            input.Places = input.Places.Reverse().ToArray();
            input.ReferencePriceCents = input.ReferencePriceCents.Reverse().ToDictionary(p => p.Key, p => p.Value);
            var b = CateringOffers.ForDay(rules, second, input);
            Assert.Equal(JsonConvert.SerializeObject(a), JsonConvert.SerializeObject(b));
            Assert.Equal(first.RandomState, second.RandomState);
            second = JsonConvert.DeserializeObject<CateringState>(JsonConvert.SerializeObject(second))!;
        }
    }

    [Fact]
    public void RepeatedAndEarlierDaysChangeNothing()
    {
        var state = CateringState.New(42);
        var rules = new CateringRules(offersPerDayAtMaturity: 60);
        CateringOffers.ForDay(rules, state, Context(10));
        var before = JsonConvert.SerializeObject(state);
        Assert.Empty(CateringOffers.ForDay(rules, state, Context(10)));
        Assert.Empty(CateringOffers.ForDay(rules, state, Context(9)));
        Assert.Equal(before, JsonConvert.SerializeObject(state));
    }

    [Fact]
    public void OffersRespectKindsDaysAreasPricesAndFees()
    {
        var rules = new CateringRules(unitsPerGuest: 1.1);
        var input = Context();
        var offers = Generate(rules, CateringState.New(42), 400);
        Assert.Contains(offers, o => o.Kind == "office");
        Assert.Contains(offers, o => o.Kind == "party");
        Assert.Contains(offers, o => o.Kind == "event");
        Assert.All(offers, offer =>
        {
            var kind = rules.Kinds.Single(k => k.Id == offer.Kind);
            Assert.Contains(GameDays.Weekday(offer.GigDay), kind.Days);
            Assert.Contains(input.Places.Single(p => p.PlaceId == offer.PlaceId).Area, kind.Areas);
            Assert.Contains(offer.RequiredCategory, input.ReferencePriceCents.Keys);
            Assert.InRange(offer.GigDay - offer.PostedDay, 2, 5);
            Assert.Equal(offer.PostedDay + 1, offer.ReplyByDay);
            Assert.Equal(0, offer.StartMinute % 15);
            Assert.Equal(0, offer.DurationMinutes % 15);
            Assert.InRange(offer.Guests, kind.Guests.Min, kind.Guests.Max);
            Assert.Equal((long)Math.Round(offer.Guests * rules.UnitsPerGuest * input.ReferencePriceCents[offer.RequiredCategory] * rules.FeeMarkup,
                MidpointRounding.AwayFromZero), offer.FeeCents);
        });
    }

    [Fact]
    public void MissingPlacesOrKnownCategoriesYieldNoOffers()
    {
        var rules = new CateringRules(offersPerDayAtMaturity: 60);
        var input = Context();
        input.Places = Array.Empty<CateringPlace>();
        Assert.Empty(CateringOffers.ForDay(rules, CateringState.New(42), input));
        input = Context();
        input.ReferencePriceCents = new Dictionary<string, long> { ["unknown"] = 100 };
        Assert.Empty(CateringOffers.ForDay(rules, CateringState.New(42), input));
    }

    [Fact]
    public void LowerReputationAndImmatureFleetReceiveFewerOffers()
    {
        var rules = new CateringRules();
        var mature = Generate(rules, CateringState.New(42), 400).Count;
        var lowState = CateringState.New(42);
        lowState.BookingReputation = 0.2;
        var low = Generate(rules, lowState, 400).Count;
        Assert.InRange(low, 20, 60);
        Assert.True(low < mature / 2);
        var immature = CateringState.New(42);
        for (var day = 1; day <= 400; day++) CateringOffers.ForDay(rules, immature, Context(day, served: 100));
        Assert.Equal(low, immature.Offers.Count);
    }

    internal static CateringOffer Offer(long id = 1, int day = 3, int start = 720, int duration = 60) => new()
    {
        Id = id, Kind = "office", PostedDay = 1, ReplyByDay = 2, GigDay = day, StartMinute = start,
        DurationMinutes = duration, PlaceId = "office", Guests = 40, RequiredCategory = "fastfood", FeeCents = 58250
    };

    private static CateringState WithOffers(params CateringOffer[] offers)
    {
        var state = CateringState.New(42);
        state.Offers.AddRange(offers);
        state.NextOfferId = offers.Max(o => o.Id) + 1;
        return state;
    }

    [Fact]
    public void ReplyDeadlineIncludesTheLastMinuteButNotTheFollowingDay()
    {
        var rules = new CateringRules();
        var state = WithOffers(Offer());
        Assert.Equal(AcceptFailure.Expired, CateringOffers.Accept(rules, state, 1, 3, 0, 1).Failure);
        Assert.Single(state.Offers);
        Assert.True(CateringOffers.Accept(rules, state, 1, 2, 1439, 1).Success);
        Assert.Equal(AcceptFailure.UnknownOffer, CateringOffers.Accept(rules, state, 1, 2, 1439, 1).Failure);
    }

    [Fact]
    public void DeadlineCannotExtendBeyondGigStart()
    {
        var offer = Offer();
        offer.ReplyByDay = 5;
        var state = WithOffers(offer);
        Assert.Empty(CateringOffers.ExpireOffers(state, 3, 719));
        Assert.Single(CateringOffers.ExpireOffers(state, 3, 720));
        Assert.Empty(state.Offers);
        Assert.Empty(CateringOffers.ExpireOffers(state, 6, 0));
    }

    [Fact]
    public void AcceptanceIsStrictlyBeforeGigStart()
    {
        var rules = new CateringRules();
        var offer = Offer();
        offer.ReplyByDay = 5;
        var state = WithOffers(offer);
        Assert.Equal(AcceptFailure.Expired, CateringOffers.Accept(rules, state, 1, 3, 720, 1).Failure);
        Assert.Single(state.Offers);
        Assert.True(CateringOffers.Accept(rules, state, 1, 3, 719, 1).Success);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.01)]
    [InlineData(2.0)]
    public void StockLimitCapsEveryOffer(double unitsPerGuest)
    {
        var rules = new CateringRules(offersPerDayAtMaturity: 60, unitsPerGuest: unitsPerGuest);
        var state = CateringState.New(42);
        for (var day = 1; day <= 100; day++)
        {
            var context = Context(day);
            context.CategoryUnits = new Dictionary<string, int> { ["fastfood"] = 160, ["pizza"] = 160 };
            CateringOffers.ForDay(rules, state, context);
        }
        Assert.NotEmpty(state.Offers);
        Assert.All(state.Offers, o => Assert.InRange(Math.Ceiling(o.Guests * rules.UnitsPerGuest), 1, 160));
        if (unitsPerGuest == 2) Assert.DoesNotContain(state.Offers, o => o.Kind == "event");
    }

    [Fact]
    public void KindsWhoseMinimumGuestsExceedCapacityAreNotOffered()
    {
        var rules = new CateringRules(offersPerDayAtMaturity: 60);
        var context = Context();
        context.CategoryUnits = new Dictionary<string, int> { ["fastfood"] = 39, ["pizza"] = 39 };
        Assert.Empty(CateringOffers.ForDay(rules, CateringState.New(42), context));
        context.CategoryUnits = new Dictionary<string, int>();
        Assert.Empty(CateringOffers.ForDay(rules, CateringState.New(42), context));
        context.CategoryUnits = new Dictionary<string, int> { ["fastfood"] = -1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => CateringOffers.ForDay(rules, CateringState.New(42), context));
    }

    // A truck with three fast-food pieces and a cooler holds 240 fast food but only 80 cold drinks. Cold drinks are
    // only wanted at events (100 guests and up), so that truck never gets a cold-drinks gig; with 150 it gets ones
    // capped at 150 while fast-food events still go up to 200.
    [Theory]
    [InlineData(80)]
    [InlineData(150)]
    public void EachOfferIsCappedByItsOwnCategory(int coldDrinks)
    {
        var rules = new CateringRules(offersPerDayAtMaturity: 60);
        var state = CateringState.New(42);
        var units = new Dictionary<string, int> { ["fastfood"] = 240, ["colddrinks"] = coldDrinks };
        for (var day = 1; day <= 200; day++)
        {
            var context = Context(day);
            context.ReferencePriceCents = new Dictionary<string, long> { ["fastfood"] = 1165, ["colddrinks"] = 300 };
            context.CategoryUnits = units;
            CateringOffers.ForDay(rules, state, context);
        }
        Assert.Contains(state.Offers, o => o.RequiredCategory == "fastfood" && o.Guests > 150);
        Assert.All(state.Offers, o => Assert.InRange(o.Guests, 1, units[o.RequiredCategory]));
        if (coldDrinks < 100) Assert.DoesNotContain(state.Offers, o => o.RequiredCategory == "colddrinks");
        else Assert.Contains(state.Offers, o => o.RequiredCategory == "colddrinks");
    }

    [Fact]
    public void OverlappingGigsNeedEnoughTrucks()
    {
        var state = WithOffers(Offer(), Offer(2, start: 750));
        var rules = new CateringRules();
        Assert.Equal(AcceptFailure.TooManyGigs, CateringOffers.Accept(rules, state, 1, 1, 0, 0).Failure);
        Assert.True(CateringOffers.Accept(rules, state, 1, 1, 0, 1).Success);
        state.Gigs[0].Status = GigStatus.Started;
        Assert.Equal(AcceptFailure.TooManyGigs, CateringOffers.Accept(rules, state, 2, 1, 0, 1).Failure);
        Assert.True(CateringOffers.Accept(rules, state, 2, 1, 0, 2).Success);
        Assert.Empty(state.Offers);
        Assert.Equal(2, state.Gigs.Count);
    }

    [Fact]
    public void CapacityUsesPeakOverlapAndAllowsBackToBackAndOvernightGigs()
    {
        var rules = new CateringRules();
        var state = WithOffers(Offer(), Offer(2, start: 780), Offer(3, start: 720, duration: 120));
        Assert.True(CateringOffers.Accept(rules, state, 1, 1, 0, 1).Success);
        Assert.True(CateringOffers.Accept(rules, state, 2, 1, 0, 1).Success);
        Assert.True(CateringOffers.Accept(rules, state, 3, 1, 0, 2).Success);
        state = WithOffers(Offer(start: 1410, duration: 90), Offer(2, day: 4, start: 0));
        Assert.True(CateringOffers.Accept(rules, state, 1, 1, 0, 1).Success);
        Assert.Equal(AcceptFailure.TooManyGigs, CateringOffers.Accept(rules, state, 2, 1, 0, 1).Failure);
    }

    [Fact]
    public void DeclineAndExpirationRemoveOnlyTheirOffers()
    {
        var state = WithOffers(Offer(), Offer(2));
        Assert.True(CateringOffers.Decline(state, 1));
        Assert.False(CateringOffers.Decline(state, 99));
        Assert.Empty(CateringOffers.ExpireOffers(state, 2, 1439));
        Assert.Equal(2, Assert.Single(CateringOffers.ExpireOffers(state, 3, 0)).Id);
        Assert.Empty(state.Offers);
    }

    [Fact]
    public void CountIsCappedAndQuarterRoundingStaysInsideTheDay()
    {
        var kind = new CateringKind("tiny", new[] { "office" }, (1439, 1439), (1, 1), (1, 1), new Dictionary<string, double> { ["fastfood"] = 1 });
        var rules = new CateringRules(offersPerDayAtMaturity: 120, maxOffersPerDay: 2, kinds: new[] { kind }, leadDays: (0, 0));
        var state = CateringState.New(42);
        var offers = CateringOffers.ForDay(rules, state, Context());
        Assert.Equal(2, offers.Count);
        Assert.All(offers, o => { Assert.Equal(1425, o.StartMinute); Assert.Equal(15, o.DurationMinutes); Assert.Equal(1, o.ReplyByDay); });
    }
}
