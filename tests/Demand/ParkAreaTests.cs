using System;
using FoodTrucks.Core.Demand;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Demand;

// The park area (Peter, 14 October 2026): its own weekend curve, season and rain factors, the game's park areas in the
// classifier, and zones that set it.
public class ParkAreaTests
{
    private const string HotDog = "ba:itemname_hotdog";
    private const string IceCream = "ba:itemname_icecream";

    private static readonly DemandModel model = new(ShippedData.Tables);
    private static readonly AreaTable park = ShippedData.Tables.Area("park");

    private static DemandQuery Query(DayOfWeek day, int hour, int month = 5, bool rain = false, string item = HotDog) => new()
    {
        Areas = AreaMix.Single("park"),
        Menu = new[] { item },
        MinuteOfDay = hour * 60,
        Day = day,
        Month = month,
        Raining = rain
    };

    // The park's own crowd in passers an hour, without the street baseline.
    private static double ParkPassers(DemandQuery query)
    {
        var estimate = model.Estimate(query);
        return estimate.AreaFootfall.TryGetValue("park", out var passers) ? passers : 0;
    }

    [Fact]
    public void TheShippedParkHasAWeekendCurveSeasonsAndRain()
    {
        Assert.True(park.HasWeekendCurve);
        Assert.Equal(1.4, park.Footfall(Daypart.Lunch, DayOfWeek.Wednesday));
        Assert.Equal(2.8, park.Footfall(Daypart.Afternoon, DayOfWeek.Saturday));
        Assert.Equal(2.8, park.Footfall(Daypart.Afternoon, DayOfWeek.Sunday));
        Assert.Equal(1.3, park.SeasonFactor(Season.Summer));
        Assert.Equal(0.35, park.SeasonFactor(Season.Winter));
        Assert.Equal(0.4, park.Rain);
    }

    [Fact]
    public void AreasWithoutTheNewFieldsAreUnchanged()
    {
        var office = ShippedData.Tables.Area("office");
        Assert.False(office.HasWeekendCurve);
        Assert.Equal(office.Footfall(Daypart.Lunch), office.Footfall(Daypart.Lunch, DayOfWeek.Saturday));
        Assert.Equal(1.0, office.SeasonFactor(Season.Winter));
        Assert.Equal(1.0, office.Rain);
    }

    [Fact]
    public void AWeekdayParkHasALunchBump()
    {
        var morning = ParkPassers(Query(DayOfWeek.Wednesday, 9));
        var lunch = ParkPassers(Query(DayOfWeek.Wednesday, 12));
        var afternoon = ParkPassers(Query(DayOfWeek.Wednesday, 15));

        Assert.True(lunch > morning && lunch > afternoon);
        Assert.Equal(1.4 * ShippedData.Tables.PassersPerHour, lunch, 9);
    }

    [Fact]
    public void WeekendDaytimeIsTheParksBusiestTime()
    {
        var saturday = ParkPassers(Query(DayOfWeek.Saturday, 15));
        var wednesday = ParkPassers(Query(DayOfWeek.Wednesday, 15));

        Assert.Equal(2.8 * ShippedData.Tables.PassersPerHour, saturday, 9);
        Assert.True(saturday > 2 * wednesday);
        // Late morning to early evening: lunch and afternoon beat the morning and the evening on a Sunday.
        Assert.True(ParkPassers(Query(DayOfWeek.Sunday, 12)) > ParkPassers(Query(DayOfWeek.Sunday, 9)));
        Assert.True(ParkPassers(Query(DayOfWeek.Sunday, 15)) > ParkPassers(Query(DayOfWeek.Sunday, 18)));
    }

    [Fact]
    public void ParksAreEmptyAtNight()
    {
        Assert.Equal(0, ParkPassers(Query(DayOfWeek.Saturday, 3)));
        Assert.True(ParkPassers(Query(DayOfWeek.Saturday, 22)) < 0.05 * ParkPassers(Query(DayOfWeek.Saturday, 15)));
    }

    [Fact]
    public void AfterMidnightTheNightBelongsToTheDayBefore()
    {
        // 01:00 on Monday is still Sunday's late hours (weekend curve), 01:00 on Saturday still Friday's (weekday curve).
        var mondayNight = ParkPassers(Query(DayOfWeek.Monday, 1));
        var saturdayNight = ParkPassers(Query(DayOfWeek.Saturday, 1));

        Assert.Equal(0.1 * ShippedData.Tables.PassersPerHour, mondayNight, 9);
        Assert.Equal(0.05 * 1.1 * ShippedData.Tables.PassersPerHour, saturdayNight, 9);
    }

