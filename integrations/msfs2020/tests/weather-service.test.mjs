import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import test from 'node:test';

const require = createRequire(import.meta.url);
const weather = require('../PackageSources/html_ui/InGamePanels/MetarViewer/weather-service.js');

function deferred() {
  let resolve;
  let reject;
  const promise = new Promise((resolvePromise, rejectPromise) => {
    resolve = resolvePromise;
    reject = rejectPromise;
  });
  return { promise, resolve, reject };
}

test('simulator source waits for the facility listener and normalizes object results', async () => {
  const calls = [];
  let ready;
  const environment = {
    RegisterViewListener(name, callback) {
      calls.push(['listener', name]);
      ready = callback;
    },
    Coherent: {
      call(name, ident) {
        calls.push(['call', name, ident]);
        return Promise.resolve({
          icao: 'EGLL',
          metarString: 'METAR EGLL 200050Z 24007KT 9999 FEW035 17/11 Q1005',
        });
      },
    },
  };
  const source = new weather.SimulatorMetarSource(environment, {
    listenerTimeoutMilliseconds: 1000,
    callTimeoutMilliseconds: 1000,
  });

  const pending = source.getRawMetar(' egll ');
  assert.deepEqual(calls, [['listener', 'JS_LISTENER_FACILITY']]);
  ready();

  assert.equal(
    await pending,
    'METAR EGLL 200050Z 24007KT 9999 FEW035 17/11 Q1005',
  );
  assert.deepEqual(calls[1], ['call', 'GET_METAR_BY_IDENT', 'EGLL']);
});

test('simulator source accepts the legacy string result and rejects Invalid METAR', () => {
  assert.equal(weather.normalizeSimulatorMetar(' EGLL 151250Z 00000KT CAVOK '), 'EGLL 151250Z 00000KT CAVOK');
  assert.equal(weather.normalizeSimulatorMetar({ metarString: 'Invalid METAR' }), null);
  assert.equal(weather.normalizeSimulatorMetar({ icao: '' }), null);
});

test('simulator facility registration can recover after a transient failure', async () => {
  let registrations = 0;
  const environment = {
    RegisterViewListener(_name, callback) {
      registrations += 1;
      if (registrations === 1) throw new Error('listener unavailable');
      callback();
    },
    Coherent: {
      call: async () => 'EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013',
    },
  };
  const source = new weather.SimulatorMetarSource(environment);

  await assert.rejects(source.getRawMetar('EGLL'), /listener unavailable/u);
  assert.match(await source.getRawMetar('EGLL'), /^EGLL /u);
  assert.equal(registrations, 2);
});

test('weather service falls back to the next source and records its provider', async () => {
  const service = new weather.MetarService([
    {
      name: 'Simulator',
      getRawMetar: async () => null,
    },
    {
      name: 'Fallback',
      getRawMetar: async () => 'EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013',
    },
  ]);

  const report = await service.getMetar('EGLL');
  assert.equal(report.stationId, 'EGLL');
  assert.equal(report.source, 'Fallback');
});

test('all available source failures produce an unavailable error', async () => {
  const service = new weather.MetarService([
    {
      isAvailable: () => false,
      getRawMetar: async () => null,
    },
    {
      name: 'Broken fallback',
      getRawMetar: async () => {
        throw new Error('network failed');
      },
    },
  ]);

  await assert.rejects(service.getMetar('EGLL'), /temporarily unavailable/u);
});

test('normalizes cache keys and expires positive reports after the configured lifetime', async () => {
  let now = 1000;
  let calls = 0;
  const source = {
    name: 'Fixture',
    async getRawMetar() {
      calls += 1;
      return 'EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013';
    },
  };
  const service = new weather.MetarService([source], {
    cacheLifetimeMilliseconds: 60000,
    now: () => now,
  });

  await service.getMetar(' egll ');
  now += 59000;
  await service.getMetar('EGLL');
  assert.equal(calls, 1);
  now += 2000;
  await service.getMetar('EGLL');
  assert.equal(calls, 2);
});

test('does not cache missing reports', async () => {
  let calls = 0;
  const service = new weather.MetarService([{
    async getRawMetar() {
      calls += 1;
      return null;
    },
  }]);

  assert.equal(await service.getMetar('EGLL'), null);
  assert.equal(await service.getMetar('EGLL'), null);
  assert.equal(calls, 2);
});

