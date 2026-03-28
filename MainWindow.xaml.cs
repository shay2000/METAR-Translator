using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using MetarViewer.ViewModels;

namespace MetarViewer.Views;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        // Apply Mica background
        SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();

        // Set window size
        AppWindow.Resize(new Windows.Graphics.SizeInt32(900, 800));

        // Load last station on startup
        _ = ViewModel.LoadLastStationAsync();
    }

    private async void SearchTextBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            await ViewModel.FetchMetarCommand.ExecuteAsync(null);
        }
    }
}
