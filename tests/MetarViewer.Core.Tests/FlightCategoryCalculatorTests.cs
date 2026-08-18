using MetarViewer.Models;
using MetarViewer.Parsing;
using Xunit;

namespace MetarViewer.Tests;

/// <summary>
/// Tests the flight category rules, which decide whether conditions permit visual flight.
///
/// This is the one piece of the decoding that a pilot acts on, and its thresholds were previously
/// buried in the parser. Each boundary is checked from both sides, because an error of one unit
/// here changes the answer from "legal to fly" to "not".
/// </summary>
public class FlightCategoryCalculatorTests
{
    [Fact]
    public void Determine_ReportsVfrForCavok()
    {
        // CAVOK states outright that ceiling and visibility are fine, so no thresholds apply.
        var metar = new MetarData { IsCavok = true };

        Assert.Equal(FlightCategories.Vfr, FlightCategoryCalculator.Determine(metar));
    }

    [Theory]
    [InlineData(0.5, FlightCategories.LowIfr)]
    [InlineData(0.9, FlightCategories.LowIfr)]
    [InlineData(1, FlightCategories.Ifr)]           // low IFR is below one mile, not at it
    [InlineData(2.9, FlightCategories.Ifr)]
    [InlineData(3, FlightCategories.MarginalVfr)]
    [InlineData(5, FlightCategories.MarginalVfr)]   // marginal VFR includes five miles
    [InlineData(5.1, FlightCategories.Vfr)]
    [InlineData(10, FlightCategories.Vfr)]
    public void Determine_AppliesVisibilityThresholds(decimal visibilityMiles, string expectedCategory)
    {
        var metar = new MetarData { Visibility = visibilityMiles, VisibilityUnit = "SM" };

        Assert.Equal(expectedCategory, FlightCategoryCalculator.Determine(metar));
    }

    [Theory]
    [InlineData(400, FlightCategories.LowIfr)]
    [InlineData(500, FlightCategories.Ifr)]         // low IFR is below 500 feet, not at it
    [InlineData(900, FlightCategories.Ifr)]
    [InlineData(1000, FlightCategories.MarginalVfr)]
    [InlineData(3000, FlightCategories.MarginalVfr)]
    [InlineData(3100, FlightCategories.Vfr)]
    public void Determine_AppliesCeilingThresholds(int ceilingFeet, string expectedCategory)
    {
        var metar = CreateMetarWithCeiling("BKN", ceilingFeet);

        Assert.Equal(expectedCategory, FlightCategoryCalculator.Determine(metar));
    }

    [Theory]
    [InlineData("BKN")]
    [InlineData("OVC")]
    [InlineData("VV")]
    public void Determine_TreatsBrokenOvercastAndVerticalVisibilityAsACeiling(string coverage)
    {
        var metar = CreateMetarWithCeiling(coverage, 400);

        Assert.Equal(FlightCategories.LowIfr, FlightCategoryCalculator.Determine(metar));
    }

    [Theory]
    [InlineData("FEW")]
    [InlineData("SCT")]
    public void Determine_DoesNotTreatScatteredCloudAsACeiling(string coverage)
    {
        // Scattered cloud can be flown through, so a low scattered layer is not a ceiling and
        // must not restrict the category.
        var metar = CreateMetarWithCeiling(coverage, 400);
        metar.Visibility = 10m;
        metar.VisibilityUnit = "SM";

        Assert.Equal(FlightCategories.Vfr, FlightCategoryCalculator.Determine(metar));
    }

    [Fact]
    public void Determine_UsesTheLowestCeilingLayer()
    {
        var metar = new MetarData
        {
            Visibility = 10m,
            VisibilityUnit = "SM",
            CloudLayers =
            [
                new CloudLayer { Coverage = "OVC", Altitude = 4000 },
                new CloudLayer { Coverage = "BKN", Altitude = 800 }
            ]
        };

        // The lowest ceiling is what an aircraft meets first, regardless of the order reported.
        Assert.Equal(FlightCategories.Ifr, FlightCategoryCalculator.Determine(metar));
    }

    [Fact]
    public void Determine_ReportsTheWorseOfVisibilityAndCeiling()
    {
        // Good visibility does not make a low ceiling safe, so the more restrictive of the two
        // decides the category.
        var metar = CreateMetarWithCeiling("OVC", 400);
        metar.Visibility = 10m;
        metar.VisibilityUnit = "SM";

        Assert.Equal(FlightCategories.LowIfr, FlightCategoryCalculator.Determine(metar));
    }

    [Fact]
    public void Determine_ReturnsNullWhenNeitherVisibilityNorCeilingIsReported()
    {
        // With nothing to judge, no category is reported rather than assuming the best case.
        Assert.Null(FlightCategoryCalculator.Determine(new MetarData()));
    }

    [Fact]
    public void Determine_IgnoresACeilingLayerWithNoReportedAltitude()
    {
        var metar = new MetarData
        {
            CloudLayers = [new CloudLayer { Coverage = "BKN", Altitude = null }]
        };

        Assert.Null(FlightCategoryCalculator.Determine(metar));
    }

    [Fact]
    public void Determine_ConvertsVisibilityBeforeComparingThresholds()
    {
        // The thresholds are in statute miles, so a report in metres must be converted first.
        // 800 metres is half a mile, which is low IFR.
        var metar = new MetarData { Visibility = 800m, VisibilityUnit = "m" };

        Assert.Equal(FlightCategories.LowIfr, FlightCategoryCalculator.Determine(metar));
    }

    private static MetarData CreateMetarWithCeiling(string coverage, int altitudeFeet) => new()
    {
        CloudLayers = [new CloudLayer { Coverage = coverage, Altitude = altitudeFeet }]
    };
}
