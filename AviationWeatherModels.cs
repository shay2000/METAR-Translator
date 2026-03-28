using System.Text.Json.Serialization;

namespace MetarViewer.Models;

public class AviationWeatherMetarResponse
{
    [JsonPropertyName("icaoId")]
    public string? IcaoId { get; set; }

    [JsonPropertyName("reportTime")]
    public string? ReportTime { get; set; }

    [JsonPropertyName("obsTime")]
    public long? ObservationEpoch { get; set; }

    [JsonPropertyName("rawOb")]
    public string? RawObservation { get; set; }

    [JsonPropertyName("temp")]
    public decimal? Temperature { get; set; }

    [JsonPropertyName("dewp")]
    public decimal? DewPoint { get; set; }

    [JsonPropertyName("wdir")]
    public int? WindDirection { get; set; }

    [JsonPropertyName("wspd")]
    public int? WindSpeed { get; set; }

    [JsonPropertyName("wgst")]
    public int? WindGust { get; set; }

    [JsonPropertyName("visib")]
    public string? Visibility { get; set; }

    [JsonPropertyName("altim")]
    public decimal? Altimeter { get; set; }

    [JsonPropertyName("fltCat")]
    public string? FlightCategory { get; set; }

    [JsonPropertyName("wxString")]
    public string? WeatherString { get; set; }

    [JsonPropertyName("clouds")]
    public List<AviationWeatherCloud>? Clouds { get; set; }
}

public class AviationWeatherCloud
{
    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    [JsonPropertyName("base")]
    public int? Base { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
