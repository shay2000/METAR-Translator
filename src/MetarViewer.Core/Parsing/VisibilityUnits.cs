using System.Globalization;

namespace MetarViewer.Parsing;

/// <summary>
/// Canonical visibility unit names and the conversions between them.
///
/// Visibility was previously represented by loose strings ("SM", "km", "m") compared with
/// ad-hoc casing rules, and the conversion factors were repeated at each call site.
/// </summary>
internal static class VisibilityUnits
{
    public const string StatuteMiles = "SM";
    public const string Kilometres = "km";
    public const string Metres = "m";

    private const decimal StatuteMilesPerKilometre = 0.621371m;
    private const decimal MetresPerStatuteMile = 1609.344m;

    /// <summary>
    /// Converts a visibility reading to statute miles, which is the unit the flight
    /// category thresholds are defined in. Returns null when the unit is unrecognised.
    /// </summary>
    public static decimal? ToStatuteMiles(decimal? visibility, string? unit)
    {
        if (!visibility.HasValue)
        {
            return null;
        }

        return unit?.ToUpperInvariant() switch
        {
            "SM" => visibility.Value,
            "KM" => visibility.Value * StatuteMilesPerKilometre,
            "M" => visibility.Value / MetresPerStatuteMile,
            _ => null
        };
    }

    /// <summary>
    /// Parses a visibility value that may be a whole number, a decimal, a fraction
    /// ("1/2"), or a mixed fraction ("1 1/2").
    /// </summary>
    public static decimal? ParseDistance(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        // A trailing "+" means "or greater"; the underlying number is what matters.
        var normalized = value.Trim().TrimEnd('+');

        if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var wholeValue))
        {
            return wholeValue;
        }

        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 2 &&
            decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var wholeMiles))
        {
            var fraction = ParseFraction(parts[1]);
            if (fraction.HasValue)
            {
                return wholeMiles + fraction.Value;
            }
        }

        return ParseFraction(normalized);
    }

    /// <summary>
    /// Parses a fraction such as "1/2". Returns null when the value is not a valid fraction.
    /// </summary>
    public static decimal? ParseFraction(string value)
    {
        var parts = value.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 ||
            !decimal.TryParse(parts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var numerator) ||
            !decimal.TryParse(parts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var denominator) ||
            denominator == 0)
        {
            return null;
        }

        return numerator / denominator;
    }
}
