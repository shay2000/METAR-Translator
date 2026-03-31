using System.Text.Json.Serialization;

namespace MetarViewer.Models;

/// <summary>
/// Represents the JSON response structure from the aviationweather.gov METAR API.
/// </summary>
public class AviationWeatherMetarResponse
{
    /// <summary>
    /// The ICAO identifier for the station.
    /// </summary>
    [JsonPropertyName("icaoId")]
    public string? IcaoId { get; set; }

    /// <summary>
    /// The common name of the airport.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// The report time as a string.
    /// </summary>
    [JsonPropertyName("reportTime")]
    public string? ReportTime { get; set; }

    /// <summary>
    /// The observation time as a Unix epoch.
    /// </summary>
    [JsonPropertyName("obsTime")]
    public long? ObservationEpoch { get; set; }

    /// <summary>
    /// The complete raw METAR string.
    /// </summary>
    [JsonPropertyName("rawOb")]
    public string? RawObservation { get; set; }

    /// <summary>
    /// Temperature in degrees C.
    /// </summary>
    [JsonPropertyName("temp")]
    public decimal? Temperature { get; set; }

    /// <summary>
    /// Dew point in degrees C.
    /// </summary>
    [JsonPropertyName("dewp")]
    public decimal? DewPoint { get; set; }

    /// <summary>
    /// Wind direction in degrees.
    /// </summary>
    [JsonPropertyName("wdir")]
    public int? WindDirection { get; set; }

    /// <summary>
    /// Wind speed in knots.
    /// </summary>
    [JsonPropertyName("wspd")]
    public int? WindSpeed { get; set; }

    /// <summary>
    /// Wind gust speed in knots.
    /// </summary>
    [JsonPropertyName("wgst")]
    public int? WindGust { get; set; }

    /// <summary>
    /// Visibility as a string (may include fractions).
    /// </summary>
    [JsonPropertyName("visib")]
    public string? Visibility { get; set; }

    /// <summary>
    /// Altimeter setting in hPa.
    /// </summary>
    [JsonPropertyName("altim")]
    public decimal? Altimeter { get; set; }

    /// <summary>
    /// Flight category (VFR, IFR, etc.).
    /// </summary>
    [JsonPropertyName("fltCat")]
    public string? FlightCategory { get; set; }

    /// <summary>
    /// String containing weather phenomena codes.
    /// </summary>
    [JsonPropertyName("wxString")]
    public string? WeatherString { get; set; }

    /// <summary>
    /// List of cloud layers.
    /// </summary>
    [JsonPropertyName("clouds")]
    public List<AviationWeatherCloud>? Clouds { get; set; }
}

/// <summary>
/// Represents a cloud layer in the aviationweather.gov response.
/// </summary>
public class AviationWeatherCloud
{
    /// <summary>
    /// Cloud coverage (FEW, SCT, BKN, OVC).
    /// </summary>
    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    /// <summary>
    /// Cloud base altitude in hundreds of feet.
    /// </summary>
    [JsonPropertyName("base")]
    public int? Base { get; set; }

    /// <summary>
    /// Cloud type (e.g., CB, TCU).
    /// </summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }
}
