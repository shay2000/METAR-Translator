using System.Net;
using System.Text;
using MetarViewer.Services;
using Xunit;

namespace MetarViewer.Tests;

public class VatsimMetarServiceTests
{
    [Fact]
    public async Task GetMetarAsync_MapsMetricMetarFromVatsim()
    {
        using var handler = new StubHttpMessageHandler(_ => CreateJsonResponse("""
            [
              {
                "id": "OMDB",
                "metar": "281700Z 32008KT 9999 FEW040 31/17 Q1008 NOSIG"
              }
            ]
            """));
        var service = CreateService(handler);

        var result = await service.GetMetarAsync("omdb");

        Assert.NotNull(result);
        Assert.Equal("OMDB", result!.StationId);
        Assert.Equal("METAR OMDB 281700Z 32008KT 9999 FEW040 31/17 Q1008 NOSIG", result.RawMetar);
        Assert.Equal(28, result.ObservationTime.Day);
        Assert.Equal(17, result.ObservationTime.Hour);
        Assert.Equal(31, result.Temperature);
        Assert.Equal(17, result.DewPoint);
        Assert.Equal(320, result.WindDirection);
        Assert.Equal(8, result.WindSpeed);
        Assert.Equal(10m, result.Visibility);
        Assert.Equal("km", result.VisibilityUnit);
        Assert.Equal(1008m, result.Altimeter);
        Assert.Equal("hPa", result.AltimeterUnit);
        Assert.Equal("VFR", result.FlightCategory);
        Assert.Single(result.CloudLayers);
    }

    [Fact]
    public async Task GetMetarAsync_MapsUsStyleMetarFromVatsim()
    {
        using var handler = new StubHttpMessageHandler(_ => CreateJsonResponse("""
            [
              {
                "id": "KJFK",
                "metar": "281651Z 32015G22KT 10SM -RA FEW060 FEW120 04/M14 A3045"
              }
            ]
            """));
        var service = CreateService(handler);

        var result = await service.GetMetarAsync("KJFK");

        Assert.NotNull(result);
        Assert.Equal("KJFK", result!.StationId);
        Assert.Equal(15, result.WindSpeed);
        Assert.Equal(22, result.WindGust);
        Assert.Equal(10m, result.Visibility);
        Assert.Equal("SM", result.VisibilityUnit);
        Assert.Equal(30.45m, result.Altimeter);
        Assert.Equal("inHg", result.AltimeterUnit);
        Assert.Contains("-RA", result.WeatherPhenomena);
    }

    [Fact]
    public async Task GetMetarAsync_EmptyResponseReturnsNull()
    {
        using var handler = new StubHttpMessageHandler(_ => CreateJsonResponse("[]"));
        var service = CreateService(handler);

        var result = await service.GetMetarAsync("OTHH");

        Assert.Null(result);
    }

    private static VatsimMetarService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = VatsimMetarService.VatsimMetarBaseUri
        };

        return new VatsimMetarService(client);
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
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
