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
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var services = new ServiceCollection();
            services.AddMetarViewerServices();
            services.AddTransient<MainViewModel>();
            services.AddTransient<MainWindow>();
            desktop.MainWindow = services.BuildServiceProvider().GetRequiredService<MainWindow>();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
