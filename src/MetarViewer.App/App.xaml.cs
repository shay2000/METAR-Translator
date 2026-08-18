using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using MetarViewer.Services;
using MetarViewer.ViewModels;
using MetarViewer.Views;

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
        // METAR sources, airport lookup, and the HTTP clients they need.
        services.AddMetarViewerServices();

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
