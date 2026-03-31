using System.Globalization;
using System.Text.RegularExpressions;
using MetarViewer.Models;

namespace MetarViewer.Services;

/// <summary>
/// A parser for raw METAR strings that uses regex and tokenization to extract weather data.
/// </summary>
internal static partial class RawMetarParser
{
    private static readonly string[] WeatherIndicators =
    [
        "RA", "SN", "DZ", "FG", "BR", "HZ", "TS", "FZ", "SH",
        "SG", "PL", "GR", "GS", "UP", "DU", "SA", "VA", "FU",
        "PO", "SQ", "FC", "SS", "DS"
    ];

    /// <summary>
    /// Parses a raw METAR string into a <see cref="MetarData"/> object.
    /// </summary>
    /// <param name="rawMetar">The raw METAR string to parse.</param>
    /// <param name="stationId">The expected ICAO station identifier.</param>
    /// <returns>A populated MetarData object.</returns>
    public static MetarData Parse(string rawMetar, string stationId)
    {
        var normalizedStationId = stationId.Trim().ToUpperInvariant();
        var normalizedRawMetar = NormalizeRawMetar(rawMetar, normalizedStationId);

        var metar = new MetarData
        {
            StationId = normalizedStationId,
            RawMetar = normalizedRawMetar
        };

        var tokens = normalizedRawMetar
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(token => token.Trim())
            .Where(token => token.Length > 0)
            .ToArray();

        if (tokens.Length == 0)
        {
            return metar;
        }

        var index = 0;

        // Skip prefix if present
        if (tokens[index] is "METAR" or "SPECI")
        {
            index++;
        }

        // Identify station ID
        if (index < tokens.Length && LooksLikeStationIdentifier(tokens[index]))
        {
            metar.StationId = tokens[index];
            index++;
        }

        // Identify observation time (DDHHMMZ)
        if (index < tokens.Length && TryParseObservationTime(tokens[index], out var observationTime))
        {
            metar.ObservationTime = observationTime;
            index++;
        }

        // Skip modifier tokens
        while (index < tokens.Length && tokens[index] is "AUTO" or "COR" or "AMD" or "RTD")
        {
            index++;
        }

        // Parse remaining tokens until RMK (Remarks)
        for (var tokenIndex = index; tokenIndex < tokens.Length; tokenIndex++)
        {
            var token = tokens[tokenIndex].ToUpperInvariant();

            if (token == "RMK")
            {
                break;
            }

            if (token == "CAVOK")
            {
                metar.IsCavok = true;
                metar.Visibility = 10m;
                metar.VisibilityUnit = "km";
                continue;
            }

            // Try each parser in sequence
            if (TryParseWind(token, metar) ||
                TryParseVisibility(tokens, ref tokenIndex, metar) ||
                TryParseCloud(token, metar) ||
                TryParseTemperatureAndDewPoint(token, metar) ||
                TryParseAltimeter(token, metar))
            {
                continue;
            }

            // If none matched, check if it's a weather phenomenon
            if (LooksLikeWeatherToken(token))
            {
                metar.WeatherPhenomena.Add(token);
            }
        }

        metar.FlightCategory = DetermineFlightCategory(metar);
        return metar;
    }

    /// <summary>
    /// Ensures the METAR string has a standard "METAR [ICAO]" prefix for easier parsing.
    /// </summary>
    private static string NormalizeRawMetar(string rawMetar, string stationId)
    {
        var trimmed = rawMetar.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return $"METAR {stationId}";
        }

        var upperTrimmed = trimmed.ToUpperInvariant();
        if (upperTrimmed.StartsWith("METAR ", StringComparison.Ordinal) ||
            upperTrimmed.StartsWith("SPECI ", StringComparison.Ordinal))
        {
            return upperTrimmed;
        }

