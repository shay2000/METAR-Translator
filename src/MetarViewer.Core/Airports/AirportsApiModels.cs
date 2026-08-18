using System.Text.Json.Serialization;

namespace MetarViewer.Airports;

/// <summary>
/// The airportsapi.com response envelope for a search, which returns many airports.
/// </summary>
internal sealed class AirportSearchResponse
{
    [JsonPropertyName("data")]
    public List<AirportResource>? Data { get; set; }
}

/// <summary>
/// The airportsapi.com response envelope for a lookup of a single airport by code.
/// </summary>
internal sealed class SingleAirportResponse
{
    [JsonPropertyName("data")]
    public AirportResource? Data { get; set; }
}

/// <summary>
/// A single airport entry in an airportsapi.com response.
/// </summary>
internal sealed class AirportResource
{
    [JsonPropertyName("attributes")]
    public AirportAttributes? Attributes { get; set; }
}

/// <summary>
/// The airport fields used for matching and display.
/// </summary>
internal sealed class AirportAttributes
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("icao_code")]
    public string? IcaoCode { get; set; }

    [JsonPropertyName("iata_code")]
    public string? IataCode { get; set; }

    [JsonPropertyName("gps_code")]
    public string? GpsCode { get; set; }

    [JsonPropertyName("local_code")]
    public string? LocalCode { get; set; }

    /// <summary>
    /// Returns every code the airport is known by, for comparison against user input.
    /// </summary>
    public IEnumerable<string?> GetAllCodes()
    {
        yield return Code;
        yield return IcaoCode;
        yield return IataCode;
        yield return GpsCode;
        yield return LocalCode;
    }
}
