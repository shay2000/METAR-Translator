using MetarViewer.Models;

namespace MetarViewer.Helpers;

public static class MetarDecoder
{
    public static string DecodeWind(MetarData metar)
    {
        if (metar.WindSpeed == null)
        {
            return "Wind information not available";
        }

        if (metar.WindSpeed == 0)
        {
            return "Wind calm";
        }

        var direction = metar.WindDirection.HasValue 
            ? $"{metar.WindDirection}°" 
            : "variable direction";

        var speed = $"{metar.WindSpeed} kt";

        if (metar.WindGust.HasValue)
        {
            return $"Wind from {direction} at {speed}, gusting {metar.WindGust} kt";
        }

        return $"Wind from {direction} at {speed}";
    }

    public static string DecodeVisibility(MetarData metar)
    {
        if (metar.IsCavok)
        {
            return "Visibility 10 km or more (CAVOK)";
        }

        if (!metar.Visibility.HasValue)
        {
            return "Visibility information not available";
        }

        var unit = metar.VisibilityUnit ?? "SM";
        return $"Visibility {metar.Visibility} {unit}";
    }

    public static string DecodeClouds(MetarData metar)
    {
        if (metar.IsCavok)
        {
            return "No clouds below 5,000 ft (CAVOK)";
        }

        if (metar.CloudLayers == null || metar.CloudLayers.Count == 0)
        {
            return "No significant cloud";
        }

        var cloudDescriptions = new List<string>();

        foreach (var layer in metar.CloudLayers)
        {
            var coverage = DecodeCoverage(layer.Coverage);
            var altitude = layer.Altitude.HasValue ? $"{layer.Altitude:N0} ft" : "unknown altitude";

            cloudDescriptions.Add($"{coverage} at {altitude}");
        }

        return string.Join(", ", cloudDescriptions);
    }

    public static string DecodeTemperature(MetarData metar)
    {
        var parts = new List<string>();

        if (metar.Temperature.HasValue)
        {
            parts.Add($"Temperature {metar.Temperature}°C");
        }

        if (metar.DewPoint.HasValue)
        {
            parts.Add($"dew point {metar.DewPoint}°C");
        }

        if (parts.Count == 0)
        {
            return "Temperature information not available";
        }

        return string.Join(", ", parts);
    }

    public static string DecodeAltimeter(MetarData metar)
    {
        if (!metar.Altimeter.HasValue)
        {
            return "Altimeter information not available";
        }

        if (string.Equals(metar.AltimeterUnit, "hPa", StringComparison.OrdinalIgnoreCase) ||
            (string.IsNullOrWhiteSpace(metar.AltimeterUnit) && metar.Altimeter >= 100))
        {
            var hpaFormat = metar.Altimeter == decimal.Truncate(metar.Altimeter.Value) ? "F0" : "F1";
            var inHg = metar.Altimeter.Value / 33.8639m;
            return $"QNH {metar.Altimeter.Value.ToString(hpaFormat)} hPa ({inHg:F2} inHg)";
        }

        var hpa = metar.Altimeter * 33.8639m;
        return $"QNH {metar.Altimeter:F2} inHg ({hpa:F0} hPa)";
    }

    public static string DecodeWeather(MetarData metar)
    {
        if (metar.IsCavok)
        {
            return "No significant weather";
        }

        if (metar.WeatherPhenomena == null || metar.WeatherPhenomena.Count == 0)
        {
            return "No significant weather";
        }

        var weatherDescriptions = metar.WeatherPhenomena
            .Select(DecodeWeatherCode)
            .ToList();

        return string.Join(", ", weatherDescriptions);
    }

    public static string GetFlightCategoryDescription(string? category)
    {
        return category?.ToUpperInvariant() switch
        {
            "VFR" => "VFR (Visual Flight Rules)",
            "MVFR" => "MVFR (Marginal VFR)",
            "IFR" => "IFR (Instrument Flight Rules)",
            "LIFR" => "LIFR (Low IFR)",
            _ => "Unknown"
        };
    }

    private static string DecodeCoverage(string coverage)
    {
        return coverage.ToUpperInvariant() switch
        {
            "FEW" => "Few clouds",
            "SCT" => "Scattered clouds",
            "BKN" => "Broken clouds",
            "OVC" => "Overcast",
            "SKC" => "Sky clear",
            "CLR" => "Clear",
            "NSC" => "No significant cloud",
            _ => coverage
        };
    }

    private static string DecodeWeatherCode(string code)
    {
        // Simplified weather code decoder
        var decoded = code switch
        {
            "RA" => "Rain",
            "SN" => "Snow",
            "DZ" => "Drizzle",
            "FG" => "Fog",
            "BR" => "Mist",
            "HZ" => "Haze",
            "TS" => "Thunderstorm",
            "TSRA" => "Thunderstorm with rain",
            "SHRA" => "Rain showers",
            "SHSN" => "Snow showers",
            "FZ" => "Freezing",
            "FZRA" => "Freezing rain",
            "+RA" => "Heavy rain",
            "-RA" => "Light rain",
            "+SN" => "Heavy snow",
            "-SN" => "Light snow",
            _ => code
        };

        return decoded;
    }
}
