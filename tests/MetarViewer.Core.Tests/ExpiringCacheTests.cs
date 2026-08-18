using MetarViewer.Airports;
using Xunit;

namespace MetarViewer.Tests;

/// <summary>
/// Tests the generic cache that replaced the two hand-written ones in the airport lookup service.
///
/// The originals expired against DateTimeOffset.UtcNow, so their expiry could not be tested at all
/// without making a test wait. Taking a clock makes the one behaviour that actually matters -
/// entries stop being used once they are stale - assertable.
/// </summary>
public class ExpiringCacheTests
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    [Fact]
    public void TryGet_ReturnsFalseForAKeyThatWasNeverStored()
    {
        var cache = CreateCache(out _);

        Assert.False(cache.TryGet("EGLL", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TryGet_ReturnsAStoredValue()
    {
        var cache = CreateCache(out _);
        cache.Set("EGLL", "London Heathrow");

        Assert.True(cache.TryGet("EGLL", out var value));
        Assert.Equal("London Heathrow", value);
    }

    [Fact]
    public void TryGet_IgnoresCaseSoThatRetypingTheSameSearchReusesTheEntry()
    {
        var cache = CreateCache(out _);
        cache.Set("Heathrow", "London Heathrow");

        Assert.True(cache.TryGet("heathrow", out var value));
        Assert.Equal("London Heathrow", value);
    }

    [Fact]
    public void TryGet_RemembersThatThereWasNoAnswer()
    {
        // Caching the absence of a result is deliberate: it stops an input the API cannot resolve
        // from being searched for again on every keystroke.
        var cache = CreateCache(out _);
        cache.Set("Nowhere", null);

        Assert.True(cache.TryGet("Nowhere", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void TryGet_StillReturnsTheValueJustBeforeItExpires()
    {
        var cache = CreateCache(out var timeProvider);
        cache.Set("EGLL", "London Heathrow");

        timeProvider.Advance(Lifetime - TimeSpan.FromSeconds(1));

        Assert.True(cache.TryGet("EGLL", out _));
    }

    [Fact]
    public void TryGet_DiscardsTheValueOnceItHasExpired()
    {
        var cache = CreateCache(out var timeProvider);
        cache.Set("EGLL", "London Heathrow");

        timeProvider.Advance(Lifetime + TimeSpan.FromSeconds(1));

        Assert.False(cache.TryGet("EGLL", out var value));
        Assert.Null(value);
    }

    [Fact]
    public void Set_RestartsTheLifetimeOfAnEntry()
    {
        var cache = CreateCache(out var timeProvider);
        cache.Set("EGLL", "London Heathrow");

        timeProvider.Advance(Lifetime - TimeSpan.FromSeconds(1));
        cache.Set("EGLL", "London Heathrow Airport");
        timeProvider.Advance(Lifetime - TimeSpan.FromSeconds(1));

        Assert.True(cache.TryGet("EGLL", out var value));
        Assert.Equal("London Heathrow Airport", value);
    }

    [Fact]
    public void Set_KeepsEntriesForDifferentKeysApart()
    {
        var cache = CreateCache(out _);
        cache.Set("EGLL", "London Heathrow");
        cache.Set("EGKK", "London Gatwick");

        Assert.True(cache.TryGet("EGLL", out var heathrow));
        Assert.True(cache.TryGet("EGKK", out var gatwick));
        Assert.Equal("London Heathrow", heathrow);
        Assert.Equal("London Gatwick", gatwick);
    }

    private static ExpiringCache<string> CreateCache(out FakeTimeProvider timeProvider)
    {
        timeProvider = new FakeTimeProvider();
        return new ExpiringCache<string>(Lifetime, timeProvider);
    }

    /// <summary>
    /// A clock the test moves by hand, so expiry can be checked without waiting.
    /// </summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = new(2024, 1, 1, 12, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow = _utcNow.Add(duration);
    }
}
