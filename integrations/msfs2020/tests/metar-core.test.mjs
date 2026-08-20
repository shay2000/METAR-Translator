import assert from 'node:assert/strict';
import { createRequire } from 'node:module';
import test from 'node:test';

const require = createRequire(import.meta.url);
const core = require('../PackageSources/html_ui/InGamePanels/MetarViewer/metar-core.js');

test('parses and decodes a typical European observation', () => {
  const report = core.parseRawMetar(
    'EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013',
    'EGLL',
    new Date('2026-08-15T13:00:00Z'),
  );

  assert.equal(report.stationId, 'EGLL');
  assert.equal(report.windDirection, 250);
  assert.equal(report.windSpeed, 12);
  assert.equal(report.visibility, 10);
  assert.equal(report.visibilityUnit, 'km');
  assert.deepEqual(report.cloudLayers, [{ coverage: 'FEW', altitude: 3500, type: null }]);
  assert.equal(report.temperature, 12);
  assert.equal(report.dewPoint, 8);
  assert.equal(report.altimeter, 1013);
  assert.equal(report.flightCategory, 'VFR');
  assert.match(core.decodeWind(report), /250°.*12 kt/u);
  assert.match(core.decodeVisibility(report), /10 km/u);
});

test('parses US visibility, fog, low ceiling, and inches of mercury', () => {
  const report = core.parseRawMetar(
    'KJFK 151251Z 18004KT 1/2SM FG OVC003 08/08 A2992',
    'KJFK',
  );

  assert.equal(report.visibility, 0.5);
  assert.deepEqual(report.weatherPhenomena, ['FG']);
  assert.equal(report.cloudLayers[0].altitude, 300);
  assert.equal(report.altimeter, 29.92);
  assert.equal(report.altimeterUnit, 'inHg');
  assert.equal(report.flightCategory, 'LIFR');
});

test('parses gusts, negative dew point, and a variable wind', () => {
  const gusts = core.parseRawMetar(
    'KDEN 151253Z 27015G25KT 10SM SKC 20/M01 A3001',
    'KDEN',
  );
  const variable = core.parseRawMetar(
    'EGKK 151250Z VRB03KT 9999 NSC 15/12 Q1020',
    'EGKK',
  );

  assert.equal(gusts.windDirection, 270);
  assert.equal(gusts.windSpeed, 15);
  assert.equal(gusts.windGust, 25);
  assert.equal(gusts.dewPoint, -1);
  assert.equal(variable.windDirection, null);
  assert.equal(variable.windSpeed, 3);
});

test('handles CAVOK and mixed-fraction statute-mile visibility', () => {
  const cavok = core.parseRawMetar(
    'EGLL 151250Z 25012KT CAVOK 12/08 Q1013',
    'EGLL',
  );
  const mixed = core.parseRawMetar(
    'KORD 151251Z 09008KT 1 1/4SM BR OVC008 05/04 A2995',
    'KORD',
  );

  assert.equal(cavok.isCavok, true);
  assert.equal(cavok.visibility, 10);
  assert.equal(cavok.flightCategory, 'VFR');
  assert.match(core.decodeClouds(cavok), /CAVOK/u);
  assert.equal(mixed.visibility, 1.25);
  assert.deepEqual(mixed.weatherPhenomena, ['BR']);
  assert.equal(mixed.flightCategory, 'IFR');
});

for (const marker of ['TEMPO', 'BECMG', 'NOSIG']) {
  test(`does not let ${marker} trend conditions overwrite the observation`, () => {
    const report = core.parseRawMetar(
      `EGLL 151250Z 25012KT 9999 -RA FEW035 12/08 Q1013 ${marker} 0400 FG OVC001`,
      'EGLL',
    );

    assert.equal(report.visibility, 10);
    assert.deepEqual(report.weatherPhenomena, ['-RA']);
    assert.deepEqual(report.cloudLayers, [{ coverage: 'FEW', altitude: 3500, type: null }]);
    assert.equal(report.flightCategory, 'VFR');
  });
}

test('does not infer mist from the KBRL station identifier', () => {
  const report = core.parseRawMetar(
    'METAR KBRL 281620Z 32012KT 10SM CLR 09/M02 A3026',
    'KBRL',
  );

  assert.deepEqual(report.weatherPhenomena, []);
  assert.equal(core.decodeWeather(report), 'No significant weather');
});

test('applies exact visibility and ceiling category boundaries', () => {
  const categoryFor = (groups) => core.parseRawMetar(
    `KAAA 151200Z 00000KT ${groups} 10/05 Q1013`,
    'KAAA',
  ).flightCategory;

  assert.equal(categoryFor('1/2SM SKC'), 'LIFR');
  assert.equal(categoryFor('1SM SKC'), 'IFR');
  assert.equal(categoryFor('3SM SKC'), 'MVFR');
  assert.equal(categoryFor('5SM SKC'), 'MVFR');
  assert.equal(categoryFor('5.1SM SKC'), 'VFR');
  assert.equal(categoryFor('10SM OVC004'), 'LIFR');
  assert.equal(categoryFor('10SM OVC005'), 'IFR');
  assert.equal(categoryFor('10SM OVC010'), 'MVFR');
  assert.equal(categoryFor('10SM OVC030'), 'MVFR');
  assert.equal(categoryFor('10SM OVC031'), 'VFR');
  assert.equal(categoryFor('10SM SCT004'), 'VFR');
});

test('resolves observation dates across month boundaries', () => {
  assert.equal(
    core.parseObservationTime('312350Z', new Date('2026-04-01T00:10:00Z')).toISOString(),
    '2026-03-31T23:50:00.000Z',
  );
  assert.equal(
    core.parseObservationTime('010010Z', new Date('2026-03-31T23:55:00Z')).toISOString(),
    '2026-04-01T00:10:00.000Z',
  );
});

test('normalizes lowercase reports and strips a terminal equals sign', () => {
  const report = core.parseRawMetar(
    'egll 151250z 25012kt 9999 few035 12/08 q1013=',
    'egll',
  );

  assert.equal(report.rawMetar, 'METAR EGLL 151250Z 25012KT 9999 FEW035 12/08 Q1013');
  assert.equal(report.stationId, 'EGLL');
});
