using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FoodTrucks.Core.Common;
using FoodTrucks.Core.Demand;
using FoodTrucks.Core.Pitches;
using FoodTrucks.Core.Saves;
using FoodTrucks.Core.Tests.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Pitches;

public class PitchTablesTests
{
    internal static PitchTables Shipped => PitchTables.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "pitches.json")), ShippedData.Tables);

    [Fact]
    public void ShippedFileHasTwoOrThreeSpotsInEveryNeighbourhoodWorth()
    {
        var tables = Shipped;
        Assert.InRange(tables.Spots.Count, 15, 20);
        Assert.Equal(tables.Spots.Count, tables.Spots.Select(s => s.Address).Distinct().Count());
        Assert.All(tables.Spots, s => Assert.True(s.Footfall >= 1));
        Assert.Contains(tables.Kinds, k => k.Id == "market");
        Assert.All(tables.Places, p => Assert.All(p.Kinds, k => Assert.NotNull(tables.Kind(k))));
        Assert.All(tables.Kinds, k => Assert.Contains(tables.Places, p => p.Kinds.Contains(k.Id)));
    }

    [Theory]
    [InlineData("\"office\": 1.0", "\"offices\": 1.0", "is not an area")]
    [InlineData("\"address\": \"ba:street_fifthstreet#2\"", "\"address\": \"ba:street_fifthstreet\"", "street#number")]
    [InlineData("\"end\": \"16:00\"", "\"end\": \"09:00\"", "must be after start")]
    [InlineData("\"kinds\": [\n          \"market\"\n        ]", "\"kinds\": [\n          \"parade\"\n        ]", "one of the event kinds")]
    public void BadEntriesFailWithTheirPath(string from, string to, string message)
    {
        var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "pitches.json")).Replace("\r\n", "\n");
        Assert.Contains(from, json);
        var broken = json.Replace(from, to);
        var error = Assert.Throws<DemandDataException>(() => PitchTables.Parse(broken, ShippedData.Tables));
        Assert.Contains(message, error.Message);
        Assert.Contains("pitches.json", error.Message);
    }
}

public class CityEventsTests
{
    private static readonly PitchTables tables = PitchTablesTests.Shipped;
    private static readonly CityEvents events = new(tables);

    [Fact]
    public void AnnouncesOnlyOnTheKindsWeekdaysAndAhead()
    {
        var state = new EventsState();
        var all = new List<CityEvent>();
        for (var day = 1; day <= 140; day++) all.AddRange(events.Announce(state, day));

        Assert.NotEmpty(all);
        foreach (var city in all)
        {
            var kind = tables.Kind(city.Kind)!;
            Assert.Contains(GameDays.Weekday(city.Day), kind.Days);
            Assert.Equal(city.AnnouncedDay + kind.LeadDays, city.Day);
            Assert.Contains(city.Kind, tables.Place(city.PlaceId)!.Kinds);
        }
        // About chance × weekdays a week each: 0.6 × 2 markets and 0.4 × 2 festivals.
        Assert.InRange(all.Count / 20.0, 1.2, 2.8);
        Assert.Equal(all.Count, all.Select(e => e.Id).Distinct().Count());
    }

    [Fact]
    public void SameSaveGetsTheSameEventsAndSkippedDaysCatchUp()
    {
        var daily = new EventsState();
        for (var day = 10; day <= 30; day++) events.Announce(daily, day);
        var skipped = new EventsState();
        events.Announce(skipped, 10);
        events.Announce(skipped, 30);

        // Days 11 to 30 are announced at once; events whose day is before 30 are not announced late.
        var expected = daily.Events.Where(e => e.Day >= 30).Select(e => (e.Kind, e.PlaceId, e.Day)).ToList();
        var got = skipped.Events.Where(e => e.AnnouncedDay > 10).Select(e => (e.Kind, e.PlaceId, e.Day)).ToList();
        Assert.Equal(expected, got);
        Assert.All(skipped.Events, e => Assert.True(e.Day >= e.AnnouncedDay));
    }

    [Fact]
    public void PlacesTheGameCantFindGetNoEvents()
    {
        var state = new EventsState();
        var only = tables.Places[0].Id;
        for (var day = 1; day <= 60; day++) events.Announce(state, day, p => p.Id == only);
        Assert.NotEmpty(state.Events);
        Assert.All(state.Events, e => Assert.Equal(only, e.PlaceId));
    }

