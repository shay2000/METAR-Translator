using MetarViewer.Models;
using MetarViewer.Parsing;
using Xunit;

namespace MetarViewer.Tests;

/// <summary>
/// Tests each METAR group parser in isolation.
///
/// These groups used to be parsed by private methods of a single parser class, reachable only by
/// feeding a whole report through it. Now that each is its own type, the edge cases of one group
/// can be stated without constructing a report that is valid in every other respect.
/// </summary>
public class TokenParsersTests
{
    [Fact]
    public void WindTokenParser_ParsesDirectionSpeedAndGust()
    {
        var metar = Parse(new WindTokenParser(), "27015G25KT");

        Assert.Equal(270, metar.WindDirection);
        Assert.Equal(15, metar.WindSpeed);
        Assert.Equal(25, metar.WindGust);
    }

    [Fact]
    public void WindTokenParser_OmitsGustWhenNotReported()
    {
        var metar = Parse(new WindTokenParser(), "09008KT");

        Assert.Equal(90, metar.WindDirection);
        Assert.Equal(8, metar.WindSpeed);
        Assert.Null(metar.WindGust);
    }

    [Fact]
    public void WindTokenParser_LeavesDirectionUnsetWhenVariable()
    {
        // "VRB" reports that the wind has no single direction, which is not the same as a
        // direction of zero degrees.
        var metar = Parse(new WindTokenParser(), "VRB05KT");

        Assert.Null(metar.WindDirection);
        Assert.Equal(5, metar.WindSpeed);
    }

    [Fact]
    public void WindTokenParser_ParsesCalmWindAsZero()
    {
        var metar = Parse(new WindTokenParser(), "00000KT");

        Assert.Equal(0, metar.WindDirection);
        Assert.Equal(0, metar.WindSpeed);
    }

    [Theory]
    [InlineData("27015")]
    [InlineData("BKN020")]
    [InlineData("15/10")]
    public void WindTokenParser_RejectsTokensThatAreNotWind(string token)
    {
        Assert.False(TryParse(new WindTokenParser(), token, out _));
    }

    [Fact]
    public void VisibilityTokenParser_TreatsNineThousandNineHundredAndNinetyNineAsTenKilometres()
    {
        // "9999" is the standard way of reporting ten kilometres or more, not a distance of
        // 9,999 metres.
        var metar = Parse(new VisibilityTokenParser(), "9999");

        Assert.Equal(10m, metar.Visibility);
        Assert.Equal("km", metar.VisibilityUnit);
    }

    [Fact]
    public void VisibilityTokenParser_ParsesFourDigitGroupAsMetres()
    {
        var metar = Parse(new VisibilityTokenParser(), "0800");

        Assert.Equal(800m, metar.Visibility);
        Assert.Equal("m", metar.VisibilityUnit);
    }

    [Theory]
    [InlineData("10SM", 10)]
    [InlineData("1/2SM", 0.5)]
    [InlineData("P6SM", 6)]
    [InlineData("M1/4SM", 0.25)]
    public void VisibilityTokenParser_ParsesStatuteMiles(string token, decimal expectedMiles)
    {
        // "P" means "or more" and "M" means "or less"; both are dropped because only the
        // distance is recorded.
        var metar = Parse(new VisibilityTokenParser(), token);

        Assert.Equal(expectedMiles, metar.Visibility);
        Assert.Equal("SM", metar.VisibilityUnit);
    }

    [Fact]
    public void VisibilityTokenParser_ParsesMixedFractionSpanningTwoTokens()
    {
        // A visibility of one and a half miles is written "1 1/2SM", which tokenises into two
        // separate tokens, so the parser has to consume both.
        var context = new MetarTokenContext(["1", "1/2SM"], 0);
        var metar = new MetarData();

        Assert.True(new VisibilityTokenParser().TryParse(context, metar));
        Assert.Equal(1.5m, metar.Visibility);
        Assert.Equal("SM", metar.VisibilityUnit);
        Assert.Equal(1, context.Index);
    }

    [Theory]
    [InlineData("BKN020")]
    [InlineData("27015KT")]
    [InlineData("SM")]
    public void VisibilityTokenParser_RejectsTokensThatAreNotVisibility(string token)
    {
        Assert.False(TryParse(new VisibilityTokenParser(), token, out _));
    }

