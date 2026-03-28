using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace MetarViewer.Services;

public sealed record ResolvedAirport(string StationId, string? DisplayName, string? IataCode);

public sealed record AirportSuggestion(string StationId, string DisplayName, string? IataCode)
{
    public string DisplayText =>
        string.IsNullOrWhiteSpace(DisplayName)
            ? StationId
            : $"{StationId} - {DisplayName}";

    public override string ToString() => DisplayText;
}

public interface IAirportLookupService
{
    Task<string?> ResolveAirportAsync(string input);
    Task<ResolvedAirport?> ResolveAirportDetailsAsync(string input, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AirportSuggestion>> GetSuggestionsAsync(string input, CancellationToken cancellationToken = default);
}

public class AirportLookupService : IAirportLookupService
{
    internal const string AirportsApiHttpClientName = "AirportsApi";
    internal static readonly Uri AirportsApiBaseUri = new("https://airportsapi.com/api/");
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan LookupCacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SuggestionCacheLifetime = TimeSpan.FromMinutes(2);
    private static readonly Regex NonAlphaNumericRegex = new("[^A-Z0-9]+", RegexOptions.Compiled);
    private static readonly HashSet<string> IgnoredNameWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "AIRPORT", "AIRFIELD", "AERODROME", "INTERNATIONAL", "INTL", "REGIONAL",
        "MUNICIPAL", "CITY", "FIELD", "HELIPORT", "BASE", "STRIP"
    };

    private readonly ConcurrentDictionary<string, AirportLookupCacheEntry> _lookupCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AirportSuggestionCacheEntry> _suggestionCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly IHttpClientFactory _httpClientFactory;

    public AirportLookupService(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<string?> ResolveAirportAsync(string input)
    {
        var result = await ResolveAirportDetailsAsync(input);
        return result?.StationId;
    }

    public async Task<ResolvedAirport?> ResolveAirportDetailsAsync(string input, CancellationToken cancellationToken = default)
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

        var apiMatch = await ResolveViaAirportsApiAsync(trimmedInput, normalizedInput, cancellationToken);
        if (apiMatch != null)
        {
            CacheLookupResult(normalizedInput, apiMatch);
            return apiMatch;
        }

        var heuristicMatch = ResolveViaHeuristic(normalizedInput);
        CacheLookupResult(normalizedInput, heuristicMatch);
        return heuristicMatch;
    }