    [Fact]
    public void ANewSaveStartsAnnouncingToday()
    {
        var state = new EventsState();
        events.Announce(state, 200);
        Assert.Equal(200, state.LastAnnouncedDay);
        Assert.All(state.Events, e => Assert.Equal(200, e.AnnouncedDay));
    }

    [Fact]
    public void BookingTakesPlacesUntilFullOrOver()
    {
        var state = new EventsState();
        var city = new CityEvent { Id = 7, Kind = "market", PlaceId = "p", Day = 5, Start = 600, End = 960, Places = 2, FeeCents = 15000 };
        state.Events.Add(city);
        var before = 3 * 1440L;

        Assert.Equal(BookingResult.Booked, CityEvents.Book(state, 7, before));
        Assert.Equal(BookingResult.Booked, CityEvents.Book(state, 7, before));
        Assert.Equal(BookingResult.Full, CityEvents.Book(state, 7, before));
        Assert.Equal(BookingResult.NotFound, CityEvents.Book(state, 8, before));
        city.Booked = 0;
        Assert.Equal(BookingResult.Over, CityEvents.Book(state, 7, CityEvents.EndsAt(city)));
        Assert.Empty(CityEvents.Bookable(state, CityEvents.EndsAt(city)));
    }

    [Fact]
    public void BookedPlacesGoToTheFirstVehiclesThere()
    {
        var city = new CityEvent { Id = 1, Booked = 1, Places = 2, Day = 5, Start = 600, End = 960 };
        Assert.True(CityEvents.Claim(city, "cart", 6360));
        Assert.True(CityEvents.Claim(city, "cart", 6370));
        Assert.False(CityEvents.Claim(city, "truck", 6370));
        city.Booked = 2;
        Assert.True(CityEvents.Claim(city, "truck", 6370));

        var unbooked = new CityEvent { Id = 2, Booked = 0, Places = 2 };
        Assert.False(CityEvents.Claim(unbooked, "cart", 6360));
    }

    [Fact]
    public void APlaceComesBackWhenItsVehicleClosesAndCatchUpOrderCantShareIt()
    {
        var state = new EventsState();
        var city = new CityEvent { Id = 1, Booked = 1, Places = 2, Day = 5, Start = 600, End = 960 };
        state.Events.Add(city);
        var ten = 4 * 1440L + 600;
        // A cart is caught up first: open from 10:00, closed at 12:00.
        Assert.True(CityEvents.Claim(city, "cart", ten));
        Assert.Equal(1, CityEvents.Release(state, "cart", ten + 120));
        // Then a truck opened at 11:30 in the same skip: the cart still held the place then.
        Assert.False(CityEvents.CanClaim(city, "truck", ten + 90));
        Assert.False(CityEvents.Claim(city, "truck", ten + 90));
        // From 12:00 the place is free.
        Assert.True(CityEvents.Claim(city, "truck", ten + 120));
    }

    [Fact]
    public void SeparateVisitsTakeOnePlaceAtATime()
    {
        var city = new CityEvent { Id = 1, Booked = 2, Places = 2, Day = 5, Start = 600, End = 960 };
        var ten = 4 * 1440L + 600;
        // A cart caught up first: there 10:00 to 11:00 and again 12:00 to 13:00.
        Assert.True(CityEvents.Claim(city, "cart", ten));
        Holds.Release(city.Holds, "cart", ten + 60);
        Assert.True(CityEvents.Claim(city, "cart", ten + 120));
        Holds.Release(city.Holds, "cart", ten + 180);
        // A truck from 10:30: the cart never holds more than one of the two places.
        Assert.True(CityEvents.Claim(city, "truck", ten + 30));
        // A third vehicle at 12:30 finds both taken.
        Assert.False(CityEvents.Claim(city, "van", ten + 150));
        // At 11:30 only the truck is there, but the truck runs on and the cart comes back at 12:00: still full.
        Assert.False(CityEvents.Claim(city, "van", ten + 90));
    }

    [Fact]
    public void AWindowThatClosedAsItOpenedHeldNothing()
    {
        var city = new CityEvent { Id = 1, Booked = 1, Places = 1, Day = 5, Start = 600, End = 960 };
        var noon = 4 * 1440L + 720;
        Assert.True(CityEvents.Claim(city, "truck", noon));
        Holds.Release(city.Holds, "truck", noon);
        Assert.True(CityEvents.Claim(city, "cart", noon - 30));
    }

