using System.IO;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using MetarViewer.Services;
using MetarViewer.ViewModels;

namespace MetarViewer.Views;

public sealed partial class MainWindow : Window
{
    private CancellationTokenSource? _suggestionCancellationTokenSource;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // Apply Mica background
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");
        if (File.Exists(iconPath))
        {
            AppWindow.SetIcon(iconPath);
        }

        // Set window size
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 800));

        // Load last station on startup
        _ = ViewModel.LoadLastStationAsync();
    }

    private async void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        ViewModel.SearchText = sender.Text;

        if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
        {
            sender.IsSuggestionListOpen = false;
            return;
        }

        _suggestionCancellationTokenSource?.Cancel();
        _suggestionCancellationTokenSource?.Dispose();
        _suggestionCancellationTokenSource = new CancellationTokenSource();

        try
        {
            await ViewModel.UpdateAirportSuggestionsAsync(sender.Text, _suggestionCancellationTokenSource.Token);
            sender.IsSuggestionListOpen = ViewModel.AirportSuggestions.Count > 0;
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void SearchBox_SuggestionChosen(AutoSuggestBox sender, AutoSuggestBoxSuggestionChosenEventArgs args)
    {
        if (args.SelectedItem is AirportSuggestion suggestion)
        {
            ViewModel.SelectAirportSuggestion(suggestion);
            sender.IsSuggestionListOpen = false;
        }
    }

    private async void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (args.ChosenSuggestion is AirportSuggestion suggestion)
        {
            ViewModel.SelectAirportSuggestion(suggestion);
        }
        else
        {
            ViewModel.SearchText = sender.Text;
            ViewModel.ClearAirportSuggestions();
        }

        sender.IsSuggestionListOpen = false;
        await ViewModel.FetchMetarCommand.ExecuteAsync(null);
    }
}
