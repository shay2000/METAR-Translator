using MetarViewer.Models;
using MetarViewer.Services;

namespace MetarViewer.App.Tests;

internal sealed class StubMetarService : IMetarService
{
    public Func<string, CancellationToken, Task<MetarData?>> Handler { get; set; } =
        static (_, _) => Task.FromResult<MetarData?>(null);

    public List<string> RequestedStationIds { get; } = [];

    public Task<MetarData?> GetMetarAsync(
        string stationId,
        CancellationToken cancellationToken = default)
    {
        RequestedStationIds.Add(stationId);
        return Handler(stationId, cancellationToken);
    }
}

internal sealed class StubAirportLookupService : IAirportLookupService
{
    public Func<string, CancellationToken, Task<string?>> ResolveHandler { get; set; } =
        static (_, _) => Task.FromResult<string?>(null);

    public Func<string, CancellationToken, Task<ResolvedAirport?>> ResolveDetailsHandler { get; set; } =
        static (_, _) => Task.FromResult<ResolvedAirport?>(null);

    public Func<string, CancellationToken, Task<IReadOnlyList<AirportSuggestion>>> SuggestionsHandler { get; set; } =
        static (_, _) => Task.FromResult<IReadOnlyList<AirportSuggestion>>([]);

    public List<string> ResolveRequests { get; } = [];

    public List<string> ResolveDetailsRequests { get; } = [];

    public List<string> SuggestionRequests { get; } = [];

    public Task<string?> ResolveAirportAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        ResolveRequests.Add(input);
        return ResolveHandler(input, cancellationToken);
    }

    public Task<ResolvedAirport?> ResolveAirportDetailsAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        ResolveDetailsRequests.Add(input);
        return ResolveDetailsHandler(input, cancellationToken);
    }

    public Task<IReadOnlyList<AirportSuggestion>> GetSuggestionsAsync(
        string input,
        CancellationToken cancellationToken = default)
    {
        SuggestionRequests.Add(input);
        return SuggestionsHandler(input, cancellationToken);
    }
}
