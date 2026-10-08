using System;
using System.Linq;
using FoodTrucks.Core.Demand;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

public class LiveCrowdsTests
{
    private static readonly LiveCrowds live = ShippedData.Rules.LiveCrowds;

    private static VenueNow Venue(VenueKind kind, bool open, int? closedAgo = null, int? workers = null) =>
        new() { Kind = kind, Open = open, ClosedMinutesAgo = closedAgo, Workers = workers };

    [Fact]
    public void AClosedClubBringsNobodyUntilItsGuestsComeOut()
    {
        Assert.Equal(1.0, live.Weight(Venue(VenueKind.Nightclub, true), 23 * 60, DayOfWeek.Friday));
        Assert.Equal(0.0, live.Weight(Venue(VenueKind.Nightclub, false), 14 * 60, DayOfWeek.Friday));
        Assert.Equal(2.5, live.Weight(Venue(VenueKind.Nightclub, false, closedAgo: 10), 4 * 60 + 10, DayOfWeek.Saturday));
        Assert.Equal(0.0, live.Weight(Venue(VenueKind.Nightclub, false, closedAgo: 45), 4 * 60 + 45, DayOfWeek.Saturday));
    }

    [Fact]
    public void TheatresLetOutAtHalfPastTenAndForTheWeekendMatinee()
    {
        Assert.Equal(1.0, live.Weight(Venue(VenueKind.Theatre, true), 20 * 60, DayOfWeek.Wednesday));
        Assert.Equal(2.5, live.Weight(Venue(VenueKind.Theatre, true), 22 * 60 + 40, DayOfWeek.Wednesday));
        Assert.Equal(2.5 * 1.5, live.Weight(Venue(VenueKind.Theatre, true), 22 * 60 + 40, DayOfWeek.Saturday));
        Assert.Equal(1.0, live.Weight(Venue(VenueKind.Theatre, true), 17 * 60 + 10, DayOfWeek.Wednesday));
        Assert.Equal(2.5 * 1.5, live.Weight(Venue(VenueKind.Theatre, true), 17 * 60 + 10, DayOfWeek.Sunday));
        // The doors close as the last audience leaves: the surge still comes.
        Assert.Equal(2.5, live.Weight(Venue(VenueKind.Theatre, false, closedAgo: 10), 22 * 60 + 40, DayOfWeek.Wednesday));
    }

    [Fact]
    public void CinemasEmptyEveryTwoAndAHalfHoursFromFiveIncludingAfterMidnight()
    {
        var surges = Enumerable.Range(0, 1440).Where(m => live.Weight(Venue(VenueKind.Cinema, true), m, DayOfWeek.Tuesday) > 1).ToList();
        Assert.Contains(17 * 60, surges);
        Assert.Contains(19 * 60 + 30, surges);
        Assert.Contains(22 * 60, surges);
        Assert.Contains(30, surges);
        Assert.DoesNotContain(16 * 60, surges);
    }

    [Fact]
    public void OfficesBringTheirStaffAtWork()
    {
        Assert.Equal(2.0, live.Weight(Venue(VenueKind.Office, true, workers: 40), 12 * 60, DayOfWeek.Monday));
        Assert.Equal(0.5, live.Weight(Venue(VenueKind.Office, true, workers: 5), 12 * 60, DayOfWeek.Monday));
        Assert.Equal(0.0, live.Weight(Venue(VenueKind.Office, false, workers: 0), 12 * 60, DayOfWeek.Sunday));
        Assert.Equal(1.0, live.Weight(Venue(VenueKind.Office, true), 12 * 60, DayOfWeek.Monday));
    }

    [Fact]
    public void KindsComeFromTheData()
    {
        Assert.Equal(VenueKind.Theatre, live.KindOf("ba:businesstype_theater"));
        Assert.Equal(VenueKind.Other, live.KindOf("ba:businesstype_bakery"));
        Assert.Equal(VenueKind.Other, live.KindOf(null));
    }

    [Fact]
    public void AClosedVenueAddsNoCrowdToThePitch()
    {
        var classifier = new AreaClassifier(ShippedData.Tables);
        var open = classifier.FromPlaces(new[] { new NearbyPlace("ba:buildingtype_retail", "ba:businesstype_nightclub", 20, 1.0) });
        var closed = classifier.FromPlaces(new[] { new NearbyPlace("ba:buildingtype_retail", "ba:businesstype_nightclub", 20, 0.0) });

        Assert.True(open.PresenceOf("nightlife") > 0);
        Assert.Equal(0, closed.PresenceOf("nightlife"));
    }
}
