using System.Globalization;
using System.Net;
using System.Text.Json;
using MetarViewer.Models;
using MetarViewer.Parsing;

namespace MetarViewer.Services;

/// <summary>
/// Service for retrieving METAR data from aviationweather.gov API.
/// </summary>
public class AviationWeatherMetarService : IMetarService
{
    internal const string AviationWeatherHttpClientName = "AviationWeather";
    public static readonly Uri AviationWeatherBaseUri = new("https://aviationweather.gov/api/data/");

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, CachedMetar> _cache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Initializes a new instance of the <see cref="AviationWeatherMetarService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API requests.</param>
    public AviationWeatherMetarService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Asynchronously retrieves METAR data for a specific station ID from aviationweather.gov.
    /// </summary>
    /// <param name="stationId">The 4-character ICAO code for the station (e.g., KLAX, EGLL).</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A MetarData object if found, otherwise null.</returns>
    public async Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
    {
        var normalizedStationId = NormalizeStationId(stationId);
        if (string.IsNullOrEmpty(normalizedStationId))
        {
            return null;
        }

        // Check local cache first to avoid over-requesting
        if (_cache.TryGetValue(normalizedStationId, out var cached))
        {
            if (DateTime.UtcNow - cached.Timestamp < _cacheExpiration)
            {
                return cached.Data;
            }

            _cache.Remove(normalizedStationId);
        }

        try
        {
            // Fetch METAR data in JSON format from aviationweather.gov
            using var response = await _httpClient.GetAsync(
                $"metar?ids={Uri.EscapeDataString(normalizedStationId)}&format=json",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var reports = await JsonSerializer.DeserializeAsync<List<AviationWeatherMetarResponse>>(
                responseStream,
                SerializerOptions,
                cancellationToken);

            var report = reports?.FirstOrDefault();
            if (report == null)
            {
                return null;
            }

            var metarData = MapToMetarData(report, normalizedStationId);

            // Cache the result for future requests
            _cache[normalizedStationId] = new CachedMetar
            {
                Data = metarData,
                Timestamp = DateTime.UtcNow
            };

            return metarData;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <summary>
    /// Maps the API response model to our internal MetarData model.
    /// </summary>
    private static MetarData MapToMetarData(AviationWeatherMetarResponse response, string fallbackStationId)
    {
        var metarData = new MetarData
        {
            StationId = response.IcaoId ?? fallbackStationId,
            StationName = string.IsNullOrWhiteSpace(response.Name) ? null : response.Name.Trim(),
            RawMetar = response.RawObservation ?? string.Empty,
            Temperature = RoundToInt(response.Temperature),
            DewPoint = RoundToInt(response.DewPoint),
            WindDirection = response.WindDirection,
            WindSpeed = response.WindSpeed,
            WindGust = response.WindGust,
            // The API always reports visibility in statute miles.
            Visibility = VisibilityUnits.ParseDistance(response.Visibility),
            VisibilityUnit = string.IsNullOrWhiteSpace(response.Visibility) ? null : VisibilityUnits.StatuteMiles,
            Altimeter = response.Altimeter,
            AltimeterUnit = response.Altimeter.HasValue ? PressureUnits.Hectopascals : null,
            FlightCategory = response.FlightCategory,
            IsCavok = response.RawObservation?.Contains("CAVOK", StringComparison.OrdinalIgnoreCase) ?? false
        };

        if (TryParseObservationTime(response, out var observationTime))
        {
            metarData.ObservationTime = observationTime;
        }

        // Map cloud layers if present
        if (response.Clouds != null)
        {
            foreach (var cloud in response.Clouds)
            {
                metarData.CloudLayers.Add(new CloudLayer
                {
                    Coverage = cloud.Cover ?? string.Empty,
                    Altitude = cloud.Base,
                    Type = cloud.Type
                });
            }
        }

        // Extract weather phenomena from various fields
        foreach (var weatherCode in ExtractWeatherPhenomena(response))
        {
            metarData.WeatherPhenomena.Add(weatherCode);
        }

        return metarData;
    }

    private static string NormalizeStationId(string stationId)
    {
        return stationId.Trim().ToUpperInvariant();
    }

    private static int? RoundToInt(decimal? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Attempts to parse the observation time from multiple possible response fields.
    /// </summary>
    private static bool TryParseObservationTime(AviationWeatherMetarResponse response, out DateTime observationTime)
    {
        if (DateTimeOffset.TryParse(
                response.ReportTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var reportTime))
        {
            observationTime = reportTime.UtcDateTime;
            return true;
        }

        if (response.ObservationEpoch.HasValue)
        {
            observationTime = DateTimeOffset.FromUnixTimeSeconds(response.ObservationEpoch.Value).UtcDateTime;
            return true;
        }

        observationTime = default;
        return false;
    }

    /// <summary>
    /// Extracts weather phenomena codes (like RA, SN, FG) from the weather string and raw METAR.
    /// </summary>
    private static IEnumerable<string> ExtractWeatherPhenomena(AviationWeatherMetarResponse response)
    {
        var source = string.IsNullOrWhiteSpace(response.WeatherString)
            ? response.RawObservation
            : response.WeatherString;

        if (string.IsNullOrWhiteSpace(source))
        {
            return [];
        }

        return source
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToUpperInvariant())
            .Where(MetarTokenClassifier.LooksLikeWeatherToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Internal representation of a cached METAR entry.
    /// </summary>
    private class CachedMetar
    {
        public MetarData Data { get; set; } = null!;
        public DateTime Timestamp { get; set; }
    }
}
