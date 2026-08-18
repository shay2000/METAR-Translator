using MetarViewer.Services;
using Xunit;

namespace MetarViewer.Tests;

/// <summary>
/// Characterization tests that capture the behaviour of <see cref="RawMetarParser"/> as it
/// existed before the parsing refactor. These tests intentionally assert on observable
/// output for realistic METAR reports so that later restructuring can be proven to
/// preserve behaviour rather than assumed to.
/// </summary>
public class RawMetarParserCharacterizationTests
{
    [Fact]
    public void Parse_TypicalEuropeanReport_ExtractsAllGroups()
    {
        var metar = RawMetarParser.Parse("EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013", "EGLL");

        Assert.Equal("EGLL", metar.StationId);
        Assert.Equal(250, metar.WindDirection);
        Assert.Equal(12, metar.WindSpeed);
        Assert.Null(metar.WindGust);
        Assert.Equal(10m, metar.Visibility);
        Assert.Equal("km", metar.VisibilityUnit);
        Assert.Equal(12, metar.Temperature);
        Assert.Equal(8, metar.DewPoint);
        Assert.Equal(1013m, metar.Altimeter);
        Assert.Equal("hPa", metar.AltimeterUnit);
        Assert.Equal("VFR", metar.FlightCategory);

        var layer = Assert.Single(metar.CloudLayers);
        Assert.Equal("FEW", layer.Coverage);
        Assert.Equal(3500, layer.Altitude);
    }

    [Fact]
    public void Parse_TypicalUsReport_UsesStatuteMilesAndInchesOfMercury()
    {
        var metar = RawMetarParser.Parse("KJFK 151251Z 18004KT 1/2SM FG OVC003 08/08 A2992", "KJFK");

        Assert.Equal("KJFK", metar.StationId);
        Assert.Equal(0.5m, metar.Visibility);
        Assert.Equal("SM", metar.VisibilityUnit);
        Assert.Equal(29.92m, metar.Altimeter);
        Assert.Equal("inHg", metar.AltimeterUnit);
        Assert.Equal(["FG"], metar.WeatherPhenomena);
        Assert.Equal("LIFR", metar.FlightCategory);
    }

    [Fact]
    public void Parse_GustingWind_CapturesGustSpeed()
    {
        var metar = RawMetarParser.Parse("KDEN 151253Z 27015G25KT 10SM SKC 20/M01 A3001", "KDEN");

        Assert.Equal(270, metar.WindDirection);
        Assert.Equal(15, metar.WindSpeed);
        Assert.Equal(25, metar.WindGust);
        Assert.Equal(20, metar.Temperature);
        Assert.Equal(-1, metar.DewPoint);
    }

    [Fact]
    public void Parse_VariableWind_LeavesDirectionUnset()
    {
        var metar = RawMetarParser.Parse("EGKK 151250Z VRB03KT 9999 NSC 15/12 Q1020", "EGKK");

        Assert.Null(metar.WindDirection);
        Assert.Equal(3, metar.WindSpeed);
    }

    [Fact]
    public void Parse_Cavok_SetsTenKilometreVisibilityAndVfr()
    {
        var metar = RawMetarParser.Parse("EGLL 151250Z 25012KT CAVOK 12/08 Q1013", "EGLL");

        Assert.True(metar.IsCavok);
        Assert.Equal(10m, metar.Visibility);
        Assert.Equal("km", metar.VisibilityUnit);
        Assert.Equal("VFR", metar.FlightCategory);
    }

    [Fact]
    public void Parse_MixedFractionVisibility_CombinesBothTokens()
    {
        var metar = RawMetarParser.Parse("KORD 151251Z 09008KT 1 1/4SM BR OVC008 05/04 A2995", "KORD");

        Assert.Equal(1.25m, metar.Visibility);
        Assert.Equal("SM", metar.VisibilityUnit);
        Assert.Equal(["BR"], metar.WeatherPhenomena);
    }

    [Fact]
    public void Parse_MetreVisibility_RecordedInMetres()
    {
        var metar = RawMetarParser.Parse("LFPG 151230Z 21008KT 5000 -RA BKN012 09/07 Q1008", "LFPG");

        Assert.Equal(5000m, metar.Visibility);
        Assert.Equal("m", metar.VisibilityUnit);
        Assert.Equal(["-RA"], metar.WeatherPhenomena);
    }

    [Fact]
    public void Parse_MultipleCloudLayers_PreservesOrderAndConvertsToFeet()
    {
        var metar = RawMetarParser.Parse("KSFO 151256Z 28012KT 10SM FEW015 SCT025 BKN040 17/12 A3005", "KSFO");

        Assert.Equal(3, metar.CloudLayers.Count);
        Assert.Equal("FEW", metar.CloudLayers[0].Coverage);
        Assert.Equal(1500, metar.CloudLayers[0].Altitude);
        Assert.Equal("SCT", metar.CloudLayers[1].Coverage);
        Assert.Equal(2500, metar.CloudLayers[1].Altitude);
        Assert.Equal("BKN", metar.CloudLayers[2].Coverage);
        Assert.Equal(4000, metar.CloudLayers[2].Altitude);
    }

