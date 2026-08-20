(function (root, factory) {
    "use strict";

    var core = typeof module !== "undefined" && module.exports
        ? require("./metar-core.js")
        : root.MetarViewerCore;
    var weather = typeof module !== "undefined" && module.exports
        ? require("./weather-service.js")
        : root.MetarViewerWeather;
    var airports = typeof module !== "undefined" && module.exports
        ? require("./airport-service.js")
        : root.MetarViewerAirports;
    var api = factory(core, weather, airports);

    if (typeof module !== "undefined" && module.exports) {
        module.exports = api;
    }

    if (root) {
        root.MetarViewerPanelApp = api;
    }
}(typeof globalThis !== "undefined" ? globalThis : this, function (core, weather, airports) {
    "use strict";

    if (!core || !weather || !airports) {
        throw new Error("METAR Viewer services must be loaded before panel-app.js");
    }

    var LAST_STATION_KEY = "metar-viewer.last-station";
    var SUGGESTION_DELAY_MILLISECONDS = 250;

    function formatObservationTime(value) {
        if (!(value instanceof Date) || Number.isNaN(value.getTime())) {
            return "Observation time unavailable";
        }

        var monthNames = [
            "Jan", "Feb", "Mar", "Apr", "May", "Jun",
            "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"
        ];
        var day = String(value.getUTCDate()).padStart(2, "0");
        var hour = String(value.getUTCHours()).padStart(2, "0");
        var minute = String(value.getUTCMinutes()).padStart(2, "0");
        return day + " " + monthNames[value.getUTCMonth()] + " " + value.getUTCFullYear() +
            ", " + hour + ":" + minute + " UTC";
    }

    function createDefaultServices(environment) {
        var runtime = environment || (typeof globalThis !== "undefined" ? globalThis : {});
        var fetchFunction = typeof runtime.fetch === "function" ? runtime.fetch.bind(runtime) : null;
        var simulatorSource = new weather.SimulatorMetarSource(runtime);
        var vatsimSource = new weather.VatsimMetarSource(fetchFunction);
        var weatherService = new weather.MetarService([simulatorSource, vatsimSource]);
        var apiClient = new airports.AirportsApiClient(fetchFunction);
        var finder = new airports.AirportCandidateFinder(apiClient);

        return {
            weatherService: weatherService,
            airportLookup: new airports.AirportLookupService(finder)
        };
    }

    function createStorage(environment) {
        var runtime = environment || {};

        function readFallback(key) {
            try {
                return runtime.localStorage ? runtime.localStorage.getItem(key) : null;
            } catch (_error) {
                return null;
            }
        }

        function writeFallback(key, value) {
            try {
                if (runtime.localStorage) {
                    runtime.localStorage.setItem(key, value);
                }
            } catch (_error) {
                // Persistence is useful, but never required to display weather.
            }
        }

        return {
            get: function (key) {
                try {
                    if (typeof runtime.GetStoredData === "function") {
                        return runtime.GetStoredData(key) || readFallback(key);
                    }
                } catch (_error) {
                    // Fall through to local storage when the simulator store is unavailable.
                }
                return readFallback(key);
            },
            set: function (key, value) {
                try {
                    if (typeof runtime.SetStoredData === "function") {
                        runtime.SetStoredData(key, value);
                        return;
                    }
                } catch (_error) {
                    // Fall through to local storage when the simulator store is unavailable.
                }
                writeFallback(key, value);
            }
        };
    }

    function requiredElement(rootElement, selector) {
        var element = rootElement.querySelector(selector);
        if (!element) {
            throw new Error("METAR Viewer is missing required element " + selector + ".");
        }
        return element;
    }

    function isAbortError(error) {
        return weather.isAbortError(error);
    }

    class MetarViewerApp {
        constructor(rootElement, options) {
            if (!rootElement || typeof rootElement.querySelector !== "function") {
                throw new Error("MetarViewerApp requires its panel root element.");
            }

            this.rootElement = rootElement;
            this.options = options || {};
            this.environment = this.options.environment ||
                (typeof globalThis !== "undefined" ? globalThis : {});
            var defaults = this.options.weatherService && this.options.airportLookup
                ? null
                : createDefaultServices(this.environment);
            this.weatherService = this.options.weatherService || defaults.weatherService;
            this.airportLookup = this.options.airportLookup || defaults.airportLookup;
            this.storage = this.options.storage || createStorage(this.environment);
            this.suggestionDelayMilliseconds = this.options.suggestionDelayMilliseconds === undefined
                ? SUGGESTION_DELAY_MILLISECONDS
                : this.options.suggestionDelayMilliseconds;

            this.elements = {
                form: requiredElement(rootElement, "#search-form"),
                input: requiredElement(rootElement, "#airport-search"),
                searchButton: requiredElement(rootElement, "#search-button"),
                suggestions: requiredElement(rootElement, "#airport-suggestions"),
                status: requiredElement(rootElement, "#status-message"),
                result: requiredElement(rootElement, "#metar-result"),
                provider: requiredElement(rootElement, "#provider-badge"),
                station: requiredElement(rootElement, "#station-heading"),
                observationTime: requiredElement(rootElement, "#observation-time"),
                flightCategory: requiredElement(rootElement, "#flight-category"),
                wind: requiredElement(rootElement, "#decoded-wind"),
                visibility: requiredElement(rootElement, "#decoded-visibility"),
                temperature: requiredElement(rootElement, "#decoded-temperature"),
                altimeter: requiredElement(rootElement, "#decoded-altimeter"),
                clouds: requiredElement(rootElement, "#decoded-clouds"),
                raw: requiredElement(rootElement, "#raw-metar"),
                categoryDescription: requiredElement(rootElement, "#category-description"),
                weather: requiredElement(rootElement, "#decoded-weather"),
                refresh: requiredElement(rootElement, "#refresh-button"),
                connection: requiredElement(rootElement, "#connection-state")
            };

            this.active = false;
            this.destroyed = false;
            this.hasRestoredStation = false;
            this.suggestionTimer = null;
            this.suggestionController = null;
            this.weatherController = null;
            this.suggestionGeneration = 0;
            this.weatherGeneration = 0;
            this.suggestions = [];
            this.activeSuggestionIndex = -1;
            this.selectedSuggestion = null;
            this.currentResolution = null;
            this.hasSimulatorInputFocus = false;

            this.boundInput = this.onInput.bind(this);
            this.boundInputFocus = this.onInputFocus.bind(this);
            this.boundInputBlur = this.releaseInputFocus.bind(this);
            this.boundKeyDown = this.onKeyDown.bind(this);
            this.boundSubmit = this.onSubmit.bind(this);
            this.boundRefresh = this.onRefresh.bind(this);
            this.boundSuggestionClick = this.onSuggestionClick.bind(this);
            this.boundDocumentClick = this.onDocumentClick.bind(this);
            this.connect();
        }

        connect() {
            this.elements.input.addEventListener("input", this.boundInput);
            this.elements.input.addEventListener("keydown", this.boundKeyDown);
            this.elements.input.addEventListener("focus", this.boundInputFocus);
            this.elements.input.addEventListener("blur", this.boundInputBlur);
            this.elements.form.addEventListener("submit", this.boundSubmit);
            this.elements.refresh.addEventListener("click", this.boundRefresh);
            this.elements.suggestions.addEventListener("click", this.boundSuggestionClick);
            if (this.environment.document && typeof this.environment.document.addEventListener === "function") {
                this.environment.document.addEventListener("pointerdown", this.boundDocumentClick);
            }
        }

        activate() {
            var self = this;
            if (this.destroyed) {
                return;
            }

            this.active = true;
            this.elements.connection.textContent = "Ready";

            if (!this.hasRestoredStation) {
                this.hasRestoredStation = true;
                Promise.resolve(this.storage.get(LAST_STATION_KEY)).then(function (stored) {
                    var stationId = core.normalizeStationId(stored);
                    if (!self.active || self.elements.input.value.trim() ||
                        !/^[A-Z]{4}$/.test(stationId)) {
                        return;
                    }
                    self.elements.input.value = stationId;
                    self.selectedSuggestion = {
                        stationId: stationId,
                        displayName: null,
                        displayText: stationId
                    };
                    self.fetchStation(self.selectedSuggestion, false);
                }).catch(function () {
                    // Restoring a previous station is optional.
                });
            }
        }

        deactivate() {
            this.active = false;
            this.cancelSuggestionWork();
            this.cancelWeatherWork();
            this.releaseInputFocus();
            this.hideSuggestions();
            this.setBusy(false);
            this.elements.connection.textContent = "Paused";
        }

        destroy() {
            if (this.destroyed) {
                return;
            }

            this.deactivate();
            this.destroyed = true;
            this.elements.input.removeEventListener("input", this.boundInput);
            this.elements.input.removeEventListener("keydown", this.boundKeyDown);
            this.elements.input.removeEventListener("focus", this.boundInputFocus);
            this.elements.input.removeEventListener("blur", this.boundInputBlur);
            this.elements.form.removeEventListener("submit", this.boundSubmit);
            this.elements.refresh.removeEventListener("click", this.boundRefresh);
            this.elements.suggestions.removeEventListener("click", this.boundSuggestionClick);
            if (this.environment.document && typeof this.environment.document.removeEventListener === "function") {
                this.environment.document.removeEventListener("pointerdown", this.boundDocumentClick);
            }
        }

        cancelSuggestionWork() {
            if (this.suggestionTimer !== null) {
                clearTimeout(this.suggestionTimer);
                this.suggestionTimer = null;
            }
            if (this.suggestionController) {
                this.suggestionController.abort();
                this.suggestionController = null;
            }
            this.suggestionGeneration += 1;
        }

        cancelWeatherWork() {
            if (this.weatherController) {
                this.weatherController.abort();
                this.weatherController = null;
            }
            this.weatherGeneration += 1;
        }

        onInputFocus() {
            if (this.hasSimulatorInputFocus) {
                return;
            }
            if (typeof this.environment.OnInputFieldFocus === "function") {
                this.environment.OnInputFieldFocus();
                this.hasSimulatorInputFocus = true;
            } else if (this.environment.Coherent &&
                typeof this.environment.Coherent.trigger === "function") {
                this.environment.Coherent.trigger(
                    "FOCUS_INPUT_FIELD",
                    this.elements.input.id || "airport-search",
                    "",
                    "",
                    this.elements.input.value,
                    false
                );
                this.hasSimulatorInputFocus = true;
            }
        }

        releaseInputFocus() {
            if (!this.hasSimulatorInputFocus) {
                return;
            }
            if (typeof this.environment.OnInputFieldUnfocus === "function") {
                this.environment.OnInputFieldUnfocus();
            } else if (this.environment.Coherent &&
                typeof this.environment.Coherent.trigger === "function") {
                this.environment.Coherent.trigger(
                    "UNFOCUS_INPUT_FIELD",
                    this.elements.input.id || "airport-search"
                );
            }
            this.hasSimulatorInputFocus = false;
        }

        unfocusInputElement() {
            if (typeof this.elements.input.blur === "function") {
                this.elements.input.blur();
            }
            this.releaseInputFocus();
        }

        onInput() {
            if (!this.active) {
                return;
            }

            this.selectedSuggestion = null;
            this.currentResolution = null;
            this.cancelSuggestionWork();
            this.cancelWeatherWork();
            this.hideSuggestions();
            this.setBusy(false);

            var query = this.elements.input.value.trim();
            if (query.length < 2) {
                return;
            }

            var self = this;
            var generation = this.suggestionGeneration;
            this.suggestionTimer = setTimeout(function () {
                self.suggestionTimer = null;
                self.loadSuggestions(query, generation);
            }, this.suggestionDelayMilliseconds);
        }

        async loadSuggestions(query, generation) {
            if (!this.active || generation !== this.suggestionGeneration ||
                query !== this.elements.input.value.trim()) {
                return;
            }

            var controller = weather.createAbortController(this.environment);
            this.suggestionController = controller;
            try {
                var suggestions = await this.airportLookup.getSuggestions(query, controller.signal);
                if (!this.active || controller.signal.aborted ||
                    generation !== this.suggestionGeneration ||
                    query !== this.elements.input.value.trim()) {
                    return;
                }
                this.renderSuggestions(suggestions);
            } catch (error) {
                if (!isAbortError(error) && this.active &&
                    controller === this.suggestionController &&
                    generation === this.suggestionGeneration &&
                    query === this.elements.input.value.trim()) {
                    this.hideSuggestions();
                }
            } finally {
                if (this.suggestionController === controller) {
                    this.suggestionController = null;
                }
            }
        }

        renderSuggestions(suggestions) {
            var documentObject = this.rootElement.ownerDocument || this.environment.document;
            this.suggestions = Array.isArray(suggestions) ? suggestions.slice(0, 5) : [];
            this.activeSuggestionIndex = -1;
            this.elements.suggestions.textContent = "";

            for (var index = 0; index < this.suggestions.length; index += 1) {
                var suggestion = this.suggestions[index];
                var button = documentObject.createElement("button");
                var code = documentObject.createElement("span");
                var name = documentObject.createElement("span");

                button.type = "button";
                button.className = "suggestion-option";
                button.setAttribute("role", "option");
                button.setAttribute("aria-selected", "false");
                button.setAttribute("data-suggestion-index", String(index));
                code.className = "suggestion-code";
                code.textContent = suggestion.stationId;
                name.className = "suggestion-name";
                name.textContent = suggestion.displayName || suggestion.displayText || suggestion.stationId;
                button.appendChild(code);
                button.appendChild(name);
                this.elements.suggestions.appendChild(button);
            }

            var visible = this.suggestions.length > 0;
            this.elements.suggestions.hidden = !visible;
            this.elements.input.setAttribute("aria-expanded", visible ? "true" : "false");
        }

        hideSuggestions() {
            this.suggestions = [];
            this.activeSuggestionIndex = -1;
            this.elements.suggestions.textContent = "";
            this.elements.suggestions.hidden = true;
            this.elements.input.setAttribute("aria-expanded", "false");
        }

        onKeyDown(event) {
            if (event.key === "Escape") {
                this.cancelSuggestionWork();
                this.hideSuggestions();
                return;
            }

            if (event.key === "ArrowDown" || event.key === "ArrowUp") {
                if (!this.suggestions.length) {
                    return;
                }
                event.preventDefault();
                var offset = event.key === "ArrowDown" ? 1 : -1;
                this.setActiveSuggestion(this.activeSuggestionIndex + offset);
                return;
            }

            if (event.key === "Enter") {
                event.preventDefault();
                if (this.activeSuggestionIndex >= 0) {
                    this.selectSuggestion(this.activeSuggestionIndex);
                } else {
                    this.submitCurrentInput();
                }
            }
        }

        setActiveSuggestion(index) {
            var count = this.suggestions.length;
            if (!count) {
                return;
            }

            this.activeSuggestionIndex = (index + count) % count;
            var children = this.elements.suggestions.children;
            for (var childIndex = 0; childIndex < children.length; childIndex += 1) {
                var active = childIndex === this.activeSuggestionIndex;
                children[childIndex].classList.toggle("is-active", active);
                children[childIndex].setAttribute("aria-selected", active ? "true" : "false");
            }
        }

        onSuggestionClick(event) {
            var target = event.target;
            while (target && target !== this.elements.suggestions &&
                !target.hasAttribute("data-suggestion-index")) {
                target = target.parentElement;
            }

            if (!target || target === this.elements.suggestions) {
                return;
            }
            this.selectSuggestion(Number(target.getAttribute("data-suggestion-index")));
        }

        selectSuggestion(index) {
            var suggestion = this.suggestions[index];
            if (!suggestion) {
                return;
            }

            this.unfocusInputElement();
            this.cancelSuggestionWork();
            this.hideSuggestions();
            this.selectedSuggestion = suggestion;
            this.elements.input.value = suggestion.displayText || suggestion.stationId;
            this.fetchStation(suggestion, false);
        }

        onDocumentClick(event) {
            if (!this.rootElement.contains(event.target)) {
                this.cancelSuggestionWork();
                this.hideSuggestions();
            }
        }

        onSubmit(event) {
            event.preventDefault();
            this.submitCurrentInput();
        }

        async submitCurrentInput() {
            if (!this.active) {
                return;
            }

            this.elements.result.hidden = true;
            this.elements.provider.hidden = true;
            var query = this.elements.input.value.trim();
            if (!query) {
                this.showStatus("Enter an ICAO, IATA, or airport name.", "warning");
                return;
            }

            this.unfocusInputElement();
            this.cancelSuggestionWork();
            this.hideSuggestions();
            var selected = this.selectedSuggestion &&
                (this.selectedSuggestion.displayText === query || this.selectedSuggestion.stationId === query.toUpperCase())
                ? this.selectedSuggestion
                : null;

            if (selected) {
                this.fetchStation(selected, false);
                return;
            }

            var normalizedQuery = core.normalizeStationId(query);
            if (/^[A-Z]{4}$/.test(normalizedQuery)) {
                var direct = {
                    stationId: normalizedQuery,
                    displayName: null,
                    iataCode: null,
                    displayText: normalizedQuery
                };
                this.selectedSuggestion = direct;
                this.elements.input.value = normalizedQuery;
                this.fetchStation(direct, false);
                return;
            }

            this.cancelWeatherWork();
            var controller = weather.createAbortController(this.environment);
            this.weatherController = controller;
            var generation = this.weatherGeneration;
            this.setBusy(true);
            this.showStatus("Resolving airport…", "info");

            try {
                var resolution = await this.airportLookup.resolve(query, controller.signal);
                if (!this.isCurrentWeatherRequest(controller, generation)) {
                    return;
                }
                if (!resolution) {
                    this.showStatus("No matching airport was found. Try a four-letter ICAO code.", "warning");
                    this.setBusy(false);
                    return;
                }
                this.selectedSuggestion = resolution;
                this.elements.input.value = resolution.displayText || resolution.stationId;
                this.fetchStation(resolution, false);
            } catch (error) {
                if (this.isCurrentWeatherRequest(controller, generation) && !isAbortError(error)) {
                    this.showStatus("Airport search is temporarily unavailable. A four-letter ICAO code still works.", "error");
                    this.setBusy(false);
                }
            }
        }

        onRefresh() {
            if (this.currentResolution) {
                this.fetchStation(this.currentResolution, true);
            }
        }

        async fetchStation(resolution, forceRefresh) {
            if (!this.active || !resolution || !resolution.stationId) {
                return;
            }

            this.cancelWeatherWork();
            var controller = weather.createAbortController(this.environment);
            this.weatherController = controller;
            var generation = this.weatherGeneration;
            this.currentResolution = resolution;
            if (!forceRefresh) {
                this.elements.result.hidden = true;
                this.elements.provider.hidden = true;
            }
            this.setBusy(true);
            this.showStatus("Loading " + resolution.stationId + " weather…", "info");

            try {
                var report = await this.weatherService.getMetar(resolution.stationId, {
                    forceRefresh: Boolean(forceRefresh),
                    signal: controller.signal
                });

                if (!this.isCurrentWeatherRequest(controller, generation)) {
                    return;
                }

                if (!report) {
                    this.showStatus("No current METAR is available for " + resolution.stationId + ".", "warning");
                    return;
                }

                this.renderMetar(report, resolution);
                this.storage.set(LAST_STATION_KEY, resolution.stationId);
                this.hideStatus();
            } catch (error) {
                if (this.isCurrentWeatherRequest(controller, generation) && !isAbortError(error)) {
                    this.showStatus("Weather services are temporarily unavailable. Try again shortly.", "error");
                }
            } finally {
                if (this.isCurrentWeatherRequest(controller, generation)) {
                    this.weatherController = null;
                    this.setBusy(false);
                }
            }
        }

        isCurrentWeatherRequest(controller, generation) {
            return this.active && !controller.signal.aborted &&
                controller === this.weatherController && generation === this.weatherGeneration;
        }

        renderMetar(report, resolution) {
            var stationName = resolution.displayName;
            this.elements.station.textContent = stationName
                ? report.stationId + " — " + stationName
                : report.stationId;
            this.elements.observationTime.textContent = formatObservationTime(report.observationTime);
            this.elements.flightCategory.textContent = report.flightCategory || "N/A";
            this.elements.flightCategory.setAttribute("data-category", report.flightCategory || "");
            this.elements.wind.textContent = core.decodeWind(report);
            this.elements.visibility.textContent = core.decodeVisibility(report);
            this.elements.temperature.textContent = core.decodeTemperature(report);
            this.elements.altimeter.textContent = core.decodeAltimeter(report);
            this.elements.clouds.textContent = core.decodeClouds(report);
            this.elements.raw.textContent = report.rawMetar;
            this.elements.categoryDescription.textContent =
                core.getFlightCategoryDescription(report.flightCategory);
            this.elements.weather.textContent = core.decodeWeather(report);
            this.elements.provider.textContent = report.source || "Weather service";
            this.elements.provider.hidden = false;
            this.elements.result.hidden = false;
            this.elements.connection.textContent = "Updated " +
                String(new Date().getHours()).padStart(2, "0") + ":" +
                String(new Date().getMinutes()).padStart(2, "0");
        }

        showStatus(message, tone) {
            this.elements.status.textContent = message;
            this.elements.status.setAttribute("data-tone", tone || "info");
            this.elements.status.hidden = false;
        }

        hideStatus() {
            this.elements.status.hidden = true;
            this.elements.status.textContent = "";
        }

        setBusy(busy) {
            this.elements.searchButton.disabled = busy;
            this.elements.refresh.disabled = busy;
            this.elements.searchButton.classList.toggle("is-loading", busy);
            if (busy) {
                this.elements.connection.textContent = "Loading";
            } else if (this.elements.connection.textContent === "Loading") {
                this.elements.connection.textContent = "Ready";
            }
        }
    }

    return Object.freeze({
        LAST_STATION_KEY: LAST_STATION_KEY,
        formatObservationTime: formatObservationTime,
        createDefaultServices: createDefaultServices,
        createStorage: createStorage,
        MetarViewerApp: MetarViewerApp
    });
}));
