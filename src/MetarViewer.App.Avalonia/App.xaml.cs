using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using MetarViewer.Services;
using MetarViewer.ViewModels;
using MetarViewer.Views;

namespace MetarViewer;

public partial class App : Application
{
    private ServiceProvider? _serviceProvider;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddMetarViewerServices();
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();

            _serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = true,
                ValidateScopes = true
            });

            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.AttachViewModel(_serviceProvider.GetRequiredService<MainViewModel>());
            desktop.MainWindow = mainWindow;
            desktop.Exit += (_, _) => DisposeServices();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void DisposeServices()
    {
        _serviceProvider?.Dispose();
        _serviceProvider = null;
    }
}
