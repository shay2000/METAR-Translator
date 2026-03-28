using System.Net;
using System.Text;
using MetarViewer.Services;
using Xunit;

namespace MetarViewer.Tests;

public class AirportLookupServiceTests
{
    [Fact]
    public async Task ResolveAirportAsync_LocalMatchWinsWithoutHttp()
    {
        using var airportFile = new TemporaryAirportDataFile("""
            [
              {
                "icao": "EGLL",
                "iata": "LHR",
                "name": "London Heathrow Airport",
                "city": "London",
                "country": "United Kingdom"
              }
            ]
            """);
        using var handler = new StubHttpMessageHandler(_ => CreateJsonResponse("{}"));
        var service = CreateService(airportFile.Path, handler);

        var result = await service.ResolveAirportAsync("LHR");

        Assert.Equal("EGLL", result);
        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAirportAsync_NoLocalMatchQueriesApiAndReturnsIcao()
    {
        using var airportFile = new TemporaryAirportDataFile("[]");
        using var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(
                "https://airportsapi.com/api/airports?filter%5Bname%5D=Heathrow&sort=name&include=country%2Cregion&page%5Bsize%5D=15",
                request.RequestUri?.ToString());

            return CreateJsonResponse("""
                {
                  "data": [
                    {
                      "id": "EGLL",
                      "type": "airports",
                      "attributes": {
                        "name": "London Heathrow Airport",
                        "code": "EGLL",
                        "type": "large_airport",
                        "gps_code": "EGLL",
                        "icao_code": "EGLL",
                        "iata_code": "LHR",
                        "local_code": null
                      }
                    }
                  ]
                }
                """);
        });
        var service = CreateService(airportFile.Path, handler);

        var result = await service.ResolveAirportAsync("Heathrow");

        Assert.Equal("EGLL", result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAirportAsync_NameSearchUsesApiBeforeLocalManualList()
    {
        using var airportFile = new TemporaryAirportDataFile("""
            [
              {
                "icao": "ZZZZ",
                "iata": null,
                "name": "Heathrow Practice Airport",
                "city": "London",
                "country": "United Kingdom"
              }
            ]
            """);
        using var handler = new StubHttpMessageHandler(_ => CreateJsonResponse("""
            {
              "data": [
                {
                  "id": "EGLL",
                  "type": "airports",
                  "attributes": {
                    "name": "London Heathrow Airport",
                    "code": "EGLL",
                    "type": "large_airport",
                    "gps_code": "EGLL",
                    "icao_code": "EGLL",
                    "iata_code": "LHR",
                    "local_code": null
                  }
                }
              ]
            }
            """));
        var service = CreateService(airportFile.Path, handler);

        var result = await service.ResolveAirportAsync("Heathrow");

        Assert.Equal("EGLL", result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAirportAsync_ApiFailureFallsBackToLocalAirportSearch()
    {
        using var airportFile = new TemporaryAirportDataFile("""
            [
              {
                "icao": "EGLL",
                "iata": "LHR",
                "name": "London Heathrow Airport",
                "city": "London",
                "country": "United Kingdom"
              }
            ]
            """);
        using var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var service = CreateService(airportFile.Path, handler);

        var result = await service.ResolveAirportAsync("Heathrow");

        Assert.Equal("EGLL", result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAirportAsync_HttpFailureFallsBackToHeuristic()
    {
        using var airportFile = new TemporaryAirportDataFile("[]");
        using var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var service = CreateService(airportFile.Path, handler);

        var result = await service.ResolveAirportAsync("egll");

        Assert.Equal("EGLL", result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAirportAsync_CachesApiResultsByNormalizedInput()
    {
        using var airportFile = new TemporaryAirportDataFile("[]");
        using var handler = new StubHttpMessageHandler(_ => CreateJsonResponse("""
            {
              "data": [
                {
                  "id": "EGLL",
                    "type": "airports",
                    "attributes": {
                      "name": "London Heathrow Airport",
                      "code": "EGLL",
                      "type": "large_airport",
                      "gps_code": "EGLL",
                      "icao_code": "EGLL",
                      "iata_code": "LHR",
                      "local_code": null
                    }
                }
              ]
            }
            """));
        var service = CreateService(airportFile.Path, handler);

        var firstResult = await service.ResolveAirportAsync("Heathrow");
        var secondResult = await service.ResolveAirportAsync("heathrow");

        Assert.Equal("EGLL", firstResult);
        Assert.Equal("EGLL", secondResult);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAirportAsync_ApiPrefersOperationalAirportType()
    {
        using var airportFile = new TemporaryAirportDataFile("[]");
        using var handler = new StubHttpMessageHandler(_ => CreateJsonResponse("""
            {
              "data": [
                {
                  "id": "ZXHX",
                  "type": "airports",
                  "attributes": {
                    "name": "Heathrow Downtown Heliport",
                    "code": "ZXHX",
                    "type": "heliport",
                    "gps_code": "ZXHX",
                    "icao_code": "ZXHX",
                    "iata_code": null,
                    "local_code": null
                  }
                },
                {
                  "id": "EGLL",
                  "type": "airports",
                  "attributes": {
                    "name": "London Heathrow Airport",
                    "code": "EGLL",
                    "type": "large_airport",
                    "gps_code": "EGLL",
                    "icao_code": "EGLL",
                    "iata_code": "LHR",
                    "local_code": null
                  }
                }
              ]
            }
            """));
        var service = CreateService(airportFile.Path, handler);

        var result = await service.ResolveAirportAsync("Heathrow");

        Assert.Equal("EGLL", result);
    }

    private static AirportLookupService CreateService(string dataPath, HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://airportsapi.com/api/")
        };

        return new AirportLookupService(dataPath, new StubHttpClientFactory(client));
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
        private readonly HttpClient _client;

        public StubHttpClientFactory(HttpClient client)
        {
            _client = client;
        }

        public HttpClient CreateClient(string name)
        {
            return _client;
        }
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

    private sealed class TemporaryAirportDataFile : IDisposable
    {
        public TemporaryAirportDataFile(string json)
        {
            Path = System.IO.Path.Combine(AppContext.BaseDirectory, $"airports-{Guid.NewGuid():N}.json");
            File.WriteAllText(Path, json);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (File.Exists(Path))
            {
                File.Delete(Path);
            }
        }
    }
}
