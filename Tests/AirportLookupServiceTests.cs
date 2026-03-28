using System.Net;
using System.Text;
using MetarViewer.Services;
using Xunit;

namespace MetarViewer.Tests;

public class AirportLookupServiceTests
{
    [Fact]
    public async Task ResolveAirportDetailsAsync_ExactCodeUsesDirectApiLookup()
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal("https://airportsapi.com/api/airports/LHR", request.RequestUri?.ToString());
            return CreateJsonResponse(CreateSingleAirportResponse("EGLL", "London Heathrow Airport", "large_airport", "LHR"));
        });
        var service = CreateService(handler);

        var result = await service.ResolveAirportDetailsAsync("LHR");

        Assert.NotNull(result);
        Assert.Equal("EGLL", result!.StationId);
        Assert.Equal("London Heathrow Airport", result.DisplayName);
        Assert.Equal("LHR", result.IataCode);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAirportAsync_NameSearchQueriesApiAndReturnsIcao()
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            Assert.Equal(
                "https://airportsapi.com/api/airports?filter%5Bname%5D=Heathrow&sort=name&include=country%2Cregion&page%5Bsize%5D=20",
                request.RequestUri?.ToString());

            return CreateJsonResponse(CreateSearchResponse(
                CreateAirportResource("EGLL", "London Heathrow Airport", "large_airport", "LHR")));
        });
        var service = CreateService(handler);

        var result = await service.ResolveAirportAsync("Heathrow");

        Assert.Equal("EGLL", result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAirportAsync_DirectLookupNotFoundFallsBackToCodeSearch()
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.ToString() switch
            {
                "https://airportsapi.com/api/airports/LHR" => new HttpResponseMessage(HttpStatusCode.NotFound),
                "https://airportsapi.com/api/airports?filter%5Bcode%5D=LHR&sort=name&include=country%2Cregion&page%5Bsize%5D=20"
                    => CreateJsonResponse(CreateSearchResponse(
                        CreateAirportResource("EGLL", "London Heathrow Airport", "large_airport", "LHR"))),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected request URI: {request.RequestUri}")
            };
        });
        var service = CreateService(handler);

        var result = await service.ResolveAirportAsync("LHR");

        Assert.Equal("EGLL", result);
        Assert.Equal(2, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAirportDetailsAsync_TypoUsesRelaxedNameSearchAndReturnsClosestAirport()
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.ToString() switch
            {
                "https://airportsapi.com/api/airports?filter%5Bname%5D=Heatrow&sort=name&include=country%2Cregion&page%5Bsize%5D=20"
                    => CreateJsonResponse("""{ "data": [] }"""),
                "https://airportsapi.com/api/airports?filter%5Bname%5D=Heatr&sort=name&include=country%2Cregion&page%5Bsize%5D=50"
                    => CreateJsonResponse("""{ "data": [] }"""),
                "https://airportsapi.com/api/airports?filter%5Bname%5D=Heat&sort=name&include=country%2Cregion&page%5Bsize%5D=50"
                    => CreateJsonResponse(CreateSearchResponse(
                        CreateAirportResource("EGLL", "London Heathrow Airport", "large_airport", "LHR"),
                        CreateAirportResource("ZXHX", "Heathrow Downtown Heliport", "heliport", null))),
                "https://airportsapi.com/api/airports?filter%5Bname%5D=He&sort=name&include=country%2Cregion&page%5Bsize%5D=50"
                    => CreateJsonResponse(CreateSearchResponse(
                        CreateAirportResource("EGLL", "London Heathrow Airport", "large_airport", "LHR"))),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected request URI: {request.RequestUri}")
            };
        });
        var service = CreateService(handler);

        var result = await service.ResolveAirportDetailsAsync("Heatrow");

        Assert.NotNull(result);
        Assert.Equal("EGLL", result!.StationId);
        Assert.Equal("London Heathrow Airport", result.DisplayName);
    }

    [Fact]
    public async Task GetSuggestionsAsync_TypoReturnsClosestAirportInSuggestions()
    {
        using var handler = new StubHttpMessageHandler(request =>
        {
            return request.RequestUri?.ToString() switch
            {
                "https://airportsapi.com/api/airports?filter%5Bname%5D=Heatrow&sort=name&include=country%2Cregion&page%5Bsize%5D=20"
                    => CreateJsonResponse("""{ "data": [] }"""),
                "https://airportsapi.com/api/airports?filter%5Bname%5D=Heatr&sort=name&include=country%2Cregion&page%5Bsize%5D=50"
                    => CreateJsonResponse("""{ "data": [] }"""),
                "https://airportsapi.com/api/airports?filter%5Bname%5D=Heat&sort=name&include=country%2Cregion&page%5Bsize%5D=50"
                    => CreateJsonResponse(CreateSearchResponse(
                        CreateAirportResource("EGLL", "London Heathrow Airport", "large_airport", "LHR"),
                        CreateAirportResource("EGHE", "Heathrow Executive Airfield", "small_airport", null))),
                "https://airportsapi.com/api/airports?filter%5Bname%5D=He&sort=name&include=country%2Cregion&page%5Bsize%5D=50"
                    => CreateJsonResponse(CreateSearchResponse(
                        CreateAirportResource("EGLL", "London Heathrow Airport", "large_airport", "LHR"))),
                _ => throw new Xunit.Sdk.XunitException($"Unexpected request URI: {request.RequestUri}")
            };
        });
        var service = CreateService(handler);

        var suggestions = await service.GetSuggestionsAsync("Heatrow");

        Assert.NotEmpty(suggestions);
        Assert.Equal("EGLL", suggestions[0].StationId);
        Assert.Equal("London Heathrow Airport", suggestions[0].DisplayName);
        Assert.Equal("EGLL - London Heathrow Airport", suggestions[0].DisplayText);
        Assert.Equal("EGLL - London Heathrow Airport", suggestions[0].ToString());
    }

    [Fact]
    public async Task ResolveAirportAsync_HttpFailureFallsBackToHeuristic()
    {
        using var handler = new StubHttpMessageHandler(_ => throw new HttpRequestException("boom"));
        var service = CreateService(handler);

        var result = await service.ResolveAirportAsync("egll");

        Assert.Equal("EGLL", result);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAirportAsync_CachesApiResultsByNormalizedInput()
    {
        using var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(CreateSearchResponse(
            CreateAirportResource("EGLL", "London Heathrow Airport", "large_airport", "LHR"))));
        var service = CreateService(handler);

        var firstResult = await service.ResolveAirportAsync("Heathrow");
        var secondResult = await service.ResolveAirportAsync("heathrow");

        Assert.Equal("EGLL", firstResult);
        Assert.Equal("EGLL", secondResult);
        Assert.Equal(1, handler.CallCount);
    }

    [Fact]
    public async Task ResolveAirportAsync_ApiPrefersOperationalAirportType()
    {
        using var handler = new StubHttpMessageHandler(_ => CreateJsonResponse(CreateSearchResponse(
            CreateAirportResource("ZXHX", "Heathrow Downtown Heliport", "heliport", null),
            CreateAirportResource("EGLL", "London Heathrow Airport", "large_airport", "LHR"))));
        var service = CreateService(handler);

        var result = await service.ResolveAirportAsync("Heathrow");

        Assert.Equal("EGLL", result);
    }

    private static AirportLookupService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://airportsapi.com/api/")
        };

        return new AirportLookupService(new StubHttpClientFactory(client));
    }

    private static HttpResponseMessage CreateJsonResponse(string json)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static string CreateSingleAirportResponse(string stationId, string name, string airportType, string? iataCode)
    {
        return $$"""
            {
              "data": {{CreateAirportResource(stationId, name, airportType, iataCode)}}
            }
            """;
    }

    private static string CreateSearchResponse(params string[] airportResources)
    {
        return $$"""
            {
              "data": [
                {{string.Join(",\n        ", airportResources)}}
              ]
            }
            """;
    }

    private static string CreateAirportResource(string stationId, string name, string airportType, string? iataCode)
    {
        var serializedIataCode = iataCode == null ? "null" : $"\"{iataCode}\"";

        return $$"""
            {
              "id": "{{stationId}}",
              "type": "airports",
              "attributes": {
                "name": "{{name}}",
                "code": "{{stationId}}",
                "type": "{{airportType}}",
                "gps_code": "{{stationId}}",
                "icao_code": "{{stationId}}",
                "iata_code": {{serializedIataCode}},
                "local_code": null
              }
            }
            """;
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
}
