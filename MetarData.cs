namespace MetarViewer.Models;

public class MetarData
{
    public string StationId { get; set; } = string.Empty;
    public DateTime ObservationTime { get; set; }
    public string RawMetar { get; set; } = string.Empty;
    public int? Temperature { get; set; }
    public int? DewPoint { get; set; }
    public int? WindDirection { get; set; }
    public int? WindSpeed { get; set; }
    public int? WindGust { get; set; }
    public decimal? Visibility { get; set; }
    public string? VisibilityUnit { get; set; }
    public decimal? Altimeter { get; set; }
    public List<CloudLayer> CloudLayers { get; set; } = new();
    public List<string> WeatherPhenomena { get; set; } = new();
    public string? FlightCategory { get; set; }
    public bool IsCavok { get; set; }
}

public class CloudLayer
{
    public string Coverage { get; set; } = string.Empty;
    public int? Altitude { get; set; }
    public string? Type { get; set; }
}

public class Airport
{
    public string Icao { get; set; } = string.Empty;
    public string? Iata { get; set; }
    public string Name { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Country { get; set; } = string.Empty;
}
