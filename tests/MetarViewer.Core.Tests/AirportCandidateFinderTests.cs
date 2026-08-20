using MetarViewer.Airports;
using Xunit;

namespace MetarViewer.Tests;

/// <summary>
/// Tests the airport search strategy: which searches are issued, in what order, and when the
/// search stops.
///
/// Because the transport now sits behind an interface, these tests state the strategy directly
/// rather than through hand-written JSON and expected URLs. That matters because the point of the
/// strategy is to avoid needless requests, which is a claim about the calls made rather than about
/// the value returned.
/// </summary>
public class AirportCandidateFinderTests
{
    [Fact]
    public async Task FindAsync_StopsAfterAnExactCodeLookupSucceeds()
    {
        // An exact code identifies an airport outright, so no further searching is justified.
        var apiClient = new StubAirportsApiClient
        {
            AirportsByCode = { ["EGLL"] = CreateAirport("London Heathrow Airport", "EGLL", "large_airport") }
        };
        var finder = new AirportCandidateFinder(apiClient);

        var matches = await finder.FindAsync("EGLL", "EGLL");

        Assert.Equal("EGLL", Assert.Single(matches).StationId);
        Assert.Empty(apiClient.ExecutedQueries);
    }

    [Fact]
    public async Task FindAsync_FallsBackToACodeSearchWhenTheDirectLookupFindsNothing()
    {
        // The API indexes several kinds of code, so an airport not published under the code may
        // still be searchable by it.
        var apiClient = new StubAirportsApiClient
        {
            SearchResults = { ["filter[code]:LHR"] = [CreateAirport("London Heathrow Airport", "EGLL", "large_airport", "LHR")] }
        };
        var finder = new AirportCandidateFinder(apiClient);

        var matches = await finder.FindAsync("LHR", "LHR");

        Assert.Equal("EGLL", Assert.Single(matches).StationId);
        Assert.Equal("filter[code]:LHR", apiClient.ExecutedQueries[0]);
    }

    [Fact]
    public async Task FindAsync_SearchesByNameForANameSearch()
    {
        var apiClient = new StubAirportsApiClient
        {
            SearchResults = { ["filter[name]:Heathrow"] = [CreateAirport("London Heathrow Airport", "EGLL", "large_airport", "LHR")] }
        };
        var finder = new AirportCandidateFinder(apiClient);

        var matches = await finder.FindAsync("Heathrow", "HEATHROW");

        Assert.Equal("EGLL", Assert.Single(matches).StationId);
        Assert.Equal(["filter[name]:Heathrow"], apiClient.ExecutedQueries);
        Assert.Empty(apiClient.RequestedCodes);
    }

    [Fact]
    public async Task FindAsync_WidensTheSearchWhenAnExactNameFindsNothing()
    {
        // This is what rescues a misspelling: the exact name matches nothing, so progressively
        // shorter fragments are tried.
        var apiClient = new StubAirportsApiClient
        {
            SearchResults = { ["filter[name]:Heat"] = [CreateAirport("London Heathrow Airport", "EGLL", "large_airport", "LHR")] }
        };
        var finder = new AirportCandidateFinder(apiClient);

        var matches = await finder.FindAsync("Heatrow", "HEATROW");

        Assert.Equal("EGLL", Assert.Single(matches).StationId);
        Assert.Equal("filter[name]:Heatrow", apiClient.ExecutedQueries[0]);
        Assert.Contains("filter[name]:Heat", apiClient.ExecutedQueries);
    }

    [Fact]
    public async Task FindAsync_OrdersMatchesByHowWellTheyMatch()
    {
        var apiClient = new StubAirportsApiClient
        {
            SearchResults =
            {
                ["filter[name]:Heathrow"] =
                [
                    CreateAirport("Heathrow Downtown Heliport", "ZXHX", "heliport"),
                    CreateAirport("London Heathrow Airport", "EGLL", "large_airport", "LHR")
                ]
            }
        };
        var finder = new AirportCandidateFinder(apiClient);

        var matches = await finder.FindAsync("Heathrow", "HEATHROW");

        Assert.Equal("EGLL", matches[0].StationId);
        Assert.Equal("ZXHX", matches[1].StationId);
    }

