import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import test from 'node:test';

const require = createRequire(import.meta.url);
const core = require('../PackageSources/html_ui/InGamePanels/MetarViewer/metar-core.js');
const panel = require('../PackageSources/html_ui/InGamePanels/MetarViewer/panel-app.js');

function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

class FakeClassList {
  constructor() {
    this.values = new Set();
  }

  toggle(name, force) {
    if (force) this.values.add(name);
    else this.values.delete(name);
  }
}

class FakeElement {
  constructor(ownerDocument) {
    this.ownerDocument = ownerDocument;
    this.listeners = new Map();
    this.attributes = new Map();
    this.children = [];
    this.parentElement = null;
    this.classList = new FakeClassList();
    this.hidden = false;
    this.disabled = false;
    this.value = '';
    this._textContent = '';
  }

  get textContent() {
    return this._textContent;
  }

  set textContent(value) {
    this._textContent = String(value);
    if (value === '') this.children = [];
  }

  addEventListener(name, callback) {
    if (!this.listeners.has(name)) this.listeners.set(name, new Set());
    this.listeners.get(name).add(callback);
  }

  removeEventListener(name, callback) {
    if (this.listeners.has(name)) this.listeners.get(name).delete(callback);
  }

  emit(name, event = {}) {
    event.target = event.target || this;
    event.preventDefault = event.preventDefault || (() => {});
    for (const callback of this.listeners.get(name) || []) callback(event);
  }

  setAttribute(name, value) {
    this.attributes.set(name, String(value));
  }

  getAttribute(name) {
    return this.attributes.has(name) ? this.attributes.get(name) : null;
  }

  hasAttribute(name) {
    return this.attributes.has(name);
  }

  appendChild(child) {
    child.parentElement = this;
    this.children.push(child);
    return child;
  }
}

class FakeDocument extends FakeElement {
  constructor() {
    super(null);
    this.ownerDocument = this;
  }

  createElement() {
    return new FakeElement(this);
  }
}

function createPanelFixture(overrides = {}) {
  const document = new FakeDocument();
  const selectors = [
    '#search-form', '#airport-search', '#search-button', '#airport-suggestions',
    '#status-message', '#metar-result', '#provider-badge', '#station-heading',
    '#observation-time', '#flight-category', '#decoded-wind', '#decoded-visibility',
    '#decoded-temperature', '#decoded-altimeter', '#decoded-clouds', '#raw-metar',
    '#category-description', '#decoded-weather', '#refresh-button', '#connection-state',
  ];
  const elements = new Map(selectors.map((selector) => [selector, new FakeElement(document)]));
  elements.get('#airport-suggestions').hidden = true;
  elements.get('#status-message').hidden = true;
  elements.get('#metar-result').hidden = true;
  elements.get('#provider-badge').hidden = true;

  const root = new FakeElement(document);
  root.querySelector = (selector) => elements.get(selector) || null;
  root.contains = (target) => target === root || [...elements.values()].includes(target);
  const environment = Object.assign({ document }, overrides.environment || {});
  const app = new panel.MetarViewerApp(root, {
    environment,
    storage: { get: () => null, set: () => {} },
    airportLookup: overrides.airportLookup || {
      getSuggestions: async () => [],
      resolve: async () => null,
    },
    weatherService: overrides.weatherService || {
      getMetar: async () => null,
    },
    suggestionDelayMilliseconds: overrides.suggestionDelayMilliseconds ?? 0,
  });

  return { app, document, elements };
}

function tick() {
  return new Promise((resolve) => setTimeout(resolve, 0));
}

function fixtureReport(station = 'EGLL') {
  const report = core.parseRawMetar(
    `${station} 151250Z 25012KT 9999 FEW035 12/08 Q1013`,
    station,
    new Date('2026-08-15T13:00:00Z'),
  );
  report.source = 'Fixture';
  return report;
}

test('late suggestions cannot replace results for newer input', async () => {
  const requests = new Map();
  const { app, elements } = createPanelFixture({
    airportLookup: {
      getSuggestions(query) {
        const operation = deferred();
        requests.set(query, operation);
        return operation.promise;
      },
      resolve: async () => null,
    },
  });
  app.activate();
  const input = elements.get('#airport-search');

  input.value = 'Lon';
  input.emit('input');
  await tick();
  input.value = 'London';
  input.emit('input');
  await tick();

  requests.get('London').resolve([{ stationId: 'EGLL', displayName: 'London Heathrow' }]);
  await tick();
  assert.equal(app.suggestions[0].stationId, 'EGLL');

  requests.get('Lon').resolve([{ stationId: 'KAAA', displayName: 'Stale result' }]);
  await tick();
  assert.equal(app.suggestions[0].stationId, 'EGLL');
  app.destroy();
});

