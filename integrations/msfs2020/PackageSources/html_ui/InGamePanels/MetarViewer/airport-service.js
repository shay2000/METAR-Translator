(function (root, factory) {
    "use strict";

    var core = typeof module !== "undefined" && module.exports
        ? require("./metar-core.js")
        : root.MetarViewerCore;
    var api = factory(core);

    if (typeof module !== "undefined" && module.exports) {
        module.exports = api;
    }

    if (root) {
        root.MetarViewerAirports = api;
    }
}(typeof globalThis !== "undefined" ? globalThis : this, function (core) {
    "use strict";

    if (!core) {
        throw new Error("MetarViewerCore must be loaded before airport-service.js");
    }

    var NO_MATCH = Number.NEGATIVE_INFINITY;
    var CONFIDENT_SCORE = 250;
    var MINIMUM_RESOLUTION_SCORE = 140;
    var MAXIMUM_SUGGESTIONS = 5;

    function createAbortError() {
        var error = new Error("The operation was aborted.");
        error.name = "AbortError";
        return error;
    }

    function raceRequest(promise, signal, timeoutMilliseconds) {
        return new Promise(function (resolve, reject) {
            var settled = false;
            var timer = null;

            function cleanup() {
                if (timer !== null) {
                    clearTimeout(timer);
                }
                if (signal && typeof signal.removeEventListener === "function") {
                    signal.removeEventListener("abort", abort);
                }
            }

            function finish(callback, value) {
                if (settled) {
                    return;
                }
                settled = true;
                cleanup();
                callback(value);
            }

            function abort() {
                finish(reject, createAbortError());
            }

            if (signal && signal.aborted) {
                abort();
                return;
            }
            if (signal && typeof signal.addEventListener === "function") {
                signal.addEventListener("abort", abort, { once: true });
            }
            if (timeoutMilliseconds > 0) {
                timer = setTimeout(function () {
                    finish(reject, new Error("Airport search timed out."));
                }, timeoutMilliseconds);
            }

            Promise.resolve(promise).then(
                function (value) { finish(resolve, value); },
                function (error) { finish(reject, error); }
            );
        });
    }

    var IGNORED_NAME_WORDS = {
        AIRPORT: true,
        AIRFIELD: true,
        AERODROME: true,
        INTERNATIONAL: true,
        INTL: true,
        REGIONAL: true,
        MUNICIPAL: true,
        CITY: true,
        FIELD: true,
        HELIPORT: true,
        BASE: true,
        STRIP: true
    };

    var TYPE_SCORES = {
        large_airport: 120,
        medium_airport: 90,
        small_airport: 60,
        seaplane_base: 25,
        heliport: -10,
        balloonport: -20
    };

    function normalizeText(value) {
        return typeof value === "string"
            ? value.toUpperCase().replace(/[^A-Z0-9]+/g, "")
            : "";
    }

    function splitWords(value) {
        return typeof value === "string"
            ? value.split(/[\s/\-,()]+/).filter(Boolean)
            : [];
    }

    function getNameTokens(value) {
        return splitWords(value).filter(function (word) {
            return word.length >= 3 && !IGNORED_NAME_WORDS[word.toUpperCase()];
        });
    }

    function levenshteinDistance(source, target) {
        var left = String(source || "");
        var right = String(target || "");
        var previous = new Array(right.length + 1);
        var current = new Array(right.length + 1);

        for (var rightIndex = 0; rightIndex <= right.length; rightIndex += 1) {
            previous[rightIndex] = rightIndex;
        }

        for (var leftIndex = 1; leftIndex <= left.length; leftIndex += 1) {
            current[0] = leftIndex;
            for (rightIndex = 1; rightIndex <= right.length; rightIndex += 1) {
                var cost = left.charAt(leftIndex - 1) === right.charAt(rightIndex - 1) ? 0 : 1;
                current[rightIndex] = Math.min(
                    previous[rightIndex] + 1,
                    current[rightIndex - 1] + 1,
                    previous[rightIndex - 1] + cost
                );
            }

            var swap = previous;
            previous = current;
            current = swap;
        }

        return previous[right.length];
    }

    function getAllCodes(attributes) {
        if (!attributes) {
            return [];
        }

        return [
            attributes.code,
            attributes.icao_code,
            attributes.iata_code,
            attributes.gps_code,
            attributes.local_code
        ].filter(function (code) { return typeof code === "string" && code.trim(); });
    }

    function getStationIdentifier(attributes) {
        if (!attributes) {
            return null;
        }

        var candidates = [attributes.icao_code, attributes.gps_code, attributes.code];
        for (var index = 0; index < candidates.length; index += 1) {
            var normalized = core.normalizeStationId(candidates[index]);
            if (/^[A-Z]{4}$/.test(normalized)) {
                return normalized;
            }
        }

        return null;
    }

    function getComparableTerms(attributes) {
        var terms = [];

        function add(value) {
            var normalized = normalizeText(value);
            if (normalized && terms.indexOf(normalized) === -1) {
                terms.push(normalized);
            }
        }

        add(attributes && attributes.name);
        getAllCodes(attributes).forEach(add);
        getNameTokens(attributes && attributes.name).forEach(add);
        return terms;
    }

    function getFuzzyScore(attributes, normalizedInput) {
        var query = normalizeText(normalizedInput);
        if (!query) {
            return 0;
        }

        return getComparableTerms(attributes).reduce(function (best, term) {
            var distance = levenshteinDistance(query, term);
            var lengthPenalty = Math.abs(term.length - query.length) * 3;
            return Math.max(best, Math.max(0, 120 - (distance * 28) - lengthPenalty));
        }, 0);
    }

    function scoreAirport(attributes, input) {
        if (!attributes || String(attributes.type || "").toLowerCase() === "closed") {
            return NO_MATCH;
        }

        var trimmedInput = String(input || "").trim();
        var normalizedInput = trimmedInput.toUpperCase();
        var score = TYPE_SCORES[attributes.type] || 0;
        var name = String(attributes.name || "");
        var nameLower = name.toLowerCase();
        var inputLower = trimmedInput.toLowerCase();

        if (getAllCodes(attributes).some(function (code) {
            return core.normalizeStationId(code) === normalizedInput;
        })) {
            score += 500;
        }

        if (nameLower === inputLower) {
            score += 300;
        } else if (inputLower && nameLower.indexOf(inputLower) === 0) {
            score += 220;
        } else if (inputLower && nameLower.indexOf(inputLower) !== -1) {
            score += 150;
        }

        if (attributes.iata_code) {
            score += 10;
        }

        return score + getFuzzyScore(attributes, normalizedInput);
    }

    function toMatch(attributes, input) {
        var stationId = getStationIdentifier(attributes);
        if (!stationId) {
            return null;
        }

        var score = scoreAirport(attributes, input);
        return score === NO_MATCH ? null : { stationId: stationId, attributes: attributes, score: score };
    }

    function looksLikeAirportCode(value) {
        return /^[A-Z0-9]{3,4}$/.test(value || "");
    }

    function couldBeStationIdentifier(value) {
        return /^[A-Z]{3,4}$/.test(value || "");
    }

    function buildRelaxedQueries(input) {
        var trimmed = String(input || "").trim();
        var normalized = trimmed.toUpperCase();
        var queries = [];
        var seen = Object.create(null);

        function add(filterKey, value, pageSize) {
            var key = filterKey + ":" + value.toUpperCase();
            if (value && !seen[key]) {
                seen[key] = true;
                queries.push({ filterKey: filterKey, value: value, pageSize: pageSize });
            }
        }

        if (looksLikeAirportCode(normalized)) {
            add("filter[code]", normalized.slice(0, Math.max(3, normalized.length - 1)), 20);
        }

        var values = splitWords(trimmed).filter(function (word) { return word.length >= 2; });
        values.push(normalizeText(trimmed));
        values.forEach(function (value) {
            [5, 4, 2].forEach(function (length) {
                if (value.length >= length) {
                    add("filter[name]", value.slice(0, length), 50);
                }
            });
        });

        return queries;
    }

    class AirportsApiClient {
        constructor(fetchFunction, options) {
            this.fetchFunction = fetchFunction || (typeof fetch === "function" ? fetch.bind(globalThis) : null);
            this.options = options || {};
            this.baseUrl = this.options.baseUrl || "https://airportsapi.com/api/";
            this.requestTimeoutMilliseconds = this.options.requestTimeoutMilliseconds === undefined
                ? 6000
                : this.options.requestTimeoutMilliseconds;
        }

        isAvailable() {
            return typeof this.fetchFunction === "function";
        }

        async request(url, signal) {
            if (!this.isAvailable()) {
                throw new Error("Airport search is unavailable.");
            }

            var requestOptions = {
                method: "GET",
                headers: { Accept: "application/vnd.api+json, application/json" }
            };
            if (signal && !signal.__metarViewerPolyfill) {
                requestOptions.signal = signal;
            }

            var request = Promise.resolve().then(function () {
                return this.fetchFunction(url, requestOptions);
            }.bind(this));
            return raceRequest(request, signal, this.requestTimeoutMilliseconds);
        }

        async getByCode(code, signal) {
            var response = await this.request(
                this.baseUrl + "airports/" + encodeURIComponent(code),
                signal
            );

            if (response.status === 404 || response.status === 204) {
                return null;
            }
            if (!response.ok) {
                throw new Error("Airport search returned HTTP " + response.status + ".");
            }

            var payload = await response.json();
            return payload && payload.data ? payload.data.attributes || null : null;
        }

        async search(filterKey, value, pageSize, signal) {
            var parameters = [
                encodeURIComponent(filterKey) + "=" + encodeURIComponent(value),
                "sort=name",
                "include=country%2Cregion",
                "page%5Bsize%5D=" + encodeURIComponent(String(pageSize))
            ].join("&");

            var response = await this.request(
                this.baseUrl + "airports?" + parameters,
                signal
            );

            if (response.status === 404 || response.status === 204) {
                return [];
            }
            if (!response.ok) {
                throw new Error("Airport search returned HTTP " + response.status + ".");
            }

            var payload = await response.json();
            return payload && Array.isArray(payload.data)
                ? payload.data.map(function (item) { return item && item.attributes; }).filter(Boolean)
                : [];
        }
    }

    class AirportCandidateFinder {
        constructor(apiClient, options) {
            this.apiClient = apiClient;
            this.options = options || {};
            this.maxRequests = this.options.maxRequests || 6;
        }

        async find(input, signal) {
            var trimmed = String(input || "").trim();
            var normalized = trimmed.toUpperCase();
            var matches = new Map();
            var requests = 0;
            var self = this;

            function add(attributes) {
                var match = toMatch(attributes, trimmed);
                if (!match) {
                    return;
                }

                var previous = matches.get(match.stationId);
                if (!previous || match.score > previous.score) {
                    matches.set(match.stationId, match);
                }
            }

            function ordered() {
                return Array.from(matches.values()).sort(function (left, right) {
                    if (left.score !== right.score) {
                        return right.score - left.score;
                    }
                    return String(left.attributes.name || "").localeCompare(String(right.attributes.name || ""));
                });
            }

            function isConfident() {
                return ordered().some(function (match) { return match.score >= CONFIDENT_SCORE; });
            }

            async function getByCode(code) {
                if (requests >= self.maxRequests) {
                    return null;
                }
                requests += 1;
                return self.apiClient.getByCode(code, signal);
            }

            async function search(query) {
                if (requests >= self.maxRequests) {
                    return [];
                }
                requests += 1;
                return self.apiClient.search(query.filterKey, query.value, query.pageSize, signal);
            }

            try {
                if (looksLikeAirportCode(normalized)) {
                    add(await getByCode(normalized));
                    if (isConfident()) {
                        return ordered();
                    }

                    (await search({ filterKey: "filter[code]", value: normalized, pageSize: 20 })).forEach(add);
                    if (isConfident()) {
                        return ordered();
                    }
                }

                (await search({ filterKey: "filter[name]", value: trimmed, pageSize: 20 })).forEach(add);
                if (isConfident()) {
                    return ordered();
                }

                if (matches.size < MAXIMUM_SUGGESTIONS) {
                    var relaxedQueries = buildRelaxedQueries(trimmed);
                    for (var index = 0; index < relaxedQueries.length && requests < this.maxRequests; index += 1) {
                        (await search(relaxedQueries[index])).forEach(add);
                    }
                }
            } catch (error) {
                if (error && error.name === "AbortError") {
                    throw error;
                }
                // Lookup is an enhancement. Direct ICAO input remains useful if it is offline,
                // rate-limited, or temporarily failing.
                return [];
            }

            return ordered();
        }
    }

    function toSuggestion(match) {
        var name = match.attributes.name || match.stationId;
        var iata = match.attributes.iata_code || null;
        var parts = [match.stationId];
        if (iata && iata.toUpperCase() !== match.stationId) {
            parts.push(iata.toUpperCase());
        }
        parts.push(name);

        return {
            stationId: match.stationId,
            displayName: name,
            iataCode: iata,
            displayText: parts.join(" · ")
        };
    }

    class AirportLookupService {
        constructor(candidateFinder, options) {
            this.candidateFinder = candidateFinder;
            this.options = options || {};
            this.now = this.options.now || Date.now;
            this.resolutionLifetimeMilliseconds = this.options.resolutionLifetimeMilliseconds || 600000;
            this.suggestionLifetimeMilliseconds = this.options.suggestionLifetimeMilliseconds || 120000;
            this.resolutionCache = new Map();
            this.suggestionCache = new Map();
        }

        getCache(cache, key) {
            var entry = cache.get(key);
            if (!entry) {
                return { found: false, value: null };
            }
            if (entry.expiresAt <= this.now()) {
                cache.delete(key);
                return { found: false, value: null };
            }
            return { found: true, value: entry.value };
        }

        async resolve(input, signal) {
            var trimmed = String(input || "").trim();
            var normalized = trimmed.toUpperCase();
            if (!normalized) {
                return null;
            }

            var cached = this.getCache(this.resolutionCache, normalized);
            if (cached.found) {
                return cached.value;
            }

            var matches = await this.candidateFinder.find(trimmed, signal);
            var best = matches.length && matches[0].score >= MINIMUM_RESOLUTION_SCORE
                ? toSuggestion(matches[0])
                : null;
            var resolution = best || (couldBeStationIdentifier(normalized)
                ? { stationId: normalized, displayName: null, iataCode: null, displayText: normalized }
                : null);

            this.resolutionCache.set(normalized, {
                value: resolution,
                expiresAt: this.now() + this.resolutionLifetimeMilliseconds
            });
            return resolution;
        }

        async getSuggestions(input, signal) {
            var trimmed = String(input || "").trim();
            var normalized = trimmed.toUpperCase();
            if (trimmed.length < 2) {
                return [];
            }

            var cached = this.getCache(this.suggestionCache, normalized);
            if (cached.found) {
                return cached.value;
            }

            var suggestions = (await this.candidateFinder.find(trimmed, signal))
                .slice(0, MAXIMUM_SUGGESTIONS)
                .map(toSuggestion);
            this.suggestionCache.set(normalized, {
                value: suggestions,
                expiresAt: this.now() + this.suggestionLifetimeMilliseconds
            });
            return suggestions;
        }
    }

    return Object.freeze({
        NO_MATCH: NO_MATCH,
        CONFIDENT_SCORE: CONFIDENT_SCORE,
        MINIMUM_RESOLUTION_SCORE: MINIMUM_RESOLUTION_SCORE,
        normalizeText: normalizeText,
        splitWords: splitWords,
        levenshteinDistance: levenshteinDistance,
        getStationIdentifier: getStationIdentifier,
        getFuzzyScore: getFuzzyScore,
        scoreAirport: scoreAirport,
        buildRelaxedQueries: buildRelaxedQueries,
        AirportsApiClient: AirportsApiClient,
        AirportCandidateFinder: AirportCandidateFinder,
        AirportLookupService: AirportLookupService
    });
}));