    [Fact]
    public void CloudTokenParser_ConvertsHundredsOfFeetToFeet()
    {
        var metar = Parse(new CloudTokenParser(), "SCT025");

        var layer = Assert.Single(metar.CloudLayers);
        Assert.Equal("SCT", layer.Coverage);
        Assert.Equal(2500, layer.Altitude);
        Assert.Null(layer.Type);
    }

    [Fact]
    public void CloudTokenParser_ParsesSignificantCloudType()
    {
        var metar = Parse(new CloudTokenParser(), "BKN030CB");

        var layer = Assert.Single(metar.CloudLayers);
        Assert.Equal("BKN", layer.Coverage);
        Assert.Equal(3000, layer.Altitude);
        Assert.Equal("CB", layer.Type);
    }

    [Fact]
    public void CloudTokenParser_ParsesClearSkyWithoutAltitude()
    {
        var metar = Parse(new CloudTokenParser(), "SKC");

        var layer = Assert.Single(metar.CloudLayers);
        Assert.Equal("SKC", layer.Coverage);
        Assert.Null(layer.Altitude);
    }

    [Fact]
    public void CloudTokenParser_ParsesVerticalVisibility()
    {
        var metar = Parse(new CloudTokenParser(), "VV002");

        var layer = Assert.Single(metar.CloudLayers);
        Assert.Equal("VV", layer.Coverage);
        Assert.Equal(200, layer.Altitude);
    }

    [Fact]
    public void CloudTokenParser_AccumulatesEveryLayerInTheReport()
    {
        var metar = new MetarData();
        var parser = new CloudTokenParser();

        Assert.True(parser.TryParse(new MetarTokenContext(["FEW010"], 0), metar));
        Assert.True(parser.TryParse(new MetarTokenContext(["OVC040"], 0), metar));

        Assert.Equal(2, metar.CloudLayers.Count);
    }

    [Fact]
    public void TemperatureTokenParser_ParsesPositiveTemperatureAndDewPoint()
    {
        var metar = Parse(new TemperatureTokenParser(), "15/10");

        Assert.Equal(15, metar.Temperature);
        Assert.Equal(10, metar.DewPoint);
    }

    [Fact]
    public void TemperatureTokenParser_ReadsLeadingMAsNegative()
    {
        var metar = Parse(new TemperatureTokenParser(), "M02/M05");

        Assert.Equal(-2, metar.Temperature);
        Assert.Equal(-5, metar.DewPoint);
    }

    [Fact]
    public void TemperatureTokenParser_LeavesDewPointUnsetWhenNotMeasured()
    {
        // A dew point of "//" means the station did not measure it, which must not be reported
        // as a temperature.
        var metar = Parse(new TemperatureTokenParser(), "15///");

        Assert.Equal(15, metar.Temperature);
        Assert.Null(metar.DewPoint);
    }

    [Fact]
    public void AltimeterTokenParser_ParsesQnhInHectopascals()
    {
        var metar = Parse(new AltimeterTokenParser(), "Q1013");

        Assert.Equal(1013m, metar.Altimeter);
        Assert.Equal("hPa", metar.AltimeterUnit);
    }

    [Fact]
    public void AltimeterTokenParser_ParsesAltimeterSettingWithImpliedDecimalPoint()
    {
        // A US altimeter setting omits the decimal point, so "A2992" is 29.92 inHg.
        var metar = Parse(new AltimeterTokenParser(), "A2992");

        Assert.Equal(29.92m, metar.Altimeter);
        Assert.Equal("inHg", metar.AltimeterUnit);
    }

    [Theory]
    [InlineData("1013")]
    [InlineData("Q101")]
    [InlineData("A299")]
    public void AltimeterTokenParser_RejectsTokensThatAreNotPressure(string token)
    {
        Assert.False(TryParse(new AltimeterTokenParser(), token, out _));
    }

    private static MetarData Parse(IMetarTokenParser parser, string token)
    {
        Assert.True(TryParse(parser, token, out var metar), $"Expected '{token}' to be parsed.");
        return metar;
    }

    private static bool TryParse(IMetarTokenParser parser, string token, out MetarData metar)
    {
        metar = new MetarData();
        return parser.TryParse(new MetarTokenContext([token], 0), metar);
    }
}
