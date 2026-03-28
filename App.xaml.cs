using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using MetarViewer.Services;
using MetarViewer.ViewModels;
using MetarViewer.Views;
using System;
using System.IO;

namespace MetarViewer;

public partial class App : Application
{
    private Window? _window;
    private readonly ServiceProvider _serviceProvider;

    public App()
    {
        InitializeComponent();

        // Set up dependency injection
        var services = new ServiceCollection();
        ConfigureServices(services);
        _serviceProvider = services.BuildServiceProvider();
    }

    private void ConfigureServices(ServiceCollection services)
    {
        // HttpClient for METAR service
        services.AddHttpClient<IMetarService, AvwxMetarService>();
        services.AddHttpClient(AirportLookupService.AirportsApiHttpClientName, client =>
        {
            client.BaseAddress = AirportLookupService.AirportsApiBaseUri;
            client.Timeout = TimeSpan.FromSeconds(10);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("MetarViewer/1.0");
        });

        // Airport lookup service
        var dataPath = Path.Combine(AppContext.BaseDirectory, "Data", "airports.json");
        services.AddSingleton<IAirportLookupService>(sp =>
            new AirportLookupService(dataPath, sp.GetRequiredService<IHttpClientFactory>()));

        // ViewModels
        services.AddTransient<MainViewModel>();

        // Views
        services.AddTransient<MainWindow>();
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = _serviceProvider.GetRequiredService<MainWindow>();
        _window.Activate();
    }
}