    [Fact]
    public async Task FindAsync_KeepsAnAirportOnceWhenSeveralSearchesReturnIt()
    {
        var heathrow = CreateAirport("London Heathrow Airport", "EGLL", "large_airport", "LHR");
        var apiClient = new StubAirportsApiClient
        {
            SearchResults =
            {
                ["filter[name]:Heatrow"] = [heathrow],
                ["filter[name]:Heatr"] = [heathrow],
                ["filter[name]:Heat"] = [heathrow]
            }
        };
        var finder = new AirportCandidateFinder(apiClient);

        var matches = await finder.FindAsync("Heatrow", "HEATROW");

        Assert.Equal("EGLL", Assert.Single(matches).StationId);
    }

    [Fact]
    public async Task FindAsync_DiscardsAirportsWithNoStationIdentifier()
    {
        // Without a station identifier there is no way to request the airport's weather, which is
        // the only reason to offer it.
        var apiClient = new StubAirportsApiClient
        {
            SearchResults = { ["filter[name]:Heliport"] = [new AirportAttributes { Name = "Rooftop Heliport", IataCode = "XXX", Type = "heliport" }] }
        };
        var finder = new AirportCandidateFinder(apiClient);

        var matches = await finder.FindAsync("Heliport", "HELIPORT");

        Assert.Empty(matches);
    }

    [Fact]
    public async Task FindAsync_DiscardsClosedAirports()
    {
        var apiClient = new StubAirportsApiClient
        {
            SearchResults = { ["filter[name]:Disused"] = [CreateAirport("Disused Field", "EGXX", "closed")] }
        };
        var finder = new AirportCandidateFinder(apiClient);

        var matches = await finder.FindAsync("Disused", "DISUSED");

        Assert.Empty(matches);
    }

    [Fact]
    public async Task FindAsync_ReportsNoMatchesWhenTheApiIsUnreachable()
    {
        // A failed search is reported as "no matches" so the caller can fall back, rather than
        // surfacing an error for a lookup the user did not explicitly ask for.
        var apiClient = new StubAirportsApiClient { Failure = new HttpRequestException("boom") };
        var finder = new AirportCandidateFinder(apiClient);

        Assert.Empty(await finder.FindAsync("Heathrow", "HEATHROW"));
    }

    [Fact]
    public async Task FindAsync_ReportsNoMatchesWhenTheRequestTimesOut()
    {
        var apiClient = new StubAirportsApiClient { Failure = new TaskCanceledException("timeout") };
        var finder = new AirportCandidateFinder(apiClient);

        Assert.Empty(await finder.FindAsync("Heathrow", "HEATHROW"));
    }

    [Fact]
    public async Task FindAsync_PropagatesCancellationRequestedByTheCaller()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var cancellation = new TaskCanceledException(
            "cancelled by caller",
            innerException: null,
            cancellationSource.Token);
        var apiClient = new StubAirportsApiClient { Failure = cancellation };
        var finder = new AirportCandidateFinder(apiClient);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            finder.FindAsync("Heathrow", "HEATHROW", cancellationSource.Token));
    }

    private static AirportAttributes CreateAirport(
        string name,
        string stationId,
        string airportType,
        string? iataCode = null) => new()
        {
            Name = name,
            Code = stationId,
            IcaoCode = stationId,
            Type = airportType,
            IataCode = iataCode
        };

    /// <summary>
    /// Returns pre-arranged airports and records what was asked for, so that tests can assert on
    /// the requests the strategy chose to make as well as on the matches it produced.
    /// </summary>
    private sealed class StubAirportsApiClient : IAirportsApiClient
    {
        public Dictionary<string, AirportAttributes> AirportsByCode { get; } = new(StringComparer.Ordinal);

        public Dictionary<string, IReadOnlyList<AirportAttributes>> SearchResults { get; } = new(StringComparer.Ordinal);

        public List<string> RequestedCodes { get; } = [];

        public List<string> ExecutedQueries { get; } = [];

        /// <summary>When set, every call throws this, standing in for an unreachable API.</summary>
        public Exception? Failure { get; init; }

        public Task<AirportAttributes?> GetAirportByCodeAsync(string code, CancellationToken cancellationToken)
        {
            if (Failure != null)
            {
                throw Failure;
            }

            RequestedCodes.Add(code);
            return Task.FromResult(AirportsByCode.GetValueOrDefault(code));
        }

        public Task<IReadOnlyList<AirportAttributes>> SearchAsync(AirportSearchQuery query, CancellationToken cancellationToken)
        {
            if (Failure != null)
            {
                throw Failure;
            }

            ExecutedQueries.Add(query.DeduplicationKey);
            return Task.FromResult(
                SearchResults.TryGetValue(query.DeduplicationKey, out var airports)
                    ? airports
                    : Array.Empty<AirportAttributes>());
        }
    }
}