    [Fact]
    public void ACatchUpInTheOtherOrderCantShareAPlaceEither()
    {
        var city = new CityEvent { Id = 1, Booked = 1, Places = 2, Day = 5, Start = 600, End = 960 };
        var ten = 4 * 1440L + 600;
        // The truck that opened at 11:30 is caught up first and still holds the place.
        Assert.True(CityEvents.Claim(city, "truck", ten + 90));
        // The cart that opened at 10:00 would overlap it from 11:30: it goes without.
        Assert.False(CityEvents.Claim(city, "cart", ten));
    }

    [Fact]
    public void RunsFromStartUntilEndAndExpiresAfterItsDay()
    {
        var state = new EventsState();
        var city = new CityEvent { Id = 1, PlaceId = "p", Day = 5, Start = 600, End = 960 };
        state.Events.Add(city);
        var start = 4 * 1440L + 600;
        Assert.False(CityEvents.Running(city, start - 1));
        Assert.True(CityEvents.Running(city, start));
        Assert.Same(city, CityEvents.RunningAt(state, "p", start + 359));
        Assert.Null(CityEvents.RunningAt(state, "p", start + 360));
        // An event stays while a window still has to sell through it, days later after a long skip.
        Assert.Equal(0, CityEvents.Expire(state, 9, start + 359));
        Assert.Equal(0, CityEvents.Expire(state, 5, start + 400));
        Assert.Equal(1, CityEvents.Expire(state, 6, start + 360));
    }
}

public class PremiumSpotsTests
{
    private static readonly PitchTables tables = PitchTablesTests.Shipped;

    [Fact]
    public void TheNearestSpotWithinTheRadiusCounts()
    {
        var first = tables.Spots[0];
        var second = tables.Spots[1];
        IEnumerable<(double, double)>? Doors(PremiumSpot s) => s == first ? new[] { (0.0, 0.0) } : s == second ? new[] { (30.0, 0.0) } : null;

        Assert.Same(first, PremiumSpots.At(tables, Doors, 10, 0));
        Assert.Same(second, PremiumSpots.At(tables, Doors, 20, 0));
        Assert.Null(PremiumSpots.At(tables, Doors, 0, tables.PremiumRadiusMetres + 1));
    }

    [Fact]
    public void FirstOpenVehicleHoldsTheSpotUntilItCloses()
    {
        var state = new SaveState();
        var cart = new VehicleState { VehicleId = "cart", IsOpen = true };
        var truck = new VehicleState { VehicleId = "truck", IsOpen = true };
        state.Vehicles.Add(cart);
        state.Vehicles.Add(truck);

        Assert.Equal((PremiumClaim.Held, "cart"), PremiumSpots.Claim(state, cart, "spot", 600));
        Assert.Equal((PremiumClaim.Taken, "cart"), PremiumSpots.Claim(state, truck, "spot", 650));
        Assert.Null(truck.PremiumSpot);

        cart.IsOpen = false;
        PremiumSpots.Release(state, cart, 720);
        Assert.Null(cart.PremiumSpot);
        // A catch-up that reaches the truck after the cart: at 11:30 the cart still held it.
        Assert.Equal((PremiumClaim.Taken, "cart"), PremiumSpots.Claim(state, truck, "spot", 690));
        Assert.Equal((PremiumClaim.Held, "truck"), PremiumSpots.Claim(state, truck, "spot", 720));
        PremiumSpots.Prune(state, 720);
        Assert.Single(state.PremiumHolds["spot"]);
    }

    [Fact]
    public void BonusRaisesAreasAndMultipliesFootfall()
    {
        var spot = new PremiumSpot("s", "n", "a#1", AreaMix.FromPresences(new Dictionary<string, double> { ["office"] = 1.0 }), 1.25);
        var kind = tables.Kind("market")!;
        var bonus = PitchBonus.Of(spot).And(PitchBonus.Of(kind));
        var mix = bonus.Apply(AreaMix.FromPresences(new Dictionary<string, double> { ["office"] = 0.4, ["residential"] = 0.6 }));

        Assert.Equal(1.0, mix.PresenceOf("office"));
        Assert.Equal(0.6, mix.PresenceOf("residential"));
        Assert.Equal(1.0, mix.PresenceOf("retail"));
        Assert.Equal(1.25 * kind.Footfall, bonus.Footfall, 9);
        Assert.Same(mix, PitchBonus.None.Apply(mix));
    }

