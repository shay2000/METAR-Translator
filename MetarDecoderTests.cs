using Xunit;
using MetarViewer.Helpers;
using MetarViewer.Models;

namespace MetarViewer.Tests;

public class MetarDecoderTests
{
    [Fact]
    public void DecodeWind_WithDirection_ReturnsCorrectString()
    {
        // Arrange
        var metar = new MetarData
        {
            WindDirection = 220,
            WindSpeed = 12
        };

        // Act
        var result = MetarDecoder.DecodeWind(metar);

        // Assert
        Assert.Contains("220°", result);
        Assert.Contains("12 kt", result);
    }

    [Fact]
    public void DecodeWind_WithGusts_IncludesGustInformation()
    {
        // Arrange
        var metar = new MetarData
        {
            WindDirection = 180,
            WindSpeed = 15,
            WindGust = 25
        };

        // Act
        var result = MetarDecoder.DecodeWind(metar);

        // Assert
        Assert.Contains("gusting", result);
        Assert.Contains("25 kt", result);
    }

    [Fact]
    public void DecodeWind_WithCalmWind_ReturnsCalm()
    {
        // Arrange
        var metar = new MetarData
        {
            WindSpeed = 0
        };

        // Act
        var result = MetarDecoder.DecodeWind(metar);

        // Assert
        Assert.Contains("calm", result.ToLower());
    }

    [Fact]
    public void DecodeVisibility_WithCavok_ReturnsCavokMessage()
    {
        // Arrange
        var metar = new MetarData
        {
            IsCavok = true
        };

        // Act
        var result = MetarDecoder.DecodeVisibility(metar);

        // Assert
        Assert.Contains("CAVOK", result);
    }

    [Fact]
    public void DecodeClouds_WithMultipleLayers_ReturnsAllLayers()
    {
        // Arrange
        var metar = new MetarData
        {
            CloudLayers = new List<CloudLayer>
            {
                new CloudLayer { Coverage = "BKN", Altitude = 2500 },
                new CloudLayer { Coverage = "OVC", Altitude = 4000 }
            }
        };

        // Act
        var result = MetarDecoder.DecodeClouds(metar);

        // Assert
        Assert.Contains("Broken", result);
        Assert.Contains("2,500", result);
        Assert.Contains("Overcast", result);
        Assert.Contains("4,000", result);
    }

    [Fact]
    public void DecodeTemperature_WithBothValues_ReturnsBoth()
    {
        // Arrange
        var metar = new MetarData
        {
            Temperature = 12,
            DewPoint = 10
        };

        // Act
        var result = MetarDecoder.DecodeTemperature(metar);

        // Assert
        Assert.Contains("12°C", result);
        Assert.Contains("10°C", result);
        Assert.Contains("dew point", result.ToLower());
    }

    [Fact]
    public void DecodeAltimeter_ConvertsToHpa()
    {
        // Arrange
        var metar = new MetarData
        {
            Altimeter = 29.92m // Standard pressure
        };

        // Act
        var result = MetarDecoder.DecodeAltimeter(metar);

        // Assert
        Assert.Contains("29.92", result);
        Assert.Contains("hPa", result);
        // Should be approximately 1013 hPa
        Assert.Contains("1013", result);
    }

    [Fact]
    public void DecodeAltimeter_WithHpaValue_DoesNotConvertAgain()
    {
        // Arrange
        var metar = new MetarData
        {
            Altimeter = 1026m,
            AltimeterUnit = "hPa"
        };

        // Act
        var result = MetarDecoder.DecodeAltimeter(metar);

        // Assert
        Assert.Contains("1026", result);
        Assert.DoesNotContain("inHg", result);
    }

    [Fact]
    public void GetFlightCategoryDescription_VFR_ReturnsCorrectDescription()
    {
        // Act
        var result = MetarDecoder.GetFlightCategoryDescription("VFR");

        // Assert
        Assert.Contains("Visual Flight Rules", result);
    }

    [Fact]
    public void DecodeWeather_WithNoWeather_ReturnsNoSignificantWeather()
    {
        // Arrange
        var metar = new MetarData
        {
            WeatherPhenomena = new List<string>()
        };

        // Act
        var result = MetarDecoder.DecodeWeather(metar);

        // Assert
        Assert.Contains("No significant weather", result);
    }
}
