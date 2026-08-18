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

/// <summary>
/// The main view model for the application, responsible for search logic, 
/// data fetching, and decoding state management.
/// </summary>
public partial class MainViewModel : ObservableObject
{
    private readonly IMetarService _metarService;
    private readonly IAirportLookupService _airportLookupService;
    private AirportSuggestion? _selectedAirportSuggestion;

    /// <summary>
    /// The text entered in the search box.
    /// </summary>
    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>
    /// Current list of airport search suggestions.
    /// </summary>
    [ObservableProperty]
    private IReadOnlyList<AirportSuggestion> _airportSuggestions = Array.Empty<AirportSuggestion>();

    /// <summary>
    /// Indicates if a data fetch operation is in progress.
    /// </summary>
    [ObservableProperty]
    private bool _isLoading;

    /// <summary>
    /// Holds any error message to display to the user.
    /// </summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// The currently loaded METAR data.
    /// </summary>
    [ObservableProperty]
    private MetarData? _currentMetar;

    // Decoded property fields for UI binding
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

    /// <summary>
    /// The current application theme (Light, Dark, or Default).
    /// </summary>
    [ObservableProperty]
    private ElementTheme _currentTheme = ElementTheme.Default;

    /// <summary>
    /// Gets whether there is currently an error to display.
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    /// <summary>
    /// UI visibility for the loading spinner.
    /// </summary>
    public Visibility LoadingVisibility => IsLoading ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// UI visibility for the main METAR display.
    /// </summary>
    public Visibility CurrentMetarVisibility => CurrentMetar is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>
    /// Glyph for the theme toggle button based on current theme.
    /// </summary>
    public string ThemeToggleGlyph => CurrentTheme switch
    {
        ElementTheme.Dark => "\u263E", // Moon
        ElementTheme.Light => "\u2600", // Sun
        _ => "\u25D0" // Half circle
    };

    /// <summary>
    /// Tooltip text for the theme toggle button.
    /// </summary>
    public string ThemeToggleToolTip => CurrentTheme switch
    {
        ElementTheme.Dark => "Dark mode enabled. Click to switch to light mode",
        ElementTheme.Light => "Light mode enabled. Click to switch to dark mode",
        _ => "Theme follows the app default. Click to switch to dark mode"
    };

    /// <summary>
    /// Formatted observation time string.
    /// </summary>
    public string ObservationTimeText =>
        CurrentMetar is { ObservationTime: var time } && time != default
            ? $"{time:dd MMM yyyy HH:mm} UTC"
            : string.Empty;

    /// <summary>
    /// Header text for the station (e.g., "KLAX - Los Angeles Intl").
    /// </summary>
    public string StationHeaderText =>
        CurrentMetar is null
            ? string.Empty
            : string.IsNullOrWhiteSpace(CurrentMetar.StationName)
                ? CurrentMetar.StationId
                : $"{CurrentMetar.StationId} - {CurrentMetar.StationName}";

    /// <summary>
    /// Initializes a new instance of the <see cref="MainViewModel"/> class.
    /// </summary>
    public MainViewModel(IMetarService metarService, IAirportLookupService airportLookupService)
    {
        _metarService = metarService;
        _airportLookupService = airportLookupService;
    }

    /// <summary>
    /// Clears selected suggestion if user modifies the search text manually.
    /// </summary>
    partial void OnSearchTextChanged(string value)
    {
        if (_selectedAirportSuggestion != null &&
            !string.Equals(value.Trim(), _selectedAirportSuggestion.DisplayText, StringComparison.OrdinalIgnoreCase))
        {
            _selectedAirportSuggestion = null;
        }
    }

    // Property update notifications for compound UI properties
    partial void OnIsLoadingChanged(bool value) => OnPropertyChanged(nameof(LoadingVisibility));
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
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

    /// <summary>
    /// Asynchronously fetches and decodes the METAR for the current search text.
    /// </summary>
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
            // Resolve input to an airport
            var resolvedAirport = GetSelectedAirportResolution()
                ?? await _airportLookupService.ResolveAirportDetailsAsync(SearchText);

            if (resolvedAirport == null)
            {
                ErrorMessage = "Could not find airport. Please check your input.";
                return;
            }

            // Fetch the METAR data
            var metar = await _metarService.GetMetarAsync(resolvedAirport.StationId);

            if (metar == null)
            {
                ErrorMessage = "Could not retrieve METAR. The station may not be available or there may be a network issue.";
                return;
            }

            // Mix in the resolved airport name if the service didn't provide one
            if (!string.IsNullOrWhiteSpace(resolvedAirport.DisplayName))
            {
                metar.StationName = resolvedAirport.DisplayName;
            }

            CurrentMetar = metar;
            DecodeMetar(metar);

            // Remember for next launch
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

    /// <summary>
    /// Toggles between light and dark themes.
    /// </summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        CurrentTheme = CurrentTheme == ElementTheme.Dark
            ? ElementTheme.Light
            : ElementTheme.Dark;
    }

    /// <summary>
    /// Loads the last successfully searched station on app startup.
    /// </summary>
    public async Task LoadLastStationAsync()
    {
        var lastStation = LoadLastStation();
        if (!string.IsNullOrEmpty(lastStation))
        {
            SearchText = lastStation;
            await FetchMetarAsync();
        }
    }

    /// <summary>
    /// Updates the list of suggestions as the user types.
    /// </summary>
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
            // Throttled search task
        }
        catch
        {
            AirportSuggestions = Array.Empty<AirportSuggestion>();
        }
    }

    /// <summary>
    /// Selects a suggestion from the search box list.
    /// </summary>
    public void SelectAirportSuggestion(AirportSuggestion suggestion)
    {
        _selectedAirportSuggestion = suggestion;
        SearchText = suggestion.DisplayText;
        AirportSuggestions = Array.Empty<AirportSuggestion>();
    }

    /// <summary>
    /// Clears the current list of suggestions.
    /// </summary>
    public void ClearAirportSuggestions()
    {
        AirportSuggestions = Array.Empty<AirportSuggestion>();
    }

    /// <summary>
    /// Helper to run the METAR decoder on raw data and populate view model fields.
    /// </summary>
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
            // Settings persistence is best-effort
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

    /// <summary>
    /// Converts a selected suggestion into a resolution object for further lookups.
    /// </summary>
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
