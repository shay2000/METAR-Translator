import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import test from 'node:test';

const require = createRequire(import.meta.url);
const airports = require('../PackageSources/html_ui/InGamePanels/MetarViewer/airport-service.js');
const weather = require('../PackageSources/html_ui/InGamePanels/MetarViewer/weather-service.js');

function airport(name, icaoCode, type = 'large_airport', iataCode = null) {
  return {
    name,
    icao_code: icaoCode,
    code: icaoCode,
    gps_code: icaoCode,
    iata_code: iataCode,
    type,
  };
}

test('unrelated airport priors alone stay below the automatic resolution threshold', () => {
  const score = airports.scoreAirport(
    airport('Alpha International Airport', 'KAAA', 'large_airport', 'AAA'),
    'Heatrow',
  );

  assert.ok(score < airports.MINIMUM_RESOLUTION_SCORE);
});

test('a Heathrow typo remains relevant and ranks ahead of an unrelated airport', () => {
  const relevant = airports.scoreAirport(
    airport('London Heathrow Airport', 'EGLL', 'large_airport', 'LHR'),
    'Heatrow',
  );
  const unrelated = airports.scoreAirport(
    airport('Alpha International Airport', 'KAAA', 'large_airport', 'AAA'),
    'Heatrow',
  );

  assert.ok(relevant >= airports.MINIMUM_RESOLUTION_SCORE);
  assert.ok(relevant > unrelated);
});

test('exact codes outrank exact names and closed airports are rejected', () => {
  const codeMatch = airports.scoreAirport(airport('Somewhere Else', 'EGLL'), 'EGLL');
  const nameMatch = airports.scoreAirport(airport('EGLL', 'EGKK'), 'EGLL');
  const closed = airports.scoreAirport(airport('Old Field', 'EGXX', 'closed'), 'Old Field');

  assert.ok(codeMatch > nameMatch);
  assert.equal(closed, airports.NO_MATCH);
});

test('same-name large airport ranks above a heliport', () => {
  const large = airports.scoreAirport(airport('Heathrow', 'EGLL'), 'Heathrow');
  const heliport = airports.scoreAirport(airport('Heathrow', 'ZXHX', 'heliport'), 'Heathrow');

  assert.ok(large > heliport);
});

test('query relaxation follows bounded typo fragments', () => {
  const queries = airports.buildRelaxedQueries('Heatrow');
  assert.deepEqual(
    queries.map((query) => [query.filterKey, query.value]),
    [
      ['filter[name]', 'Heatr'],
      ['filter[name]', 'Heat'],
      ['filter[name]', 'He'],
    ],
  );
});

test('rate limiting stops candidate search fan-out after one request', async () => {
  let requests = 0;
  const client = new airports.AirportsApiClient(async () => {
    requests += 1;
    return { status: 429, ok: false };
  });
  const finder = new airports.AirportCandidateFinder(client);

  assert.deepEqual(await finder.find('EGLL'), []);
  assert.equal(requests, 1);
});

test('true not-found responses continue into the next search strategy', async () => {
  const urls = [];
  const client = new airports.AirportsApiClient(async (url) => {
    urls.push(url);
    if (urls.length === 1) {
      return { status: 404, ok: false };
    }
    return {
      status: 200,
      ok: true,
      json: async () => ({ data: [{ attributes: airport('London Heathrow Airport', 'EGLL', 'large_airport', 'LHR') }] }),
    };
  });
  const finder = new airports.AirportCandidateFinder(client);
  const matches = await finder.find('LHR');

  assert.equal(urls.length, 2);
  assert.match(urls[0], /\/airports\/LHR$/u);
  assert.match(urls[1], /filter%5Bcode%5D=LHR/u);
  assert.equal(matches[0].stationId, 'EGLL');
});

test('offline airport lookup still permits a direct station-shaped input', async () => {
  const finder = {
    async find() {
      return [];
    },
  };
  const lookup = new airports.AirportLookupService(finder);

  assert.deepEqual(await lookup.resolve(' egll '), {
    stationId: 'EGLL',
    displayName: null,
    iataCode: null,
    displayText: 'EGLL',
  });
  assert.equal(await lookup.resolve('Heathrow Airport'), null);
});

test('resolution and suggestion caches normalize input and honor expiry', async () => {
  let now = 1000;
  let calls = 0;
  const finder = {
    async find() {
      calls += 1;
      return [{
        stationId: 'EGLL',
        attributes: airport('London Heathrow Airport', 'EGLL', 'large_airport', 'LHR'),
        score: 500,
      }];
    },
  };
  const lookup = new airports.AirportLookupService(finder, {
    now: () => now,
    resolutionLifetimeMilliseconds: 600000,
    suggestionLifetimeMilliseconds: 120000,
  });

  await lookup.resolve(' Heathrow ');
  await lookup.resolve('heathrow');
  assert.equal(calls, 1);

  await lookup.getSuggestions('Lon');
  await lookup.getSuggestions(' lon ');
  assert.equal(calls, 2);

  now += 120001;
  await lookup.getSuggestions('LON');
  assert.equal(calls, 3);
});

test('cancellation propagates and is never cached as an empty lookup', async () => {
  let calls = 0;
  const finder = {
    async find(_input, signal) {
      calls += 1;
      if (signal && signal.aborted) {
        const error = new Error('aborted');
        error.name = 'AbortError';
        throw error;
      }
      return [];
    },
  };
  const lookup = new airports.AirportLookupService(finder);
  const controller = new AbortController();
  controller.abort();

  await assert.rejects(lookup.getSuggestions('London', controller.signal), { name: 'AbortError' });
  await lookup.getSuggestions('London');
  assert.equal(calls, 2);
});

test('airport requests time out instead of leaving name and IATA lookups hanging', async () => {
  const client = new airports.AirportsApiClient(
    () => new Promise(() => {}),
    { requestTimeoutMilliseconds: 5 },
  );

  await assert.rejects(client.getByCode('LHR'), /timed out/u);
});

test('airport fetch omits a polyfilled signal that native fetch would reject', async () => {
  let requestOptions;
  const client = new airports.AirportsApiClient(async (_url, options) => {
    requestOptions = options;
    return { status: 404, ok: false };
  });
  const controller = weather.createAbortController({});

  assert.equal(await client.getByCode('EGLL', controller.signal), null);
  assert.equal(Object.hasOwn(requestOptions, 'signal'), false);
});