        var firstToken = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.Equals(firstToken, stationId, StringComparison.OrdinalIgnoreCase)
            ? $"METAR {trimmed.ToUpperInvariant()}"
            : $"METAR {stationId} {trimmed.ToUpperInvariant()}";
    }

    private static bool LooksLikeStationIdentifier(string candidate)
    {
        return candidate.Length == 4 && candidate.All(char.IsLetter);
    }

    /// <summary>
    /// Parses the observation time string (e.g., "151230Z") into a UTC DateTime.
    /// Infers the month and year based on the current date.
    /// </summary>
    private static bool TryParseObservationTime(string token, out DateTime observationTime)
    {
        var match = ObservationTimeRegex().Match(token);
        if (!match.Success ||
            !int.TryParse(match.Groups["day"].Value, out var day) ||
            !int.TryParse(match.Groups["hour"].Value, out var hour) ||
            !int.TryParse(match.Groups["minute"].Value, out var minute))
        {
            observationTime = default;
            return false;
        }

        var now = DateTime.UtcNow;
        var candidates = new List<DateTime>();

        // Check current, previous, and next month to find the closest match to 'now'
        foreach (var monthOffset in new[] { -1, 0, 1 })
        {
            var candidateMonth = now.AddMonths(monthOffset);
            if (day > DateTime.DaysInMonth(candidateMonth.Year, candidateMonth.Month))
            {
                continue;
            }

            candidates.Add(new DateTime(candidateMonth.Year, candidateMonth.Month, day, hour, minute, 0, DateTimeKind.Utc));
        }

        if (candidates.Count == 0)
        {
            observationTime = default;
            return false;
        }

        observationTime = candidates
            .OrderBy(candidate => Math.Abs((candidate - now).TotalDays))
            .First();

        return true;
    }

    /// <summary>
    /// Parses wind information (e.g., "27015G25KT" or "VRB05KT").
    /// </summary>
    private static bool TryParseWind(string token, MetarData metar)
    {
        var match = WindRegex().Match(token);
        if (!match.Success)
        {
            return false;
        }

        if (match.Groups["direction"].Value != "VRB" &&
            int.TryParse(match.Groups["direction"].Value, out var direction))
        {
            metar.WindDirection = direction;
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

    /// <summary>
    /// Parses visibility. Handles meters, kilometers, and statute miles (including fractions like "1 1/4SM").
    /// </summary>
    private static bool TryParseVisibility(string[] tokens, ref int tokenIndex, MetarData metar)
    {
        var token = tokens[tokenIndex].ToUpperInvariant();

        // Standard European 9999 (10km or more)
        if (token == "9999")
        {
            metar.Visibility = 10m;
            metar.VisibilityUnit = "km";
            return true;
        }

        // Generic meter visibility (e.g., "5000")
        if (MeterVisibilityRegex().IsMatch(token) &&
            int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var visibilityMeters))
        {
            metar.Visibility = visibilityMeters;
            metar.VisibilityUnit = "m";
            return true;
        }

        // Statute miles (e.g., "1/2SM" or "10SM")
        if (TryParseSmVisibilityToken(token, out var singleTokenVisibility))
        {
            metar.Visibility = singleTokenVisibility;
            metar.VisibilityUnit = "SM";
            return true;
        }

        // Lookahead for mixed fractions (e.g., "1 1/4SM")
        if (tokenIndex + 1 < tokens.Length &&
            decimal.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var wholeMiles) &&
            TryParseSmVisibilityToken(tokens[tokenIndex + 1].ToUpperInvariant(), out var fractionalMiles))
        {
            metar.Visibility = wholeMiles + fractionalMiles;
            metar.VisibilityUnit = "SM";
            tokenIndex++;
            return true;
        }

        return false;
    }

    private static bool TryParseSmVisibilityToken(string token, out decimal visibility)
    {
        visibility = 0m;
        if (!token.EndsWith("SM", StringComparison.Ordinal))
        {
            return false;
        }

        var numericPortion = token[..^2].TrimStart('P', 'M');

        if (decimal.TryParse(numericPortion, NumberStyles.Number, CultureInfo.InvariantCulture, out var wholeMiles))
        {
            visibility = wholeMiles;
            return true;
        }

        var fractionParts = numericPortion.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (fractionParts.Length != 2 ||
            !decimal.TryParse(fractionParts[0], NumberStyles.Number, CultureInfo.InvariantCulture, out var numerator) ||
            !decimal.TryParse(fractionParts[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var denominator) ||
            denominator == 0)
        {
            return false;
        }

        visibility = numerator / denominator;
        return true;
    }

    /// <summary>
    /// Parses cloud layers (e.g., "SCT025", "BKN030CB").
    /// </summary>
    private static bool TryParseCloud(string token, MetarData metar)
    {
        var match = CloudRegex().Match(token);
        if (!match.Success)
        {
            return false;
        }

        var coverage = match.Groups["coverage"].Value;
        var altitude = int.TryParse(match.Groups["altitude"].Value, out var hundredsOfFeet)
            ? hundredsOfFeet * 100
            : (int?)null;

        metar.CloudLayers.Add(new CloudLayer
        {
            Coverage = coverage,
            Altitude = altitude,
            Type = string.IsNullOrWhiteSpace(match.Groups["type"].Value) ? null : match.Groups["type"].Value
        });

        return true;
    }

    /// <summary>
    /// Parses temperature and dew point (e.g., "15/10", "M02/M05").
    /// </summary>
    private static bool TryParseTemperatureAndDewPoint(string token, MetarData metar)
    {
        var match = TemperatureRegex().Match(token);
        if (!match.Success)
        {
            return false;
        }

        metar.Temperature = ParseSignedTemperature(match.Groups["temperature"].Value);
        if (match.Groups["dewPoint"].Value != "//")
        {
            metar.DewPoint = ParseSignedTemperature(match.Groups["dewPoint"].Value);
        }

        return true;
    }

    /// <summary>
    /// Parses temperatures with an 'M' prefix indicating minus.
    /// </summary>
    private static int ParseSignedTemperature(string token)
    {
        return token.StartsWith('M')
            ? -int.Parse(token[1..], CultureInfo.InvariantCulture)
            : int.Parse(token, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Parses altimeter settings in hPa (Q1013) or inHg (A2992).
    /// </summary>
    private static bool TryParseAltimeter(string token, MetarData metar)
    {
        // European QNH (e.g., Q1013)
        var qnhMatch = QnhRegex().Match(token);
        if (qnhMatch.Success &&
            decimal.TryParse(qnhMatch.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var hpa))
        {
            metar.Altimeter = hpa;
            metar.AltimeterUnit = "hPa";
            return true;
        }

        // US Altimeter (e.g., A2992)
        var inHgMatch = AltimeterRegex().Match(token);
        if (inHgMatch.Success &&
            decimal.TryParse(inHgMatch.Groups["value"].Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var inHgValue))
        {
            metar.Altimeter = inHgValue / 100m;
            metar.AltimeterUnit = "inHg";
            return true;
        }

        return false;
    }

    private static bool LooksLikeWeatherToken(string token)
    {
        if (token.Length < 2 || token is "NOSIG" or "TEMPO" or "BECMG")
        {
            return false;
        }

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
    /// Determines the flight category (VFR, MVFR, IFR, LIFR) based on visibility and ceiling.
    /// </summary>
    private static string? DetermineFlightCategory(MetarData metar)
    {
        if (metar.IsCavok)
        {
            return "VFR";
        }

        var visibilitySm = ConvertVisibilityToStatuteMiles(metar.Visibility, metar.VisibilityUnit);
        
        // Ceiling is the lowest BKN, OVC, or VV layer
        var ceiling = metar.CloudLayers
            .Where(layer => layer.Altitude.HasValue &&
                            layer.Coverage is "BKN" or "OVC" or "VV")
            .Select(layer => layer.Altitude!.Value)
            .DefaultIfEmpty(int.MaxValue)
            .Min();

        // LIFR: Visibility < 1 mile OR Ceiling < 500 feet
        if (visibilitySm.HasValue && visibilitySm.Value < 1m || ceiling < 500)
        {
            return "LIFR";
        }

        // IFR: Visibility < 3 miles OR Ceiling < 1000 feet
        if (visibilitySm.HasValue && visibilitySm.Value < 3m || ceiling < 1000)
        {
            return "IFR";
        }

        // MVFR: Visibility <= 5 miles OR Ceiling <= 3000 feet
        if (visibilitySm.HasValue && visibilitySm.Value <= 5m || ceiling <= 3000)
        {
            return "MVFR";
        }

        // VFR: Everything else
        return visibilitySm.HasValue || ceiling != int.MaxValue
            ? "VFR"
            : null;
    }

    private static decimal? ConvertVisibilityToStatuteMiles(decimal? visibility, string? visibilityUnit)
    {
        if (!visibility.HasValue)
        {
            return null;
        }

        return visibilityUnit?.ToUpperInvariant() switch
        {
            "SM" => visibility.Value,
            "KM" => visibility.Value * 0.621371m,
            "M" => visibility.Value / 1609.344m,
            _ => null
        };
    }

    [GeneratedRegex("^(?<day>\\d{2})(?<hour>\\d{2})(?<minute>\\d{2})Z$")]
    private static partial Regex ObservationTimeRegex();

    [GeneratedRegex("^(?<direction>\\d{3}|VRB)(?<speed>\\d{2,3})(G(?<gust>\\d{2,3}))?KT$")]
    private static partial Regex WindRegex();

    [GeneratedRegex("^\\d{4}$")]
    private static partial Regex MeterVisibilityRegex();

    [GeneratedRegex("^(?<coverage>FEW|SCT|BKN|OVC|VV|NSC|SKC|CLR|NCD)(?<altitude>\\d{3})?(?<type>CB|TCU)?$")]
    private static partial Regex CloudRegex();

    [GeneratedRegex("^(?<temperature>M?\\d{2})/(?<dewPoint>M?\\d{2}|//)$")]
    private static partial Regex TemperatureRegex();

    [GeneratedRegex("^Q(?<value>\\d{4})$")]
    private static partial Regex QnhRegex();

    [GeneratedRegex("^A(?<value>\\d{4})$")]
    private static partial Regex AltimeterRegex();
}
