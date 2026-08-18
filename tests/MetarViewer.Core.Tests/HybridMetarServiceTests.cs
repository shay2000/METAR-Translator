using MetarViewer.Models;
using MetarViewer.Services;
using Xunit;

namespace MetarViewer.Tests;

public class HybridMetarServiceTests
{
    [Fact]
    public async Task GetMetarAsync_FallsBackToNextSourceWhenFirstHasNoReport()
    {
        var missing = new StubMetarService(null);
        var found = new StubMetarService(new MetarData { StationId = "OTHH", Temperature = 30 });
        var service = new HybridMetarService(missing, found);

        var result = await service.GetMetarAsync("OTHH");

        Assert.NotNull(result);
        Assert.Equal("OTHH", result!.StationId);
        Assert.Equal(1, missing.CallCount);
        Assert.Equal(1, found.CallCount);
    }

    [Fact]
    public async Task GetMetarAsync_StopsAtFirstSourceThatHasAReport()
    {
        var preferred = new StubMetarService(new MetarData { StationId = "OMAA", Temperature = 28 });
        var fallback = new StubMetarService(new MetarData { StationId = "OMAA", Temperature = 99 });
        var service = new HybridMetarService(preferred, fallback);

        var result = await service.GetMetarAsync("OMAA");

        Assert.NotNull(result);
        Assert.Equal(28, result!.Temperature);
        Assert.Equal(0, fallback.CallCount);
    }

    [Fact]
    public async Task GetMetarAsync_ReturnsNullWhenNoSourceHasAReport()
    {
        var service = new HybridMetarService(new StubMetarService(null), new StubMetarService(null));

        Assert.Null(await service.GetMetarAsync("ZZZZ"));
    }

    [Fact]
    public async Task GetMetarAsync_TriesSourcesInTheOrderGiven()
    {
        var callOrder = new List<string>();
        var service = new HybridMetarService(
            new StubMetarService(null, () => callOrder.Add("first")),
            new StubMetarService(null, () => callOrder.Add("second")));

        await service.GetMetarAsync("EGLL");

        Assert.Equal(new[] { "first", "second" }, callOrder);
    }

    [Fact]
    public void Constructor_RequiresAtLeastOneSource()
    {
        Assert.Throws<ArgumentException>(() => new HybridMetarService(Array.Empty<Func<IMetarService>>()));
    }

    private sealed class StubMetarService : IMetarService
    {
        private readonly MetarData? _result;
        private readonly Action? _onCall;

        public StubMetarService(MetarData? result, Action? onCall = null)
        {
            _result = result;
            _onCall = onCall;
        }

        public int CallCount { get; private set; }

        public Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
        {
            CallCount++;
            _onCall?.Invoke();
            return Task.FromResult(_result);
        }
    }
}
