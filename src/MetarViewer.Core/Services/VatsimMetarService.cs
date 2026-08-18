using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MetarViewer.Models;

namespace MetarViewer.Services;

/// <summary>
/// Service for retrieving METAR data from the VATSIM METAR API.
/// </summary>
/// <remarks>
/// This service always queries the API. Caching is applied by
/// <see cref="CachingMetarService"/> so that the policy lives in one place.
/// </remarks>
public sealed class VatsimMetarService : IMetarService
{
    internal const string VatsimMetarHttpClientName = "VatsimMetar";
    public static readonly Uri VatsimMetarBaseUri = new("https://metar.vatsim.net/");

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="VatsimMetarService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client to use for API requests.</param>
    public VatsimMetarService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    /// <summary>
    /// Asynchronously retrieves METAR data for a specific station ID from VATSIM.
    /// </summary>
    /// <param name="stationId">The 4-character ICAO code for the station.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A MetarData object if found, otherwise null.</returns>
    public async Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
    {
        var normalizedStationId = StationId.Normalize(stationId);
        if (normalizedStationId.Length == 0)
        {
            return null;
        }

        try
        {
            // API expects station ID in the URL and format=json
            using var response = await _httpClient.GetAsync(
                $"{Uri.EscapeDataString(normalizedStationId)}?format=json",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent || !response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var reports = await JsonSerializer.DeserializeAsync<List<VatsimMetarResponse>>(
                responseStream,
                SerializerOptions,
                cancellationToken);

            var report = reports?.FirstOrDefault(item => string.Equals(item.Id, normalizedStationId, StringComparison.OrdinalIgnoreCase));
            if (report == null || string.IsNullOrWhiteSpace(report.Metar))
            {
                return null;
            }

            // VATSIM provides raw METAR strings, so we use RawMetarParser
            return RawMetarParser.Parse(report.Metar, normalizedStationId);
        }
        catch (HttpRequestException)
        {
            // Network level errors
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timeout or background cancellation
            return null;
        }
        catch (JsonException)
        {
            // Data format errors
            return null;
        }
    }

    /// <summary>
    /// Represents the JSON response structure from the VATSIM API.
    /// </summary>
    private sealed class VatsimMetarResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("metar")]
        public string? Metar { get; set; }
    }
}