    [Fact]
    public void Parse_CumulonimbusCloudType_IsCaptured()
    {
        var metar = RawMetarParser.Parse("EDDF 151250Z 24010KT 9999 BKN030CB 18/14 Q1011", "EDDF");

        var layer = Assert.Single(metar.CloudLayers);
        Assert.Equal("BKN", layer.Coverage);
        Assert.Equal(3000, layer.Altitude);
        Assert.Equal("CB", layer.Type);
    }

    [Fact]
    public void Parse_MissingDewPoint_LeavesDewPointUnset()
    {
        var metar = RawMetarParser.Parse("KLAX 151253Z 26006KT 10SM CLR 22///", "KLAX");

        Assert.Equal(22, metar.Temperature);
        Assert.Null(metar.DewPoint);
    }

    [Fact]
    public void Parse_RemarksSection_IsNotTreatedAsWeather()
    {
        var metar = RawMetarParser.Parse("KBOS 151254Z 31009KT 10SM SKC 11/02 A3010 RMK AO2 SLP192", "KBOS");

        Assert.Empty(metar.WeatherPhenomena);
        Assert.Equal(11, metar.Temperature);
    }

    [Fact]
    public void Parse_AutomatedStationModifier_IsSkippedBeforeWeatherGroups()
    {
        var metar = RawMetarParser.Parse("EGSS 151250Z AUTO 22014KT 9999 OVC021 14/11 Q1015", "EGSS");

        Assert.Equal(220, metar.WindDirection);
        Assert.Equal(14, metar.WindSpeed);
        Assert.Empty(metar.WeatherPhenomena);
    }

    [Fact]
    public void Parse_TrendGroupTempo_IsNotMisreadAsWeatherPhenomenon()
    {
        // "TEMPO" ends in "PO" (dust/sand whirls), so a naive substring check would
        // classify the trend group as a weather phenomenon.
        var metar = RawMetarParser.Parse("EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013 TEMPO 4000 RA", "EGLL");

        Assert.DoesNotContain("TEMPO", metar.WeatherPhenomena);
    }

    [Fact]
    public void Parse_ReportWithoutStationPrefix_PrependsRequestedStation()
    {
        var metar = RawMetarParser.Parse("151250Z 25012KT 9999 FEW035 12/08 Q1013", "EGLL");

        Assert.Equal("EGLL", metar.StationId);
        Assert.StartsWith("METAR EGLL", metar.RawMetar, StringComparison.Ordinal);
    }

    [Fact]
    public void Parse_LowercaseInput_IsNormalizedToUpperCase()
    {
        var metar = RawMetarParser.Parse("eglL 151250z 25012kt 9999 few035 12/08 q1013", "egll");

        Assert.Equal("EGLL", metar.StationId);
        Assert.Equal(250, metar.WindDirection);
        Assert.Equal(1013m, metar.Altimeter);
    }

    [Fact]
    public void Parse_EmptyReport_ReturnsStationOnly()
    {
        var metar = RawMetarParser.Parse(string.Empty, "EGLL");

        Assert.Equal("EGLL", metar.StationId);
        Assert.Null(metar.Visibility);
        Assert.Null(metar.FlightCategory);
        Assert.Empty(metar.CloudLayers);
    }

    [Theory]
    // Visibility-driven categories, with no ceiling present.
    [InlineData("KJFK 151251Z 18004KT 1/2SM OVC050 08/08 A2992", "LIFR")]
    [InlineData("KJFK 151251Z 18004KT 2SM OVC050 08/08 A2992", "IFR")]
    [InlineData("KJFK 151251Z 18004KT 4SM OVC050 08/08 A2992", "MVFR")]
    [InlineData("KJFK 151251Z 18004KT 10SM OVC050 08/08 A2992", "VFR")]
    // Ceiling-driven categories, with generous visibility.
    [InlineData("KJFK 151251Z 18004KT 10SM OVC004 08/08 A2992", "LIFR")]
    [InlineData("KJFK 151251Z 18004KT 10SM OVC008 08/08 A2992", "IFR")]
    [InlineData("KJFK 151251Z 18004KT 10SM OVC025 08/08 A2992", "MVFR")]
    public void Parse_FlightCategory_MatchesVisibilityAndCeilingThresholds(string rawMetar, string expected)
    {
        var metar = RawMetarParser.Parse(rawMetar, "KJFK");

        Assert.Equal(expected, metar.FlightCategory);
    }

    [Fact]
    public void Parse_ScatteredLayersDoNotFormCeiling()
    {
        // Only BKN, OVC and VV layers count towards the ceiling.
        var metar = RawMetarParser.Parse("KJFK 151251Z 18004KT 10SM SCT004 08/08 A2992", "KJFK");

        Assert.Equal("VFR", metar.FlightCategory);
    }

    [Fact]
    public void Parse_ObservationTime_UsesDayHourAndMinuteFromReport()
    {
        var metar = RawMetarParser.Parse("EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013", "EGLL");

        Assert.Equal(15, metar.ObservationTime.Day);
        Assert.Equal(12, metar.ObservationTime.Hour);
        Assert.Equal(50, metar.ObservationTime.Minute);
        Assert.Equal(DateTimeKind.Utc, metar.ObservationTime.Kind);
    }
}
