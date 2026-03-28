using System.Text.Json.Serialization;

namespace MetarViewer.Models;

public class AvwxMetarResponse
{
    [JsonPropertyName("station")]
    public string? Station { get; set; }

    [JsonPropertyName("time")]
    public AvwxTime? Time { get; set; }

    [JsonPropertyName("raw")]
    public string? Raw { get; set; }

    [JsonPropertyName("temperature")]
    public AvwxValue? Temperature { get; set; }

    [JsonPropertyName("dewpoint")]
    public AvwxValue? Dewpoint { get; set; }

    [JsonPropertyName("wind_direction")]
    public AvwxValue? WindDirection { get; set; }

    [JsonPropertyName("wind_speed")]
    public AvwxValue? WindSpeed { get; set; }

    [JsonPropertyName("wind_gust")]
    public AvwxValue? WindGust { get; set; }

    [JsonPropertyName("visibility")]
    public AvwxValue? Visibility { get; set; }

    [JsonPropertyName("altimeter")]
    public AvwxValue? Altimeter { get; set; }

    [JsonPropertyName("clouds")]
    public List<AvwxCloud>? Clouds { get; set; }

    [JsonPropertyName("wx_codes")]
    public List<AvwxWeatherCode>? WxCodes { get; set; }

    [JsonPropertyName("flight_rules")]
    public string? FlightRules { get; set; }

    [JsonPropertyName("other")]
    public List<string>? Other { get; set; }
}

public class AvwxTime
{
    [JsonPropertyName("dt")]
    public string? Dt { get; set; }
}

public class AvwxValue
{
    [JsonPropertyName("value")]
    public decimal? Value { get; set; }

    [JsonPropertyName("spoken")]
    public string? Spoken { get; set; }
}

public class AvwxCloud
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("altitude")]
    public int? Altitude { get; set; }

    [JsonPropertyName("modifier")]
    public string? Modifier { get; set; }
}

public class AvwxWeatherCode
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }
}