    public async Task<IReadOnlyList<AirportSuggestion>> GetSuggestionsAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Trim().Length < 2)
        {
            return Array.Empty<AirportSuggestion>();
        }

        var trimmedInput = input.Trim();
        var normalizedInput = trimmedInput.ToUpperInvariant();

        if (TryGetCachedSuggestions(normalizedInput, out var cachedSuggestions))
        {
            return cachedSuggestions;
        }

        var candidates = await FindAirportCandidatesAsync(trimmedInput, normalizedInput, includeRelaxedSearch: true, cancellationToken);
        var suggestions = candidates
            .Take(5)
            .Select(candidate => new AirportSuggestion(
                candidate.Resolution.StationId,
                candidate.Resolution.DisplayName ?? candidate.Resolution.StationId,
                candidate.Resolution.IataCode))
            .ToList();

        CacheSuggestions(normalizedInput, suggestions);
        return suggestions;
    }

    private bool TryGetCachedResult(string normalizedInput, out ResolvedAirport? cachedResult)
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

    private bool TryGetCachedSuggestions(string normalizedInput, out IReadOnlyList<AirportSuggestion> suggestions)
    {
        if (_suggestionCache.TryGetValue(normalizedInput, out var entry))
        {
            if (entry.ExpiresAt > DateTimeOffset.UtcNow)
            {
                suggestions = entry.Results;
                return true;
            }

            _suggestionCache.TryRemove(normalizedInput, out _);
        }

        suggestions = Array.Empty<AirportSuggestion>();
        return false;
    }

    private void CacheLookupResult(string normalizedInput, ResolvedAirport? result)
    {
        _lookupCache[normalizedInput] = new AirportLookupCacheEntry(
            result,
            DateTimeOffset.UtcNow.Add(LookupCacheLifetime));
    }

    private void CacheSuggestions(string normalizedInput, IReadOnlyList<AirportSuggestion> suggestions)
    {
        _suggestionCache[normalizedInput] = new AirportSuggestionCacheEntry(
            suggestions,
            DateTimeOffset.UtcNow.Add(SuggestionCacheLifetime));
    }

    private async Task<ResolvedAirport?> ResolveViaAirportsApiAsync(
        string trimmedInput,
        string normalizedInput,
        CancellationToken cancellationToken)
    {
        var candidates = await FindAirportCandidatesAsync(trimmedInput, normalizedInput, includeRelaxedSearch: true, cancellationToken);
        var bestCandidate = candidates.FirstOrDefault();

        return bestCandidate is { Score: >= 120 }
            ? bestCandidate.Resolution
            : null;
    }

    private async Task<List<AirportCandidate>> FindAirportCandidatesAsync(
        string trimmedInput,
        string normalizedInput,
        bool includeRelaxedSearch,
        CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient(AirportsApiHttpClientName);
        var collectedCandidates = new Dictionary<string, AirportCandidate>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (LooksLikeAirportCode(normalizedInput))
            {
                var directAirport = await GetAirportByCodeAsync(client, normalizedInput, cancellationToken);
                var directCandidate = ToAirportCandidate(directAirport?.Attributes, trimmedInput, normalizedInput);
                if (directCandidate != null)
                {
                    collectedCandidates[directCandidate.Resolution.StationId] = directCandidate;

                    if (HasConfidentCandidate(collectedCandidates))
                    {
                        return OrderCandidates(collectedCandidates);
                    }
                }

                var codeMatches = await SearchAirportsAsync(
                    client,
                    20,
                    cancellationToken,
                    ("filter[code]", normalizedInput),
                    ("sort", "name"),
                    ("include", "country,region"));
                AddCandidates(collectedCandidates, codeMatches, trimmedInput, normalizedInput);

                if (HasConfidentCandidate(collectedCandidates))
                {
                    return OrderCandidates(collectedCandidates);
                }
            }

            var nameMatches = await SearchAirportsAsync(
                client,
                20,
                cancellationToken,
                ("filter[name]", trimmedInput),
                ("sort", "name"),
                ("include", "country,region"));

            AddCandidates(collectedCandidates, nameMatches, trimmedInput, normalizedInput);

            if (HasConfidentCandidate(collectedCandidates))
            {
                return OrderCandidates(collectedCandidates);
            }

            if (includeRelaxedSearch && collectedCandidates.Count < 5)
            {
                foreach (var relaxedQuery in BuildRelaxedQueries(trimmedInput, normalizedInput))
                {
                    var relaxedMatches = await SearchAirportsAsync(
                        client,
                        relaxedQuery.PageSize,
                        cancellationToken,
                        (relaxedQuery.FilterKey, relaxedQuery.Value),
                        ("sort", "name"),
                        ("include", "country,region"));

                    AddCandidates(collectedCandidates, relaxedMatches, trimmedInput, normalizedInput);
                }
            }
        }
        catch (HttpRequestException)
        {
            return new List<AirportCandidate>();
        }
        catch (TaskCanceledException)
        {
            return new List<AirportCandidate>();
        }
        catch (JsonException)
        {
            return new List<AirportCandidate>();
        }

        return OrderCandidates(collectedCandidates);
    }

    private static async Task<AirportsApiAirportResource?> GetAirportByCodeAsync(
        HttpClient client,
        string code,
        CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync($"airports/{Uri.EscapeDataString(code)}", cancellationToken);

        if (response.StatusCode == HttpStatusCode.NotFound || !response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<AirportsApiSingleAirportResponse>(stream, JsonOptions, cancellationToken);
        return payload?.Data;
    }

    private static async Task<IReadOnlyList<AirportsApiAirportResource>> SearchAirportsAsync(
        HttpClient client,
        int pageSize,
        CancellationToken cancellationToken,
        params (string Key, string Value)[] queryParameters)
    {
        var serializedQueryParameters = queryParameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
            .Select(parameter => $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value)}")
            .Append($"{Uri.EscapeDataString("page[size]")}={pageSize}");

        using var response = await client.GetAsync($"airports?{string.Join("&", serializedQueryParameters)}", cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return Array.Empty<AirportsApiAirportResource>();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<AirportsApiAirportSearchResponse>(stream, JsonOptions, cancellationToken);
        return payload?.Data is { Count: > 0 } matches
            ? matches
            : Array.Empty<AirportsApiAirportResource>();
    }

    private static void AddCandidates(
        IDictionary<string, AirportCandidate> collectedCandidates,
        IEnumerable<AirportsApiAirportResource> airports,
        string trimmedInput,
        string normalizedInput)
    {
        foreach (var airport in airports)
        {
            var candidate = ToAirportCandidate(airport.Attributes, trimmedInput, normalizedInput);
            if (candidate == null)
            {
                continue;
            }

            if (!collectedCandidates.TryGetValue(candidate.Resolution.StationId, out var existingCandidate) ||
                candidate.Score > existingCandidate.Score)
            {
                collectedCandidates[candidate.Resolution.StationId] = candidate;
            }
        }
    }

    private static bool HasConfidentCandidate(IDictionary<string, AirportCandidate> collectedCandidates)
    {
        return collectedCandidates.Values.Any(candidate => candidate.Score >= 250);
    }

    private static List<AirportCandidate> OrderCandidates(IDictionary<string, AirportCandidate> collectedCandidates)
    {
        return collectedCandidates.Values
            .OrderByDescending(candidate => candidate.Score)
            .ThenBy(candidate => candidate.Resolution.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AirportCandidate? ToAirportCandidate(
        AirportsApiAirportAttributes? attributes,
        string trimmedInput,
        string normalizedInput)
    {
        var stationIdentifier = GetStationIdentifier(attributes);
        if (string.IsNullOrEmpty(stationIdentifier))
        {
            return null;
        }

        var score = ScoreAirportMatch(attributes, trimmedInput, normalizedInput);
        if (score == int.MinValue)
        {
            return null;
        }

        return new AirportCandidate(
            new ResolvedAirport(stationIdentifier, attributes?.Name, attributes?.IataCode),
            score);
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

    private static int ScoreAirportMatch(
        AirportsApiAirportAttributes? attributes,
        string trimmedInput,
        string normalizedInput)
    {
        if (attributes == null || !IsSupportedAirportType(attributes.Type))
        {
            return int.MinValue;
        }

        var score = GetAirportTypeScore(attributes.Type);
        var airportName = attributes.Name ?? string.Empty;

        if (MatchesAnyCode(attributes, normalizedInput))
        {
            score += 500;
        }

        if (string.Equals(airportName, trimmedInput, StringComparison.OrdinalIgnoreCase))
        {
            score += 300;
        }
        else if (airportName.StartsWith(trimmedInput, StringComparison.OrdinalIgnoreCase))
        {
            score += 220;
        }
        else if (airportName.Contains(trimmedInput, StringComparison.OrdinalIgnoreCase))
        {
            score += 150;
        }

        if (!string.IsNullOrWhiteSpace(attributes.IataCode))
        {
            score += 10;
        }

        score += GetFuzzyScore(attributes, normalizedInput);
        return score;
    }

    private static int GetFuzzyScore(AirportsApiAirportAttributes attributes, string normalizedInput)
    {
        var normalizedQuery = NormalizeForFuzzyMatching(normalizedInput);
        if (string.IsNullOrEmpty(normalizedQuery))
        {
            return 0;
        }

        var candidateTerms = new List<string>();
        AddCandidateTerm(candidateTerms, attributes.Name);
        AddCandidateTerm(candidateTerms, attributes.Code);
        AddCandidateTerm(candidateTerms, attributes.IcaoCode);
        AddCandidateTerm(candidateTerms, attributes.IataCode);
        AddCandidateTerm(candidateTerms, attributes.GpsCode);
        AddCandidateTerm(candidateTerms, attributes.LocalCode);

        foreach (var token in GetNameTokens(attributes.Name))
        {
            AddCandidateTerm(candidateTerms, token);
        }

        if (candidateTerms.Count == 0)
        {
            return 0;
        }

        var bestScore = 0;
        foreach (var term in candidateTerms)
        {
            var distance = CalculateLevenshteinDistance(normalizedQuery, term);
            var lengthPenalty = Math.Abs(term.Length - normalizedQuery.Length) * 3;
            var score = Math.Max(0, 120 - (distance * 28) - lengthPenalty);
            if (score > bestScore)
            {
                bestScore = score;
            }
        }

        return bestScore;
    }

    private static void AddCandidateTerm(ICollection<string> candidateTerms, string? value)
    {
        var normalized = NormalizeForFuzzyMatching(value);
        if (!string.IsNullOrEmpty(normalized))
        {
            candidateTerms.Add(normalized);
        }
    }

    private static IEnumerable<string> GetNameTokens(string? airportName)
    {
        if (string.IsNullOrWhiteSpace(airportName))
        {
            return Array.Empty<string>();
        }

        return airportName
            .Split([' ', '/', '-', ',', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length >= 3 && !IgnoredNameWords.Contains(token));
    }

    private static string NormalizeForFuzzyMatching(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return NonAlphaNumericRegex.Replace(value.ToUpperInvariant(), string.Empty);
    }

    private static int CalculateLevenshteinDistance(string source, string target)
    {
        if (source.Length == 0)
        {
            return target.Length;
        }

        if (target.Length == 0)
        {
            return source.Length;
        }

        var distances = new int[source.Length + 1, target.Length + 1];

        for (var sourceIndex = 0; sourceIndex <= source.Length; sourceIndex++)
        {
            distances[sourceIndex, 0] = sourceIndex;
        }

        for (var targetIndex = 0; targetIndex <= target.Length; targetIndex++)
        {
            distances[0, targetIndex] = targetIndex;
        }

        for (var sourceIndex = 1; sourceIndex <= source.Length; sourceIndex++)
        {
            for (var targetIndex = 1; targetIndex <= target.Length; targetIndex++)
            {
                var cost = source[sourceIndex - 1] == target[targetIndex - 1] ? 0 : 1;

                distances[sourceIndex, targetIndex] = Math.Min(
                    Math.Min(
                        distances[sourceIndex - 1, targetIndex] + 1,
                        distances[sourceIndex, targetIndex - 1] + 1),
                    distances[sourceIndex - 1, targetIndex - 1] + cost);
            }
        }

        return distances[source.Length, target.Length];
    }

    private static IEnumerable<RelaxedQuery> BuildRelaxedQueries(string trimmedInput, string normalizedInput)
    {
        var seenQueries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (LooksLikeAirportCode(normalizedInput) && normalizedInput.Length >= 3)
        {
            var prefixLength = Math.Max(3, normalizedInput.Length - 1);
            var codePrefix = normalizedInput[..prefixLength];
            if (seenQueries.Add($"filter[code]:{codePrefix}"))
            {
                yield return new RelaxedQuery("filter[code]", codePrefix, 20);
            }
        }

        var alphanumericInput = NormalizeForFuzzyMatching(trimmedInput);
        foreach (var queryFragment in GetRelaxedNameFragments(trimmedInput, alphanumericInput))
        {
            if (seenQueries.Add($"filter[name]:{queryFragment}"))
            {
                yield return new RelaxedQuery("filter[name]", queryFragment, 50);
            }
        }
    }

    private static IEnumerable<string> GetRelaxedNameFragments(string trimmedInput, string alphanumericInput)
    {
        foreach (var token in trimmedInput
                     .Split([' ', '/', '-', ',', '(', ')'], StringSplitOptions.RemoveEmptyEntries)
                     .Where(token => token.Length >= 2))
        {
            foreach (var fragment in BuildFragments(token))
            {
                yield return fragment;
            }
        }

        foreach (var fragment in BuildFragments(alphanumericInput))
        {
            yield return fragment;
        }
    }

    private static IEnumerable<string> BuildFragments(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            yield break;
        }

        var normalizedValue = value.Trim();
        if (normalizedValue.Length >= 5)
        {
            yield return normalizedValue[..5];
        }

        if (normalizedValue.Length >= 4)
        {
            yield return normalizedValue[..4];
        }

        if (normalizedValue.Length >= 2)
        {
            yield return normalizedValue[..2];
        }
    }

    private static bool IsSupportedAirportType(string? airportType)
    {
        return !string.Equals(airportType, "closed", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetAirportTypeScore(string? airportType)
    {
        return airportType switch
        {
            "large_airport" => 120,
            "medium_airport" => 90,
            "small_airport" => 60,
            "seaplane_base" => 25,
            "heliport" => -10,
            "balloonport" => -20,
            _ => 0
        };
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

    private static ResolvedAirport? ResolveViaHeuristic(string normalizedInput)
    {
        return normalizedInput.Length is >= 3 and <= 4 && normalizedInput.All(char.IsLetter)
            ? new ResolvedAirport(normalizedInput, null, null)
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

    private sealed record AirportLookupCacheEntry(ResolvedAirport? Result, DateTimeOffset ExpiresAt);
    private sealed record AirportSuggestionCacheEntry(IReadOnlyList<AirportSuggestion> Results, DateTimeOffset ExpiresAt);
    private sealed record AirportCandidate(ResolvedAirport Resolution, int Score);
    private sealed record RelaxedQuery(string FilterKey, string Value, int PageSize);

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
    }
}