    [Fact]
    public void AnEventsCrowdComesWhateverTheVenuesNearbyAreDoing()
    {
        var model = new DemandModel(ShippedData.Tables);
        var nightlife = AreaMix.FromPresences(new Dictionary<string, double> { ["nightlife"] = 0.9 });
        DemandQuery Query(AreaMix? pitch) => new()
        {
            Areas = nightlife,
            Menu = new[] { "ba:itemname_hotdog" },
            MinuteOfDay = 20 * 60,
            Day = DayOfWeek.Saturday,
            Month = 5,
            AreaLive = new Dictionary<string, double> { ["nightlife"] = 0.0 },
            PitchAreas = pitch
        };
        var closed = model.Estimate(Query(null));
        var festival = model.Estimate(Query(AreaMix.FromPresences(new Dictionary<string, double> { ["nightlife"] = 1.0 })));
        Assert.False(closed.AreaFootfall.TryGetValue("nightlife", out var none) && none > 0);
        Assert.True(festival.AreaFootfall["nightlife"] > 0);
    }

    [Fact]
    public void PitchFootfallMultipliesCustomersInTheModel()
    {
        var model = new DemandModel(ShippedData.Tables);
        DemandQuery Query(double pitch) => new()
        {
            Areas = AreaMix.FromPresences(new Dictionary<string, double> { ["office"] = 0.8 }),
            Menu = new[] { "ba:itemname_hotdog" },
            MinuteOfDay = 12 * 60,
            Day = DayOfWeek.Tuesday,
            Month = 5,
            PitchFootfall = pitch
        };
        var plain = model.Estimate(Query(1.0));
        var premium = model.Estimate(Query(1.25));
        Assert.Equal(plain.CustomersPerHour * 1.25, premium.CustomersPerHour, 9);
        Assert.Equal(plain.Appetite, premium.Appetite, 12);
    }
}

public class PartnershipsTests
{
    private static readonly PartnershipRules rules = new();

    [Fact]
    public void OneVenuePerVehicleAndOneVehiclePerVenue()
    {
        var state = new SaveState();
        Assert.Equal(PartnerProblem.None, Partnerships.Sign(state, "cart", "ba:street_a#1", 3));
        Assert.Equal(PartnerProblem.VehicleHasPartner, Partnerships.Sign(state, "cart", "ba:street_b#2", 3));
        Assert.Equal(PartnerProblem.VenueHasPartner, Partnerships.Sign(state, "truck", "ba:street_a#1", 3));
        Assert.True(Partnerships.End(state, "cart"));
        Assert.Equal(PartnerProblem.None, Partnerships.Sign(state, "truck", "ba:street_a#1", 4));
        Assert.Equal(1, Partnerships.Prune(state, p => p.VehicleId != "truck"));
        Assert.Empty(state.Partnerships);
    }

    [Fact]
    public void ADayCountsOnceItsMinutesReachTheMinimum()
    {
        var partnership = new PartnershipState { VehicleId = "cart", Venue = "v#1" };
        Partnerships.Worked(partnership, rules, 5, 40);
        Assert.Null(partnership.LastWorkedDay);
        Partnerships.Worked(partnership, rules, 6, 40);
        Assert.Null(partnership.LastWorkedDay);
        Partnerships.Worked(partnership, rules, 6, 20);
        Assert.Equal(6, partnership.LastWorkedDay);

        // Only on a score the game worked out from paying customers that day.
        Assert.False(Partnerships.BonusDue(partnership, 7));
        partnership.ScoredDay = 7;
        Assert.True(Partnerships.BonusDue(partnership, 7));
        // Once per worked day.
        Partnerships.BonusGiven(partnership);
        Assert.False(Partnerships.BonusDue(partnership, 7));
        partnership.ScoredDay = 8;
        Assert.False(Partnerships.BonusDue(partnership, 8));

        // Working the next day too: its bonus waits for the next morning's score, never two on one score.
        partnership.ScoredDay = 7;
        partnership.LastWorkedDay = 7;
        Assert.False(Partnerships.BonusDue(partnership, 7));
        partnership.ScoredDay = 8;
        Assert.True(Partnerships.BonusDue(partnership, 8));

        // Too old for the score of day 9, which counts the orders of days 8 and 9.
        var old = new PartnershipState { VehicleId = "cart", Venue = "v#1", LastWorkedDay = 6, ScoredDay = 9 };
        Assert.False(Partnerships.BonusDue(old, 9));
    }

