using System;
using System.Collections.Generic;
using System.Linq;
using FoodTrucks.Core.Catering;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Catering;

public class CateringGigsTests
{
    private static readonly CateringRules rules = new();
    private static readonly string[] menu = { "hotdog", "burger", "coffee", "hotdog" };
    private static readonly IReadOnlyDictionary<string, int> stock = new Dictionary<string, int> { ["hotdog"] = 20, ["burger"] = 20, ["coffee"] = 500 };
    private static string? Category(string item) => item == "hotdog" || item == "burger" ? "fastfood" : item == "coffee" ? "coffee" : null;

    private static CateringState Book(CateringRules? custom = null, double reputation = 1, int start = 720, int duration = 60)
    {
        var state = CateringState.New(42);
        state.BookingReputation = reputation;
        state.NextOfferId = 2;
        state.Offers.Add(CateringOffersTests.Offer(start: start, duration: duration));
        Assert.True(CateringOffers.Accept(custom ?? rules, state, 1, 1, 0, 1).Success);
        return state;
    }

    [Fact]
    public void UnlocatableBookedGigIsCancelledBeforeNoShowWithoutReputationLoss()
    {
        var state = Book(reputation: 0.6);
        var cancelled = CateringGigs.CancelUnlocatable(state, _ => false);
        Assert.Single(cancelled);
        Assert.Empty(state.Gigs);
        Assert.Empty(state.Offers);
        Assert.Empty(state.History);
        Assert.Empty(CateringGigs.MissOverdue(rules, state, 10, 0));
        Assert.Equal(0.6, state.BookingReputation);
        Assert.Equal(0, state.Failed);
        Assert.Empty(CateringGigs.CancelUnlocatable(state, _ => false));
        var reloaded = JsonConvert.DeserializeObject<CateringState>(JsonConvert.SerializeObject(state))!;
        Assert.Empty(reloaded.Gigs);
        Assert.Equal(0.6, reloaded.BookingReputation);
    }

