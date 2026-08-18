namespace MetarViewer.Parsing;

/// <summary>
/// Classifies individual METAR tokens.
///
/// This logic previously existed as two separate copies, one in the raw METAR parser and
/// one in the aviationweather.gov service. The copies had drifted apart, which meant the
/// same report could be interpreted differently depending on which source served it.
/// </summary>
internal static class MetarTokenClassifier
{
    /// <summary>
    /// Two-letter codes for weather phenomena as defined by the METAR standard.
    /// </summary>
    private static readonly string[] WeatherIndicators =
    [
        "RA", "SN", "DZ", "FG", "BR", "HZ", "TS", "FZ", "SH",
        "SG", "PL", "GR", "GS", "UP", "DU", "SA", "VA", "FU",
        "PO", "SQ", "FC", "SS", "DS"
    ];

    /// <summary>
    /// Tokens that are structural parts of a report rather than weather phenomena.
    /// </summary>
    /// <remarks>
    /// Several of these would otherwise be misread as weather because they contain a
    /// weather indicator as a substring. "TEMPO" ends in "PO" (dust/sand whirls) and
    /// "BECMG" contains "CM"; both are trend groups, not observed weather.
    /// </remarks>
    private static readonly HashSet<string> NonWeatherTokens = new(StringComparer.Ordinal)
    {
        "METAR", "SPECI", "AUTO", "COR", "AMD", "RTD",
        "NOSIG", "TEMPO", "BECMG", "RMK", "CAVOK", "NSW"
    };

    /// <summary>
    /// Determines whether a token looks like a four-letter ICAO station identifier.
    /// </summary>
    public static bool LooksLikeStationIdentifier(string token)
    {
        return token.Length == 4 && token.All(char.IsLetter);
    }

    /// <summary>
    /// Determines whether a token represents an observed weather phenomenon.
    /// </summary>
    public static bool LooksLikeWeatherToken(string token)
    {
        if (token.Length < 2 || NonWeatherTokens.Contains(token))
        {
            return false;
        }

        // Strip the intensity prefix ("+" heavy, "-" light) and the vicinity prefix ("VC").
        var candidate = token.TrimStart('+', '-');
        if (candidate.StartsWith("VC", StringComparison.Ordinal))
        {
            candidate = candidate[2..];
        }

        return candidate.Length is >= 2 and <= 8 &&
               candidate.All(char.IsLetter) &&
               WeatherIndicators.Any(indicator => candidate.Contains(indicator, StringComparison.Ordinal));
    }

    /// <summary>
    /// Determines whether a token is a report-type prefix.
    /// </summary>
    public static bool IsReportTypePrefix(string token) => token is "METAR" or "SPECI";

    /// <summary>
    /// Determines whether a token is a report modifier such as "AUTO" or "COR".
    /// </summary>
    public static bool IsReportModifier(string token) => token is "AUTO" or "COR" or "AMD" or "RTD";
}
