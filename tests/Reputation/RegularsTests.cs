using System;
using System.Linq;
using FoodTrucks.Core.Reputation;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace FoodTrucks.Core.Tests.Reputation;

public class RegularsTests
{
    private static readonly Regulars regulars = new(new RegularsRules());

    private static SpotVisit Visit(string key = "pitch", int minutes = 60) => new() { SpotKey = key, OpenMinutes = minutes };

    [Fact]
    public void SixWeeklyVisitsReachTheCapAndStayThere()
    {
        var state = new RegularsState();
        Assert.Equal(0, regulars.Bonus(state, "pitch", DayOfWeek.Monday));
        for (var week = 0; week < 10; week++)
        {
            regulars.EndOfDay(state, week * 7 + 1, new[] { Visit() });
            Assert.Equal(Math.Min(0.30, (week + 1) * 0.05), regulars.Bonus(state, "pitch", DayOfWeek.Monday), 10);
        }

        Assert.Equal(0.30, regulars.Bonus(state, "pitch", DayOfWeek.Monday));
        Assert.Equal(1.30, regulars.DemandFactor(state, "pitch", DayOfWeek.Monday));
        Assert.Equal(64, Assert.Single(state.Entries).LastVisitDay);
    }

    [Fact]
    public void MissingTheScheduledWeekdayRemovesAPenalty()
    {
        var state = new RegularsState();
        for (var week = 0; week < 6; week++) regulars.EndOfDay(state, week * 7 + 1, new[] { Visit() });

        regulars.EndOfDay(state, 43, Array.Empty<SpotVisit>());

        Assert.Equal(0.225, regulars.Bonus(state, "pitch", DayOfWeek.Monday), 10);
        Assert.Equal(36, Assert.Single(state.Entries).LastVisitDay);
    }

    [Fact]
    public void OtherWeekdaysAndOtherVisitedSpotsAreUntouched()
    {
        var state = new RegularsState();
        regulars.EndOfDay(state, 1, new[] { Visit() });
        regulars.EndOfDay(state, 2, new[] { Visit("other") });
        regulars.EndOfDay(state, 8, new[] { Visit("new") });

        Assert.Equal(0, regulars.Bonus(state, "pitch", DayOfWeek.Monday));
        Assert.Equal(0.05, regulars.Bonus(state, "other", DayOfWeek.Tuesday));
        Assert.Equal(0.05, regulars.Bonus(state, "new", DayOfWeek.Monday));
        Assert.Equal(2, state.Entries.Count);
    }

    [Fact]
    public void RepeatedAndEarlierDaysAreIgnored()
    {
        var state = new RegularsState();
        regulars.EndOfDay(state, 8, new[] { Visit() });
        regulars.EndOfDay(state, 8, new[] { Visit() });
        regulars.EndOfDay(state, 8, Array.Empty<SpotVisit>());
        regulars.EndOfDay(state, 1, new[] { Visit("other") });

        Assert.Equal(0.05, regulars.Bonus(state, "pitch", DayOfWeek.Monday));
        Assert.Single(state.Entries);
        Assert.Equal(8, state.LastUpdatedDay);
    }

    [Fact]
    public void SeveralVehiclesCountOnceAndShortVisitsDoNotAddUp()
    {
        var state = new RegularsState();
        regulars.EndOfDay(state, 1,
            new[] { Visit("short", 30), Visit("short", 30), Visit("pitch", 59), Visit(), Visit("pitch", 120) });

        Assert.Equal(0, regulars.Bonus(state, "short", DayOfWeek.Monday));
        Assert.Equal(0.05, regulars.Bonus(state, "pitch", DayOfWeek.Monday));
        Assert.Single(state.Entries);
        regulars.EndOfDay(state, 8, new[] { Visit("pitch", 59) });
        Assert.Empty(state.Entries);
    }

    [Fact]
    public void SpotKeysAreOrdinalAndVisitOrderDoesNotMatter()
    {
        var first = new RegularsState();
        var second = new RegularsState();
        var visits = new[] { Visit("pitch"), Visit("Pitch"), Visit("z") };

        regulars.EndOfDay(first, 1, visits);
        regulars.EndOfDay(second, 1, visits.Reverse());

        Assert.Equal(first.Entries.Select(e => (e.SpotKey, e.Weekday, e.Bonus)), second.Entries.Select(e => (e.SpotKey, e.Weekday, e.Bonus)));
        Assert.Equal(3, first.Entries.Count);
    }

