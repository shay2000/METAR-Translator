using System.IO;
using System.Text.Json;
using MetarViewer.Models;

namespace MetarViewer.Services;

public interface IAirportLookupService
{
    Task<string?> ResolveAirportAsync(string input);
}

public class AirportLookupService : IAirportLookupService
{
    private List<Airport>? _airports;
    private readonly string _dataPath;

    public AirportLookupService(string dataPath)
    {
        _dataPath = dataPath;
    }

    public async Task<string?> ResolveAirportAsync(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        input = input.Trim();

        // If it looks like a 4-letter ICAO code, return it directly
        if (input.Length == 4 && input.All(char.IsLetter))
        {
            return input.ToUpperInvariant();
        }

        // Load airports if not already loaded
        if (_airports == null)
        {
            await LoadAirportsAsync();
        }

        if (_airports == null || _airports.Count == 0)
        {
            // Fallback: if input is all letters and 3-4 chars, try it anyway
            if (input.All(char.IsLetter) && input.Length >= 3 && input.Length <= 4)
            {
                return input.ToUpperInvariant();
            }
            return null;
        }

        var inputUpper = input.ToUpperInvariant();

        // Try exact ICAO match
        var exactIcao = _airports.FirstOrDefault(a => 
            a.Icao.Equals(inputUpper, StringComparison.OrdinalIgnoreCase));
        if (exactIcao != null)
        {
            return exactIcao.Icao;
        }

        // Try exact IATA match
        var exactIata = _airports.FirstOrDefault(a => 
            !string.IsNullOrEmpty(a.Iata) && 
            a.Iata.Equals(inputUpper, StringComparison.OrdinalIgnoreCase));
        if (exactIata != null)
        {
            return exactIata.Icao;
        }

        // Try partial name match
        var nameMatch = _airports.FirstOrDefault(a =>
            a.Name.Contains(input, StringComparison.OrdinalIgnoreCase) ||
            a.City.Contains(input, StringComparison.OrdinalIgnoreCase));

        if (nameMatch != null)
        {
            return nameMatch.Icao;
        }

        return null;
    }

    private async Task LoadAirportsAsync()
    {
        try
        {
            if (File.Exists(_dataPath))
            {
                var json = await File.ReadAllTextAsync(_dataPath);
                _airports = JsonSerializer.Deserialize<List<Airport>>(json) ?? new List<Airport>();
            }
            else
            {
                _airports = new List<Airport>();
            }
        }
        catch
        {
            _airports = new List<Airport>();
        }
    }
}
