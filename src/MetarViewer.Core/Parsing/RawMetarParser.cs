using System.Text.RegularExpressions;
using MetarViewer.Models;
using MetarViewer.Parsing;

namespace MetarViewer.Services;

/// <summary>
/// Parses raw METAR strings into <see cref="MetarData"/>.
///
/// The parser handles the fixed prelude of a report (type, station, observation time,
/// modifiers) and then delegates each observation group to an <see cref="IMetarTokenParser"/>.
/// </summary>
internal static partial class RawMetarParser
{
    /// <summary>Token that introduces the remarks section.</summary>
    private const string RemarksToken = "RMK";

    /// <summary>Token indicating ceiling and visibility are OK.</summary>
    private const string CavokToken = "CAVOK";

    private const decimal CavokVisibilityKilometres = 10m;

    /// <summary>
    /// The group parsers, applied in order until one consumes the current token.
    /// </summary>
    /// <remarks>
    /// Order matters: the visibility parser must run before the temperature parser so that
    /// a bare four-digit group is read as metres rather than being misinterpreted.
    /// </remarks>
    private static readonly IMetarTokenParser[] TokenParsers =
    [
        new WindTokenParser(),
        new VisibilityTokenParser(),
        new CloudTokenParser(),
        new TemperatureTokenParser(),
        new AltimeterTokenParser()
    ];

    /// <summary>
    /// Parses a raw METAR string.
    /// </summary>
    /// <param name="rawMetar">The raw report text.</param>
    /// <param name="stationId">The station the report was requested for, used as a fallback.</param>
    public static MetarData Parse(string rawMetar, string stationId)
    {
        var normalizedStationId = stationId.Trim().ToUpperInvariant();
        var normalizedRawMetar = NormalizeRawMetar(rawMetar, normalizedStationId);

        var metar = new MetarData
        {
            StationId = normalizedStationId,
            RawMetar = normalizedRawMetar
        };

        var tokens = Tokenize(normalizedRawMetar);
        if (tokens.Length == 0)
        {
            return metar;
        }

        var index = ParsePrelude(tokens, metar);
        ParseObservationGroups(tokens, index, metar);

        metar.FlightCategory = FlightCategoryCalculator.Determine(metar);
        return metar;
    }

    /// <summary>
    /// Splits a report into non-empty tokens.
    /// </summary>
    private static string[] Tokenize(string rawMetar)
    {
        return rawMetar
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length > 0)
            .ToArray();
    }

    /// <summary>
    /// Reads the fixed leading portion of a report: report type, station identifier,
    /// observation time and any modifiers.
    /// </summary>
    /// <returns>The index of the first observation group.</returns>
    private static int ParsePrelude(string[] tokens, MetarData metar)
    {
        var index = 0;

        if (MetarTokenClassifier.IsReportTypePrefix(tokens[index]))
        {
            index++;
        }

        if (index < tokens.Length && MetarTokenClassifier.LooksLikeStationIdentifier(tokens[index]))
        {
            metar.StationId = tokens[index];
            index++;
        }

        if (index < tokens.Length && TryParseObservationTime(tokens[index], out var observationTime))
        {
            metar.ObservationTime = observationTime;
            index++;
        }

        // Modifiers were previously skipped without being recorded.
        while (index < tokens.Length && MetarTokenClassifier.IsReportModifier(tokens[index]))
        {
            switch (tokens[index])
            {
                case "AUTO":
                    metar.IsAutomated = true;
                    break;
                case "COR":
                    metar.IsCorrected = true;
                    break;
            }

            index++;
        }

        return index;
    }

    /// <summary>
    /// Parses the observation groups, stopping at the remarks section.
    /// </summary>
    private static void ParseObservationGroups(string[] tokens, int startIndex, MetarData metar)
    {
        for (var index = startIndex; index < tokens.Length; index++)
        {
            var token = tokens[index];

            if (token == RemarksToken)
            {
                // Retain the remarks text rather than discarding it.
                var remarks = string.Join(' ', tokens[(index + 1)..]);
                metar.Remarks = string.IsNullOrWhiteSpace(remarks) ? null : remarks;
                return;
            }

            // BECMG, TEMPO and NOSIG introduce the trend forecast appended to some METARs.
            // Those groups describe expected conditions, not the observation itself, so allowing
            // the normal token parsers to continue would overwrite the current visibility,
            // ceiling and weather with forecast values.
            if (MetarTokenClassifier.IsTrendSectionStart(token))
            {
                return;
            }

            if (token == CavokToken)
            {
                metar.IsCavok = true;
                metar.Visibility = CavokVisibilityKilometres;
                metar.VisibilityUnit = VisibilityUnits.Kilometres;
                continue;
            }

            var context = new MetarTokenContext(tokens, index);
            if (TryParseWithTokenParsers(context, metar))
            {
                // A parser may have consumed a look-ahead token as well.
                index = context.Index;
                continue;
            }

            if (MetarTokenClassifier.LooksLikeWeatherToken(token))
            {
                metar.WeatherPhenomena.Add(token);
                continue;
            }

            // Anything left is recorded so that gaps in coverage remain visible.
            metar.UnparsedTokens.Add(token);
        }
    }

    /// <summary>
    /// Applies each group parser in turn until one consumes the current token.
    /// </summary>
    private static bool TryParseWithTokenParsers(MetarTokenContext context, MetarData metar)
    {
        foreach (var parser in TokenParsers)
        {
            if (parser.TryParse(context, metar))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Ensures the report begins with "METAR [ICAO]" so the prelude can be parsed
    /// positionally, and normalises it to upper case.
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
            ? $"METAR {upperTrimmed}"
            : $"METAR {stationId} {upperTrimmed}";
    }

    /// <summary>
    /// Parses a day-hour-minute observation time such as "151230Z".
    /// </summary>
    /// <remarks>
    /// A report carries no month or year, so the calendar month whose matching day falls
    /// closest to the current time is chosen. This keeps reports near a month boundary from
    /// being dated a month out.
    /// </remarks>
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

    [GeneratedRegex("^(?<day>\\d{2})(?<hour>\\d{2})(?<minute>\\d{2})Z$")]
    private static partial Regex ObservationTimeRegex();
}
