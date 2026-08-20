using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MetarViewer.Services;
using MetarViewer.ViewModels;

namespace MetarViewer.Views;

/// <summary>
/// The main application window, responsible for UI interaction and event handling.
/// </summary>
public sealed partial class MainWindow : Window
{
    private CancellationTokenSource? _suggestionCancellationTokenSource;

    /// <summary>
    /// Gets the view model instance for this window.
    /// </summary>
    public MainViewModel ViewModel { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="MainWindow"/> class.
    /// </summary>
    /// <param name="viewModel">The view model to use for data binding.</param>
    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // Apply Mica effect for a modern Windows 11 appearance
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        // Set application icon from assets
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // Set initial window size
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 800));

        // Asynchronously load the last station on startup
        _ = ViewModel.LoadLastStationAsync();
    }

    /// <summary>
    /// Handles text changes in the search box to provide auto-suggestions.
    /// </summary>
    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchText = sender.Text;

        // Only search if the change was due to user input
        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.IsSuggestionListOpen = false;
            return;
        }

        // Cancel previous pending search
        _suggestionCancellationTokenSource?.Cancel();
        _suggestionCancellationTokenSource?.Dispose();
        _suggestionCancellationTokenSource = new CancellationTokenSource();

        try
        {
            // Update suggestions with a debounce-like cancellation
            await ViewModel.UpdateAirportSuggestionsAsync(sender.Text, _suggestionCancellationTokenSource.Token);
            sender.IsSuggestionListOpen = ViewModel.AirportSuggestions.Count > 0;
        }
        catch (OperationCanceledException)
        {
            // Suppression is expected for typing fast
        }
    }

    /// <summary>
    /// Handles the selection of a suggestion from the list.
    /// </summary>
    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is AirportSuggestion suggestion)
        {
            ViewModel.SelectAirportSuggestion(suggestion);
            sender.IsSuggestionListOpen = false;
        }
    }

    /// <summary>
    /// Handles the final submission of a query (via Enter or click).
    /// </summary>
    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is AirportSuggestion suggestion)
        {
            ViewModel.SelectAirportSuggestion(suggestion);
        }
        else
        {
            // If no suggestion chosen, use the raw text
            ViewModel.SearchText = sender.Text;
            ViewModel.ClearAirportSuggestions();
        }

        sender.IsSuggestionListOpen = false;
        // Trigger the fetch command
        await ViewModel.FetchMetarCommand.ExecuteAsync(null);
    }
}
