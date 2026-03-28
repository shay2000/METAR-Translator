using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using MetarViewer.Models;

namespace MetarViewer.Services;

public class VatsimMetarService
{
    internal const string VatsimMetarHttpClientName = "VatsimMetar";
    public static readonly Uri VatsimMetarBaseUri = new("https://metar.vatsim.net/");

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, CachedMetar> _cache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(60);

    public VatsimMetarService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
    {
        var normalizedStationId = stationId.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalizedStationId))
        {
            return null;
        }

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

            var metarData = RawMetarParser.Parse(report.Metar, normalizedStationId);
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

    private sealed class CachedMetar
    {
        public MetarData Data { get; set; } = null!;
        public DateTime Timestamp { get; set; }
    }

    private sealed class VatsimMetarResponse
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("metar")]
        public string? Metar { get; set; }
    }
}