    [Fact]
    public void OnlySurgesGrowAndTheBonusFollowsTheGamesMean()
    {
        var live = ShippedData.Rules.LiveCrowds;
        var club = new VenueNow { Kind = VenueKind.Nightclub, Open = true, Guests = 40 };
        // A busy club's guests are not a surge: the partner's boost leaves them alone.
        Assert.Equal(live.Weight(club, 23 * 60, DayOfWeek.Friday), live.Weight(club, 23 * 60, DayOfWeek.Friday, rules.Surge));
        // A quiet club's closing: the surge part grows, then the guests scale it.
        var closing = new VenueNow { Kind = VenueKind.Nightclub, Open = false, ClosedMinutesAgo = 5, Guests = 6 };
        Assert.Equal((1 + (live.ClubClosingSurge - 1) * rules.Surge) * 0.3, live.Weight(closing, 3 * 60 + 5, DayOfWeek.Saturday, rules.Surge), 9);
        Assert.Equal((100, 85), Partnerships.WithBonus(80, 80, 80, 98, rules));
        Assert.Equal((65, 68), Partnerships.WithBonus(70, 70, 70, 60, rules));
    }
}

public class GuestCountTests
{
    [Fact]
    public void APlayersVenueBringsItsGuestsAndAnAiOneItsUsualCrowd()
    {
        var live = ShippedData.Rules.LiveCrowds;
        Assert.Equal(1.0, live.Weight(new VenueNow { Kind = VenueKind.Nightclub, Open = true }, 23 * 60, DayOfWeek.Friday));
        Assert.Equal(1.5, live.Weight(new VenueNow { Kind = VenueKind.Nightclub, Open = true, Guests = 30 }, 23 * 60, DayOfWeek.Friday), 9);
        Assert.Equal(live.MinGuestWeight, live.Weight(new VenueNow { Kind = VenueKind.Nightclub, Open = true, Guests = 0 }, 23 * 60, DayOfWeek.Friday), 9);
        // A show letting out on a weekday: surge times the guests.
        Assert.Equal(live.TheatreSurge * 0.5, live.Weight(new VenueNow { Kind = VenueKind.Theatre, Open = true, Guests = 10 }, 22 * 60 + 40, DayOfWeek.Tuesday), 9);
        Assert.Equal(live.ClosedWeight, live.Weight(new VenueNow { Kind = VenueKind.Theatre, Open = false, Guests = 40 }, 12 * 60, DayOfWeek.Tuesday));
    }
}

public class PitchSaveTests
{
    [Fact]
    public void EventsAndPartnershipsRoundTripAndOldSavesReadEmpty()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        state.Events.LastAnnouncedDay = 4;
        state.Events.Events.Add(new CityEvent { Id = 3, Kind = "market", PlaceId = "p", Day = 8, Start = 600, End = 960, Places = 2, Booked = 1, Holds = { new PlaceHold { VehicleId = "cart", From = 10680 } } });
        state.Partnerships.Add(new PartnershipState { VehicleId = "cart", Venue = "ba:street_a#1", SignedDay = 2, LastWorkedDay = 3 });
        state.Vehicles.Add(new VehicleState { VehicleId = "cart", PremiumSpot = "midtown-fifth-street" });

        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal("cart", loaded.State!.Events.Events.Single().Holds.Single().VehicleId);
        Assert.Equal(3, loaded.State.Partnerships.Single().LastWorkedDay);
        Assert.Equal("midtown-fifth-street", loaded.State.Vehicles.Single().PremiumSpot);

        var old = codec.Load("{\"schemaVersion\":2}");
        Assert.Equal(LoadStatus.Loaded, old.Status);
        Assert.Empty(old.State!.Events.Events);
        Assert.Empty(old.State.Partnerships);
    }

    [Fact]
    public void AnUnpaidCateringFeeRoundTripsAndOldSavesReadNone()
    {
        var codec = new SaveCodec();
        var state = new SaveState();
        state.Catering.Unpaid.Add(new FoodTrucks.Core.Catering.UnpaidFee { GigId = 4, VehicleId = "truck", PlaceId = "ba:street_a#1", FeeCents = 6250, Units = 40 });
        state.Catering.NextOfferId = 5;
        var loaded = codec.Load(codec.Serialize(state));
        Assert.Equal(LoadStatus.Loaded, loaded.Status);
        Assert.Equal(6250, loaded.State!.Catering.Unpaid.Single().FeeCents);
        Assert.Empty(codec.Load("{\"schemaVersion\":2}").State!.Catering.Unpaid);
    }

    [Fact]
    public void TwoPartnershipsForOneVenueAreRejected()
    {
        var codec = new SaveCodec();
        var json = "{\"schemaVersion\":2,\"partnerships\":[{\"vehicleId\":\"a\",\"venue\":\"v#1\"},{\"vehicleId\":\"b\",\"venue\":\"v#1\"}]}";
        Assert.Equal(LoadStatus.Corrupt, codec.Load(json).Status);
    }
}
