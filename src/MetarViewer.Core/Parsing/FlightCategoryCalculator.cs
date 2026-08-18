using MetarViewer.Models;

namespace MetarViewer.Parsing;

/// <summary>
/// Derives the flight category (VFR, MVFR, IFR, LIFR) from visibility and ceiling.
/// </summary>
internal static class FlightCategoryCalculator
{
    /// <summary>Cloud coverages that constitute a ceiling.</summary>
    private static readonly string[] CeilingCoverages = ["BKN", "OVC", "VV"];

    // Thresholds are expressed in statute miles and feet above ground level.
    private const decimal LowIfrVisibilityMiles = 1m;
    private const decimal IfrVisibilityMiles = 3m;
    private const decimal MarginalVfrVisibilityMiles = 5m;
    private const int LowIfrCeilingFeet = 500;
    private const int IfrCeilingFeet = 1000;
    private const int MarginalVfrCeilingFeet = 3000;

    /// <summary>
    /// Determines the flight category for a report, or null when neither visibility nor
    /// ceiling information is available.
    /// </summary>
    public static string? Determine(MetarData metar)
    {
        if (metar.IsCavok)
        {
            return FlightCategories.Vfr;
        }

        var visibilityMiles = VisibilityUnits.ToStatuteMiles(metar.Visibility, metar.VisibilityUnit);
        var ceilingFeet = GetCeilingFeet(metar);

        if ((visibilityMiles.HasValue && visibilityMiles.Value < LowIfrVisibilityMiles) || ceilingFeet < LowIfrCeilingFeet)
        {
            return FlightCategories.LowIfr;
        }

        if ((visibilityMiles.HasValue && visibilityMiles.Value < IfrVisibilityMiles) || ceilingFeet < IfrCeilingFeet)
        {
            return FlightCategories.Ifr;
        }

        if ((visibilityMiles.HasValue && visibilityMiles.Value <= MarginalVfrVisibilityMiles) || ceilingFeet <= MarginalVfrCeilingFeet)
        {
            return FlightCategories.MarginalVfr;
        }

        // With no visibility reading and no ceiling there is nothing to base a category on.
        return visibilityMiles.HasValue || ceilingFeet != int.MaxValue
            ? FlightCategories.Vfr
            : null;
    }

    /// <summary>
    /// Returns the ceiling in feet, being the lowest broken, overcast or vertical
    /// visibility layer. Returns <see cref="int.MaxValue"/> when there is no ceiling.
    /// </summary>
    private static int GetCeilingFeet(MetarData metar)
    {
        return metar.CloudLayers
            .Where(layer => layer.Altitude.HasValue &&
                            CeilingCoverages.Contains(layer.Coverage, StringComparer.OrdinalIgnoreCase))
            .Select(layer => layer.Altitude!.Value)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
    }
}