test('Escape cancels an in-flight lookup so it cannot reopen suggestions', async () => {
  const operation = deferred();
  const { app, elements } = createPanelFixture({
    airportLookup: {
      getSuggestions: () => operation.promise,
      resolve: async () => null,
    },
  });
  app.activate();
  const input = elements.get('#airport-search');
  input.value = 'Lon';
  input.emit('input');
  await tick();
  input.emit('keydown', { key: 'Escape' });

  operation.resolve([{ stationId: 'EGLL', displayName: 'London Heathrow' }]);
  await tick();
  assert.equal(app.suggestions.length, 0);
  assert.equal(elements.get('#airport-suggestions').hidden, true);
  app.destroy();
});

test('a stale suggestion failure cannot erase newer successful results', async () => {
  const requests = new Map();
  const { app, elements } = createPanelFixture({
    airportLookup: {
      getSuggestions(query) {
        const operation = deferred();
        requests.set(query, operation);
        return operation.promise;
      },
      resolve: async () => null,
    },
  });
  app.activate();
  const input = elements.get('#airport-search');
  input.value = 'Lon';
  input.emit('input');
  await tick();
  input.value = 'London';
  input.emit('input');
  await tick();

  requests.get('London').resolve([{ stationId: 'EGLL', displayName: 'London Heathrow' }]);
  await tick();
  requests.get('Lon').reject(new Error('stale failure'));
  await tick();

  assert.equal(app.suggestions[0].stationId, 'EGLL');
  assert.equal(elements.get('#airport-suggestions').hidden, false);
  app.destroy();
});

test('changing input aborts and suppresses a late weather result', async () => {
  const operation = deferred();
  let signal;
  const { app, elements } = createPanelFixture({
    weatherService: {
      getMetar(_station, options) {
        signal = options.signal;
        return operation.promise;
      },
    },
  });
  app.activate();
  const pending = app.fetchStation({ stationId: 'EGLL', displayName: 'London Heathrow' }, false);

  const input = elements.get('#airport-search');
  input.value = 'Paris';
  input.emit('input');
  assert.equal(signal.aborted, true);
  operation.resolve(fixtureReport());
  await pending;

  assert.equal(elements.get('#metar-result').hidden, true);
  app.destroy();
});

test('selecting a suggestion fetches it without reopening suggestions', async () => {
  let suggestionCalls = 0;
  let weatherStation;
  const { app, elements } = createPanelFixture({
    airportLookup: {
      getSuggestions: async () => {
        suggestionCalls += 1;
        return [];
      },
      resolve: async () => null,
    },
    weatherService: {
      async getMetar(station) {
        weatherStation = station;
        return fixtureReport(station);
      },
    },
    suggestionDelayMilliseconds: 1,
  });
  app.activate();
  app.renderSuggestions([{
    stationId: 'EGLL',
    displayName: 'London Heathrow Airport',
    displayText: 'EGLL · LHR · London Heathrow Airport',
  }]);

  app.selectSuggestion(0);
  await tick();
  await tick();

  assert.equal(weatherStation, 'EGLL');
  assert.equal(suggestionCalls, 0);
  assert.equal(elements.get('#airport-suggestions').hidden, true);
  assert.equal(elements.get('#metar-result').hidden, false);
  assert.match(elements.get('#connection-state').textContent, /^Updated \d{2}:\d{2}$/u);
  app.destroy();
});

test('pressing Enter cancels a pending suggestion debounce', async () => {
  let suggestionCalls = 0;
  const { app, elements } = createPanelFixture({
    airportLookup: {
      getSuggestions: async () => {
        suggestionCalls += 1;
        return [];
      },
      resolve: async () => ({ stationId: 'EGLL', displayName: 'London Heathrow' }),
    },
    weatherService: {
      getMetar: async () => fixtureReport(),
    },
    suggestionDelayMilliseconds: 20,
  });
  app.activate();
  const input = elements.get('#airport-search');
  input.value = 'EGLL';
  input.emit('input');
  input.emit('keydown', { key: 'Enter' });

  await new Promise((resolve) => setTimeout(resolve, 30));
  assert.equal(suggestionCalls, 0);
  assert.equal(elements.get('#metar-result').hidden, false);
  app.destroy();
});

