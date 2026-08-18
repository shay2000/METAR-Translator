using System.Text.Json;

namespace MetarViewer.Airports;

/// <summary>
/// An airport that matched the search, together with the station its weather is reported under
/// and how well it matched.
/// </summary>
/// <param name="StationId">The ICAO station identifier to request weather for.</param>
/// <param name="Attributes">The airport as returned by the API.</param>
/// <param name="Score">The match score; see <see cref="AirportMatchScorer"/>.</param>
internal sealed record AirportMatch(string StationId, AirportAttributes Attributes, int Score);

/// <summary>
/// Searches airportsapi.com for the airports a search string might mean, best match first.
///
/// This is the search strategy on its own: try the cheapest, most specific query, and only widen
/// the search while the result is still in doubt. It previously shared a method with the HTTP
/// calls, the JSON envelopes and the scoring, so the order of the searches could not be read
/// without reading all of them.
/// </summary>
internal sealed class AirportCandidateFinder
{
    /// <summary>
    /// The number of matches that makes broadening the search pointless. Only five suggestions
    /// are ever shown, so further requests could not change what the user sees.
    /// </summary>
    private const int SufficientMatchCount = 5;

    private readonly IAirportsApiClient _apiClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="AirportCandidateFinder"/> class.
    /// </summary>
    public AirportCandidateFinder(IAirportsApiClient apiClient)
    {
        ArgumentNullException.ThrowIfNull(apiClient);

        _apiClient = apiClient;
    }

    /// <summary>
    /// Finds the airports matching a search string, ordered by descending score.
    /// </summary>
    /// <param name="trimmedInput">The search text as typed.</param>
    /// <param name="normalizedInput">The upper-cased search text.</param>
    /// <param name="cancellationToken">Abandons the search.</param>
    /// <returns>
    /// The matches, or an empty list if the API could not be reached. A failed search is reported
    /// as "no matches" so the caller can fall back rather than surface an error for a lookup the
    /// user did not explicitly ask for.
    /// </returns>
    public async Task<IReadOnlyList<AirportMatch>> FindAsync(
        string trimmedInput,
        string normalizedInput,
        CancellationToken cancellationToken = default)
    {
        // Keyed by station so the same airport found by several searches is kept once, at its
        // best score.
        var matches = new Dictionary<string, AirportMatch>(StringComparer.OrdinalIgnoreCase);

        try
        {
            if (AirportCodes.LooksLikeAirportCode(normalizedInput))
            {
                // An exact code is the one search that can answer outright, so it goes first.
                var exactAirport = await _apiClient.GetAirportByCodeAsync(normalizedInput, cancellationToken).ConfigureAwait(false);
                if (TryAdd(matches, exactAirport, trimmedInput, normalizedInput) && HasConfidentMatch(matches))
                {
                    return Order(matches);
                }

                // The API indexes several kinds of code, so a code it did not publish the airport
                // under may still be searchable.
                await AddSearchResultsAsync(
                    matches,
                    new AirportSearchQuery(AirportSearchQuery.CodeFilter, normalizedInput, AirportSearchQuery.CodePageSize),
                    trimmedInput,
                    normalizedInput,
                    cancellationToken).ConfigureAwait(false);

                if (HasConfidentMatch(matches))
                {
                    return Order(matches);
                }
            }

            // Codes are also words, so a name search runs even for something that looks like a
            // code: "SAN" is both an airport code and the start of many airport names.
            await AddSearchResultsAsync(
                matches,
                new AirportSearchQuery(AirportSearchQuery.NameFilter, trimmedInput, AirportSearchQuery.NamePageSize),
                trimmedInput,
                normalizedInput,
                cancellationToken).ConfigureAwait(false);

            if (HasConfidentMatch(matches))
            {
                return Order(matches);
            }

            if (matches.Count < SufficientMatchCount)
            {
                // Every fallback is tried even once something has been found, because a shorter
                // fragment can still turn up a much better match for a misspelling.
                foreach (var relaxedQuery in AirportSearchQueryBuilder.BuildRelaxedQueries(trimmedInput, normalizedInput))
                {
                    await AddSearchResultsAsync(matches, relaxedQuery, trimmedInput, normalizedInput, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (HttpRequestException)
        {
            return Array.Empty<AirportMatch>();
        }
        catch (TaskCanceledException)
        {
            return Array.Empty<AirportMatch>();
        }
        catch (JsonException)
        {
            return Array.Empty<AirportMatch>();
        }

        return Order(matches);
    }

    private async Task AddSearchResultsAsync(
        IDictionary<string, AirportMatch> matches,
        AirportSearchQuery query,
        string trimmedInput,
        string normalizedInput,
        CancellationToken cancellationToken)
    {
        var airports = await _apiClient.SearchAsync(query, cancellationToken).ConfigureAwait(false);

        foreach (var airport in airports)
        {
            TryAdd(matches, airport, trimmedInput, normalizedInput);
        }
    }

    /// <summary>
    /// Records an airport as a match unless it cannot be flown to, has no station identifier, or
    /// has already been found by a better-scoring search.
    /// </summary>
    private static bool TryAdd(
        IDictionary<string, AirportMatch> matches,
        AirportAttributes? attributes,
        string trimmedInput,
        string normalizedInput)
    {
        // Without a station identifier there is no way to ask for the airport's weather, which is
        // the only reason to offer it.
        var stationId = AirportCodes.GetStationIdentifier(attributes);
        if (stationId == null || attributes == null)
        {
            return false;
        }

        var score = AirportMatchScorer.Score(attributes, trimmedInput, normalizedInput);
        if (score == AirportMatchScorer.NoMatch)
        {
            return false;
        }

        if (matches.TryGetValue(stationId, out var existing) && score <= existing.Score)
        {
            return false;
        }

        matches[stationId] = new AirportMatch(stationId, attributes, score);
        return true;
    }

    private static bool HasConfidentMatch(Dictionary<string, AirportMatch> matches) =>
        matches.Values.Any(match => match.Score >= AirportMatchScorer.ConfidentScore);

    /// <summary>
    /// Orders matches by score, falling back to the airport name so that equally good matches
    /// are always listed in the same order.
    /// </summary>
    private static List<AirportMatch> Order(Dictionary<string, AirportMatch> matches) =>
        matches.Values
            .OrderByDescending(match => match.Score)
            .ThenBy(match => match.Attributes.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
