using MetarViewer.Models;
using MetarViewer.Services;
using Xunit;

namespace MetarViewer.Tests;

public class CachingMetarServiceTests
{
    [Fact]
    public async Task GetMetarAsync_SecondLookupIsServedFromCache()
    {
        var inner = new CountingMetarService(_ => new MetarData { StationId = "EGLL" });
        var service = new CachingMetarService(inner);

        await service.GetMetarAsync("EGLL");
        await service.GetMetarAsync("EGLL");

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetMetarAsync_CacheKeyIgnoresCasingAndWhitespace()
    {
        var inner = new CountingMetarService(_ => new MetarData { StationId = "EGLL" });
        var service = new CachingMetarService(inner);

        await service.GetMetarAsync("egll");
        await service.GetMetarAsync("  EGLL  ");

        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetMetarAsync_QueriesAgainOnceTheEntryHasExpired()
    {
        var timeProvider = new FakeTimeProvider(new DateTimeOffset(2026, 3, 28, 16, 0, 0, TimeSpan.Zero));
        var inner = new CountingMetarService(_ => new MetarData { StationId = "EGLL" });
        var lifetime = TimeSpan.FromSeconds(60);
        var service = new CachingMetarService(inner, lifetime, timeProvider);

        await service.GetMetarAsync("EGLL");

        // Still inside the window, so the cached report is reused.
        timeProvider.Advance(TimeSpan.FromSeconds(59));
        await service.GetMetarAsync("EGLL");
        Assert.Equal(1, inner.CallCount);

        // Once the lifetime has elapsed the station is queried again.
        timeProvider.Advance(TimeSpan.FromSeconds(2));
        await service.GetMetarAsync("EGLL");
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task GetMetarAsync_DifferentStationsAreCachedSeparately()
    {
        var inner = new CountingMetarService(stationId => new MetarData { StationId = stationId });
        var service = new CachingMetarService(inner);

        var first = await service.GetMetarAsync("EGLL");
        var second = await service.GetMetarAsync("KJFK");

        Assert.Equal("EGLL", first!.StationId);
        Assert.Equal("KJFK", second!.StationId);
        Assert.Equal(2, inner.CallCount);
    }

    [Fact]
    public async Task GetMetarAsync_FailedLookupIsNotCached()
    {
        var inner = new CountingMetarService(_ => null);
        var service = new CachingMetarService(inner);

        await service.GetMetarAsync("EGLL");
        await service.GetMetarAsync("EGLL");

        // A miss must not be remembered, otherwise a station that starts reporting again
        // would appear unavailable until the entry expired.
        Assert.Equal(2, inner.CallCount);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetMetarAsync_BlankStationIdIsRejectedWithoutQuerying(string stationId)
    {
        var inner = new CountingMetarService(_ => new MetarData());
        var service = new CachingMetarService(inner);

        Assert.Null(await service.GetMetarAsync(stationId));
        Assert.Equal(0, inner.CallCount);
    }

    [Fact]
    public async Task GetMetarAsync_ResolvesInnerServicePerLookup()
    {
        var created = 0;
        var service = new CachingMetarService(() =>
        {
            created++;
            return new CountingMetarService(_ => null);
        });

        await service.GetMetarAsync("EGLL");
        await service.GetMetarAsync("KJFK");

        // Resolving per lookup is what lets IHttpClientFactory retire handlers.
        Assert.Equal(2, created);
    }

    [Fact]
    public async Task GetMetarAsync_ConcurrentLookupsShareOneUpstreamRequest()
    {
        var inner = new DeferredMetarService();
        var service = new CachingMetarService(inner);

        var firstLookup = service.GetMetarAsync("EGLL");
        var secondLookup = service.GetMetarAsync(" egll ");

        Assert.Equal(1, inner.CallCount);

        inner.Complete(new MetarData { StationId = "EGLL" });

        var results = await Task.WhenAll(firstLookup, secondLookup);
        Assert.All(results, result => Assert.Equal("EGLL", result!.StationId));
        Assert.Equal(1, inner.CallCount);
    }

    [Fact]
    public async Task GetMetarAsync_CancelingOneWaiterDoesNotCancelTheSharedRequest()
    {
        var inner = new DeferredMetarService();
        var service = new CachingMetarService(inner);
        using var cancellation = new CancellationTokenSource();

        var canceledLookup = service.GetMetarAsync("EGLL", cancellation.Token);
        var survivingLookup = service.GetMetarAsync("EGLL");

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledLookup);

        inner.Complete(new MetarData { StationId = "EGLL" });

        Assert.Equal("EGLL", (await survivingLookup)!.StationId);
        Assert.Equal(1, inner.CallCount);
    }

    private sealed class CountingMetarService : IMetarService
    {
        private readonly Func<string, MetarData?> _resultFactory;

        public CountingMetarService(Func<string, MetarData?> resultFactory)
        {
            _resultFactory = resultFactory;
        }

        public int CallCount { get; private set; }

        public Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            return Task.FromResult(_resultFactory(stationId));
        }
    }

    private sealed class DeferredMetarService : IMetarService
    {
        private readonly TaskCompletionSource<MetarData?> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _callCount;

        public int CallCount => _callCount;

        public Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            return _completion.Task;
        }

        public void Complete(MetarData? result) => _completion.SetResult(result);
    }

    /// <summary>
    /// A clock the test controls, so cache expiry can be verified without waiting.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public FakeTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan delta) => _utcNow += delta;
    }
}