    [Fact]
    public void SummerIsStrongerAndWinterWeakerOnTopOfTheMonth()
    {
        var may = ParkPassers(Query(DayOfWeek.Saturday, 15, month: 5));
        var july = ParkPassers(Query(DayOfWeek.Saturday, 15, month: 7));
        var january = ParkPassers(Query(DayOfWeek.Saturday, 15, month: 1));

        Assert.Equal(may * 1.3, july, 9);
        // January's month footfall 0.6 times the park's winter 0.35.
        Assert.Equal(may * 0.6 * 0.35, january, 9);
        // The office crowd has no season factor: January is only the month's 0.6.
        var office = new DemandQuery { Areas = AreaMix.Single("office"), Menu = new[] { HotDog }, MinuteOfDay = 720, Day = DayOfWeek.Wednesday, Month = 1 };
        var officeMay = new DemandQuery { Areas = AreaMix.Single("office"), Menu = new[] { HotDog }, MinuteOfDay = 720, Day = DayOfWeek.Wednesday, Month = 5 };
        Assert.Equal(model.Estimate(officeMay).AreaFootfall["office"] * 0.6, model.Estimate(office).AreaFootfall["office"], 9);
    }

    [Fact]
    public void RainEmptiesTheParkMoreThanTheStreet()
    {
        var dry = model.Estimate(Query(DayOfWeek.Saturday, 15));
        var wet = model.Estimate(Query(DayOfWeek.Saturday, 15, rain: true));

        Assert.Equal(dry.AreaFootfall["park"] * ShippedData.Tables.RainFootfall * 0.4, wet.AreaFootfall["park"], 9);
        Assert.Equal(dry.Baseline * ShippedData.Tables.RainFootfall, wet.Baseline, 9);
    }

    [Fact]
    public void ParkCrowdsWantIceCreamMoreThanTheStreet()
    {
        // Saturday 15:00 in May: the park's crowd 2.8 at ice cream x2.2, the street's 0.5 at x1.
        var estimate = model.Estimate(Query(DayOfWeek.Saturday, 15, item: IceCream));
        Assert.Equal(0.20 * (0.5 + 2.2 * 2.8) / (0.5 + 2.8), estimate.Appetite, 9);
    }

    [Fact]
    public void TheGamesParkAreasVoteForPark()
    {
        var classifier = new AreaClassifier(ShippedData.Tables);
        var inside = classifier.FromPlaces(new[] { new NearbyPlace("parkarea", null, 0) });
        var edge = classifier.FromPlaces(new[] { new NearbyPlace("parkarea", null, 50) });
        var skate = classifier.FromPlaces(new[] { new NearbyPlace("skatepark", null, 0) });
        var saturation = ShippedData.Tables.Classifier.Saturation;

        Assert.Equal(1 - Math.Exp(-1.5 / saturation), inside.PresenceOf("park"), 12);
        Assert.Equal(1 - Math.Exp(-1.5 * 0.5 / saturation), edge.PresenceOf("park"), 12);
        Assert.Equal(1 - Math.Exp(-0.75 / saturation), skate.PresenceOf("park"), 12);
        Assert.Single(inside.Presences);
        Assert.True(classifier.Draws("park", "parkarea", null));
    }

    [Fact]
    public void AZoneCanMakeAPark()
    {
        var zones = JObject.Parse(ShippedData.Zones);
        zones["zones"] = JArray.Parse("[{\"id\":\"square\",\"mode\":\"add\",\"areas\":{\"park\":0.8},\"polygon\":[[0,0],[10,0],[10,10],[0,10]]}]");
        var tables = DemandTables.Parse(ShippedData.Demand, ShippedData.Economy, zones.ToString());
        var classifier = new AreaClassifier(tables);

        var mix = classifier.Classify(new[] { new NearbyPlace("ba:buildingtype_office", null, 10) }, 5, 5);
        Assert.Equal(0.8, mix.PresenceOf("park"));
        Assert.True(mix.PresenceOf("office") > 0);
        Assert.Equal(0, classifier.Classify(Array.Empty<NearbyPlace>(), 20, 20).PresenceOf("park"));
    }

    [Theory]
    [InlineData("weekendFootfall", "{\"early\":1}", "areas.park.weekendFootfall")]
    [InlineData("seasons", "{\"monsoon\":1}", "areas.park.seasons")]
    [InlineData("rain", "-1", "areas.park.rain")]
    [InlineData("crowds", "1", "areas.park")]
    public void BadParkFieldsNameTheField(string field, string value, string path)
    {
        var demand = JObject.Parse(ShippedData.Demand);
        demand["areas"]!["park"]![field] = JToken.Parse(value);

        var error = Assert.Throws<DemandDataException>(() => DemandTables.Parse(demand.ToString(), ShippedData.Economy, ShippedData.Zones));
        Assert.Contains("demand.json", error.Message);
        Assert.Contains(path, error.Message);
    }
}