    [Fact]
    public void CustomRulesControlGainPenaltyAndMinimumVisit()
    {
        var custom = new Regulars(new RegularsRules(gainPerVisit: 0.2, maxBonus: 0.4, missPenalty: 0.1, minOpenMinutes: 90));
        var state = new RegularsState();
        custom.EndOfDay(state, 1, new[] { Visit(minutes: 89) });
        Assert.Empty(state.Entries);
        custom.EndOfDay(state, 8, new[] { Visit(minutes: 90) });
        custom.EndOfDay(state, 15, Array.Empty<SpotVisit>());

        Assert.Equal(0.1, custom.Bonus(state, "pitch", DayOfWeek.Monday), 10);
    }

    [Fact]
    public void SkippedWeeksDecayBeforeTodaysVisit()
    {
        var state = new RegularsState();
        for (var day = 1; day <= 36; day += 7) regulars.EndOfDay(state, day, new[] { Visit() });
        Assert.Equal(0.30, regulars.Bonus(state, "pitch", DayOfWeek.Monday));

        regulars.EndOfDay(state, 71, new[] { Visit() });

        Assert.Equal(0.05, regulars.Bonus(state, "pitch", DayOfWeek.Monday));
        Assert.Equal(71, Assert.Single(state.Entries).LastVisitDay);
    }

    [Fact]
    public void CatchUpChargesEachMissedWeekday()
    {
        var state = new RegularsState { LastUpdatedDay = 1 };
        state.Entries.Add(new RegularsEntry { SpotKey = "pitch", Weekday = DayOfWeek.Tuesday, Bonus = 0.30 });
        regulars.EndOfDay(state, 16, Array.Empty<SpotVisit>());
        Assert.Equal(0.075, regulars.Bonus(state, "pitch", DayOfWeek.Tuesday), 10);
    }

    [Fact]
    public void LongBreaksRemoveEntriesAndFirstUseSkipsCatchUp()
    {
        var state = new RegularsState();
        regulars.EndOfDay(state, 1000000000, new[] { Visit() });
        Assert.Equal(0.05, Assert.Single(state.Entries).Bonus);
        regulars.EndOfDay(state, int.MaxValue, Array.Empty<SpotVisit>());
        Assert.Empty(state.Entries);
    }

    [Fact]
    public void ZeroMissPenaltyPreservesEntriesAcrossLongBreaks()
    {
        var noDecay = new Regulars(new RegularsRules(missPenalty: 0));
        var state = new RegularsState();
        noDecay.EndOfDay(state, 1, new[] { Visit() });
        noDecay.EndOfDay(state, int.MaxValue, Array.Empty<SpotVisit>());
        Assert.Equal(0.05, noDecay.Bonus(state, "pitch", DayOfWeek.Monday));
    }

    [Theory]
    [InlineData("nullEntries")]
    [InlineData("nullEntry")]
    [InlineData("emptyKey")]
    [InlineData("nullKey")]
    [InlineData("duplicate")]
    [InlineData("negativeBonus")]
    [InlineData("largeBonus")]
    [InlineData("nanBonus")]
    [InlineData("infiniteBonus")]
    [InlineData("unknownWeekday")]
    public void BadRegularsStateFailsValidation(string problem)
    {
        var entry = new RegularsEntry { SpotKey = "pitch", Weekday = DayOfWeek.Monday, Bonus = 0.1 };
        var state = new RegularsState();
        state.Entries.Add(entry);
        switch (problem)
        {
            case "nullEntries": state.Entries = null!; break;
            case "nullEntry": state.Entries.Add(null!); break;
            case "emptyKey": entry.SpotKey = ""; break;
            case "nullKey": entry.SpotKey = null!; break;
            case "duplicate": state.Entries.Add(new RegularsEntry { SpotKey = "pitch", Weekday = DayOfWeek.Monday }); break;
            case "negativeBonus": entry.Bonus = -0.1; break;
            case "largeBonus": entry.Bonus = 5.1; break;
            case "nanBonus": entry.Bonus = double.NaN; break;
            case "infiniteBonus": entry.Bonus = double.PositiveInfinity; break;
            case "unknownWeekday": entry.Weekday = (DayOfWeek)99; break;
        }

        Assert.Throws<JsonSerializationException>(() => regulars.Bonus(state, "pitch", DayOfWeek.Monday));
    }

    [Fact]
    public void RegularsStateKeepsEnumNamesAndUnknownFields()
    {
        var state = JsonConvert.DeserializeObject<RegularsState>("{\"future\":7,\"Entries\":[{\"SpotKey\":\"pitch\",\"Weekday\":\"Monday\",\"note\":true}]}")!;
        var root = JObject.Parse(JsonConvert.SerializeObject(state));

        Assert.Equal("Monday", root["Entries"]![0]!["Weekday"]!.Value<string>());
        Assert.Equal(7, root["future"]!.Value<int>());
        Assert.True(root["Entries"]![0]!["note"]!.Value<bool>());
        Assert.Equal(int.MinValue, state.LastUpdatedDay);
    }
}