test('coalesces concurrent requests for the same station', async () => {
  const operation = deferred();
  let calls = 0;
  const service = new weather.MetarService([{
    name: 'Fixture',
    getRawMetar() {
      calls += 1;
      return operation.promise;
    },
  }]);

  const first = service.getMetar('EGLL');
  const second = service.getMetar(' egll ');
  operation.resolve('EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013');

  const [left, right] = await Promise.all([first, second]);
  assert.equal(calls, 1);
  assert.strictEqual(left, right);
});

test('aborting one waiter does not cancel another waiter on the shared request', async () => {
  const operation = deferred();
  let upstreamSignal;
  const service = new weather.MetarService([{
    name: 'Fixture',
    getRawMetar(_stationId, signal) {
      upstreamSignal = signal;
      return operation.promise;
    },
  }]);
  const firstController = new AbortController();
  const secondController = new AbortController();
  const first = service.getMetar('EGLL', { signal: firstController.signal });
  const second = service.getMetar('EGLL', { signal: secondController.signal });

  firstController.abort();
  await assert.rejects(first, { name: 'AbortError' });
  assert.equal(upstreamSignal.aborted, false);

  operation.resolve('EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013');
  assert.equal((await second).stationId, 'EGLL');
});

test('a fresh caller can join shared work after the only previous waiter cancels', async () => {
  const operation = deferred();
  let calls = 0;
  const service = new weather.MetarService([{
    name: 'Fixture',
    getRawMetar() {
      calls += 1;
      return operation.promise;
    },
  }]);
  const controller = new AbortController();
  const first = service.getMetar('EGLL', { signal: controller.signal });
  controller.abort();
  await assert.rejects(first, { name: 'AbortError' });

  const second = service.getMetar('EGLL');
  operation.resolve('EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013');
  assert.equal((await second).stationId, 'EGLL');
  assert.equal(calls, 1);
});

test('late cleanup from cleared work cannot delete a replacement in-flight request', async () => {
  const operations = [deferred(), deferred()];
  let calls = 0;
  const service = new weather.MetarService([{
    name: 'Fixture',
    getRawMetar() {
      return operations[calls++].promise;
    },
  }]);

  const oldRequest = service.getMetar('EGLL');
  service.clear();
  const replacement = service.getMetar('EGLL');
  operations[0].resolve('EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013');
  await oldRequest;

  const thirdCaller = service.getMetar('EGLL');
  assert.equal(calls, 2);
  operations[1].resolve('EGLL 151251Z 25013KT 9999 FEW035 12/08 Q1013');
  const [second, third] = await Promise.all([replacement, thirdCaller]);
  assert.strictEqual(second, third);
  assert.equal(second.windSpeed, 13);
});

test('abort-controller fallback works without a native AbortController', () => {
  const controller = weather.createAbortController({});
  let events = 0;
  controller.signal.addEventListener('abort', () => { events += 1; });

  controller.abort();
  controller.abort();

  assert.equal(controller.signal.aborted, true);
  assert.equal(controller.signal.__metarViewerPolyfill, true);
  assert.equal(events, 1);
});

test('VATSIM source treats true misses differently from server failures', async () => {
  const notFound = new weather.VatsimMetarSource(async () => ({ status: 404, ok: false }));
  const failed = new weather.VatsimMetarSource(async () => ({ status: 429, ok: false }));

  assert.equal(await notFound.getRawMetar('EGLL'), null);
  await assert.rejects(failed.getRawMetar('EGLL'), /HTTP 429/u);
});

test('VATSIM requires an exact station id and ignores malformed id-less reports', async () => {
  const source = new weather.VatsimMetarSource(async () => ({
    status: 200,
    ok: true,
    json: async () => [
      { id: 'KJFK', metar: 'KJFK 151251Z 18004KT 10SM SKC 08/08 A2992' },
      { metar: 'KORD 151251Z 09008KT 10SM SKC 05/04 A2995' },
    ],
  }));

  assert.equal(await source.getRawMetar('EGLL'), null);
});

test('VATSIM timeout settles shared work so the next lookup can retry', async () => {
  let calls = 0;
  const source = new weather.VatsimMetarSource(
    async () => {
      calls += 1;
      if (calls === 1) return new Promise(() => {});
      return {
        status: 200,
        ok: true,
        json: async () => [{
          id: 'EGLL',
          metar: 'EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013',
        }],
      };
    },
    { requestTimeoutMilliseconds: 5 },
  );
  const service = new weather.MetarService([source]);

  await assert.rejects(service.getMetar('EGLL'), /temporarily unavailable/u);
  assert.equal(service.inFlight.size, 0);
  assert.equal((await service.getMetar('EGLL')).stationId, 'EGLL');
  assert.equal(calls, 2);
});
