using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using MetarViewer.Models;

namespace MetarViewer.Services;

public class AvwxMetarService : IMetarService
{
    private readonly HttpClient _httpClient;
    private readonly Dictionary<string, CachedMetar> _cache = new();
    private readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(60);

    public AvwxMetarService(HttpClient httpClient)
    {
        _httpClient = httpClient;
        _httpClient.BaseAddress = new Uri("https://avwx.rest/api/");
        // Note: For production use, add your AVWX API token here
        // _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiToken}");
    }

    public async Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
    {
        // Check cache first
        if (_cache.TryGetValue(stationId.ToUpperInvariant(), out var cached))
        {
            if (DateTime.UtcNow - cached.Timestamp < _cacheExpiration)
            {
                return cached.Data;
            }
            _cache.Remove(stationId.ToUpperInvariant());
        }

        try
        {
            var response = await _httpClient.GetAsync(
                $"metar/{stationId.ToUpperInvariant()}",
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var avwxResponse = await response.Content.ReadFromJsonAsync<AvwxMetarResponse>(
                cancellationToken: cancellationToken);

            if (avwxResponse == null)
            {
                return null;
            }

            var metarData = MapToMetarData(avwxResponse);

            // Cache the result
            _cache[stationId.ToUpperInvariant()] = new CachedMetar
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
        catch (JsonException)
        {
            return null;
        }
    }

    private MetarData MapToMetarData(AvwxMetarResponse response)
    {
        var metarData = new MetarData
        {
            StationId = response.Station ?? string.Empty,
            RawMetar = response.Raw ?? string.Empty,
            Temperature = (int?)response.Temperature?.Value,
            DewPoint = (int?)response.Dewpoint?.Value,
            WindDirection = (int?)response.WindDirection?.Value,
            WindSpeed = (int?)response.WindSpeed?.Value,
            WindGust = (int?)response.WindGust?.Value,
            Visibility = response.Visibility?.Value,
            Altimeter = response.Altimeter?.Value,
            FlightCategory = response.FlightRules,
            IsCavok = response.Other?.Contains("CAVOK") ?? false
        };

        // Parse observation time
        if (DateTime.TryParse(response.Time?.Dt, out var observationTime))
        {
            metarData.ObservationTime = observationTime;
        }

        // Map cloud layers
        if (response.Clouds != null)
        {
            foreach (var cloud in response.Clouds)
            {
                metarData.CloudLayers.Add(new CloudLayer
                {
                    Coverage = cloud.Type ?? string.Empty,
                    Altitude = cloud.Altitude,
                    Type = cloud.Modifier
                });
            }
        }

        // Map weather phenomena
        if (response.WxCodes != null)
        {
            foreach (var wx in response.WxCodes)
            {
                if (!string.IsNullOrEmpty(wx.Value))
                {
                    metarData.WeatherPhenomena.Add(wx.Value);
                }
            }
        }

        // Determine visibility unit (AVWX returns in statute miles or meters)
        metarData.VisibilityUnit = "SM";

        return metarData;
    }

    private class CachedMetar
    {
        public MetarData Data { get; set; } = null!;
        public DateTime Timestamp { get; set; }
    }
}
