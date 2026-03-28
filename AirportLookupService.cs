using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using MetarViewer.Models;

namespace MetarViewer.Services;

public interface IAirportLookupService
{
    Task<string?> ResolveAirportAsync(string input);
}

public class AirportLookupService : IAirportLookupService
{
    internal const string AirportsApiHttpClientName = "AirportsApi";
    internal static readonly Uri AirportsApiBaseUri = new("https://airportsapi.com/api/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LookupCacheLifetime = TimeSpan.FromMinutes(10);

    private readonly ConcurrentDictionary<string, AirportLookupCacheEntry> _lookupCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _airportsLoadLock = new(1, 1);
    private readonly IHttpClientFactory _httpClientFactory;
    private List<Airport>? _airports;
    private readonly string _dataPath;

    public AirportLookupService(string dataPath, IHttpClientFactory httpClientFactory)
    {
        _dataPath = dataPath;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string?> ResolveAirportAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmedInput = input.Trim();
        var normalizedInput = trimmedInput.ToUpperInvariant();

        if (TryGetCachedResult(normalizedInput, out var cachedResult))
        {
            return cachedResult;
        }

        await EnsureAirportsLoadedAsync();

        var localMatch = ResolveFromLocalAirports(trimmedInput, normalizedInput);
        if (!string.IsNullOrEmpty(localMatch))
        {
            CacheLookupResult(normalizedInput, localMatch);
            return localMatch;
        }

        var apiMatch = await ResolveViaAirportsApiAsync(trimmedInput, normalizedInput);
        if (!string.IsNullOrEmpty(apiMatch))
        {
            CacheLookupResult(normalizedInput, apiMatch);
            return apiMatch;
        }

        var heuristicMatch = ResolveViaHeuristic(normalizedInput);
        CacheLookupResult(normalizedInput, heuristicMatch);
        return heuristicMatch;
    }

    private bool TryGetCachedResult(string normalizedInput, out string? cachedResult)
    {
        if (_lookupCache.TryGetValue(normalizedInput, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
            {
                cachedResult = entry.Result;
                return true;
            }

            _lookupCache.TryRemove(normalizedInput, out _);
        }

        cachedResult = null;
        return false;
    }

    private void CacheLookupResult(string normalizedInput, string? result)
    {
        _lookupCache[normalizedInput] = new AirportLookupCacheEntry(
            result,
            DateTimeOffset.UtcNow.Add(LookupCacheLifetime));
    }

    private async Task EnsureAirportsLoadedAsync()
    {
        if (_airports != null)
        {
            return;
        }

        await _airportsLoadLock.WaitAsync();

        try
        {
            if (_airports == null)
            {
                await LoadAirportsAsync();
            }
        }
        finally
        {
            _airportsLoadLock.Release();
        }
    }

    private async Task LoadAirportsAsync()
    {
        try
        {
            if (File.Exists(_dataPath))
            {
                var json = await File.ReadAllTextAsync(_dataPath);
                _airports = JsonSerializer.Deserialize<List<Airport>>(json, JsonOptions) ?? new List<Airport>();
            }
            else
            {
                _airports = new List<Airport>();
            }
        }
        catch
        {
            _airports = new List<Airport>();
        }
    }

    private string? ResolveFromLocalAirports(string trimmedInput, string normalizedInput)
    {
        if (_airports == null || _airports.Count == 0)
        {
            return null;
        }

        var exactIcao = _airports.FirstOrDefault(a =>
            a.Icao.Equals(normalizedInput, StringComparison.OrdinalIgnoreCase));
        if (exactIcao != null)
        {
            return exactIcao.Icao;
        }

        var exactIata = _airports.FirstOrDefault(a =>
            !string.IsNullOrEmpty(a.Iata) &&
            a.Iata.Equals(normalizedInput, StringComparison.OrdinalIgnoreCase));
        if (exactIata != null)
        {
            return exactIata.Icao;
        }

        var nameMatch = _airports.FirstOrDefault(a =>
            a.Name.Contains(trimmedInput, StringComparison.OrdinalIgnoreCase) ||
            a.City.Contains(trimmedInput, StringComparison.OrdinalIgnoreCase));

        return nameMatch?.Icao;
    }

    private async Task<string?> ResolveViaAirportsApiAsync(string trimmedInput, string normalizedInput)
    {
        var client = _httpClientFactory.CreateClient(AirportsApiHttpClientName);

        try
        {
            if (LooksLikeAirportCode(normalizedInput))
            {
                var directAirport = await GetAirportByCodeAsync(client, normalizedInput);
                var directMatch = GetStationIdentifier(directAirport?.Attributes);
                if (!string.IsNullOrEmpty(directMatch))
                {
                    return directMatch;
                }

                var codeMatches = await SearchAirportsAsync(client, ("filter[code]", normalizedInput));
                var exactCodeMatch = SelectExactCodeMatch(codeMatches, normalizedInput);
                if (!string.IsNullOrEmpty(exactCodeMatch))
                {
                    return exactCodeMatch;
                }
            }

            var nameMatches = await SearchAirportsAsync(client, ("filter[name]", trimmedInput));
            var nameMatch = SelectBestNameMatch(nameMatches, trimmedInput, normalizedInput);
            if (!string.IsNullOrEmpty(nameMatch))
            {
                return nameMatch;
            }
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }

    private static async Task<AirportsApiAirportResource?> GetAirportByCodeAsync(HttpClient client, string code)
    {
        using var response = await client.GetAsync($"airports/{Uri.EscapeDataString(code)}");

        if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        var payload = await JsonSerializer.DeserializeAsync<AirportsApiSingleAirportResponse>(stream, JsonOptions);
        return payload?.Data;
    }

    private static async Task<IReadOnlyList<AirportsApiAirportResource>> SearchAirportsAsync(
        HttpClient client,
        params (string Key, string Value)[] filters)
    {
        var queryParameters = filters
            .Where(filter => !string.IsNullOrWhiteSpace(filter.Value))
            .Select(filter => $"{Uri.EscapeDataString(filter.Key)}={Uri.EscapeDataString(filter.Value)}")
            .Append($"{Uri.EscapeDataString("page[size]")}=10");

        using var response = await client.GetAsync($"airports?{string.Join("&", queryParameters)}");

        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<AirportsApiAirportResource>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        var payload = await JsonSerializer.DeserializeAsync<AirportsApiAirportSearchResponse>(stream, JsonOptions);
        return payload?.Data is { Count: > 0 } matches
            ? matches
            : Array.Empty<AirportsApiAirportResource>();
    }

    private static string? SelectExactCodeMatch(IEnumerable<AirportsApiAirportResource> airports, string normalizedInput)
    {
        var exactMatch = airports.FirstOrDefault(airport => MatchesAnyCode(airport.Attributes, normalizedInput));
        return GetStationIdentifier(exactMatch?.Attributes);
    }

    private static string? SelectBestNameMatch(
        IEnumerable<AirportsApiAirportResource> airports,
        string trimmedInput,
        string normalizedInput)
    {
        var exactCodeMatch = airports.FirstOrDefault(airport => MatchesAnyCode(airport.Attributes, normalizedInput));
        if (exactCodeMatch != null)
        {
            return GetStationIdentifier(exactCodeMatch.Attributes);
        }

        var exactNameMatch = airports.FirstOrDefault(airport =>
            string.Equals(airport.Attributes?.Name, trimmedInput, StringComparison.OrdinalIgnoreCase));
        if (exactNameMatch != null)
        {
            return GetStationIdentifier(exactNameMatch.Attributes);
        }

        var partialNameMatch = airports.FirstOrDefault(airport =>
            airport.Attributes?.Name?.Contains(trimmedInput, StringComparison.OrdinalIgnoreCase) == true);
        if (partialNameMatch != null)
        {
            return GetStationIdentifier(partialNameMatch.Attributes);
        }

        return null;
    }

    private static bool MatchesAnyCode(AirportsApiAirportAttributes? attributes, string normalizedInput)
    {
        if (attributes == null)
        {
            return false;
        }

        return string.Equals(attributes.Code, normalizedInput, StringComparison.OrdinalIgnoreCase)
            || string.Equals(attributes.IcaoCode, normalizedInput, StringComparison.OrdinalIgnoreCase)
            || string.Equals(attributes.IataCode, normalizedInput, StringComparison.OrdinalIgnoreCase)
            || string.Equals(attributes.GpsCode, normalizedInput, StringComparison.OrdinalIgnoreCase)
            || string.Equals(attributes.LocalCode, normalizedInput, StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetStationIdentifier(AirportsApiAirportAttributes? attributes)
    {
        if (attributes == null)
        {
            return null;
        }

        return NormalizeStationIdentifier(attributes.IcaoCode)
            ?? NormalizeStationIdentifier(attributes.GpsCode)
            ?? NormalizeStationIdentifier(attributes.Code);
    }

    private static string? NormalizeStationIdentifier(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalized = candidate.Trim().ToUpperInvariant();
        return LooksLikeStationIdentifier(normalized) ? normalized : null;
    }

    private static string? ResolveViaHeuristic(string normalizedInput)
    {
        return normalizedInput.Length is >= 3 and <= 4 && normalizedInput.All(char.IsLetter)
            ? normalizedInput
            : null;
    }

    private static bool LooksLikeAirportCode(string normalizedInput)
    {
        return normalizedInput.Length is >= 3 and <= 4 && normalizedInput.All(char.IsLetterOrDigit);
    }

    private static bool LooksLikeStationIdentifier(string normalizedInput)
    {
        return normalizedInput.Length == 4 && normalizedInput.All(char.IsLetter);
    }

    private sealed record AirportLookupCacheEntry(string? Result, DateTimeOffset ExpiresAt);

    private sealed class AirportsApiAirportSearchResponse
    {
        [JsonPropertyName("data")]
        public List<AirportsApiAirportResource>? Data { get; set; }
    }

    private sealed class AirportsApiSingleAirportResponse
    {
        [JsonPropertyName("data")]
        public AirportsApiAirportResource? Data { get; set; }
    }

    private sealed class AirportsApiAirportResource
    {
        [JsonPropertyName("attributes")]
        public AirportsApiAirportAttributes? Attributes { get; set; }
    }

    private sealed class AirportsApiAirportAttributes
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("code")]
        public string? Code { get; set; }

        [JsonPropertyName("icao_code")]
        public string? IcaoCode { get; set; }

        [JsonPropertyName("iata_code")]
        public string? IataCode { get; set; }

        [JsonPropertyName("gps_code")]
        public string? GpsCode { get; set; }

        [JsonPropertyName("local_code")]
        public string? LocalCode { get; set; }
    }
}
