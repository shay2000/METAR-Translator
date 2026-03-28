using MetarViewer.Models;

namespace MetarViewer.Services;

public class HybridMetarService : IMetarService
{
    private readonly VatsimMetarService _vatsimMetarService;
    private readonly AviationWeatherMetarService _aviationWeatherMetarService;

    public HybridMetarService(IHttpClientFactory httpClientFactory)
    {
        _vatsimMetarService = new VatsimMetarService(httpClientFactory.CreateClient(VatsimMetarService.VatsimMetarHttpClientName));
        _aviationWeatherMetarService = new AviationWeatherMetarService(httpClientFactory.CreateClient(AviationWeatherMetarService.AviationWeatherHttpClientName));
    }

    public async Task<MetarData?> GetMetarAsync(string stationId, CancellationToken cancellationToken = default)
    {
        var vatsimMetar = await _vatsimMetarService.GetMetarAsync(stationId, cancellationToken);
        if (vatsimMetar != null)
        {
            return vatsimMetar;
        }

        return await _aviationWeatherMetarService.GetMetarAsync(stationId, cancellationToken);
    }
}
