using System.Net;
using System.Text;
using MetarViewer.Services;
using Xunit;

namespace MetarViewer.Tests;

public class HybridMetarServiceTests
{
    [Fact]
    public async Task GetMetarAsync_UsesAviationWeatherFallbackWhenVatsimMisses()
    {
        using var vatsimHandler = new StubHttpMessageHandler(_ => CreateJsonResponse("[]"));
        using var aviationWeatherHandler = new StubHttpMessageHandler(_ => CreateJsonResponse("""
            [
              {
                "icaoId": "OTHH",
                "reportTime": "2026-03-28T16:00:00.000Z",
                "temp": 30,
                "dewp": 19,
                "wdir": 330,
                "wspd": 11,
                "visib": "6+",
                "altim": 1008,
                "rawOb": "METAR OTHH 281600Z 33011KT 6000 FEW030 30/19 Q1008",
                "clouds": [
                  { "cover": "FEW", "base": 3000 }
                ],
                "fltCat": "VFR"
              }
            ]
            """));

        var service = new HybridMetarService(new StubHttpClientFactory(
            CreateClient(VatsimMetarService.VatsimMetarBaseUri, vatsimHandler),
            CreateClient(AviationWeatherMetarService.AviationWeatherBaseUri, aviationWeatherHandler)));

        var result = await service.GetMetarAsync("OTHH");

        Assert.NotNull(result);
        Assert.Equal("OTHH", result!.StationId);
        Assert.Equal(30, result.Temperature);
        Assert.Equal(1008m, result.Altimeter);
    }

    [Fact]
    public async Task GetMetarAsync_ReturnsVatsimResultWithoutUsingFallback()
    {
        using var vatsimHandler = new StubHttpMessageHandler(_ => CreateJsonResponse("""
            [
              {
                "id": "OMAA",
                "metar": "281700Z 31010KT 9999 FEW030 28/14 Q1010"
              }
            ]
            """));
        using var aviationWeatherHandler = new StubHttpMessageHandler(_ => throw new Xunit.Sdk.XunitException("Fallback should not be called"));

        var service = new HybridMetarService(new StubHttpClientFactory(
            CreateClient(VatsimMetarService.VatsimMetarBaseUri, vatsimHandler),
            CreateClient(AviationWeatherMetarService.AviationWeatherBaseUri, aviationWeatherHandler)));

        var result = await service.GetMetarAsync("OMAA");

        Assert.NotNull(result);
        Assert.Equal("OMAA", result!.StationId);
        Assert.Equal(28, result.Temperature);
    }

    private static HttpClient CreateClient(Uri baseAddress, HttpMessageHandler handler)
    {
        return new HttpClient(handler)
        {
            BaseAddress = baseAddress
        };
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpClient _vatsimClient;
        private readonly HttpClient _aviationWeatherClient;

        public StubHttpClientFactory(HttpClient vatsimClient, HttpClient aviationWeatherClient)
        {
            _vatsimClient = vatsimClient;
            _aviationWeatherClient = aviationWeatherClient;
        }

        public HttpClient CreateClient(string name)
        {
            return name switch
            {
                VatsimMetarService.VatsimMetarHttpClientName => _vatsimClient,
                AviationWeatherMetarService.AviationWeatherHttpClientName => _aviationWeatherClient,
                _ => throw new Xunit.Sdk.XunitException($"Unexpected client name: {name}")
            };
        }
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responseFactory;

        public StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory)
        {
            _responseFactory = responseFactory;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_responseFactory(request));
        }
    }
}
