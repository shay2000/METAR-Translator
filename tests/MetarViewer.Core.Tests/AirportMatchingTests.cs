using MetarViewer.Airports;
using Xunit;

namespace MetarViewer.Tests;

/// <summary>
/// Tests the airport matching rules extracted from AirportLookupService.
///
/// These rules decide which airport a search resolves to. They previously lived among the HTTP
/// calls, so the only way to check them was to stub the network and infer the ranking from which
/// airport came out on top. They can now be asserted directly.
/// </summary>
public class AirportMatchingTests
{
    [Theory]
    [InlineData("LHR")]      // a 3-letter IATA code
    [InlineData("EGLL")]     // a 4-letter ICAO code
    [InlineData("K7B2")]     // codes may contain digits
    public void LooksLikeAirportCode_AcceptsCodeShapedInput(string input)
    {
        Assert.True(AirportCodes.LooksLikeAirportCode(input));
    }

    [Theory]
    [InlineData("HEATHROW")]
    [InlineData("LO")]
    [InlineData("EGLLX")]
    [InlineData("LH R")]     // a space means this is the start of a name
    public void LooksLikeAirportCode_RejectsNameShapedInput(string input)
    {
        Assert.False(AirportCodes.LooksLikeAirportCode(input));
    }

    [Fact]
    public void GetStationIdentifier_PrefersTheIcaoCode()
    {
        // The ICAO code is the identifier weather is reported against, so it wins even when the
        // airport publishes others.
        var attributes = new AirportAttributes { IcaoCode = "EGLL", GpsCode = "EGKK", Code = "EGSS" };

        Assert.Equal("EGLL", AirportCodes.GetStationIdentifier(attributes));
    }

    [Fact]
    public void GetStationIdentifier_FallsBackThroughGpsCodeToCode()
    {
        Assert.Equal("EGKK", AirportCodes.GetStationIdentifier(
            new AirportAttributes { GpsCode = "EGKK", Code = "EGSS" }));

        Assert.Equal("EGSS", AirportCodes.GetStationIdentifier(
            new AirportAttributes { Code = "EGSS" }));
    }

    [Fact]
    public void GetStationIdentifier_IgnoresCodesThatAreNotStationIdentifiers()
    {
        // An IATA code cannot be used to request a METAR, so a 3-letter code is not accepted as a
        // station identifier even when it is the only code available.
        var attributes = new AirportAttributes { IcaoCode = "LHR", IataCode = "LHR" };

        Assert.Null(AirportCodes.GetStationIdentifier(attributes));
    }

    [Fact]
    public void GetStationIdentifier_NormalisesCaseAndWhitespace()
    {
        Assert.Equal("EGLL", AirportCodes.GetStationIdentifier(
            new AirportAttributes { IcaoCode = "  egll " }));
    }

    [Fact]
    public void GetStationIdentifier_ReturnsNullWhenThereIsNoAirport()
    {
        Assert.Null(AirportCodes.GetStationIdentifier(null));
    }

    [Fact]
    public void IsSupportedAirportType_ExcludesClosedAirports()
    {
        // A closed airport reports no weather, so offering it could only mislead.
        Assert.False(AirportCodes.IsSupportedAirportType("closed"));
        Assert.True(AirportCodes.IsSupportedAirportType("large_airport"));
        Assert.True(AirportCodes.IsSupportedAirportType(null));
    }

    [Fact]
    public void Score_RejectsClosedAirportsOutright()
    {
        var attributes = new AirportAttributes { Name = "Old Field", Code = "EGXX", Type = "closed" };

        Assert.Equal(AirportMatchScorer.NoMatch, AirportMatchScorer.Score(attributes, "Old Field", "OLD FIELD"));
    }

    [Fact]
    public void Score_UnrelatedAirportCannotResolveOnTypeAndIataPriorsAlone()
    {
        var unrelated = CreateAirport("Alpha International Airport", "KAAA", "large_airport", "AAA");

        var score = AirportMatchScorer.Score(unrelated, "Heatrow", "HEATROW");

        Assert.True(score < AirportMatchScorer.MinimumResolutionScore);
    }

    [Fact]
    public void Score_RelevantTypoStillMeetsTheResolutionThreshold()
    {
        var relevant = CreateAirport("London Heathrow Airport", "EGLL", "large_airport", "LHR");

        var score = AirportMatchScorer.Score(relevant, "Heatrow", "HEATROW");

        Assert.True(score >= AirportMatchScorer.MinimumResolutionScore);
    }

    [Fact]
    public void Score_RanksAnExactCodeMatchAboveAnExactNameMatch()
    {
        // Codes are unique, so typing one is a clearer statement of intent than typing a name
        // that several airports may share.
        var codeMatch = CreateAirport("Somewhere Else", "EGLL", "large_airport");
        var nameMatch = CreateAirport("EGLL", "EGKK", "large_airport");

        Assert.True(
            AirportMatchScorer.Score(codeMatch, "EGLL", "EGLL") >
            AirportMatchScorer.Score(nameMatch, "EGLL", "EGLL"));
    }

