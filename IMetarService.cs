using MetarViewer.Models;

namespace MetarViewer.Services;

public interface IMetarService
{
    Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default);
}
