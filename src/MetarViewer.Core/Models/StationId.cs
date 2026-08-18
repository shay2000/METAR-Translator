namespace MetarViewer.Models;

/// <summary>
/// Helpers for handling ICAO station identifiers.
///
/// Each service previously normalised identifiers itself, sometimes inline and sometimes
/// through a private method, so a lookup could succeed against one source and miss against
/// another purely because of casing or stray whitespace.
/// </summary>
public static class StationId
{
    /// <summary>
    /// Normalises a station identifier to the canonical form used for lookups and cache keys:
    /// trimmed and upper case.
    /// </summary>
    public static string Normalize(string? stationId) =>
        stationId?.Trim().ToUpperInvariant() ?? string.Empty;
}
