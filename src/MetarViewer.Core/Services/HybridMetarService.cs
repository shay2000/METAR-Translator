using MetarViewer.Models;

namespace MetarViewer.Services;

/// <summary>
/// A hybrid service that attempts to retrieve METAR data from VATSIM first,
/// falling back to Aviation Weather if VATSIM is unavailable or doesn't have the data.
/// </summary>
public class HybridMetarService : IMetarService
{
    private readonly VatsimMetarService _vatsimMetarService;
    private readonly AviationWeatherMetarService _aviationWeatherMetarService;

    /// <summary>
    /// Initializes a new instance of the <see cref="HybridMetarService"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Factory to create named HTTP clients for different services.</param>
    public HybridMetarService(IHttpClientFactory httpClientFactory)
    {
        _vatsimMetarService = new VatsimMetarService(httpClientFactory.CreateClient(VatsimMetarService.VatsimMetarHttpClientName));
        _aviationWeatherMetarService = new AviationWeatherMetarService(httpClientFactory.CreateClient(AviationWeatherMetarService.AviationWeatherHttpClientName));
    }

    /// <summary>
    /// Asynchronously retrieves METAR data by trying multiple sources.
    /// </summary>
    /// <param name="stationId">The 4-character ICAO code for the station.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A MetarData object if found in any source, otherwise null.</returns>
    public async Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
    {
        // Try VATSIM first (often preferred for flight simulation)
        var vatsimMetar = await _vatsimMetarService.GetMetarAsync(stationId, cancellationToken);
        if (vatsimMetar != null)
        {
            return vatsimMetar;
        }

        // Fallback to real-world Aviation Weather data
        return await _aviationWeatherMetarService.GetMetarAsync(stationId, cancellationToken);
    }
}
