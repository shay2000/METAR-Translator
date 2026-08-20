import assert from 'node:assert/strict';
import { mkdir, mkdtemp, readFile, rm, stat, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import path from 'node:path';
import test from 'node:test';

import {
  EXPECTED,
  validateBuiltPackage,
  validateSourceTree,
} from '../tools/msfs-validation.mjs';

const FIXED_FILETIME = 116_444_736_000_000_000;

async function writeFixtureFile(root, relativePath, contents) {
  const absolutePath = path.join(root, ...relativePath.split('/'));
  await mkdir(path.dirname(absolutePath), { recursive: true });
  await writeFile(absolutePath, contents);
}

async function temporaryRoot(t, prefix) {
  const root = await mkdtemp(path.join(tmpdir(), prefix));
  t.after(async () => {
    await rm(root, { recursive: true, force: true });
  });
  return root;
}

async function createSourceFixture(t) {
  const parent = await temporaryRoot(t, 'metar-viewer-msfs-source-');
  const root = path.join(parent, 'msfs2020');
  await mkdir(root, { recursive: true });

  await writeFixtureFile(root, EXPECTED.sourceProject, `<?xml version="1.0" encoding="utf-8"?>
<Project Version="2" Name="MetarViewerToolbar" FolderName="Packages">
  <OutputDirectory>.</OutputDirectory>
  <TemporaryOutputDirectory>_PackageInt</TemporaryOutputDirectory>
  <Packages><Package>PackageDefinitions\\metar-viewer-toolbar.xml</Package></Packages>
</Project>
`);
  await writeFixtureFile(root, EXPECTED.sourcePackageDefinition, `<?xml version="1.0" encoding="utf-8"?>
<AssetPackage Name="metar-viewer-toolbar" Version="1.0.0">
  <ItemSettings><ContentType>MISC</ContentType><Title>METAR Viewer</Title><Creator>METAR Viewer</Creator></ItemSettings>
  <AssetGroups>
    <AssetGroup Name="Copy_MetarViewer"><Type>Copy</Type><AssetDir>PackageSources\\html_ui\\</AssetDir><OutputDir>html_ui\\</OutputDir></AssetGroup>
    <AssetGroup Name="InGamePanels_MetarViewer"><Type>SPB</Type><AssetDir>PackageSources\\InGamePanels\\</AssetDir><OutputDir>InGamePanels\\</OutputDir></AssetGroup>
  </AssetGroups>
</AssetPackage>
`);
  await writeFixtureFile(root, EXPECTED.sourcePanelDefinition, `<?xml version="1.0" encoding="utf-8"?>
<SimBase.Document Type="InGamePanels" version="1.0">
  <Filename>InGamePanel_MetarViewer.spb</Filename>
  <InGamePanels.InGamePanelDefinition id="PANEL_METAR_VIEWER" Name="METAR Viewer" url="html_ui/InGamePanels/MetarViewer/MetarViewer.html" resizeDirections="Both" minWidth="24" minHeight="24" defaultWidth="42" defaultHeight="54" icon="ICON_TOOLBAR_METAR_VIEWER" buttonVisible="true"></InGamePanels.InGamePanelDefinition>
</SimBase.Document>
`);
  await writeFixtureFile(root, EXPECTED.sourceIcon, `<?xml version="1.0" encoding="utf-8"?>
<svg id="ICON_TOOLBAR_METAR_VIEWER" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><path d="M0 0h1" /></svg>
`);
  await writeFixtureFile(root, EXPECTED.sourcePanelHtml, '<!doctype html><html><body>METAR Viewer</body></html>\n');

  return root;
}

function jsonBytes(value) {
  return Buffer.from(`${JSON.stringify(value, null, 2)}\n`, 'utf8');
}

async function createBuiltPackageFixture(t) {
  const parent = await temporaryRoot(t, 'metar-viewer-msfs-package-');
  const root = path.join(parent, EXPECTED.packageName);
  await mkdir(root, { recursive: true });

  const payloads = new Map([
    [EXPECTED.builtPanelHtml, Buffer.from('<!doctype html><html>METAR</html>\n', 'utf8')],
    [EXPECTED.builtIcon, Buffer.from('<svg id="ICON_TOOLBAR_METAR_VIEWER"></svg>\n', 'utf8')],
    [EXPECTED.spbPath, Buffer.from([
      0x53, 0x50, 0x42, 0x00, 0x01, 0x00, 0x00, 0x00,
      0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80,
      0x90, 0xa0, 0xb0, 0xc0, 0xd0, 0xe0, 0xf0, 0x00,
    ])],
  ]);

  for (const [relativePath, contents] of payloads) {
    await writeFixtureFile(root, relativePath, contents);
  }

  const layoutEntries = [];
  for (const relativePath of [...payloads.keys()].sort()) {
    const info = await stat(path.join(root, ...relativePath.split('/')));
    layoutEntries.push({ path: relativePath, size: info.size, date: FIXED_FILETIME });
  }
  const layoutBytes = jsonBytes({ content: layoutEntries });
  await writeFixtureFile(root, 'layout.json', layoutBytes);

  const manifest = {
    dependencies: [],
    content_type: 'MISC',
    title: 'METAR Viewer Toolbar',
    manufacturer: '',
    creator: 'METAR Viewer',
    package_version: '1.0.0',
    minimum_game_version: '1.37.0',
    total_package_size: '00000000000000000000',
  };
  const placeholderManifestBytes = jsonBytes(manifest);
  const payloadSize = [...payloads.values()].reduce((total, contents) => total + contents.length, 0);
  const totalSize = payloadSize + layoutBytes.length + placeholderManifestBytes.length;
  manifest.total_package_size = String(totalSize).padStart(20, '0');
  const manifestBytes = jsonBytes(manifest);
  assert.equal(manifestBytes.length, placeholderManifestBytes.length, 'fixed-width package size must preserve manifest byte length');
  await writeFixtureFile(root, 'manifest.json', manifestBytes);

  return root;
}

test('source validator accepts the expected project, package, panel, and icon wiring', async (t) => {
  const root = await createSourceFixture(t);

  const result = await validateSourceTree(root);

  assert.equal(result.packageName, EXPECTED.packageName);
  assert.match(result.contentHash, /^[0-9a-f]{64}$/u);
});

test('source validator rejects a panel URL that does not resolve to the required entry point', async (t) => {
  const root = await createSourceFixture(t);
  const panelPath = path.join(root, ...EXPECTED.sourcePanelDefinition.split('/'));
  const panelXml = await readFile(panelPath, 'utf8');
  await writeFile(panelPath, panelXml.replace(EXPECTED.panelUrl, 'html_ui/InGamePanels/MetarViewer/Wrong.html'));

  await assert.rejects(
    validateSourceTree(root),
    /attribute url must be "html_ui\/InGamePanels\/MetarViewer\/MetarViewer\.html"/u,
  );
});

test('source validator rejects missing local assets referenced by panel HTML', async (t) => {
  const root = await createSourceFixture(t);
  const htmlPath = path.join(root, ...EXPECTED.sourcePanelHtml.split('/'));
  await writeFile(htmlPath, '<!doctype html><html><script src="missing.js"></script></html>\n');

  await assert.rejects(
    validateSourceTree(root),
    /local panel asset .* is missing: .*missing\.js/u,
  );
});

test('built-package validator checks inventory, sizes, total size, and returns a stable tree hash', async (t) => {
  const root = await createBuiltPackageFixture(t);

  const first = await validateBuiltPackage(root);
  const second = await validateBuiltPackage(root);

  assert.equal(first.payloadFileCount, 3);
  assert.equal(first.contentHash, second.contentHash);
  assert.match(first.contentHash, /^[0-9a-f]{64}$/u);
  assert.equal(first.warnings.length, 0);
});

test('same-size payload mutation changes the package content hash', async (t) => {
  const root = await createBuiltPackageFixture(t);
  const before = await validateBuiltPackage(root);
  const htmlPath = path.join(root, ...EXPECTED.builtPanelHtml.split('/'));
  const html = await readFile(htmlPath);
  html[html.indexOf(Buffer.from('METAR'))] = 'X'.charCodeAt(0);
  await writeFile(htmlPath, html);

  const after = await validateBuiltPackage(root);
  assert.notEqual(after.contentHash, before.contentHash);
  assert.equal(after.totalSize, before.totalSize);
});

test('built-package validator rejects stale layout sizes', async (t) => {
  const root = await createBuiltPackageFixture(t);
  const layoutPath = path.join(root, 'layout.json');
  const layout = JSON.parse(await readFile(layoutPath, 'utf8'));
  layout.content[0].size += 1;
  await writeFile(layoutPath, jsonBytes(layout));

  await assert.rejects(validateBuiltPackage(root), /layout\.json size mismatch/u);
});

test('built-package validator rejects layout path traversal', async (t) => {
  const root = await createBuiltPackageFixture(t);
  const layoutPath = path.join(root, 'layout.json');
  const layout = JSON.parse(await readFile(layoutPath, 'utf8'));
  layout.content[0].path = '../escape.js';
  await writeFile(layoutPath, jsonBytes(layout));

  await assert.rejects(validateBuiltPackage(root), /empty or traversal segment/u);
});

test('built-package validator rejects unlisted payload files', async (t) => {
  const root = await createBuiltPackageFixture(t);
  await writeFixtureFile(root, 'html_ui/InGamePanels/MetarViewer/unlisted.js', 'console.log("unlisted");\n');

  await assert.rejects(validateBuiltPackage(root), /not listed in layout\.json/u);
});

test('built-package validator rejects source XML posing as a compiled SPB', async (t) => {
  const root = await createBuiltPackageFixture(t);
  const spbPath = path.join(root, ...EXPECTED.spbPath.split('/'));
  await writeFile(spbPath, '<SimBase.Document>not compiled</SimBase.Document>');

  await assert.rejects(validateBuiltPackage(root), /appears to contain XML/u);
});

test('built-package validator rejects an incorrect fixed-width total package size', async (t) => {
  const root = await createBuiltPackageFixture(t);
  const manifestPath = path.join(root, 'manifest.json');
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  manifest.total_package_size = '00000000000000000000';
  await writeFile(manifestPath, jsonBytes(manifest));

  await assert.rejects(validateBuiltPackage(root), /total_package_size mismatch/u);
});

test('built-package validator accepts older SDK manifests without total_package_size and warns', async (t) => {
  const root = await createBuiltPackageFixture(t);
  const manifestPath = path.join(root, 'manifest.json');
  const manifest = JSON.parse(await readFile(manifestPath, 'utf8'));
  delete manifest.total_package_size;
  await writeFile(manifestPath, jsonBytes(manifest));

  const result = await validateBuiltPackage(root);

  assert.equal(result.warnings.length, 1);
  assert.match(result.warnings[0], /does not contain total_package_size/u);
});
