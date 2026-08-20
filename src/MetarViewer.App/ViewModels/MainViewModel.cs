using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    private int _suggestionGeneration;
    private string? _submittedSearchText;

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

    /// <summary>
    /// Airport name supplied by the lookup service when the weather provider does not include
    /// one. This is kept outside <see cref="MetarData"/> so cached reports remain unchanged.
    /// </summary>
    [ObservableProperty]
    private string? _resolvedStationName;

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
    /// Whether the application is using its dark appearance.
    /// </summary>
    [ObservableProperty]
    private bool _isDarkTheme;

    /// <summary>
    /// Gets whether there is currently an error to display.
    /// </summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool HasSuggestions => AirportSuggestions.Count > 0;

    /// <summary>
    /// UI visibility for the loading spinner.
    /// </summary>
    public bool HasCurrentMetar => CurrentMetar is not null;

    /// <summary>
    /// Glyph for the theme toggle button based on current theme.
    /// </summary>
    public string ThemeToggleGlyph => IsDarkTheme ? "☀" : "☾";

    /// <summary>
    /// Tooltip text for the theme toggle button.
    /// </summary>
    public string ThemeToggleToolTip => IsDarkTheme
        ? "Switch to light mode"
        : "Switch to dark mode";

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
            : string.IsNullOrWhiteSpace(CurrentMetar.StationName) && string.IsNullOrWhiteSpace(ResolvedStationName)
                ? CurrentMetar.StationId
                : $"{CurrentMetar.StationId} - {GetStationDisplayName()}";

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
        Interlocked.Increment(ref _suggestionGeneration);

        if (!string.Equals(value.Trim(), _submittedSearchText, StringComparison.OrdinalIgnoreCase))
        {
            _submittedSearchText = null;
        }

        if (_selectedAirportSuggestion != null &&
            !string.Equals(value.Trim(), _selectedAirportSuggestion.DisplayText, StringComparison.OrdinalIgnoreCase))
        {
            _selectedAirportSuggestion = null;
        }
    }

    // Property update notifications for compound UI properties
    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));
    partial void OnAirportSuggestionsChanged(IReadOnlyList<AirportSuggestion> value) =>
        OnPropertyChanged(nameof(HasSuggestions));
    partial void OnCurrentMetarChanged(MetarData? value)
    {
        OnPropertyChanged(nameof(HasCurrentMetar));
        OnPropertyChanged(nameof(ObservationTimeText));
        OnPropertyChanged(nameof(StationHeaderText));
    }
    partial void OnResolvedStationNameChanged(string? value) => OnPropertyChanged(nameof(StationHeaderText));
    partial void OnIsDarkThemeChanged(bool value)
    {
        OnPropertyChanged(nameof(ThemeToggleGlyph));
        OnPropertyChanged(nameof(ThemeToggleToolTip));
    }

    /// <summary>
    /// Asynchronously fetches and decodes the METAR for the current search text.
    /// </summary>
    [RelayCommand]
    private async Task FetchMetarAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            ErrorMessage = "Please enter an airport code or name";
            return;
        }

        var submittedSearchText = SearchText.Trim();
        _submittedSearchText = submittedSearchText;
        IsLoading = true;
        ErrorMessage = null;
        CurrentMetar = null;
        ResolvedStationName = null;
        ClearAirportSuggestions();

        try
        {
            // Resolve input to an airport
            var resolvedAirport = GetSelectedAirportResolution()
                ?? await _airportLookupService.ResolveAirportDetailsAsync(SearchText, cancellationToken);

            if (resolvedAirport == null)
            {
                ErrorMessage = "Could not find airport. Please check your input.";
                return;
            }

            // Fetch the METAR data
            var metar = await _metarService.GetMetarAsync(resolvedAirport.StationId, cancellationToken);

            if (metar == null)
            {
                ErrorMessage = "Could not retrieve METAR. The station may not be available or there may be a network issue.";
                return;
            }

            if (!string.Equals(SearchText.Trim(), submittedSearchText, StringComparison.OrdinalIgnoreCase))
            {
                // The user moved on while the network request was in flight.
                return;
            }

            // Keep lookup metadata separate so a report shared by the cache is never mutated.
            ResolvedStationName = resolvedAirport.DisplayName;
            CurrentMetar = metar;
            DecodeMetar(metar);

            // Remember for next launch
            SaveLastStation(resolvedAirport.StationId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A canceled command is an expected outcome when the app closes.
        }
        catch (Exception ex)
        {
            ErrorMessage = $"An error occurred: {ex.Message}";
        }
        finally
        {
            if (string.Equals(SearchText.Trim(), submittedSearchText, StringComparison.OrdinalIgnoreCase))
            {
                ClearAirportSuggestions();
            }

            IsLoading = false;
        }
    }

    /// <summary>
    /// Toggles between light and dark themes.
    /// </summary>
    [RelayCommand]
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;
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
            await FetchMetarAsync(CancellationToken.None);
        }
    }

    /// <summary>
    /// Updates the list of suggestions as the user types.
    /// </summary>
    public async Task UpdateAirportSuggestionsAsync(string input, CancellationToken cancellationToken = default)
    {
        var trimmedInput = input.Trim();
        if (trimmedInput.Length < 2 ||
            IsSelectedAirportText(trimmedInput) ||
            IsSubmittedSearchText(trimmedInput))
        {
            ClearAirportSuggestions();
            return;
        }

        var suggestionGeneration = _suggestionGeneration;

        try
        {
            var suggestions = await _airportLookupService.GetSuggestionsAsync(trimmedInput, cancellationToken);

            // Some transports cannot cancel an already-completed response. Only the result for
            // the text still visible in the search box is allowed to update the interface.
            if (suggestionGeneration == _suggestionGeneration && IsCurrentSuggestionInput(trimmedInput))
            {
                AirportSuggestions = suggestions;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Throttled search task
        }
        catch
        {
            if (suggestionGeneration == _suggestionGeneration && IsCurrentSuggestionInput(trimmedInput))
            {
                AirportSuggestions = Array.Empty<AirportSuggestion>();
            }
        }
    }

    /// <summary>
    /// Selects a suggestion from the search box list.
    /// </summary>
    public void SelectAirportSuggestion(AirportSuggestion suggestion)
    {
        _selectedAirportSuggestion = suggestion;
        SearchText = suggestion.DisplayText;
        ClearAirportSuggestions();
    }

    /// <summary>
    /// Clears the current list of suggestions.
    /// </summary>
    public void ClearAirportSuggestions()
    {
        Interlocked.Increment(ref _suggestionGeneration);
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
            var settingsPath = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);
            File.WriteAllText(settingsPath, stationId);
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
            var settingsPath = GetSettingsPath();
            return File.Exists(settingsPath) ? File.ReadAllText(settingsPath).Trim() : null;
        }
        catch
        {
            return null;
        }
    }

    private static string GetSettingsPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "METAR Viewer",
        "last-station.txt");

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

    private bool IsSelectedAirportText(string input) =>
        _selectedAirportSuggestion is { } selected &&
        string.Equals(input, selected.DisplayText, StringComparison.OrdinalIgnoreCase);

    private bool IsCurrentSuggestionInput(string input) =>
        string.Equals(SearchText.Trim(), input, StringComparison.OrdinalIgnoreCase) &&
        !IsSelectedAirportText(input) &&
        !IsSubmittedSearchText(input);

    private bool IsSubmittedSearchText(string input) =>
        string.Equals(input, _submittedSearchText, StringComparison.OrdinalIgnoreCase);

    private string GetStationDisplayName() =>
        !string.IsNullOrWhiteSpace(CurrentMetar?.StationName)
            ? CurrentMetar.StationName
            : ResolvedStationName!;
}
