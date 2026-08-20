using System.Collections.Concurrent;
using MetarViewer.Models;

namespace MetarViewer.Services;

/// <summary>
/// Caches METAR lookups for a short period so that repeated requests for the same station do
/// not re-query the upstream API.
///
/// Every METAR service previously carried its own private cache dictionary, expiry constant
/// and nested cache-entry class. Holding the cache in one decorator means the policy is
/// defined once and applies no matter which source ultimately answers the request.
/// </summary>
public sealed class CachingMetarService : IMetarService
{
    /// <summary>How long a report stays usable. METARs are normally issued twice an hour.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromSeconds(60);

    private readonly Func<IMetarService> _innerFactory;
    private readonly TimeSpan _lifetime;
    private readonly TimeProvider _timeProvider;

    // Guards _entries; lookups can be issued concurrently from the UI.
    private readonly Dictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    // A burst of requests for one station shares the same upstream lookup. Each caller can stop
    // waiting independently without canceling the request another caller still needs.
    private readonly ConcurrentDictionary<string, Lazy<Task<MetarData?>>> _inFlightRequests =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingMetarService"/> class.
    /// </summary>
    /// <param name="inner">The service that performs the actual lookup on a cache miss.</param>
    /// <param name="lifetime">How long a cached report remains valid. Defaults to one minute.</param>
    /// <param name="timeProvider">The clock used for expiry. Defaults to the system clock.</param>
    public CachingMetarService(IMetarService inner, TimeSpan? lifetime = null, TimeProvider? timeProvider = null)
        : this(() => inner, lifetime, timeProvider)
    {
        ArgumentNullException.ThrowIfNull(inner);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CachingMetarService"/> class that obtains
    /// its inner service on demand.
    /// </summary>
    /// <remarks>
    /// The cache lives for as long as the application does, but the services it wraps hold an
    /// <see cref="HttpClient"/>. Resolving the inner service per lookup rather than holding one
    /// forever lets <see cref="IHttpClientFactory"/> retire handlers on its normal schedule, so
    /// DNS changes are still picked up.
    /// </remarks>
    /// <param name="innerFactory">Supplies the service used on a cache miss.</param>
    /// <param name="lifetime">How long a cached report remains valid. Defaults to one minute.</param>
    /// <param name="timeProvider">The clock used for expiry. Defaults to the system clock.</param>
    public CachingMetarService(Func<IMetarService> innerFactory, TimeSpan? lifetime = null, TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(innerFactory);

        _innerFactory = innerFactory;
        _lifetime = lifetime ?? DefaultLifetime;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
    {
        var normalizedStationId = StationId.Normalize(stationId);
        if (normalizedStationId.Length == 0)
        {
            return null;
        }

        if (TryGetCached(normalizedStationId, out var cached))
        {
            return cached;
        }

        var candidate = new Lazy<Task<MetarData?>>(
            () => FetchAndStoreAsync(normalizedStationId),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var request = _inFlightRequests.GetOrAdd(normalizedStationId, candidate);
        var requestTask = request.Value;

        if (ReferenceEquals(request, candidate))
        {
            _ = requestTask.ContinueWith(
                completedTask =>
                {
                    // Observe a fault even if every caller stopped waiting before it occurred.
                    _ = completedTask.Exception;
                    RemoveInFlightRequest(normalizedStationId, request);
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        return await requestTask.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<MetarData?> FetchAndStoreAsync(string stationId)
    {
        var metar = await _innerFactory()
            .GetMetarAsync(stationId, CancellationToken.None)
            .ConfigureAwait(false);

        // Only successful lookups are cached, so a transient failure does not mask a station
        // that starts reporting again moments later.
        if (metar != null)
        {
            Store(stationId, metar);
        }

        return metar;
    }

    private void RemoveInFlightRequest(string stationId, Lazy<Task<MetarData?>> request)
    {
        // Conditional removal avoids deleting a newer request for the same station.
        ((ICollection<KeyValuePair<string, Lazy<Task<MetarData?>>>>)_inFlightRequests)
            .Remove(new KeyValuePair<string, Lazy<Task<MetarData?>>>(stationId, request));
    }

    private bool TryGetCached(string stationId, out MetarData? metar)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(stationId, out var entry))
            {
                metar = null;
                return false;
            }

            if (_timeProvider.GetUtcNow() - entry.StoredAt >= _lifetime)
            {
                _entries.Remove(stationId);
                metar = null;
                return false;
            }

            metar = entry.Data;
            return true;
        }
    }

    private void Store(string stationId, MetarData metar)
    {
        lock (_gate)
        {
            _entries[stationId] = new CacheEntry(metar, _timeProvider.GetUtcNow());
        }
    }

    private sealed record CacheEntry(MetarData Data, DateTimeOffset StoredAt);
}
