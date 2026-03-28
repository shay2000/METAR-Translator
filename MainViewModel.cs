using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using MetarViewer.Helpers;
using MetarViewer.Models;
using MetarViewer.Services;

namespace MetarViewer.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IMetarService _metarService;
    private readonly IAirportLookupService _airportLookupService;
    private AirportSuggestion? _selectedAirportSuggestion;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<AirportSuggestion> _airportSuggestions = Array.Empty<AirportSuggestion>();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private MetarData? _currentMetar;

    [ObservableProperty]
    private string _decodedWind = string.Empty;

    [ObservableProperty]
    private string _decodedVisibility = string.Empty;

    [ObservableProperty]
    private string _decodedClouds = string.Empty;

    [ObservableProperty]
    private string _decodedTemperature = string.Empty;

    [ObservableProperty]
    private string _decodedAltimeter = string.Empty;

    [ObservableProperty]
    private string _decodedWeather = string.Empty;

    [ObservableProperty]
    private string _flightCategoryDescription = string.Empty;

    [ObservableProperty]
    private ElementTheme _currentTheme = ElementTheme.Default;

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;

    public Visibility CurrentMetarVisibility => CurrentMetar is null ? Visibility.Collapsed : Visibility.Visible;

    public string ThemeToggleGlyph => CurrentTheme switch
    {
        ElementTheme.Dark => "\u263E",
        ElementTheme.Light => "\u2600",
        _ => "\u25D0"
    };

    public string ThemeToggleToolTip => CurrentTheme switch
    {
        ElementTheme.Dark => "Dark mode enabled. Click to switch to light mode",
        ElementTheme.Light => "Light mode enabled. Click to switch to dark mode",
        _ => "Theme follows the app default. Click to switch to dark mode"
    };

    public string ObservationTimeText =>
        CurrentMetar is { ObservationTime: var time } && time != default
            ? $"{time:dd MMM yyyy HH:mm} UTC"
            : string.Empty;

    public string StationHeaderText =>
        CurrentMetar is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(CurrentMetar.StationName)
                ? CurrentMetar.StationId
                : $"{CurrentMetar.StationId} - {CurrentMetar.StationName}";

    public MainViewModel(IMetarService metarService, IAirportLookupService airportLookupService)
    {
        _metarService = metarService;
        _airportLookupService = airportLookupService;
    }

    partial void OnSearchTextChanged(string value)
    {
        if (_selectedAirportSuggestion != null &&
            !string.Equals(value.Trim(), _selectedAirportSuggestion.DisplayText, StringComparison.OrdinalIgnoreCase))
        {
            _selectedAirportSuggestion = null;
        }
    }

    partial void OnIsLoadingChanged(bool value)
    {
        OnPropertyChanged(nameof(LoadingVisibility));
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(HasError));
    }

    partial void OnCurrentMetarChanged(MetarData? value)
    {
        OnPropertyChanged(nameof(CurrentMetarVisibility));
        OnPropertyChanged(nameof(ObservationTimeText));
        OnPropertyChanged(nameof(StationHeaderText));
    }

    partial void OnCurrentThemeChanged(ElementTheme value)
    {
        OnPropertyChanged(nameof(ThemeToggleGlyph));
        OnPropertyChanged(nameof(ThemeToggleToolTip));
    }

    [RelayCommand]
    private async Task FetchMetarAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            ErrorMessage = "Please enter an airport code or name";
            return;
        }

        IsLoading = true;
        ErrorMessage = null;
        CurrentMetar = null;
        ClearAirportSuggestions();

        try
        {
            var resolvedAirport = GetSelectedAirportResolution()
                ?? await _airportLookupService.ResolveAirportDetailsAsync(SearchText);

            if (resolvedAirport == null)
            {
                ErrorMessage = "Could not find airport. Please check your input.";
                return;
            }

            var metar = await _metarService.GetMetarAsync(resolvedAirport.StationId);

            if (metar == null)
            {
                ErrorMessage = "Could not retrieve METAR. The station may not be available or there may be a network issue.";
                return;
            }

            if (!string.IsNullOrWhiteSpace(resolvedAirport.DisplayName))
            {
                metar.StationName = resolvedAirport.DisplayName;
            }

            CurrentMetar = metar;
            DecodeMetar(metar);

            SaveLastStation(resolvedAirport.StationId);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"An error occurred: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void ToggleTheme()
    {
        CurrentTheme = CurrentTheme == ElementTheme.Dark
            ? ElementTheme.Light
            : ElementTheme.Dark;
    }

    public async Task LoadLastStationAsync()
    {
        var lastStation = LoadLastStation();
        if (!string.IsNullOrEmpty(lastStation))
        {
            SearchText = lastStation;
            await FetchMetarAsync();
        }
    }

    public async Task UpdateAirportSuggestionsAsync(string input, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Trim().Length < 2)
        {
            AirportSuggestions = Array.Empty<AirportSuggestion>();
            return;
        }

        try
        {
            AirportSuggestions = await _airportLookupService.GetSuggestionsAsync(input, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            AirportSuggestions = Array.Empty<AirportSuggestion>();
        }
    }

    public void SelectAirportSuggestion(AirportSuggestion suggestion)
    {
        _selectedAirportSuggestion = suggestion;
        SearchText = suggestion.DisplayText;
        AirportSuggestions = Array.Empty<AirportSuggestion>();
    }

    public void ClearAirportSuggestions()
    {
        AirportSuggestions = Array.Empty<AirportSuggestion>();
    }

    private void DecodeMetar(MetarData metar)
    {
        DecodedWind = MetarDecoder.DecodeWind(metar);
        DecodedVisibility = MetarDecoder.DecodeVisibility(metar);
        DecodedClouds = MetarDecoder.DecodeClouds(metar);
        DecodedTemperature = MetarDecoder.DecodeTemperature(metar);
        DecodedAltimeter = MetarDecoder.DecodeAltimeter(metar);
        DecodedWeather = MetarDecoder.DecodeWeather(metar);
        FlightCategoryDescription = MetarDecoder.GetFlightCategoryDescription(metar.FlightCategory);
    }

    private void SaveLastStation(string stationId)
    {
        try
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            settings.Values["LastStation"] = stationId;
        }
        catch
        {
            // Ignore settings errors
        }
    }

    private string? LoadLastStation()
    {
        try
        {
            var settings = Windows.Storage.ApplicationData.Current.LocalSettings;
            return settings.Values["LastStation"] as string;
        }
        catch
        {
            return null;
        }
    }

    private ResolvedAirport? GetSelectedAirportResolution()
    {
        return _selectedAirportSuggestion == null
            ? null
            : new ResolvedAirport(
                _selectedAirportSuggestion.StationId,
                _selectedAirportSuggestion.DisplayName,
                _selectedAirportSuggestion.IataCode);
    }
}
