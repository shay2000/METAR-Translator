using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using MetarViewer.Services;
using MetarViewer.ViewModels;

namespace MetarViewer.Views;

public partial class MainWindow : Window
{
    private static readonly TimeSpan SuggestionDebounceDelay = TimeSpan.FromMilliseconds(250);

    private CancellationTokenSource? _suggestionCancellationTokenSource;
    private MainViewModel? _viewModel;
    private bool _suppressSuggestionLookup;

    public MainViewModel ViewModel =>
        _viewModel ?? throw new InvalidOperationException("The view model has not been attached.");

    public MainWindow()
    {
        InitializeComponent();
        Opened += MainWindow_Opened;
        Closed += MainWindow_Closed;
    }

    internal void AttachViewModel(MainViewModel viewModel)
    {
        ArgumentNullException.ThrowIfNull(viewModel);
        if (_viewModel != null)
        {
            throw new InvalidOperationException("A view model is already attached.");
        }

        _viewModel = viewModel;
        DataContext = viewModel;
        viewModel.IsDarkTheme = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
        viewModel.PropertyChanged += ViewModel_PropertyChanged;
    }

    private async void SearchBox_TextChanged(object? sender, TextChangedEventArgs args)
    {
        if (_viewModel == null || _suppressSuggestionLookup)
        {
            return;
        }

        CancelSuggestionLookup();
        var cancellation = new CancellationTokenSource();
        _suggestionCancellationTokenSource = cancellation;

        try
        {
            await Task.Delay(SuggestionDebounceDelay, cancellation.Token);
            await ViewModel.UpdateAirportSuggestionsAsync(ViewModel.SearchText, cancellation.Token);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            // A newer keystroke or a selection superseded this lookup.
        }
    }

    private async void SearchBox_KeyDown(object? sender, KeyEventArgs args)
    {
        if (args.Key != Key.Enter || _viewModel == null) return;

        args.Handled = true;
        CancelSuggestionLookup();
        ViewModel.ClearAirportSuggestions();
        await ViewModel.FetchMetarCommand.ExecuteAsync(null);
    }

    private void Suggestions_SelectionChanged(object? sender, SelectionChangedEventArgs args)
    {
        if (_viewModel != null && sender is ListBox { SelectedItem: AirportSuggestion suggestion } list)
        {
            CancelSuggestionLookup();
            _suppressSuggestionLookup = true;
            try
            {
                ViewModel.SelectAirportSuggestion(suggestion);
            }
            finally
            {
                _suppressSuggestionLookup = false;
            }

            list.SelectedItem = null;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(MainViewModel.IsDarkTheme) && Application.Current is { } app)
        {
            app.RequestedThemeVariant = ViewModel.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }
    }

    private async void MainWindow_Opened(object? sender, EventArgs args)
    {
        if (_viewModel != null)
        {
            Task loadTask;
            _suppressSuggestionLookup = true;
            try
            {
                // LoadLastStationAsync sets SearchText before its first await. Suppress only that
                // programmatic text change; user input should not be blocked by the network call.
                loadTask = _viewModel.LoadLastStationAsync();
            }
            finally
            {
                _suppressSuggestionLookup = false;
            }

            await loadTask;
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs args)
    {
        CancelSuggestionLookup();
        if (_viewModel != null)
        {
            _viewModel.FetchMetarCommand.Cancel();
            _viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }

        Opened -= MainWindow_Opened;
        Closed -= MainWindow_Closed;
    }

    private void CancelSuggestionLookup()
    {
        var cancellation = Interlocked.Exchange(ref _suggestionCancellationTokenSource, null);
        if (cancellation == null)
        {
            return;
        }

        cancellation.Cancel();
        cancellation.Dispose();
    }
}
