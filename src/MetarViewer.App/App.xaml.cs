using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using MetarViewer.Services;
using MetarViewer.ViewModels;
using MetarViewer.Views;
using System;

namespace MetarViewer;

/// <summary>
/// The main application class, responsible for dependency injection setup and window activation.
/// </summary>
public partial class App : Application
{
    private Window? _window;
    private readonly ServiceProvider _serviceProvider;

    /// <summary>
    /// Initializes the singleton application object.
    /// Sets up the Dependency Injection container.
    /// </summary>
    public App()
    {
        InitializeComponent();

        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Registers services, view models, and views in the DI container.
    /// </summary>
    private void ConfigureServices(ServiceCollection services)
    {
        // Aviation Weather API Client
        services.AddHttpClient(AviationWeatherMetarService.AviationWeatherHttpClientName, client =>
        {
            client.BaseAddress = AviationWeatherMetarService.AviationWeatherBaseUri;
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MetarViewer/1.0");
        });

        // VATSIM METAR API Client
        services.AddHttpClient(VatsimMetarService.VatsimMetarHttpClientName, client =>
        {
            client.BaseAddress = VatsimMetarService.VatsimMetarBaseUri;
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MetarViewer/1.0");
        });

        // Airports Lookup API Client
        services.AddHttpClient(AirportLookupService.AirportsApiHttpClientName, client =>
        {
            client.BaseAddress = AirportLookupService.AirportsApiBaseUri;
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MetarViewer/1.0");
        });

        // Register core services using the Hybrid implementation
        services.AddSingleton<IMetarService>(sp =>
            new HybridMetarService(sp.GetRequiredService<IHttpClientFactory>()));

        services.AddSingleton<IAirportLookupService>(sp =>
            new AirportLookupService(sp.GetRequiredService<IHttpClientFactory>()));

        // ViewModels
        services.AddTransient<MainViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Resolve and show the main window
        _window = _serviceProvider.GetRequiredService<MainWindow>();
        _window.Activate();
    }
}
