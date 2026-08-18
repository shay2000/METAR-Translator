namespace MetarViewer.Models;

/// <summary>
/// The flight category codes used throughout the application.
/// </summary>
/// <remarks>
/// These values are part of the model contract: the flight category is surfaced directly
/// in the user interface and is also supplied verbatim by the aviationweather.gov API.
/// </remarks>
public static class FlightCategories
{
    /// <summary>Visual flight rules.</summary>
    public const string Vfr = "VFR";

    /// <summary>Marginal visual flight rules.</summary>
    public const string MarginalVfr = "MVFR";

    /// <summary>Instrument flight rules.</summary>
    public const string Ifr = "IFR";

    /// <summary>Low instrument flight rules.</summary>
    public const string LowIfr = "LIFR";
}
