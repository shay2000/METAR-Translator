namespace MetarViewer.Models;

/// <summary>
/// Represents decoded METAR data for a specific station.
/// </summary>
public class MetarData
{
    /// <summary>
    /// The ICAO station identifier (e.g., KLAX).
    /// </summary>
    public string StationId { get; set; } = string.Empty;

    /// <summary>
    /// The human-readable name of the station/airport.
    /// </summary>
    public string? StationName { get; set; }

    /// <summary>
    /// The time the observation was made.
    /// </summary>
    public DateTime ObservationTime { get; set; }

    /// <summary>
    /// The original raw METAR string.
    /// </summary>
    public string RawMetar { get; set; } = string.Empty;

    /// <summary>
    /// Temperature in degrees Celsius.
    /// </summary>
    public int? Temperature { get; set; }

    /// <summary>
    /// Dew point in degrees Celsius.
    /// </summary>
    public int? DewPoint { get; set; }

    /// <summary>
    /// Wind direction in degrees (0-360).
    /// </summary>
    public int? WindDirection { get; set; }

    /// <summary>
    /// Wind speed in knots.
    /// </summary>
    public int? WindSpeed { get; set; }

    /// <summary>
    /// Wind gust speed in knots, if present.
    /// </summary>
    public int? WindGust { get; set; }

    /// <summary>
    /// Visibility distance.
    /// </summary>
    public decimal? Visibility { get; set; }

    /// <summary>
    /// Unit of visibility (e.g., SM, meters).
    /// </summary>
    public string? VisibilityUnit { get; set; }

    /// <summary>
    /// Altimeter setting.
    /// </summary>
    public decimal? Altimeter { get; set; }

    /// <summary>
    /// Unit of altimeter setting (e.g., hPa, inHg).
    /// </summary>
    public string? AltimeterUnit { get; set; }

    /// <summary>
    /// List of observed cloud layers.
    /// </summary>
    public List<CloudLayer> CloudLayers { get; set; } = new();

    /// <summary>
    /// List of observed weather phenomena (e.g., RA, SN, FG).
    /// </summary>
    public List<string> WeatherPhenomena { get; set; } = new();

    /// <summary>
    /// The flight category (e.g., VFR, MVFR, IFR, LIFR).
    /// </summary>
    public string? FlightCategory { get; set; }

    /// <summary>
    /// Indicates if Ceiling and Visibility are OK.
    /// </summary>
    public bool IsCavok { get; set; }
}

/// <summary>
/// Represents a layer of cloud coverage.
/// </summary>
public class CloudLayer
{
    /// <summary>
    /// The amount of cloud coverage (e.g., FEW, SCT, BKN, OVC).
    /// </summary>
    public string Coverage { get; set; } = string.Empty;

    /// <summary>
    /// The altitude of the cloud layer in feet AGL.
    /// </summary>
    public int? Altitude { get; set; }

    /// <summary>
    /// The type of cloud (e.g., CB, TCU).
    /// </summary>
    public string? Type { get; set; }
}
