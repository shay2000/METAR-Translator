(function (root, factory) {
    "use strict";

    var api = factory();

    if (typeof module !== "undefined" && module.exports) {
        module.exports = api;
    }

    if (root) {
        root.MetarViewerCore = api;
    }
}(typeof globalThis !== "undefined" ? globalThis : this, function () {
    "use strict";

    var WEATHER_INDICATORS = [
        "RA", "SN", "DZ", "FG", "BR", "HZ", "TS", "FZ", "SH",
        "SG", "PL", "GR", "GS", "UP", "DU", "SA", "VA", "FU",
        "PO", "SQ", "FC", "SS", "DS"
    ];

    var NON_WEATHER_TOKENS = {
        METAR: true,
        SPECI: true,
        AUTO: true,
        COR: true,
        AMD: true,
        RTD: true,
        NOSIG: true,
        TEMPO: true,
        BECMG: true,
        RMK: true,
        CAVOK: true,
        NSW: true
    };

    var WEATHER_DESCRIPTIONS = {
        RA: "Rain",
        SN: "Snow",
        DZ: "Drizzle",
        FG: "Fog",
        BR: "Mist",
        HZ: "Haze",
        TS: "Thunderstorm",
        TSRA: "Thunderstorm with rain",
        SHRA: "Rain showers",
        SHSN: "Snow showers",
        FZ: "Freezing",
        FZRA: "Freezing rain",
        "+RA": "Heavy rain",
        "-RA": "Light rain",
        "+SN": "Heavy snow",
        "-SN": "Light snow"
    };

    var COVERAGE_DESCRIPTIONS = {
        FEW: "Few clouds",
        SCT: "Scattered clouds",
        BKN: "Broken clouds",
        OVC: "Overcast",
        VV: "Vertical visibility",
        SKC: "Sky clear",
        CLR: "Clear",
        NSC: "No significant cloud",
        NCD: "No cloud detected"
    };

    var TREND_MARKERS = { TEMPO: true, BECMG: true, NOSIG: true };
    var REPORT_MODIFIERS = { AUTO: true, COR: true, AMD: true, RTD: true };
    var CEILING_COVERAGES = { BKN: true, OVC: true, VV: true };

    function normalizeStationId(value) {
        return typeof value === "string" ? value.trim().toUpperCase() : "";
    }

    function looksLikeStationIdentifier(value) {
        return /^[A-Z]{4}$/.test(value || "");
    }

    function looksLikeWeatherToken(token) {
        if (!token || token.length < 2 || NON_WEATHER_TOKENS[token]) {
            return false;
        }

        var candidate = token.replace(/^[+-]/, "");
        if (candidate.indexOf("VC") === 0) {
            candidate = candidate.slice(2);
        }

        if (candidate.length < 2 || candidate.length > 8 || !/^[A-Z]+$/.test(candidate)) {
            return false;
        }

        return WEATHER_INDICATORS.some(function (indicator) {
            return candidate.indexOf(indicator) !== -1;
        });
    }

    function parseFraction(value) {
        var parts = String(value || "").split("/");
        if (parts.length !== 2) {
            return null;
        }

        var numerator = Number(parts[0]);
        var denominator = Number(parts[1]);
        if (!Number.isFinite(numerator) || !Number.isFinite(denominator) || denominator === 0) {
            return null;
        }

        return numerator / denominator;
    }

    function parseDistance(value) {
        var text = String(value || "").trim();
        if (!text) {
            return null;
        }

        if (text.indexOf("/") !== -1) {
            return parseFraction(text);
        }

        var parsed = Number(text);
        return Number.isFinite(parsed) ? parsed : null;
    }

    function parseStatuteMiles(token) {
        if (!token || token.slice(-2) !== "SM") {
            return null;
        }

        return parseDistance(token.slice(0, -2).replace(/^[PM]/, ""));
    }

    function addMonthsUtc(date, monthOffset) {
        return new Date(Date.UTC(
            date.getUTCFullYear(),
            date.getUTCMonth() + monthOffset,
            1,
            0,
            0,
            0,
            0
        ));
    }

    function parseObservationTime(token, now) {
        var match = /^(\d{2})(\d{2})(\d{2})Z$/.exec(token || "");
        if (!match) {
            return null;
        }

        var day = Number(match[1]);
        var hour = Number(match[2]);
        var minute = Number(match[3]);
        if (day < 1 || day > 31 || hour > 23 || minute > 59) {
            return null;
        }

        var reference = now instanceof Date ? now : new Date();
        var candidates = [];

        [-1, 0, 1].forEach(function (monthOffset) {
            var month = addMonthsUtc(reference, monthOffset);
            var year = month.getUTCFullYear();
            var monthIndex = month.getUTCMonth();
            var daysInMonth = new Date(Date.UTC(year, monthIndex + 1, 0)).getUTCDate();

            if (day <= daysInMonth) {
                candidates.push(new Date(Date.UTC(year, monthIndex, day, hour, minute, 0, 0)));
            }
        });

        if (!candidates.length) {
            return null;
        }

        candidates.sort(function (left, right) {
            return Math.abs(left.getTime() - reference.getTime()) -
                Math.abs(right.getTime() - reference.getTime());
        });

        return candidates[0];
    }

    function normalizeRawMetar(rawMetar, stationId) {
        var station = normalizeStationId(stationId);
        var text = typeof rawMetar === "string" ? rawMetar.trim().toUpperCase() : "";

        // Some feeds retain the telecommunication terminator. It is not an observation group.
        text = text.replace(/=+$/, "").trim();

        if (!text) {
            return "METAR " + station;
        }

        if (text.indexOf("METAR ") === 0 || text.indexOf("SPECI ") === 0) {
            return text;
        }

        var firstToken = text.split(/\s+/)[0];
        return firstToken === station
            ? "METAR " + text
            : "METAR " + station + " " + text;
    }

    function createMetar(stationId, rawMetar) {
        return {
            stationId: stationId,
            stationName: null,
            observationTime: null,
            rawMetar: rawMetar,
            temperature: null,
            dewPoint: null,
            windDirection: null,
            windSpeed: null,
            windGust: null,
            visibility: null,
            visibilityUnit: null,
            altimeter: null,
            altimeterUnit: null,
            cloudLayers: [],
            weatherPhenomena: [],
            flightCategory: null,
            isCavok: false,
            isAutomated: false,
            isCorrected: false,
            remarks: null,
            unparsedTokens: []
        };
    }

    function parseRawMetar(rawMetar, stationId, now) {
        var station = normalizeStationId(stationId);
        var normalizedRaw = normalizeRawMetar(rawMetar, station);
        var metar = createMetar(station, normalizedRaw);
        var tokens = normalizedRaw.split(/\s+/).filter(Boolean);

        if (!tokens.length) {
            return metar;
        }

        var index = 0;
        if (tokens[index] === "METAR" || tokens[index] === "SPECI") {
            index += 1;
        }

        if (index < tokens.length && looksLikeStationIdentifier(tokens[index])) {
            metar.stationId = tokens[index];
            index += 1;
        }

        if (index < tokens.length) {
            var observationTime = parseObservationTime(tokens[index], now);
            if (observationTime) {
                metar.observationTime = observationTime;
                index += 1;
            }
        }

        while (index < tokens.length && REPORT_MODIFIERS[tokens[index]]) {
            if (tokens[index] === "AUTO") {
                metar.isAutomated = true;
            } else if (tokens[index] === "COR") {
                metar.isCorrected = true;
            }
            index += 1;
        }

        for (; index < tokens.length; index += 1) {
            var token = tokens[index];

            if (token === "RMK") {
                var remarks = tokens.slice(index + 1).join(" ");
                metar.remarks = remarks || null;
                break;
            }

            if (TREND_MARKERS[token]) {
                break;
            }

            if (token === "CAVOK") {
                metar.isCavok = true;
                metar.visibility = 10;
                metar.visibilityUnit = "km";
                continue;
            }

            var wind = /^(\d{3}|VRB)(\d{2,3})(?:G(\d{2,3}))?KT$/.exec(token);
            if (wind) {
                metar.windDirection = wind[1] === "VRB" ? null : Number(wind[1]);
                metar.windSpeed = Number(wind[2]);
                metar.windGust = wind[3] ? Number(wind[3]) : null;
                continue;
            }

            if (token === "9999") {
                metar.visibility = 10;
                metar.visibilityUnit = "km";
                continue;
            }

            if (/^\d{4}$/.test(token)) {
                metar.visibility = Number(token);
                metar.visibilityUnit = "m";
                continue;
            }

            var statuteMiles = parseStatuteMiles(token);
            if (statuteMiles !== null) {
                metar.visibility = statuteMiles;
                metar.visibilityUnit = "SM";
                continue;
            }

            if (/^\d+$/.test(token) && index + 1 < tokens.length) {
                var fractionalMiles = parseStatuteMiles(tokens[index + 1]);
                if (fractionalMiles !== null) {
                    metar.visibility = Number(token) + fractionalMiles;
                    metar.visibilityUnit = "SM";
                    index += 1;
                    continue;
                }
            }

            var cloud = /^(FEW|SCT|BKN|OVC|VV|NSC|SKC|CLR|NCD)(\d{3})?(CB|TCU)?$/.exec(token);
            if (cloud) {
                metar.cloudLayers.push({
                    coverage: cloud[1],
                    altitude: cloud[2] ? Number(cloud[2]) * 100 : null,
                    type: cloud[3] || null
                });
                continue;
            }

            var temperature = /^(M?\d{2})\/(M?\d{2}|\/\/)$/.exec(token);
            if (temperature) {
                metar.temperature = parseSignedTemperature(temperature[1]);
                if (temperature[2] !== "//") {
                    metar.dewPoint = parseSignedTemperature(temperature[2]);
                }
                continue;
            }

            var qnh = /^Q(\d{4})$/.exec(token);
            if (qnh) {
                metar.altimeter = Number(qnh[1]);
                metar.altimeterUnit = "hPa";
                continue;
            }

            var inches = /^A(\d{4})$/.exec(token);
            if (inches) {
                metar.altimeter = Number(inches[1]) / 100;
                metar.altimeterUnit = "inHg";
                continue;
            }

            if (looksLikeWeatherToken(token)) {
                if (metar.weatherPhenomena.indexOf(token) === -1) {
                    metar.weatherPhenomena.push(token);
                }
                continue;
            }

            metar.unparsedTokens.push(token);
        }

        metar.flightCategory = determineFlightCategory(metar);
        return metar;
    }

    function parseSignedTemperature(token) {
        return token.charAt(0) === "M" ? -Number(token.slice(1)) : Number(token);
    }

    function visibilityToStatuteMiles(value, unit) {
        if (value === null || value === undefined || !Number.isFinite(Number(value))) {
            return null;
        }

        var numeric = Number(value);
        if (unit === "SM") {
            return numeric;
        }
        if (unit === "km") {
            return numeric * 0.6213711922;
        }
        if (unit === "m") {
            return numeric / 1609.344;
        }

        return numeric;
    }

    function getCeilingFeet(metar) {
        var ceilings = (metar.cloudLayers || [])
            .filter(function (layer) {
                return CEILING_COVERAGES[layer.coverage] && Number.isFinite(layer.altitude);
            })
            .map(function (layer) { return layer.altitude; });

        return ceilings.length ? Math.min.apply(Math, ceilings) : Number.POSITIVE_INFINITY;
    }

    function determineFlightCategory(metar) {
        if (metar.isCavok) {
            return "VFR";
        }

        var visibilityMiles = visibilityToStatuteMiles(metar.visibility, metar.visibilityUnit);
        var ceilingFeet = getCeilingFeet(metar);

        if ((visibilityMiles !== null && visibilityMiles < 1) || ceilingFeet < 500) {
            return "LIFR";
        }
        if ((visibilityMiles !== null && visibilityMiles < 3) || ceilingFeet < 1000) {
            return "IFR";
        }
        if ((visibilityMiles !== null && visibilityMiles <= 5) || ceilingFeet <= 3000) {
            return "MVFR";
        }

        return visibilityMiles !== null || ceilingFeet !== Number.POSITIVE_INFINITY ? "VFR" : null;
    }

    function decodeWind(metar) {
        if (metar.windSpeed === null || metar.windSpeed === undefined) {
            return "Wind information not available";
        }
        if (metar.windSpeed === 0) {
            return "Wind calm";
        }

        var direction = metar.windDirection === null || metar.windDirection === undefined
            ? "variable direction"
            : metar.windDirection + "°";
        var description = "Wind from " + direction + " at " + metar.windSpeed + " kt";
        return metar.windGust === null || metar.windGust === undefined
            ? description
            : description + ", gusting " + metar.windGust + " kt";
    }

    function formatNumber(value) {
        return Number.isInteger(value) ? String(value) : String(Number(value.toFixed(2)));
    }

    function decodeVisibility(metar) {
        if (metar.isCavok) {
            return "Visibility 10 km or more (CAVOK)";
        }
        if (metar.visibility === null || metar.visibility === undefined) {
            return "Visibility information not available";
        }
        return "Visibility " + formatNumber(Number(metar.visibility)) + " " + (metar.visibilityUnit || "SM");
    }

    function decodeClouds(metar) {
        if (metar.isCavok) {
            return "No clouds below 5,000 ft (CAVOK)";
        }
        if (!metar.cloudLayers || !metar.cloudLayers.length) {
            return "No significant cloud";
        }

        return metar.cloudLayers.map(function (layer) {
            var coverage = COVERAGE_DESCRIPTIONS[layer.coverage] || layer.coverage;
            var altitude = layer.altitude === null || layer.altitude === undefined
                ? "unknown altitude"
                : Number(layer.altitude).toLocaleString("en-US") + " ft";
            return coverage + " at " + altitude + (layer.type ? " (" + layer.type + ")" : "");
        }).join(", ");
    }

    function decodeTemperature(metar) {
        var parts = [];
        if (metar.temperature !== null && metar.temperature !== undefined) {
            parts.push("Temperature " + metar.temperature + "°C");
        }
        if (metar.dewPoint !== null && metar.dewPoint !== undefined) {
            parts.push("dew point " + metar.dewPoint + "°C");
        }
        return parts.length ? parts.join(", ") : "Temperature information not available";
    }

    function hectopascalsToInches(value) {
        return value * 0.0295299830714;
    }

    function inchesToHectopascals(value) {
        return value / 0.0295299830714;
    }

    function decodeAltimeter(metar) {
        if (metar.altimeter === null || metar.altimeter === undefined) {
            return "Altimeter information not available";
        }

        if (metar.altimeterUnit === "hPa" || (!metar.altimeterUnit && metar.altimeter >= 100)) {
            return "QNH " + formatNumber(Number(metar.altimeter)) + " hPa (" +
                hectopascalsToInches(Number(metar.altimeter)).toFixed(2) + " inHg)";
        }

        return "QNH " + Number(metar.altimeter).toFixed(2) + " inHg (" +
            inchesToHectopascals(Number(metar.altimeter)).toFixed(0) + " hPa)";
    }

    function decodeWeather(metar) {
        if (metar.isCavok || !metar.weatherPhenomena || !metar.weatherPhenomena.length) {
            return "No significant weather";
        }

        return metar.weatherPhenomena.map(function (code) {
            return WEATHER_DESCRIPTIONS[code] || code;
        }).join(", ");
    }

    function getFlightCategoryDescription(category) {
        switch ((category || "").toUpperCase()) {
            case "VFR": return "VFR (Visual Flight Rules)";
            case "MVFR": return "MVFR (Marginal VFR)";
            case "IFR": return "IFR (Instrument Flight Rules)";
            case "LIFR": return "LIFR (Low IFR)";
            default: return "Unknown";
        }
    }

    return Object.freeze({
        normalizeStationId: normalizeStationId,
        looksLikeStationIdentifier: looksLikeStationIdentifier,
        looksLikeWeatherToken: looksLikeWeatherToken,
        parseObservationTime: parseObservationTime,
        parseRawMetar: parseRawMetar,
        visibilityToStatuteMiles: visibilityToStatuteMiles,
        determineFlightCategory: determineFlightCategory,
        decodeWind: decodeWind,
        decodeVisibility: decodeVisibility,
        decodeClouds: decodeClouds,
        decodeTemperature: decodeTemperature,
        decodeAltimeter: decodeAltimeter,
        decodeWeather: decodeWeather,
        getFlightCategoryDescription: getFlightCategoryDescription
    });
}));
