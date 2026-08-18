using MetarViewer.Parsing;
using Xunit;

namespace MetarViewer.Tests;

/// <summary>
/// Tests the unit names and conversions shared by the parsers, the decoder and the flight
/// category calculator.
///
/// These conversion factors were repeated at each call site as bare literals, so a correction to
/// one would have left the others wrong. Now that there is one definition of each, these tests
/// state what it must produce.
/// </summary>
public class UnitConversionTests
{
    [Theory]
    [InlineData(10, "SM", 10)]
    [InlineData(10, "sm", 10)]      // the unit is compared case-insensitively
    public void ToStatuteMiles_LeavesStatuteMilesUnchanged(decimal visibility, string unit, decimal expected)
    {
        Assert.Equal(expected, VisibilityUnits.ToStatuteMiles(visibility, unit));
    }

    [Fact]
    public void ToStatuteMiles_ConvertsKilometres()
    {
        var miles = VisibilityUnits.ToStatuteMiles(10m, "km");

        Assert.NotNull(miles);
        Assert.Equal(6.21m, Math.Round(miles!.Value, 2));
    }

    [Fact]
    public void ToStatuteMiles_ConvertsMetres()
    {
        // 1,609 metres is a mile, so 800 metres is about half of one.
        var miles = VisibilityUnits.ToStatuteMiles(800m, "m");

        Assert.NotNull(miles);
        Assert.Equal(0.50m, Math.Round(miles!.Value, 2));
    }

    [Fact]
    public void ToStatuteMiles_ReturnsNullWhenThereIsNoReading()
    {
        Assert.Null(VisibilityUnits.ToStatuteMiles(null, "SM"));
    }

    [Theory]
    [InlineData("furlongs")]
    [InlineData("")]
    [InlineData(null)]
    public void ToStatuteMiles_ReturnsNullForAnUnrecognisedUnit(string? unit)
    {
        // Guessing at an unknown unit would silently produce a wrong flight category, so the
        // conversion refuses instead.
        Assert.Null(VisibilityUnits.ToStatuteMiles(10m, unit));
    }

    [Theory]
    [InlineData("10", 10)]
    [InlineData("2.5", 2.5)]
    [InlineData("1/2", 0.5)]
    [InlineData("3/4", 0.75)]
    [InlineData("1 1/2", 1.5)]      // a mixed fraction
    [InlineData("6+", 6)]           // a trailing "+" means "or greater"
    [InlineData("  10  ", 10)]
    public void ParseDistance_ParsesEveryFormAReportUses(string value, decimal expected)
    {
        Assert.Equal(expected, VisibilityUnits.ParseDistance(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1/0")]             // dividing by zero is not a distance
    public void ParseDistance_ReturnsNullWhenThereIsNoNumberToRead(string? value)
    {
        Assert.Null(VisibilityUnits.ParseDistance(value));
    }

    [Fact]
    public void ParseFraction_DividesNumeratorByDenominator()
    {
        Assert.Equal(0.25m, VisibilityUnits.ParseFraction("1/4"));
    }

    [Theory]
    [InlineData("1/0")]
    [InlineData("1/2/3")]
    [InlineData("half")]
    [InlineData("2")]
    public void ParseFraction_ReturnsNullWhenTheValueIsNotAFraction(string value)
    {
        Assert.Null(VisibilityUnits.ParseFraction(value));
    }

    [Fact]
    public void HectopascalsToInchesOfMercury_ConvertsStandardPressure()
    {
        // 1013.25 hPa is the standard atmosphere, which is 29.92 inHg.
        var inches = PressureUnits.HectopascalsToInchesOfMercury(1013.25m);

        Assert.Equal(29.92m, Math.Round(inches, 2));
    }

    [Fact]
    public void InchesOfMercuryToHectopascals_ConvertsStandardPressure()
    {
        // 29.92 inHg is itself a rounded-off value, so converting it back lands a hundredth of a
        // hectopascal above the 1013.25 hPa it came from rather than exactly on it.
        var hectopascals = PressureUnits.InchesOfMercuryToHectopascals(29.92m);

        Assert.Equal(1013.21m, Math.Round(hectopascals, 2));
    }

    [Fact]
    public void PressureConversions_AreTheInverseOfEachOther()
    {
        var roundTripped = PressureUnits.InchesOfMercuryToHectopascals(
            PressureUnits.HectopascalsToInchesOfMercury(1013m));

        Assert.Equal(1013m, Math.Round(roundTripped, 6));
    }
}
