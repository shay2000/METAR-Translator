using System.Net;
using System.Text.Json;

namespace MetarViewer.Airports;

/// <summary>
/// Retrieves airports from airportsapi.com.
/// </summary>
/// <remarks>
/// Separating the transport from the matching lets the search order be tested against a stub that
/// returns airports directly, instead of against hand-written JSON and expected URLs.
/// </remarks>
internal interface IAirportsApiClient
{
    /// <summary>
    /// Looks up the airport published under an exact code, or null if there is no such airport.
    /// </summary>
    Task<AirportAttributes?> GetAirportByCodeAsync(string code, CancellationToken cancellationToken);

    /// <summary>
    /// Returns the airports matching a search, or an empty list if there are none.
    /// </summary>
    Task<IReadOnlyList<AirportAttributes>> SearchAsync(AirportSearchQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// The airportsapi.com implementation of <see cref="IAirportsApiClient"/>.
///
/// The URL building, JSON options and response envelopes used to sit among the matching logic in
/// the lookup service, which meant everything about talking to this one API was mixed in with
/// rules that have nothing to do with it.
/// </summary>
internal sealed class AirportsApiClient : IAirportsApiClient
{
    /// <summary>The name of the configured <see cref="HttpClient"/> for this API.</summary>
    public const string HttpClientName = "AirportsApi";

    /// <summary>The root of the API. All request paths are relative to this.</summary>
    public static readonly Uri BaseUri = new("https://airportsapi.com/api/");

    // The API uses snake_case names, which the DTOs map with attributes; Web defaults supply the
    // remaining conventions such as case-insensitive matching.
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string AirportsPath = "airports";
    private const string PageSizeParameter = "page[size]";

    // Results are asked for by name so that repeated searches are stable, and the country and
    // region are included because the API omits them otherwise.
    private static readonly (string Key, string Value) SortByName = ("sort", "name");
    private static readonly (string Key, string Value) IncludeLocation = ("include", "country,region");

    private readonly IHttpClientFactory _httpClientFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="AirportsApiClient"/> class.
    /// </summary>
    /// <param name="httpClientFactory">Supplies the client configured for <see cref="BaseUri"/>.</param>
    public AirportsApiClient(IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);

        _httpClientFactory = httpClientFactory;
    }

    /// <inheritdoc />
    public async Task<AirportAttributes?> GetAirportByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        using var response = await client
            .GetAsync($"{AirportsPath}/{Uri.EscapeDataString(code)}", cancellationToken)
            .ConfigureAwait(false);

        // A code the API has never heard of is an ordinary outcome of a search, not a failure.
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
        {
            return null;
        }

        // Rate limiting and server failures must stop the wider search. Treating them as an
        // ordinary miss would make the candidate finder issue every fallback request against an
        // already failing API.
        response.EnsureSuccessStatusCode();

        var payload = await ReadAsync<SingleAirportResponse>(response, cancellationToken).ConfigureAwait(false);
        return payload?.Data?.Attributes;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AirportAttributes>> SearchAsync(AirportSearchQuery query, CancellationToken cancellationToken)
    {
        var client = CreateClient();
        using var response = await client
            .GetAsync($"{AirportsPath}?{BuildQueryString(query)}", cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
        {
            return Array.Empty<AirportAttributes>();
        }

        response.EnsureSuccessStatusCode();

        var payload = await ReadAsync<AirportSearchResponse>(response, cancellationToken).ConfigureAwait(false);
        if (payload?.Data is not { Count: > 0 } airports)
        {
            return Array.Empty<AirportAttributes>();
        }

        // An entry without attributes carries no code or name, so there is nothing to match on.
        return airports
            .Select(airport => airport.Attributes)
            .OfType<AirportAttributes>()
            .ToList();
    }

    private static string BuildQueryString(AirportSearchQuery query)
    {
        var parameters = new List<string>(4);

        Append(parameters, query.FilterKey, query.Value);
        Append(parameters, SortByName.Key, SortByName.Value);
        Append(parameters, IncludeLocation.Key, IncludeLocation.Value);
        parameters.Add($"{Uri.EscapeDataString(PageSizeParameter)}={query.PageSize}");

        return string.Join("&", parameters);

        // A filter with no value would ask the API to match everything, so it is left off.
        static void Append(ICollection<string> parameters, string key, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parameters.Add($"{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}");
            }
        }
    }

    private static async Task<TPayload?> ReadAsync<TPayload>(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return await JsonSerializer.DeserializeAsync<TPayload>(stream, JsonOptions, cancellationToken).ConfigureAwait(false);
    }

    // Resolved per request so that the factory can retire handlers on its normal schedule, which
    // is what allows a long-lived lookup service to still notice DNS changes.
    private HttpClient CreateClient() => _httpClientFactory.CreateClient(HttpClientName);
}
