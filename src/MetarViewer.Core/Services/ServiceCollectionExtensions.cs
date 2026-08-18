using MetarViewer.Airports;
using Microsoft.Extensions.DependencyInjection;

namespace MetarViewer.Services;

/// <summary>
/// Registers the METAR and airport lookup services.
///
/// The application previously configured each HTTP client inline with its own copy of the
/// timeout and user agent, and constructed the services by hand inside factory lambdas. Keeping
/// the wiring here means the app only has to ask for the feature, and the test project can
/// build the same graph.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the METAR sources, the airport lookup service, and the HTTP clients they need.
    /// </summary>
    /// <param name="services">The container to add the registrations to.</param>
    /// <param name="configure">An optional callback for overriding the default settings.</param>
    public static IServiceCollection AddMetarViewerServices(
        this IServiceCollection services,
        Action<MetarViewerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new MetarViewerOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        services.AddMetarApiClient(VatsimMetarService.VatsimMetarHttpClientName, VatsimMetarService.VatsimMetarBaseUri, options);
        services.AddMetarApiClient(AviationWeatherMetarService.AviationWeatherHttpClientName, AviationWeatherMetarService.AviationWeatherBaseUri, options);
        services.AddMetarApiClient(AirportsApiClient.HttpClientName, AirportsApiClient.BaseUri, options);

        // VATSIM is tried first because it is the source flight simulation uses; the
        // real-world observation is the fallback when a station is missing there.
        services.AddSingleton<IMetarService>(provider => new CachingMetarService(
            () => new HybridMetarService(
                new Func<IMetarService>[]
                {
                    () => new VatsimMetarService(provider.CreateMetarClient(VatsimMetarService.VatsimMetarHttpClientName)),
                    () => new AviationWeatherMetarService(provider.CreateMetarClient(AviationWeatherMetarService.AviationWeatherHttpClientName))
                }),
            options.MetarCacheLifetime));

        services.AddSingleton<IAirportLookupService, AirportLookupService>();

        return services;
    }

    /// <summary>
    /// Registers a named HTTP client pointing at an API, with the shared timeout and user agent.
    /// </summary>
    private static void AddMetarApiClient(
        this IServiceCollection services,
        string name,
        Uri baseAddress,
        MetarViewerOptions options)
    {
        services.AddHttpClient(name, client =>
        {
            client.BaseAddress = baseAddress;
            client.Timeout = options.RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd(options.UserAgent);
        });
    }

    private static HttpClient CreateMetarClient(this IServiceProvider provider, string name) =>
        provider.GetRequiredService<IHttpClientFactory>().CreateClient(name);
}
