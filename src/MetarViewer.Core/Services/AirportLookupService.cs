using MetarViewer.Airports;

namespace MetarViewer.Services;

/// <summary>
/// Implementation of <see cref="IAirportLookupService"/> using airportsapi.com.
/// Features caching, fuzzy matching, and station ID heuristics.
///
/// This class was previously a 737-line file that held the HTTP calls, the JSON response
/// envelopes, the URL building, the fuzzy scoring, the query fallbacks and two hand-rolled caches.
/// It now only decides what to do with a search: read the cache, ask
/// <see cref="AirportCandidateFinder"/>, and choose between its answer and a local guess.
/// </summary>
public sealed class AirportLookupService : IAirportLookupService
{
    /// <summary>
    /// The shortest input worth searching for. One character matches so many airports that the
    /// results would be meaningless, and the request would be made on the first keystroke.
    /// </summary>
    private const int MinimumSuggestionInputLength = 2;

    /// <summary>How many suggestions to offer, which is what the search box can show.</summary>
    private const int MaximumSuggestionCount = 5;

    // Resolutions are held longer than suggestions because an airport's identity does not change,
    // whereas suggestions are cached only to stop typing from re-querying the API.
    private static readonly TimeSpan ResolutionCacheLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SuggestionCacheLifetime = TimeSpan.FromMinutes(2);

    private readonly AirportCandidateFinder _candidateFinder;
    private readonly ExpiringCache<ResolvedAirport> _resolutionCache;
    private readonly ExpiringCache<IReadOnlyList<AirportSuggestion>> _suggestionCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="AirportLookupService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Supplies the client configured for airportsapi.com.</param>
    public AirportLookupService(IHttpClientFactory httpClientFactory)
        : this(new AirportCandidateFinder(new AirportsApiClient(httpClientFactory)))
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AirportLookupService"/> class with an
    /// explicit search implementation and clock, for testing.
    /// </summary>
    /// <param name="candidateFinder">Finds the airports a search string might mean.</param>
    /// <param name="timeProvider">The clock used for cache expiry. Defaults to the system clock.</param>
    internal AirportLookupService(AirportCandidateFinder candidateFinder, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(candidateFinder);

        _candidateFinder = candidateFinder;
        _resolutionCache = new ExpiringCache<ResolvedAirport>(ResolutionCacheLifetime, timeProvider);
        _suggestionCache = new ExpiringCache<IReadOnlyList<AirportSuggestion>>(SuggestionCacheLifetime, timeProvider);
    }

    /// <inheritdoc />
    public async Task<string?> ResolveAirportAsync(string input, CancellationToken cancellationToken = default)
    {
        var resolvedAirport = await ResolveAirportDetailsAsync(input, cancellationToken).ConfigureAwait(false);
        return resolvedAirport?.StationId;
    }

    /// <inheritdoc />
    public async Task<ResolvedAirport?> ResolveAirportDetailsAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        var trimmedInput = input.Trim();
        var normalizedInput = trimmedInput.ToUpperInvariant();

        if (_resolutionCache.TryGet(normalizedInput, out var cachedResolution))
        {
            return cachedResolution;
        }

        var matches = await _candidateFinder.FindAsync(trimmedInput, normalizedInput, cancellationToken).ConfigureAwait(false);
        var resolution = GetConfidentResolution(matches) ?? ResolveWithoutApi(normalizedInput);

        // The outcome is cached even when nothing was found, so that an input the API cannot
        // resolve is not searched for again on every refresh.
        _resolutionCache.Set(normalizedInput, resolution);
        return resolution;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AirportSuggestion>> GetSuggestionsAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Trim().Length < MinimumSuggestionInputLength)
        {
            return Array.Empty<AirportSuggestion>();
        }

        var trimmedInput = input.Trim();
        var normalizedInput = trimmedInput.ToUpperInvariant();

        if (_suggestionCache.TryGet(normalizedInput, out var cachedSuggestions))
        {
            return cachedSuggestions ?? Array.Empty<AirportSuggestion>();
        }

        var matches = await _candidateFinder.FindAsync(trimmedInput, normalizedInput, cancellationToken).ConfigureAwait(false);

        // Unlike resolution, every match is offered regardless of score: the user picks, so a weak
        // match is a useful option rather than a wrong answer.
        var suggestions = matches
            .Take(MaximumSuggestionCount)
            .Select(match => new AirportSuggestion(
                match.StationId,
                match.Attributes.Name ?? match.StationId,
                match.Attributes.IataCode))
            .ToList();

        _suggestionCache.Set(normalizedInput, suggestions);
        return suggestions;
    }

    /// <summary>
    /// Returns the best match only if it is a strong enough match to act on without asking.
    /// A weak match is left for the suggestion list rather than silently showing the weather for
    /// an airport the user did not ask for.
    /// </summary>
    private static ResolvedAirport? GetConfidentResolution(IReadOnlyList<AirportMatch> matches)
    {
        var bestMatch = matches.FirstOrDefault();

        return bestMatch is { Score: >= AirportMatchScorer.MinimumResolutionScore }
            ? new ResolvedAirport(bestMatch.StationId, bestMatch.Attributes.Name, bestMatch.Attributes.IataCode)
            : null;
    }

    /// <summary>
    /// Falls back to treating the input as a station identifier itself.
    /// </summary>
    /// <remarks>
    /// This is what keeps the app usable when airportsapi.com is unreachable: pilots normally type
    /// the ICAO identifier, so the weather sources can be queried with it directly. The airport's
    /// name is unknown in this case, and an identifier that reports no weather simply produces
    /// "no report found" further down.
    /// </remarks>
    private static ResolvedAirport? ResolveWithoutApi(string normalizedInput) =>
        AirportCodes.CouldBeStationIdentifier(normalizedInput)
            ? new ResolvedAirport(normalizedInput, null, null)
            : null;
}
