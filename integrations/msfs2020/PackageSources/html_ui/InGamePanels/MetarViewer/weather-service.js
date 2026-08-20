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
        root.MetarViewerWeather = api;
    }
}(typeof globalThis !== "undefined" ? globalThis : this, function (core) {
    "use strict";

    if (!core) {
        throw new Error("MetarViewerCore must be loaded before weather-service.js");
    }

    function createAbortError() {
        var error = new Error("The operation was aborted.");
        error.name = "AbortError";
        return error;
    }

    function isAbortError(error) {
        return Boolean(error && error.name === "AbortError");
    }

    function createAbortController(environment) {
        var runtime = environment || (typeof globalThis !== "undefined" ? globalThis : {});
        if (typeof runtime.AbortController === "function") {
            return new runtime.AbortController();
        }

        var listeners = [];
        var signal = {
            aborted: false,
            __metarViewerPolyfill: true,
            addEventListener: function (name, callback) {
                if (name === "abort" && typeof callback === "function" &&
                    listeners.indexOf(callback) === -1) {
                    listeners.push(callback);
                }
            },
            removeEventListener: function (name, callback) {
                if (name !== "abort") {
                    return;
                }
                var index = listeners.indexOf(callback);
                if (index !== -1) {
                    listeners.splice(index, 1);
                }
            }
        };

        return {
            signal: signal,
            abort: function () {
                if (signal.aborted) {
                    return;
                }
                signal.aborted = true;
                listeners.slice().forEach(function (listener) { listener(); });
                listeners.length = 0;
            }
        };
    }

    function settleWithCleanup(promise, cleanup) {
        return Promise.resolve(promise).then(
            function (value) {
                cleanup();
                return value;
            },
            function (error) {
                cleanup();
                throw error;
            }
        );
    }

    function raceWithSignal(promise, signal, timeoutMilliseconds, timeoutMessage) {
        return new Promise(function (resolve, reject) {
            var settled = false;
            var timer = null;

            function cleanup() {
                if (timer !== null) {
                    clearTimeout(timer);
                }
                if (signal) {
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

            if (signal) {
                signal.addEventListener("abort", abort, { once: true });
            }

            if (timeoutMilliseconds > 0) {
                timer = setTimeout(function () {
                    finish(reject, new Error(timeoutMessage || "The operation timed out."));
                }, timeoutMilliseconds);
            }

            Promise.resolve(promise).then(
                function (value) { finish(resolve, value); },
                function (error) { finish(reject, error); }
            );
        });
    }

    function normalizeSimulatorMetar(result) {
        var raw = null;

        if (typeof result === "string") {
            raw = result;
        } else if (result && typeof result === "object") {
            // MSFS 2020 and 2024 have returned different shapes over time.
            raw = result.metarString || result.rawMetar || result.rawText || result.metar || null;
        }

        if (typeof raw !== "string") {
            return null;
        }

        raw = raw.trim();
        return !raw || raw.toUpperCase() === "INVALID METAR" ? null : raw;
    }

    class SimulatorMetarSource {
        constructor(environment, options) {
            this.environment = environment || (typeof globalThis !== "undefined" ? globalThis : {});
            this.options = options || {};
            this.listenerTimeoutMilliseconds = this.options.listenerTimeoutMilliseconds || 5000;
            this.callTimeoutMilliseconds = this.options.callTimeoutMilliseconds || 8000;
            this._readyPromise = null;
            this.name = "Microsoft Flight Simulator";
        }

        isAvailable() {
            return typeof this.environment.RegisterViewListener === "function" &&
                this.environment.Coherent &&
                typeof this.environment.Coherent.call === "function";
        }

        async ready(signal) {
            var self = this;

            if (!this.isAvailable()) {
                return Promise.reject(new Error("The simulator facility service is unavailable."));
            }

            if (!this._readyPromise) {
                this._readyPromise = new Promise(function (resolve, reject) {
                    try {
                        self.environment.RegisterViewListener("JS_LISTENER_FACILITY", resolve);
                    } catch (error) {
                        reject(error);
                    }
                });
            }

            var registration = this._readyPromise;
            try {
                return await raceWithSignal(
                    registration,
                    signal,
                    this.listenerTimeoutMilliseconds,
                    "The simulator facility service did not become ready."
                );
            } catch (error) {
                if (!isAbortError(error) && this._readyPromise === registration) {
                    this._readyPromise = null;
                }
                throw error;
            }
        }

        async getRawMetar(stationId, signal) {
            var normalized = core.normalizeStationId(stationId);
            if (!normalized) {
                return null;
            }

            await this.ready(signal);
            var result = await raceWithSignal(
                this.environment.Coherent.call("GET_METAR_BY_IDENT", normalized),
                signal,
                this.callTimeoutMilliseconds,
                "The simulator METAR request timed out."
            );

            return normalizeSimulatorMetar(result);
        }
    }

    class VatsimMetarSource {
        constructor(fetchFunction, options) {
            this.fetchFunction = fetchFunction || (typeof fetch === "function" ? fetch.bind(globalThis) : null);
            this.options = options || {};
            this.baseUrl = this.options.baseUrl || "https://metar.vatsim.net/";
            this.requestTimeoutMilliseconds = this.options.requestTimeoutMilliseconds === undefined
                ? 8000
                : this.options.requestTimeoutMilliseconds;
            this.name = "VATSIM";
        }

        isAvailable() {
            return typeof this.fetchFunction === "function";
        }

        async getRawMetar(stationId, signal) {
            var normalized = core.normalizeStationId(stationId);
            if (!normalized || !this.isAvailable()) {
                return null;
            }

            var requestOptions = {
                method: "GET",
                headers: { Accept: "application/json" }
            };
            if (signal && !signal.__metarViewerPolyfill) {
                requestOptions.signal = signal;
            }
            var response = await raceWithSignal(
                this.fetchFunction(
                    this.baseUrl + encodeURIComponent(normalized) + "?format=json",
                    requestOptions
                ),
                signal,
                this.requestTimeoutMilliseconds,
                "The VATSIM METAR request timed out."
            );

            if (response.status === 204 || response.status === 404) {
                return null;
            }
            if (!response.ok) {
                throw new Error("VATSIM returned HTTP " + response.status + ".");
            }

            var payload = await response.json();
            var reports = Array.isArray(payload) ? payload : [payload];
            var report = reports.find(function (item) {
                return item && typeof item === "object" &&
                    core.normalizeStationId(item.id) === normalized;
            });

            if (!report) {
                return null;
            }

            var raw = report.metar || report.rawMetar || report.rawText;
            return typeof raw === "string" && raw.trim() ? raw.trim() : null;
        }
    }

    class MetarService {
        constructor(sources, options) {
            this.sources = (sources || []).filter(function (source) {
                return source && typeof source.getRawMetar === "function";
            });
            this.options = options || {};
            this.cacheLifetimeMilliseconds = this.options.cacheLifetimeMilliseconds || 60000;
            this.now = this.options.now || Date.now;
            this.cache = new Map();
            this.inFlight = new Map();
        }

        getCached(stationId) {
            var cached = this.cache.get(stationId);
            if (!cached) {
                return null;
            }

            if (cached.expiresAt <= this.now()) {
                this.cache.delete(stationId);
                return null;
            }

            return cached.value;
        }

        async getMetar(stationId, options) {
            var normalized = core.normalizeStationId(stationId);
            var requestOptions = options || {};
            var signal = requestOptions.signal;

            if (!normalized) {
                return null;
            }
            if (signal && signal.aborted) {
                throw createAbortError();
            }

            if (!requestOptions.forceRefresh) {
                var cached = this.getCached(normalized);
                if (cached) {
                    return cached;
                }
            } else {
                this.cache.delete(normalized);
            }

            var entry = this.inFlight.get(normalized);
            if (!entry) {
                entry = this.createOperation(normalized);
                this.inFlight.set(normalized, entry);
            }

            return this.waitForOperation(entry, signal);
        }

        createOperation(stationId) {
            var self = this;
            var controller = createAbortController();
            var entry = {
                controller: controller,
                waiters: 0,
                settled: false,
                promise: null
            };

            var operation = this.fetchFromSources(
                stationId,
                controller.signal
            ).then(function (result) {
                if (result && !controller.signal.aborted) {
                    self.cache.set(stationId, {
                        expiresAt: self.now() + self.cacheLifetimeMilliseconds,
                        value: result
                    });
                }
                return result;
            });
            entry.promise = settleWithCleanup(operation, function () {
                entry.settled = true;
                if (self.inFlight.get(stationId) === entry) {
                    self.inFlight.delete(stationId);
                }
            });

            return entry;
        }

        async fetchFromSources(stationId, signal) {
            var failures = [];
            var attemptedSources = 0;

            for (var index = 0; index < this.sources.length; index += 1) {
                var source = this.sources[index];
                if (typeof source.isAvailable === "function" && !source.isAvailable()) {
                    continue;
                }

                attemptedSources += 1;
                try {
                    var raw = await source.getRawMetar(stationId, signal);
                    if (raw) {
                        var metar = core.parseRawMetar(raw, stationId);
                        metar.source = source.name || "Weather service";
                        return metar;
                    }
                } catch (error) {
                    if (isAbortError(error)) {
                        throw error;
                    }
                    failures.push(error);
                }
            }

            if (failures.length === attemptedSources && attemptedSources > 0) {
                var unavailable = new Error("Weather services are temporarily unavailable.");
                unavailable.cause = failures[0];
                throw unavailable;
            }

            return null;
        }

        waitForOperation(entry, signal) {
            entry.waiters += 1;
            var self = this;

            return settleWithCleanup(raceWithSignal(entry.promise, signal, 0), function () {
                entry.waiters -= 1;
                // Retain a reference to self so older Coherent JS engines do not collect the
                // service while an operation's finalizer is queued.
                void self;
            });
        }

        clear() {
            this.cache.clear();
            this.inFlight.forEach(function (entry) {
                entry.controller.abort();
            });
            this.inFlight.clear();
        }
    }

    return Object.freeze({
        createAbortError: createAbortError,
        isAbortError: isAbortError,
        createAbortController: createAbortController,
        settleWithCleanup: settleWithCleanup,
        raceWithSignal: raceWithSignal,
        normalizeSimulatorMetar: normalizeSimulatorMetar,
        SimulatorMetarSource: SimulatorMetarSource,
        VatsimMetarSource: VatsimMetarSource,
        MetarService: MetarService
    });
}));
