using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Microsoft.Extensions.DependencyInjection;
using MetarViewer.Services;
using MetarViewer.ViewModels;

namespace MetarViewer.Views;

public partial class MainWindow : Window
{
    private CancellationTokenSource? _suggestionCancellationTokenSource;
    public MainViewModel ViewModel { get; }

    // Avalonia's runtime XAML loader requires a public parameterless constructor.
    public MainWindow() : this(CreateViewModel())
    {
    }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        ViewModel.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(MainViewModel.IsDarkTheme) && Application.Current is { } app)
                app.RequestedThemeVariant = ViewModel.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        };
        Opened += (_, _) => _ = ViewModel.LoadLastStationAsync();
    }

    private static MainViewModel CreateViewModel()
    {
        var services = new ServiceCollection();
        services.AddMetarViewerServices();
        services.AddTransient<MainViewModel>();
        return services.BuildServiceProvider().GetRequiredService<MainViewModel>();
    }

    private async void SearchBox_TextChanged(object? sender, TextChangedEventArgs args)
    {
        _suggestionCancellationTokenSource?.Cancel();
        _suggestionCancellationTokenSource?.Dispose();
        _suggestionCancellationTokenSource = new CancellationTokenSource();
        await ViewModel.UpdateAirportSuggestionsAsync(ViewModel.SearchText, _suggestionCancellationTokenSource.Token);
    }

    private async void SearchBox_KeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter) return;
        ViewModel.ClearAirportSuggestions();
        await ViewModel.FetchMetarCommand.ExecuteAsync(null);
    }

    private void Suggestions_SelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (sender is ListBox { SelectedItem: AirportSuggestion suggestion } list)
        {
            ViewModel.SelectAirportSuggestion(suggestion);
            list.SelectedItem = null;
        }
    }
}
