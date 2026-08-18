namespace MetarViewer.Airports;

/// <summary>
/// Rules about airport and station codes: which strings look like codes, and which code an
/// airport should be identified by.
/// </summary>
internal static class AirportCodes
{
    /// <summary>
    /// Determines whether input looks like an airport code (a 3-letter IATA code or a
    /// 4-character ICAO code) rather than the name of an airport.
    /// </summary>
    public static bool LooksLikeAirportCode(string normalizedInput) =>
        normalizedInput.Length is >= 3 and <= 4 && normalizedInput.All(char.IsLetterOrDigit);

    /// <summary>
    /// Determines whether input is a well-formed ICAO station identifier. Weather is only
    /// reported against these, so a 3-letter IATA code cannot be used to fetch a METAR.
    /// </summary>
    public static bool LooksLikeStationIdentifier(string normalizedInput) =>
        normalizedInput.Length == 4 && normalizedInput.All(char.IsLetter);

    /// <summary>
    /// Determines whether input is worth trying as a station identifier when the airport
    /// database cannot be reached.
    /// </summary>
    /// <remarks>
    /// Deliberately more forgiving than <see cref="LooksLikeStationIdentifier"/>: with no
    /// database to check against, asking the weather sources costs one request and answers the
    /// question definitively, so a plausible identifier is worth trying.
    /// </remarks>
    public static bool CouldBeStationIdentifier(string normalizedInput) =>
        normalizedInput.Length is >= 3 and <= 4 && normalizedInput.All(char.IsLetter);

    /// <summary>
    /// Picks the station identifier to report weather against, preferring the ICAO code and
    /// falling back to the other codes the airport publishes.
    /// </summary>
    public static string? GetStationIdentifier(AirportAttributes? attributes)
    {
        if (attributes == null)
        {
            return null;
        }

        return AsStationIdentifier(attributes.IcaoCode)
            ?? AsStationIdentifier(attributes.GpsCode)
            ?? AsStationIdentifier(attributes.Code);
    }

    /// <summary>
    /// Returns the candidate as a normalised station identifier, or null if it is not one.
    /// </summary>
    public static string? AsStationIdentifier(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return null;
        }

        var normalized = candidate.Trim().ToUpperInvariant();
        return LooksLikeStationIdentifier(normalized) ? normalized : null;
    }

    /// <summary>
    /// Determines whether an airport can currently be flown to. Closed airports are excluded
    /// from results because they do not report weather.
    /// </summary>
    public static bool IsSupportedAirportType(string? airportType) =>
        !string.Equals(airportType, "closed", StringComparison.OrdinalIgnoreCase);
}
