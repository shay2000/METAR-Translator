using System.Globalization;
using System.Net;
using System.Text.Json;
using MetarViewer.Models;

namespace MetarViewer.Services;

public class AviationWeatherMetarService : IMetarService
{
    public static readonly Uri AviationWeatherBaseUri = new("https://aviationweather.gov/api/data/");

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly string[] WeatherIndicators =
    [
        "RA", "SN", "DZ", "FG", "BR", "HZ", "TS", "FZ", "SH",
        "SG", "PL", "GR", "GS", "UP", "DU", "SA", "VA", "FU",
        "PO", "SQ", "FC", "SS", "DS"
    ];

    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, CachedMetar> _cache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(60);

    public AviationWeatherMetarService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
    {
        var normalizedStationId = NormalizeStationId(stationId);
        if (string.IsNullOrEmpty(normalizedStationId))
        {
            return null;
        }

        // Check cache first
        if (_cache.TryGetValue(normalizedStationId, out var cached))
        {
            if (DateTime.UtcNow - cached.Timestamp < _cacheExpiration)
            {
                return cached.Data;
            }

            _cache.Remove(normalizedStationId);
        }

        try
        {
            using var response = await _httpClient.GetAsync(
                $"metar?ids={Uri.EscapeDataString(normalizedStationId)}&format=json",
                cancellationToken);

            if (response.StatusCode == HttpStatusCode.NoContent)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var reports = await JsonSerializer.DeserializeAsync<List<AviationWeatherMetarResponse>>(
                responseStream,
                SerializerOptions,
                cancellationToken);

            var report = reports?.FirstOrDefault();
            if (report == null)
            {
                return null;
            }

            var metarData = MapToMetarData(report, normalizedStationId);

            // Cache the result
            _cache[normalizedStationId] = new CachedMetar
            {
                Data = metarData,
                Timestamp = DateTime.UtcNow
            };

            return metarData;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static MetarData MapToMetarData(AviationWeatherMetarResponse response, string fallbackStationId)
    {
        var metarData = new MetarData
        {
            StationId = response.IcaoId ?? fallbackStationId,
            RawMetar = response.RawObservation ?? string.Empty,
            Temperature = RoundToInt(response.Temperature),
            DewPoint = RoundToInt(response.DewPoint),
            WindDirection = response.WindDirection,
            WindSpeed = response.WindSpeed,
            WindGust = response.WindGust,
            Visibility = ParseVisibility(response.Visibility),
            VisibilityUnit = string.IsNullOrWhiteSpace(response.Visibility) ? null : "SM",
            Altimeter = response.Altimeter,
            AltimeterUnit = response.Altimeter.HasValue ? "hPa" : null,
            FlightCategory = response.FlightCategory,
            IsCavok = response.RawObservation?.Contains("CAVOK", StringComparison.OrdinalIgnoreCase) ?? false
        };

        if (TryParseObservationTime(response, out var observationTime))
        {
            metarData.ObservationTime = observationTime;
        }

        if (response.Clouds != null)
        {
            foreach (var cloud in response.Clouds)
            {
                metarData.CloudLayers.Add(new CloudLayer
                {
                    Coverage = cloud.Cover ?? string.Empty,
                    Altitude = cloud.Base,
                    Type = cloud.Type
                });
            }
        }

        foreach (var weatherCode in ExtractWeatherPhenomena(response))
        {
            metarData.WeatherPhenomena.Add(weatherCode);
        }

        return metarData;
    }

    private static string NormalizeStationId(string stationId)
    {
        return stationId.Trim().ToUpperInvariant();
    }

    private static int? RoundToInt(decimal? value)
    {
        if (!value.HasValue)
        {
            return null;
        }

        return (int)Math.Round(value.Value, MidpointRounding.AwayFromZero);
    }

    private static bool TryParseObservationTime(AviationWeatherMetarResponse response, out DateTime observationTime)
    {
        if (DateTimeOffset.TryParse(
                response.ReportTime,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var reportTime))
        {
            observationTime = reportTime.UtcDateTime;
            return true;
        }

        if (response.ObservationEpoch.HasValue)
        {
            observationTime = DateTimeOffset.FromUnixTimeSeconds(response.ObservationEpoch.Value).UtcDateTime;
            return true;
        }

        observationTime = default;
        return false;
    }

    private static decimal? ParseVisibility(string? visibility)
    {
        if (string.IsNullOrWhiteSpace(visibility))
        {
            return null;
        }

        var normalizedVisibility = visibility.Trim().TrimEnd('+');

        if (decimal.TryParse(normalizedVisibility, NumberStyles.Number, CultureInfo.InvariantCulture, out var wholeValue))
        {
            return wholeValue;
        }

        var parts = normalizedVisibility.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var wholeMiles))
        {
            var partialMiles = ParseFraction(parts[1]);
            if (partialMiles.HasValue)
            {
                return wholeMiles + partialMiles.Value;
            }
        }

        return ParseFraction(normalizedVisibility);
    }

    private static decimal? ParseFraction(string value)
    {
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2)
        {
            return null;
        }

        if (!decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var numerator) ||
            !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var denominator) ||
            denominator == 0)
        {
            return null;
        }

        return numerator / denominator;
    }

    private static IEnumerable<string> ExtractWeatherPhenomena(AviationWeatherMetarResponse response)
    {
        var source = string.IsNullOrWhiteSpace(response.WeatherString)
            ? response.RawObservation
            : response.WeatherString;

        if (string.IsNullOrWhiteSpace(source))
        {
            return [];
        }

        return source
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim().ToUpperInvariant())
            .Where(LooksLikeWeatherToken)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool LooksLikeWeatherToken(string token)
    {
        if (token.Length < 2 ||
            token is "METAR" or "SPECI" or "AUTO" or "COR" or "NOSIG")
        {
            return false;
        }

        var candidate = token.TrimStart('+', '-');
        if (candidate.StartsWith("VC", StringComparison.Ordinal))
        {
            candidate = candidate[2..];
        }

        if (candidate.Length < 2 || candidate.Length > 8 || candidate.Any(character => !char.IsLetter(character)))
        {
            return false;
        }

        return WeatherIndicators.Any(indicator => candidate.Contains(indicator, StringComparison.Ordinal));
    }

    private class CachedMetar
    {
        public MetarData Data { get; set; } = null!;
        public DateTime Timestamp { get; set; }
    }
}
