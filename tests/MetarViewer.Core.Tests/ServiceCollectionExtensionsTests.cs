using MetarViewer.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace MetarViewer.Tests;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void AddMetarViewerServices_ResolvesTheMetarAndAirportServices()
    {
        using var provider = new ServiceCollection().AddMetarViewerServices().BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IMetarService>());
        Assert.NotNull(provider.GetRequiredService<IAirportLookupService>());
    }

    [Fact]
    public void AddMetarViewerServices_AppliesTheSharedSettingsToEveryClient()
    {
        var timeout = TimeSpan.FromSeconds(42);
        using var provider = new ServiceCollection()
            .AddMetarViewerServices(options =>
            {
                options.RequestTimeout = timeout;
                options.UserAgent = "TestAgent/9.9";
            })
            .BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpClientFactory>();

        // Every client is configured from one place, so all three agree.
        foreach (var name in new[] { "VatsimMetar", "AviationWeather", "AirportsApi" })
        {
            var client = factory.CreateClient(name);

            Assert.NotNull(client.BaseAddress);
            Assert.Equal(timeout, client.Timeout);
            Assert.Equal("TestAgent/9.9", client.DefaultRequestHeaders.UserAgent.ToString());
        }
    }

    [Fact]
    public void AddMetarViewerServices_UsesDefaultSettingsWhenNotConfigured()
    {
        using var provider = new ServiceCollection().AddMetarViewerServices().BuildServiceProvider();

        var options = provider.GetRequiredService<MetarViewerOptions>();

        Assert.Equal(TimeSpan.FromSeconds(10), options.RequestTimeout);
        Assert.Equal("MetarViewer/1.0", options.UserAgent);
        Assert.Equal(CachingMetarService.DefaultLifetime, options.MetarCacheLifetime);
    }

    [Fact]
    public void AddMetarViewerServices_MetarServiceIsASingletonSoItsCacheIsShared()
    {
        using var provider = new ServiceCollection().AddMetarViewerServices().BuildServiceProvider();

        Assert.Same(provider.GetRequiredService<IMetarService>(), provider.GetRequiredService<IMetarService>());
    }
}
