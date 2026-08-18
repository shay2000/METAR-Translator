using System.Globalization;
using System.Text.RegularExpressions;
using MetarViewer.Models;

namespace MetarViewer.Parsing;

/// <summary>
/// Parses the wind group, for example "27015G25KT" or "VRB05KT".
/// </summary>
internal sealed partial class WindTokenParser : IMetarTokenParser
{
    public bool TryParse(MetarTokenContext context, MetarData metar)
    {
        var match = WindRegex().Match(context.CurrentToken);
        if (!match.Success)
        {
            return false;
        }

        // A variable direction ("VRB") has no single numeric bearing, so it is left unset.
        var direction = match.Groups["direction"].Value;
        if (direction != "VRB" && int.TryParse(direction, out var degrees))
        {
            metar.WindDirection = degrees;
        }

        if (int.TryParse(match.Groups["speed"].Value, out var speed))
        {
            metar.WindSpeed = speed;
        }

        if (int.TryParse(match.Groups["gust"].Value, out var gust))
        {
            metar.WindGust = gust;
        }

        return true;
    }

    [GeneratedRegex("^(?<direction>\\d{3}|VRB)(?<speed>\\d{2,3})(G(?<gust>\\d{2,3}))?KT$")]
    private static partial Regex WindRegex();
}

/// <summary>
/// Parses the visibility group in metres, kilometres or statute miles.
/// </summary>
internal sealed partial class VisibilityTokenParser : IMetarTokenParser
{
    /// <summary>Visibility of 10 km or more is reported as "9999".</summary>
    private const string UnlimitedMetreVisibility = "9999";
    private const decimal UnlimitedVisibilityKilometres = 10m;

    public bool TryParse(MetarTokenContext context, MetarData metar)
    {
        var token = context.CurrentToken;

        if (token == UnlimitedMetreVisibility)
        {
            metar.Visibility = UnlimitedVisibilityKilometres;
            metar.VisibilityUnit = VisibilityUnits.Kilometres;
            return true;
        }

        if (MetreVisibilityRegex().IsMatch(token) &&
            int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var metres))
        {
            metar.Visibility = metres;
            metar.VisibilityUnit = VisibilityUnits.Metres;
            return true;
        }

        if (TryParseStatuteMiles(token, out var statuteMiles))
        {
            metar.Visibility = statuteMiles;
            metar.VisibilityUnit = VisibilityUnits.StatuteMiles;
            return true;
        }

        // A mixed fraction such as "1 1/2SM" arrives as two separate tokens.
        if (context.NextToken is { } nextToken &&
            decimal.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wholeMiles) &&
            TryParseStatuteMiles(nextToken, out var fractionalMiles))
        {
            metar.Visibility = wholeMiles + fractionalMiles;
            metar.VisibilityUnit = VisibilityUnits.StatuteMiles;
            context.ConsumeAdditionalToken();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parses a statute mile token such as "10SM", "1/2SM" or "P6SM".
    /// </summary>
    private static bool TryParseStatuteMiles(string token, out decimal visibility)
    {
        visibility = 0m;
        if (!token.EndsWith(VisibilityUnits.StatuteMiles, StringComparison.Ordinal))
        {
            return false;
        }

        // "P" means "or more" and "M" means "or less"; neither affects the numeric value.
        var numericPortion = token[..^2].TrimStart('P', 'M');

        var parsed = VisibilityUnits.ParseDistance(numericPortion);
        if (!parsed.HasValue)
        {
            return false;
        }

        visibility = parsed.Value;
        return true;
    }

    [GeneratedRegex("^\\d{4}$")]
    private static partial Regex MetreVisibilityRegex();
}

/// <summary>
/// Parses a cloud group, for example "SCT025" or "BKN030CB".
/// </summary>
internal sealed partial class CloudTokenParser : IMetarTokenParser
{
    /// <summary>Cloud bases are reported in hundreds of feet.</summary>
    private const int FeetPerAltitudeUnit = 100;

    public bool TryParse(MetarTokenContext context, MetarData metar)
    {
        var match = CloudRegex().Match(context.CurrentToken);
        if (!match.Success)
        {
            return false;
        }

        var altitude = int.TryParse(match.Groups["altitude"].Value, out var hundredsOfFeet)
            ? hundredsOfFeet * FeetPerAltitudeUnit
            : (int?)null;

        var type = match.Groups["type"].Value;

        metar.CloudLayers.Add(new CloudLayer
        {
            Coverage = match.Groups["coverage"].Value,
            Altitude = altitude,
            Type = string.IsNullOrWhiteSpace(type) ? null : type
        });

        return true;
    }

    [GeneratedRegex("^(?<coverage>FEW|SCT|BKN|OVC|VV|NSC|SKC|CLR|NCD)(?<altitude>\\d{3})?(?<type>CB|TCU)?$")]
    private static partial Regex CloudRegex();
}

/// <summary>
/// Parses the temperature and dew point group, for example "15/10" or "M02/M05".
/// </summary>
internal sealed partial class TemperatureTokenParser : IMetarTokenParser
{
    /// <summary>A dew point of "//" means the value was not measured.</summary>
    private const string MissingValue = "//";

    public bool TryParse(MetarTokenContext context, MetarData metar)
    {
        var match = TemperatureRegex().Match(context.CurrentToken);
        if (!match.Success)
        {
            return false;
        }

        metar.Temperature = ParseSignedTemperature(match.Groups["temperature"].Value);

        var dewPoint = match.Groups["dewPoint"].Value;
        if (dewPoint != MissingValue)
        {
            metar.DewPoint = ParseSignedTemperature(dewPoint);
        }

        return true;
    }

    /// <summary>
    /// Parses a temperature where a leading "M" indicates a negative value.
    /// </summary>
    private static int ParseSignedTemperature(string token)
    {
        return token.StartsWith('M')
            ? -int.Parse(token[1..], CultureInfo.InvariantCulture)
            : int.Parse(token, CultureInfo.InvariantCulture);
    }

    [GeneratedRegex("^(?<temperature>M?\\d{2})/(?<dewPoint>M?\\d{2}|//)$")]
    private static partial Regex TemperatureRegex();
}

/// <summary>
/// Parses the pressure group, either a European QNH ("Q1013") or a US altimeter setting
/// ("A2992").
/// </summary>
internal sealed partial class AltimeterTokenParser : IMetarTokenParser
{
    /// <summary>US altimeter settings omit the decimal point: "A2992" means 29.92 inHg.</summary>
    private const decimal InchesOfMercuryScale = 100m;

    public bool TryParse(MetarTokenContext context, MetarData metar)
    {
        var token = context.CurrentToken;

        var qnhMatch = QnhRegex().Match(token);
        if (qnhMatch.Success &&
            decimal.TryParse(qnhMatch.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var hectopascals))
        {
            metar.Altimeter = hectopascals;
            metar.AltimeterUnit = PressureUnits.Hectopascals;
            return true;
        }

        var inchesMatch = AltimeterRegex().Match(token);
        if (inchesMatch.Success &&
            decimal.TryParse(inchesMatch.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var inchesValue))
        {
            metar.Altimeter = inchesValue / InchesOfMercuryScale;
            metar.AltimeterUnit = PressureUnits.InchesOfMercury;
            return true;
        }

        return false;
    }

    [GeneratedRegex("^Q(?<value>\\d{4})$")]
    private static partial Regex QnhRegex();

    [GeneratedRegex("^A(?<value>\\d{4})$")]
    private static partial Regex AltimeterRegex();
}