    [Fact]
    public void LocationCancellationPreservesUsableBookingsAndStartedWork()
    {
        var state = Book();
        Assert.Empty(CateringGigs.CancelUnlocatable(state, _ => true));
        Assert.Single(state.Gigs);
        Assert.True(CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 720, menu, stock, Category).Success);
        Assert.Empty(CateringGigs.CancelUnlocatable(state, _ => false));
        Assert.Equal(GigStatus.Started, Assert.Single(state.Gigs).Status);
        Assert.True(CateringGigs.Finish(rules, state, 1, 3, 780).Success);
    }

    [Fact]
    public void UnlocatableOffersAreRemovedButUsableOffersRemain()
    {
        var state = Book();
        var offer = CateringOffersTests.Offer();
        offer.Id = 2;
        offer.PlaceId = "unavailable";
        state.NextOfferId = 3;
        state.Offers.Add(offer);
        Assert.Empty(CateringGigs.CancelUnlocatable(state, p => p != "unavailable"));
        Assert.Empty(state.Offers);
        Assert.Single(state.Gigs);
    }

    [Theory]
    [InlineData(3, 660)]
    [InlineData(3, 720)]
    [InlineData(3, 730)]
    public void EarlyAndOnTimeArrivalsWithMenuAndStockStart(int day, int minute)
    {
        var state = Book();
        var result = CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", day, minute, menu, stock, Category);
        Assert.True(result.Success);
        Assert.Equal(GigStatus.Started, Assert.Single(state.Gigs).Status);
        Assert.Equal(day, result.Gig!.ArrivedDay);
        Assert.Equal(minute, result.Gig.ArrivedMinute);
        Assert.Equal(1, state.BookingReputation);
        Assert.Equal(20, stock["hotdog"]);
    }

    [Fact]
    public void ArrivingBeyondGraceFailsLateAndCostsReputation()
    {
        var state = Book();
        var result = CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 731, menu, stock, Category);
        Assert.Equal(GigFailure.Late, result.Failure);
        Assert.Equal(0.75, state.BookingReputation);
        Assert.Empty(state.Gigs);
        Assert.Equal(GigStatus.Failed, Assert.Single(state.History).Status);
        Assert.Equal(1, state.Failed);
    }

    [Fact]
    public void StockWithoutAMatchingMenuCannotStart()
    {
        var state = Book();
        var result = CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 720, new[] { "coffee", "unknown" }, stock, Category);
        Assert.Equal(GigCheckStatus.NotReady, result.Status);
        Assert.Equal(1, state.BookingReputation);
    }

    [Fact]
    public void UnrelatedStockAndDuplicateMenuItemsDoNotSupplyMissingUnits()
    {
        var state = Book();
        var shortStock = new Dictionary<string, int> { ["hotdog"] = 39, ["coffee"] = 500, ["burger"] = -1 };
        var result = CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 720, menu, shortStock, Category);
        Assert.Equal(GigCheckStatus.NotReady, result.Status);
    }

    [Fact]
    public void FractionalUnitsAreRoundedUpAndCategoryStockIsCombined()
    {
        var custom = new CateringRules(unitsPerGuest: 1.01);
        var state = Book(custom);
        Assert.Equal(GigCheckStatus.NotReady, CateringGigs.Arrive(custom, state, 1, "truck-1", "truck", 3, 720, menu, stock, Category).Status);
        state = Book(custom);
        var enough = new Dictionary<string, int> { ["hotdog"] = 20, ["burger"] = 21 };
        Assert.True(CateringGigs.Arrive(custom, state, 1, "truck-1", "truck", 3, 720, menu, enough, Category).Success);
        var result = CateringGigs.Finish(custom, state, 1, 3, 780);
        Assert.Equal(41, result.Units);
        Assert.Equal("fastfood", result.RequiredCategory);
    }

    [Fact]
    public void LeavingBeforeScheduledEndFailsEvenAfterAnEarlyArrival()
    {
        var state = Book();
        CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 660, menu, stock, Category);
        var result = CateringGigs.Finish(rules, state, 1, 3, 779);
        Assert.False(result.Success);
        Assert.Equal(GigFailure.LeftEarly, result.Failure);
        Assert.Equal(0, result.FeeCents);
        Assert.Equal(0, result.Units);
        Assert.Equal(0.75, state.BookingReputation);
        Assert.Equal(1, state.Failed);
    }

    [Theory]
    [InlineData(0.7, 0.75)]
    [InlineData(0.99, 1.0)]
    public void CompletionPaysOnceAndRaisesReputationUpToOne(double before, double after)
    {
        var state = Book(reputation: before);
        CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 720, menu, stock, Category);
        var result = CateringGigs.Finish(rules, state, 1, 3, 780);
        Assert.True(result.Success);
        Assert.Equal(58250, result.FeeCents);
        Assert.Equal(40, result.Units);
        Assert.Equal(after, state.BookingReputation, 10);
        Assert.Equal(1, state.Completed);
        Assert.Empty(state.Gigs);
        Assert.Equal(GigStatus.Completed, Assert.Single(state.History).Status);
        Assert.Equal(0, CateringGigs.Finish(rules, state, 1, 3, 800).FeeCents);
        Assert.Equal(1, state.Completed);
    }

    [Fact]
    public void GraceDeadlineIsInclusiveAndBookedGigsBecomeNoShows()
    {
        var state = Book();
        Assert.Empty(CateringGigs.MissOverdue(rules, state, 3, 730));
        var gig = Assert.Single(CateringGigs.MissOverdue(rules, state, 3, 731));
        Assert.Equal(GigFailure.NoShow, gig.Failure);
        Assert.Equal(0.75, state.BookingReputation);
        Assert.Empty(CateringGigs.MissOverdue(rules, state, 4, 0));
        Assert.Equal(1, state.Failed);
        state = Book();
        CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 720, menu, stock, Category);
        Assert.Empty(CateringGigs.MissOverdue(rules, state, 3, 840));
        Assert.True(CateringGigs.Finish(rules, state, 1, 3, 840).Success);
    }

    [Fact]
    public void OvernightTimesUseAbsoluteMinutes()
    {
        var state = Book(start: 1430, duration: 60);
        Assert.True(CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 4, 0, menu, stock, Category).Success);
        Assert.True(CateringGigs.Finish(rules, state, 1, 4, 50).Success);
    }

    [Fact]
    public void UnknownGigsAndRepeatedArrivalsDoNotChangeState()
    {
        var state = Book();
        Assert.Equal(GigCheckStatus.UnknownGig, CateringGigs.Arrive(rules, state, 9, "truck-1", "truck", 3, 720, menu, stock, Category).Status);
        Assert.Equal(GigCheckStatus.InvalidStatus, CateringGigs.Finish(rules, state, 1, 3, 780).Status);
        CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 720, menu, stock, Category);
        Assert.Equal(GigCheckStatus.InvalidStatus, CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 4, 0, menu, stock, Category).Status);
        Assert.Equal(0, state.Failed);
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(3, 659)]
    public void TooEarlyDoesNotChangeState(int day, int minute)
    {
        var state = Book();
        var before = JsonConvert.SerializeObject(state);
        Assert.Equal(GigCheckStatus.NotYet, CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", day, minute, menu, stock, Category).Status);
        Assert.Equal(before, JsonConvert.SerializeObject(state));
    }

    [Fact]
    public void IneligibleVehicleDoesNotChangeState()
    {
        var state = Book();
        var before = JsonConvert.SerializeObject(state);
        Assert.Equal(GigCheckStatus.NotEligible, CateringGigs.Arrive(rules, state, 1, "cart-1", "cart", 3, 720, menu, stock, Category).Status);
        Assert.Equal(before, JsonConvert.SerializeObject(state));
    }

    [Fact]
    public void ShortfallCanBeRestockedThroughTheGraceDeadline()
    {
        var state = Book();
        var before = JsonConvert.SerializeObject(state);
        var shortStock = new Dictionary<string, int> { ["hotdog"] = 0, ["burger"] = 40 };
        Assert.Equal(GigCheckStatus.NotReady, CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 720, new[] { "hotdog" }, shortStock, Category).Status);
        Assert.Equal(before, JsonConvert.SerializeObject(state));
        shortStock["hotdog"] = 40;
        Assert.True(CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 730, new[] { "hotdog" }, shortStock, Category).Success);
    }

    [Fact]
    public void LateArrivalFailsEvenWithNoMenuOrStock()
    {
        var state = Book();
        var result = CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 731, Array.Empty<string>(), new Dictionary<string, int>(), Category);
        Assert.Equal(GigFailure.Late, result.Failure);
        Assert.Equal(GigCheckStatus.Failed, result.Status);
    }

    [Fact]
    public void ShortfallBecomesNoShowOnlyAfterGraceDeadline()
    {
        var state = Book();
        Assert.Equal(GigCheckStatus.NotReady, CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 730, Array.Empty<string>(), stock, Category).Status);
        Assert.Empty(CateringGigs.MissOverdue(rules, state, 3, 730));
        Assert.Equal(GigFailure.NoShow, Assert.Single(CateringGigs.MissOverdue(rules, state, 3, 731)).Failure);
    }

    [Fact]
    public void ReservationSurvivesASaveAndFinishUsesTheReservedAmount()
    {
        var custom = new CateringRules(unitsPerGuest: 0.99);
        var state = Book(custom);
        var result = CateringGigs.Arrive(custom, state, 1, "truck-1", "truck", 3, 720, menu, stock, Category);
        Assert.Equal("truck-1", result.VehicleId);
        Assert.Equal(40, result.ReservedUnits);
        Assert.Equal(40, result.Gig!.ReservedUnits);
        state = JsonConvert.DeserializeObject<CateringState>(JsonConvert.SerializeObject(state))!;
        Assert.Equal("truck-1", state.Gigs[0].VehicleId);
        Assert.Equal(40, CateringGigs.Finish(new CateringRules(unitsPerGuest: 2), state, 1, 3, 780).Units);
    }

    [Fact]
    public void OneVehicleCannotStartTwoGigsAndCompletionFreesIt()
    {
        var state = Book();
        state.NextOfferId = 3;
        state.Offers.Add(CateringOffersTests.Offer(2, start: 780));
        Assert.True(CateringOffers.Accept(rules, state, 2, 1, 0, 1).Success);
        Assert.True(CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 720, menu, stock, Category).Success);
        var before = JsonConvert.SerializeObject(state);
        Assert.Equal(GigCheckStatus.Busy, CateringGigs.Arrive(rules, state, 2, "truck-1", "truck", 3, 720, menu, stock, Category).Status);
        Assert.Equal(before, JsonConvert.SerializeObject(state));
        Assert.True(CateringGigs.Finish(rules, state, 1, 3, 780).Success);
        Assert.True(CateringGigs.Arrive(rules, state, 2, "truck-1", "truck", 3, 780, menu, stock, Category).Success);
    }

    [Fact]
    public void AnotherVehicleCanStartWhileTheFirstIsBusy()
    {
        var state = Book();
        state.NextOfferId = 3;
        state.Offers.Add(CateringOffersTests.Offer(2));
        Assert.True(CateringOffers.Accept(rules, state, 2, 1, 0, 2).Success);
        Assert.True(CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 720, menu, stock, Category).Success);
        Assert.True(CateringGigs.Arrive(rules, state, 2, "truck-2", "truck", 3, 720, menu, stock, Category).Success);
    }

    [Fact]
    public void UnfinishedGigsExpireAfterTheEndWindowAndFreeTheVehicle()
    {
        var state = Book();
        state.NextOfferId = 3;
        state.Offers.Add(CateringOffersTests.Offer(2, start: 840));
        Assert.True(CateringOffers.Accept(rules, state, 2, 1, 0, 1).Success);
        CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 720, menu, stock, Category);
        Assert.Empty(CateringGigs.MissOverdue(rules, state, 3, 840));
        Assert.Equal(GigFailure.Unfinished, Assert.Single(CateringGigs.MissOverdue(rules, state, 3, 841)).Failure);
        Assert.Equal(1, state.Failed);
        Assert.Equal(0.75, state.BookingReputation);
        Assert.True(CateringGigs.Arrive(rules, state, 2, "truck-1", "truck", 3, 841, menu, stock, Category).Success);
        Assert.Empty(CateringGigs.MissOverdue(rules, state, 3, 841));
        Assert.Equal(1, state.Failed);
    }

    [Fact]
    public void OlderStartedGigWithoutReservationStillFinishes()
    {
        var state = Book();
        state.Gigs[0].Status = GigStatus.Started;
        Assert.Equal(40, CateringGigs.Finish(rules, state, 1, 3, 780).Units);
    }

    [Fact]
    public void ReputationHasAFloorAndHistoryKeepsTheLatestFiftyOutcomes()
    {
        var state = CateringState.New(42);
        for (var i = 1; i <= 60; i++)
        {
            state.NextOfferId = i + 1;
            state.Offers.Add(CateringOffersTests.Offer(i));
            CateringOffers.Accept(rules, state, i, 1, 0, 1);
            CateringGigs.MissOverdue(rules, state, 3, 731);
            Assert.InRange(state.BookingReputation, rules.ReputationMin, 1);
        }
        Assert.Equal(rules.ReputationMin, state.BookingReputation);
        Assert.Equal(60, state.Failed);
        Assert.Equal(Enumerable.Range(11, 50).Select(i => (long)i), state.History.Select(g => g.Id));
        Assert.Empty(state.Gigs);
    }

    [Theory]
    [InlineData("nullOffers")]
    [InlineData("nullGigs")]
    [InlineData("nullHistory")]
    [InlineData("nullOffer")]
    [InlineData("nullGig")]
    [InlineData("nullHistoryEntry")]
    [InlineData("duplicateOffers")]
    [InlineData("duplicateAcrossLists")]
    [InlineData("duplicateHistory")]
    [InlineData("idTooHigh")]
    [InlineData("negativeFee")]
    [InlineData("zeroGuests")]
    [InlineData("negativeReputation")]
    [InlineData("highReputation")]
    [InlineData("nanReputation")]
    [InlineData("infiniteReputation")]
    public void BadStateFailsValidationThroughThePublicApi(string problem)
    {
        var state = Book();
        var gig = state.Gigs[0];
        switch (problem)
        {
            case "nullOffers": state.Offers = null!; break;
            case "nullGigs": state.Gigs = null!; break;
            case "nullHistory": state.History = null!; break;
            case "nullOffer": state.Offers.Add(null!); break;
            case "nullGig": state.Gigs.Add(null!); break;
            case "nullHistoryEntry": state.History.Add(null!); break;
            case "duplicateOffers": state.Offers.AddRange(new[] { CateringOffersTests.Offer(), CateringOffersTests.Offer() }); break;
            case "duplicateAcrossLists": state.Offers.Add(CateringOffersTests.Offer()); break;
            case "duplicateHistory": state.History.Add(gig); break;
            case "idTooHigh": gig.Id = state.NextOfferId; break;
            case "negativeFee": gig.FeeCents = -1; break;
            case "zeroGuests": gig.Guests = 0; break;
            case "negativeReputation": state.BookingReputation = -0.1; break;
            case "highReputation": state.BookingReputation = 1.1; break;
            case "nanReputation": state.BookingReputation = double.NaN; break;
            case "infiniteReputation": state.BookingReputation = double.PositiveInfinity; break;
        }
        Assert.Throws<JsonSerializationException>(() => CateringGigs.MissOverdue(rules, state, 3, 720));
    }

    [Fact]
    public void StateKeepsEnumNamesAndUnknownFieldsThroughBookingAndCompletion()
    {
        var state = CateringState.New(42);
        state.NextOfferId = 2;
        state.ExtensionData["future"] = new JValue(7);
        var offer = CateringOffersTests.Offer();
        offer.ExtensionData["note"] = new JValue(true);
        state.Offers.Add(offer);
        CateringOffers.Accept(rules, state, 1, 1, 0, 1);
        CateringGigs.Arrive(rules, state, 1, "truck-1", "truck", 3, 731, menu, stock, Category);
        var root = JObject.Parse(JsonConvert.SerializeObject(state));
        Assert.Equal("Failed", root["History"]![0]!["Status"]!.Value<string>());
        Assert.Equal("Late", root["History"]![0]!["Failure"]!.Value<string>());
        Assert.Equal(7, root["future"]!.Value<int>());
        Assert.True(root["History"]![0]!["note"]!.Value<bool>());
        var restored = JsonConvert.DeserializeObject<CateringState>(root.ToString())!;
        Assert.Empty(CateringGigs.MissOverdue(rules, restored, 4, 0));
        Assert.Equal(int.MinValue, CateringState.New(42).LastOfferDay);
    }
}
