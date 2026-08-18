namespace MetarViewer.Services;

/// <summary>
/// Represents a resolved airport with its station ID, display name, and IATA code.
/// </summary>
/// <param name="StationId">The ICAO station identifier to request weather for.</param>
/// <param name="DisplayName">The airport's name, if the source provided one.</param>
/// <param name="IataCode">The airport's IATA code, if it publishes one.</param>
public sealed record ResolvedAirport(string StationId, string? DisplayName, string? IataCode);

/// <summary>
/// Represents a suggestion for an airport search.
/// </summary>
/// <param name="StationId">The ICAO station identifier to request weather for.</param>
/// <param name="DisplayName">The airport's name.</param>
/// <param name="IataCode">The airport's IATA code, if it publishes one.</param>
public sealed record AirportSuggestion(string StationId, string DisplayName, string? IataCode)
{
    /// <summary>
    /// Gets the text to display in a list (e.g., "KLAX - Los Angeles Intl").
    /// </summary>
    public string DisplayText =>
        string.IsNullOrWhiteSpace(DisplayName)
            ? StationId
            : $"{StationId} - {DisplayName}";

    /// <summary>
    /// Returns <see cref="DisplayText"/> so that a suggestion shown without an explicit template
    /// still reads as the airport rather than as the record's field list.
    /// </summary>
    public override string ToString() => DisplayText;
}

/// <summary>
/// Interface for a service that looks up airports by various identifiers or names.
/// </summary>
public interface IAirportLookupService
{
    /// <summary>
    /// Resolves a search string to a single 4-character ICAO station ID.
    /// </summary>
    /// <param name="input">An airport code or name, in any case.</param>
    /// <param name="cancellationToken">Abandons the lookup.</param>
    Task<string?> ResolveAirportAsync(string input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves a search string into detailed airport information.
    /// </summary>
    /// <param name="input">An airport code or name, in any case.</param>
    /// <param name="cancellationToken">Abandons the lookup.</param>
    Task<ResolvedAirport?> ResolveAirportDetailsAsync(string input, CancellationToken cancellationToken = default);

    /// <summary>
    /// Provides a list of airport suggestions based on a partial search string.
    /// </summary>
    /// <param name="input">A partial airport code or name, in any case.</param>
    /// <param name="cancellationToken">Abandons the search.</param>
    Task<IReadOnlyList<AirportSuggestion>> GetSuggestionsAsync(string input, CancellationToken cancellationToken = default);
}