test('a direct four-letter ICAO bypasses airport-network resolution', async () => {
  let resolutionCalls = 0;
  let weatherStation;
  const { app, elements } = createPanelFixture({
    airportLookup: {
      getSuggestions: async () => [],
      resolve: async () => {
        resolutionCalls += 1;
        return null;
      },
    },
    weatherService: {
      async getMetar(station) {
        weatherStation = station;
        return fixtureReport(station);
      },
    },
  });
  app.activate();
  const input = elements.get('#airport-search');
  input.value = 'egll';
  input.emit('keydown', { key: 'Enter' });
  await tick();

  assert.equal(resolutionCalls, 0);
  assert.equal(weatherStation, 'EGLL');
  assert.equal(elements.get('#metar-result').hidden, false);
  app.destroy();
});

test('destroy aborts active work and removes panel event listeners', async () => {
  const operation = deferred();
  let signal;
  const { app, elements, document } = createPanelFixture({
    weatherService: {
      getMetar(_station, options) {
        signal = options.signal;
        return operation.promise;
      },
    },
  });
  app.activate();
  const pending = app.fetchStation({ stationId: 'EGLL' }, false);
  app.destroy();

  assert.equal(signal.aborted, true);
  assert.equal(elements.get('#airport-search').listeners.get('input').size, 0);
  assert.equal(document.listeners.get('pointerdown').size, 0);
  operation.resolve(fixtureReport());
  await pending;
});

test('MSFS input focus is always released on blur, deactivate, and destroy', () => {
  const events = [];
  const { app, elements } = createPanelFixture({
    environment: {
      OnInputFieldFocus: () => events.push('focus'),
      OnInputFieldUnfocus: () => events.push('unfocus'),
    },
  });
  const input = elements.get('#airport-search');
  app.activate();

  input.emit('focus');
  input.emit('blur');
  input.emit('focus');
  app.deactivate();
  app.activate();
  input.emit('focus');
  app.destroy();

  assert.deepEqual(events, [
    'focus', 'unfocus',
    'focus', 'unfocus',
    'focus', 'unfocus',
  ]);
});

test('a failed lookup never leaves the previous station displayed', async () => {
  const reports = [fixtureReport('EGLL'), null, new Error('offline')];
  const { app, elements } = createPanelFixture({
    weatherService: {
      async getMetar() {
        const next = reports.shift();
        if (next instanceof Error) throw next;
        return next;
      },
    },
  });
  app.activate();

  await app.fetchStation({ stationId: 'EGLL' }, false);
  assert.equal(elements.get('#metar-result').hidden, false);
  assert.match(elements.get('#station-heading').textContent, /EGLL/u);

  await app.fetchStation({ stationId: 'KJFK' }, false);
  assert.equal(elements.get('#metar-result').hidden, true);
  assert.match(elements.get('#status-message').textContent, /KJFK/u);

  await app.fetchStation({ stationId: 'KDEN' }, false);
  assert.equal(elements.get('#metar-result').hidden, true);
  assert.match(elements.get('#status-message').textContent, /temporarily unavailable/u);
  app.destroy();
});

test('a failed airport-name resolution hides previously displayed weather', async () => {
  const { app, elements } = createPanelFixture({
    airportLookup: {
      getSuggestions: async () => [],
      resolve: async () => null,
    },
    weatherService: {
      getMetar: async () => fixtureReport('EGLL'),
    },
  });
  app.activate();
  await app.fetchStation({ stationId: 'EGLL' }, false);
  assert.equal(elements.get('#metar-result').hidden, false);

  const input = elements.get('#airport-search');
  input.value = 'Nowhere Airport';
  await app.submitCurrentInput();

  assert.equal(elements.get('#metar-result').hidden, true);
  assert.match(elements.get('#status-message').textContent, /No matching airport/u);
  app.destroy();
});

test('formats observation time in an unambiguous UTC form', () => {
  assert.equal(
    panel.formatObservationTime(new Date('2026-08-20T02:07:00Z')),
    '20 Aug 2026, 02:07 UTC',
  );
});
