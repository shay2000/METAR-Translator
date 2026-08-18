using System.Net;
using System.Text;
using MetarViewer.Services;
using Xunit;

namespace MetarViewer.Tests;

public class AviationWeatherMetarServiceTests
{
    [Fact]
    public async Task GetMetarAsync_MapsOfficialApiResponse()
    {
        using var handler = new StubHttpMessageHandler(_ => CreateJsonResponse("""
            [
              {
                "icaoId": "KJFK",
                "name": "John F Kennedy International Airport",
                "obsTime": 1774713060,
                "reportTime": "2026-03-28T16:00:00.000Z",
                "temp": 3.9,
                "dewp": -13.9,
                "wdir": 320,
                "wspd": 15,
                "wgst": 22,
                "visib": "10+",
                "altim": 1031.2,
                "rawOb": "METAR KJFK 281551Z 32015G22KT 10SM -RA FEW060 FEW120 04/M14 A3045",
                "clouds": [
                  { "cover": "FEW", "base": 6000 },
                  { "cover": "FEW", "base": 12000 }
                ],
                "fltCat": "VFR"
              }
            ]
            """));
        var service = CreateService(handler);

        var result = await service.GetMetarAsync("kjfk");

        Assert.NotNull(result);
        Assert.Equal("KJFK", result!.StationId);
        Assert.Equal("John F Kennedy International Airport", result.StationName);
        Assert.Equal(4, result.Temperature);
        Assert.Equal(-14, result.DewPoint);
        Assert.Equal(320, result.WindDirection);
        Assert.Equal(15, result.WindSpeed);
        Assert.Equal(22, result.WindGust);
        Assert.Equal(10m, result.Visibility);
        Assert.Equal("SM", result.VisibilityUnit);
        Assert.Equal(1031.2m, result.Altimeter);
        Assert.Equal("hPa", result.AltimeterUnit);
        Assert.Equal("VFR", result.FlightCategory);
        Assert.Equal(2, result.CloudLayers.Count);
        Assert.Contains("-RA", result.WeatherPhenomena);
        Assert.Equal(new DateTime(2026, 3, 28, 16, 0, 0, DateTimeKind.Utc), result.ObservationTime);
    }

    [Theory]
    [InlineData("TEMPO")] // Ends in "PO", the code for dust/sand whirls.
    [InlineData("BECMG")] // Contains "CM".
    public async Task GetMetarAsync_TrendGroupIsNotReportedAsWeather(string trendGroup)
    {
        using var handler = new StubHttpMessageHandler(_ => CreateJsonResponse($$"""
            [
              {
                "icaoId": "EGLL",
                "reportTime": "2026-03-28T16:20:00.000Z",
                "rawOb": "METAR EGLL 281620Z 32012KT 9999 SCT030 09/M02 Q1026 {{trendGroup}} 3000 BR",
                "clouds": []
              }
            ]
            """));
        var service = CreateService(handler);

        var result = await service.GetMetarAsync("EGLL");

        Assert.NotNull(result);
        Assert.DoesNotContain(trendGroup, result!.WeatherPhenomena);
        // Genuine weather in the same report is still recognised.
        Assert.Contains("BR", result.WeatherPhenomena);
    }

    [Fact]
    public async Task GetMetarAsync_NoContent_ReturnsNull()
    {
        using var handler = new StubHttpMessageHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var service = CreateService(handler);

        var result = await service.GetMetarAsync("EGLL");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetMetarAsync_NormalizesStationIdBeforeQuerying()
    {
        Uri? requestedUri = null;
        using var handler = new StubHttpMessageHandler(request =>
        {
            requestedUri = request.RequestUri;
            return CreateJsonResponse("[]");
        });
        var service = CreateService(handler);

        await service.GetMetarAsync("  egll  ");

        Assert.NotNull(requestedUri);
        Assert.Contains("ids=EGLL", requestedUri!.Query, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetMetarAsync_HttpFailure_ReturnsNull()
    {
        using var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var service = CreateService(handler);

        var result = await service.GetMetarAsync("EGLL");

        Assert.Null(result);
        Assert.Equal(1, handler.CallCount);
    }

    private static AviationWeatherMetarService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = AviationWeatherMetarService.AviationWeatherBaseUri
        };

        return new AviationWeatherMetarService(client);
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

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(_responseFactory(request));
        }
    }
}
