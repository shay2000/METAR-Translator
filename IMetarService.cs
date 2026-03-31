using MetarViewer.Models;

namespace MetarViewer.Services;

/// <summary>
/// Defines a service for retrieving METAR (Meteorological Aerodrome Report) data.
/// </summary>
public interface IMetarService
{
    /// <summary>
    /// Asynchronously retrieves METAR data for a specific station ID.
    /// </summary>
    /// <param name="stationId">The 4-character ICAO code for the station (e.g., KLAX, EGLL).</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>A MetarData object if found, otherwise null.</returns>
    Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default);
}
