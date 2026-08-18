namespace MetarViewer.Parsing;

/// <summary>
/// Canonical pressure unit names used when reporting an altimeter setting.
///
/// These were previously bare string literals repeated at every site that produced an
/// altimeter reading, so a change of spelling had to be found by hand.
/// </summary>
internal static class PressureUnits
{
    /// <summary>Hectopascals, reported in a METAR as a "Q" group (e.g. "Q1026").</summary>
    public const string Hectopascals = "hPa";

    /// <summary>Inches of mercury, reported in a METAR as an "A" group (e.g. "A3045").</summary>
    public const string InchesOfMercury = "inHg";

    /// <summary>
    /// The conversion factor between the two units. This was previously written as a bare
    /// "33.8639" at each place a pressure was converted.
    /// </summary>
    private const decimal HectopascalsPerInchOfMercury = 33.8639m;

    /// <summary>Converts a pressure in hectopascals to inches of mercury.</summary>
    public static decimal HectopascalsToInchesOfMercury(decimal hectopascals) =>
        hectopascals / HectopascalsPerInchOfMercury;

    /// <summary>Converts a pressure in inches of mercury to hectopascals.</summary>
    public static decimal InchesOfMercuryToHectopascals(decimal inchesOfMercury) =>
        inchesOfMercury * HectopascalsPerInchOfMercury;
}
