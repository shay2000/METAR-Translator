using MetarViewer.Models;

namespace MetarViewer.Services;

/// <summary>
/// Queries a list of METAR sources in order and returns the first report found, so that a
/// station missing from the preferred source is still reported by a later one.
/// </summary>
/// <remarks>
/// This previously took an <see cref="IHttpClientFactory"/> and constructed its two sources
/// itself, which fixed both the source list and their order in code and meant the class had to
/// be edited to add, remove or reorder a source. It now receives the sources it should try.
/// </remarks>
public sealed class HybridMetarService : IMetarService
{
    private readonly IReadOnlyList<Func<IMetarService>> _sources;

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridMetarService"/> class.
    /// </summary>
    /// <remarks>
    /// Sources are supplied as factories because this service is registered as a singleton
    /// while the services it calls hold an <see cref="HttpClient"/>. Resolving them per lookup
    /// lets <see cref="IHttpClientFactory"/> retire handlers on its normal schedule.
    /// </remarks>
    /// <param name="sources">The sources to try, in order of preference.</param>
    public HybridMetarService(IEnumerable<Func<IMetarService>> sources)
    {
        ArgumentNullException.ThrowIfNull(sources);

        _sources = sources.ToList();
        if (_sources.Count == 0)
        {
            throw new ArgumentException("At least one METAR source is required.", nameof(sources));
        }
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridMetarService"/> class from
    /// already-constructed sources.
    /// </summary>
    /// <param name="sources">The sources to try, in order of preference.</param>
    public HybridMetarService(params IMetarService[] sources)
        : this((sources ?? throw new ArgumentNullException(nameof(sources)))
            .Select<IMetarService, Func<IMetarService>>(source => () => source))
    {
    }

    /// <summary>
    /// Asynchronously retrieves METAR data by trying each source in turn.
    /// </summary>
    /// <param name="stationId">The 4-character ICAO code for the station.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A MetarData object if found in any source, otherwise null.</returns>
    public async Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
    {
        foreach (var source in _sources)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var metar = await source()
                .GetMetarAsync(stationId, cancellationToken)
                .ConfigureAwait(false);

            if (metar != null)
            {
                return metar;
            }
        }

        return null;
    }
}