    [Fact]
    public void Score_RanksExactThenPrefixThenContainingNameMatches()
    {
        var exact = CreateAirport("Heathrow", "AAAA", "large_airport");
        var prefix = CreateAirport("Heathrow Airport", "BBBB", "large_airport");
        var contains = CreateAirport("London Heathrow", "CCCC", "large_airport");

        var exactScore = AirportMatchScorer.Score(exact, "Heathrow", "HEATHROW");
        var prefixScore = AirportMatchScorer.Score(prefix, "Heathrow", "HEATHROW");
        var containsScore = AirportMatchScorer.Score(contains, "Heathrow", "HEATHROW");

        Assert.True(exactScore > prefixScore);
        Assert.True(prefixScore > containsScore);
    }

    [Fact]
    public void Score_RanksALargeAirportAboveAHeliportWithTheSameName()
    {
        // This is the rule that makes "Heathrow" resolve to the airport rather than to a heliport
        // that happens to share the word.
        var airport = CreateAirport("Heathrow", "EGLL", "large_airport");
        var heliport = CreateAirport("Heathrow", "ZXHX", "heliport");

        Assert.True(
            AirportMatchScorer.Score(airport, "Heathrow", "HEATHROW") >
            AirportMatchScorer.Score(heliport, "Heathrow", "HEATHROW"));
    }

    [Theory]
    [InlineData("large_airport", "medium_airport")]
    [InlineData("medium_airport", "small_airport")]
    [InlineData("small_airport", "seaplane_base")]
    [InlineData("seaplane_base", "heliport")]
    [InlineData("heliport", "balloonport")]
    public void GetAirportTypeScore_RanksTypesByHowLikelyTheyAreToBeMeant(string better, string worse)
    {
        Assert.True(AirportMatchScorer.GetAirportTypeScore(better) > AirportMatchScorer.GetAirportTypeScore(worse));
    }

    [Fact]
    public void Score_BreaksTiesInFavourOfAnAirportWithAnIataCode()
    {
        // A published IATA code marks an airport as one passengers actually fly to.
        var withIata = CreateAirport("Heathrow", "EGLL", "large_airport", iataCode: "LHR");
        var withoutIata = CreateAirport("Heathrow", "EGLL", "large_airport");

        Assert.True(
            AirportMatchScorer.Score(withIata, "Heathrow", "HEATHROW") >
            AirportMatchScorer.Score(withoutIata, "Heathrow", "HEATHROW"));
    }

    [Fact]
    public void GetFuzzyScore_RewardsANearMissOverAnUnrelatedName()
    {
        // This is what lets a misspelling still find the right airport.
        var heathrow = CreateAirport("London Heathrow Airport", "EGLL", "large_airport");
        var gatwick = CreateAirport("London Gatwick Airport", "EGKK", "large_airport");

        Assert.True(
            AirportMatchScorer.GetFuzzyScore(heathrow, "HEATROW") >
            AirportMatchScorer.GetFuzzyScore(gatwick, "HEATROW"));
    }

    [Fact]
    public void GetFuzzyScore_MatchesOnASingleWordOfALongName()
    {
        // "Heathrow" must score against the word inside the full name, not against the whole
        // name, which is far longer than what was typed.
        var attributes = CreateAirport("London Heathrow Airport", "EGLL", "large_airport");

        Assert.True(AirportMatchScorer.GetFuzzyScore(attributes, "HEATHROW") > 0);
    }

    [Fact]
    public void GetFuzzyScore_IgnoresPunctuationDifferences()
    {
        var attributes = CreateAirport("St. John's Airport", "CYYT", "medium_airport");

        Assert.True(AirportMatchScorer.GetFuzzyScore(attributes, "STJOHNS") > 0);
    }

    [Fact]
    public void GetFuzzyScore_IsZeroWhenThereIsNothingToCompare()
    {
        Assert.Equal(0, AirportMatchScorer.GetFuzzyScore(new AirportAttributes(), string.Empty));
    }

    [Theory]
    [InlineData("EGLL")]
    [InlineData("LHR")]
    [InlineData("egll")]     // codes are compared case-insensitively
    public void MatchesAnyCode_RecognisesEveryKindOfCode(string input)
    {
        var attributes = new AirportAttributes { IcaoCode = "EGLL", IataCode = "LHR" };

        Assert.True(AirportMatchScorer.MatchesAnyCode(attributes, input.ToUpperInvariant()));
    }

    [Fact]
    public void MatchesAnyCode_ReturnsFalseWhenNoCodeMatches()
    {
        var attributes = new AirportAttributes { IcaoCode = "EGLL", IataCode = "LHR" };

        Assert.False(AirportMatchScorer.MatchesAnyCode(attributes, "KJFK"));
        Assert.False(AirportMatchScorer.MatchesAnyCode(null, "EGLL"));
    }

    private static AirportAttributes CreateAirport(
        string name,
        string code,
        string airportType,
        string? iataCode = null) => new()
        {
            Name = name,
            Code = code,
            IcaoCode = code,
            Type = airportType,
            IataCode = iataCode
        };
}
