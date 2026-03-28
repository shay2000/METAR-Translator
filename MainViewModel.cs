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

    [ObservableProperty]
    private string _searchText = string.Empty;

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

    public string ObservationTimeText =>
        CurrentMetar is { ObservationTime: var time } && time != default
            ? $"{time:dd MMM yyyy HH:mm} UTC"
            : string.Empty;

    public MainViewModel(IMetarService metarService, IAirportLookupService airportLookupService)
    {
        _metarService = metarService;
        _airportLookupService = airportLookupService;
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

        try
        {
            // Resolve the airport
            var stationId = await _airportLookupService.ResolveAirportAsync(SearchText);

            if (string.IsNullOrEmpty(stationId))
            {
                ErrorMessage = "Could not find airport. Please check your input.";
                return;
            }

            // Fetch METAR
            var metar = await _metarService.GetMetarAsync(stationId);

            if (metar == null)
            {
                ErrorMessage = "Could not retrieve METAR. The station may not be available or there may be a network issue.";
                return;
            }

            CurrentMetar = metar;
            DecodeMetar(metar);

            // Save last successful station
            SaveLastStation(stationId);
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
        CurrentTheme = CurrentTheme == ElementTheme.Light 
            ? ElementTheme.Dark 
            : ElementTheme.Light;
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
}
