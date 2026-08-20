import { createHash } from 'node:crypto';
import { lstat, readFile, readdir } from 'node:fs/promises';
import path from 'node:path';

export const EXPECTED = Object.freeze({
  packageName: 'metar-viewer-toolbar',
  projectName: 'MetarViewerToolbar',
  panelId: 'PANEL_METAR_VIEWER',
  panelUrl: 'html_ui/InGamePanels/MetarViewer/MetarViewer.html',
  iconId: 'ICON_TOOLBAR_METAR_VIEWER',
  spbPath: 'InGamePanels/InGamePanel_MetarViewer.spb',
  sourceProject: 'MetarViewerToolbar.xml',
  sourcePackageDefinition: 'PackageDefinitions/metar-viewer-toolbar.xml',
  sourcePanelDefinition: 'PackageSources/InGamePanels/InGamePanel_MetarViewer.xml',
  sourcePanelHtml: 'PackageSources/html_ui/InGamePanels/MetarViewer/MetarViewer.html',
  sourceIcon: 'PackageSources/html_ui/Textures/Menu/toolbar/ICON_TOOLBAR_METAR_VIEWER.svg',
  builtPanelHtml: 'html_ui/InGamePanels/MetarViewer/MetarViewer.html',
  builtIcon: 'html_ui/Textures/Menu/toolbar/ICON_TOOLBAR_METAR_VIEWER.svg',
});

const WINDOWS_FILETIME_AT_UNIX_EPOCH = 116_444_736_000_000_000;
const PACKAGE_VERSION_PATTERN = /^\d+\.\d+\.\d+$/u;
const WINDOWS_INVALID_PATH_CHARACTERS = /[<>:"|?*\u0000-\u001f]/u;
const JUNK_NAMES = new Set([
  '.ds_store',
  'thumbs.db',
  'desktop.ini',
  '__macosx',
  '.git',
]);
const FORBIDDEN_PACKAGE_EXTENSIONS = new Set([
  '.bat',
  '.cmd',
  '.dll',
  '.exe',
  '.ps1',
]);

export class ValidationError extends Error {
  constructor(message) {
    super(message);
    this.name = 'ValidationError';
  }
}

function expect(condition, message) {
  if (!condition) {
    throw new ValidationError(message);
  }
}

function escapeRegExp(value) {
  return value.replace(/[.*+?^${}()|[\]\\]/gu, '\\$&');
}

function portableKey(relativePath) {
  return relativePath.normalize('NFC').toLowerCase();
}

function compareOrdinal(left, right) {
  return Buffer.compare(Buffer.from(left, 'utf8'), Buffer.from(right, 'utf8'));
}

export function assertPortableRelativePath(relativePath, label = 'path') {
  expect(typeof relativePath === 'string' && relativePath.length > 0, `${label} must be a non-empty string.`);
  expect(relativePath === relativePath.normalize('NFC'), `${label} must use NFC Unicode normalization: "${relativePath}".`);
  expect(!relativePath.includes('\\'), `${label} must use forward slashes: "${relativePath}".`);
  expect(!path.posix.isAbsolute(relativePath), `${label} must be relative: "${relativePath}".`);

  const segments = relativePath.split('/');
  expect(!segments.some((segment) => segment.length === 0 || segment === '.' || segment === '..'), `${label} contains an empty or traversal segment: "${relativePath}".`);

  for (const segment of segments) {
    expect(!WINDOWS_INVALID_PATH_CHARACTERS.test(segment), `${label} contains a Windows-invalid character: "${relativePath}".`);
    expect(!/[ .]$/u.test(segment), `${label} contains a segment ending in a space or period: "${relativePath}".`);
    expect(!JUNK_NAMES.has(segment.toLowerCase()), `${label} contains forbidden build junk: "${relativePath}".`);
    expect(!segment.toLowerCase().startsWith('_cvt_'), `${label} contains a simulator conversion-cache path: "${relativePath}".`);
  }
}

async function assertDirectory(directoryPath, label) {
  let info;
  try {
    info = await lstat(directoryPath);
  } catch (error) {
    if (error?.code === 'ENOENT') {
      throw new ValidationError(`${label} does not exist: ${directoryPath}`);
    }
    throw error;
  }

  expect(!info.isSymbolicLink(), `${label} must not be a symbolic link: ${directoryPath}`);
  expect(info.isDirectory(), `${label} is not a directory: ${directoryPath}`);
}

async function readRequiredFile(root, relativePath, label = relativePath) {
  const absolutePath = path.join(root, ...relativePath.split('/'));
  let info;
  try {
    info = await lstat(absolutePath);
  } catch (error) {
    if (error?.code === 'ENOENT') {
      throw new ValidationError(`Required ${label} is missing: ${relativePath}`);
    }
    throw error;
  }

  expect(!info.isSymbolicLink(), `Required ${label} must not be a symbolic link: ${relativePath}`);
  expect(info.isFile(), `Required ${label} is not a regular file: ${relativePath}`);
  expect(info.size > 0, `Required ${label} is empty: ${relativePath}`);

  return {
    absolutePath,
    buffer: await readFile(absolutePath),
    info,
  };
}

async function walkRegularFiles(root) {
  await assertDirectory(root, 'Directory');
  const files = [];
  const seenPortablePaths = new Map();

  async function visit(directory, prefix) {
    const entries = await readdir(directory, { withFileTypes: true });
    entries.sort((left, right) => compareOrdinal(left.name, right.name));

    for (const entry of entries) {
      const relativePath = prefix ? `${prefix}/${entry.name}` : entry.name;
      assertPortableRelativePath(relativePath, 'Filesystem path');

      const key = portableKey(relativePath);
      const previous = seenPortablePaths.get(key);
      expect(previous === undefined, `Filesystem paths collide on Windows/MSFS VFS: "${previous}" and "${relativePath}".`);
      seenPortablePaths.set(key, relativePath);

      const absolutePath = path.join(directory, entry.name);
      if (entry.isSymbolicLink()) {
        throw new ValidationError(`Symbolic links are not allowed in package trees: ${relativePath}`);
      }
      if (entry.isDirectory()) {
        await visit(absolutePath, relativePath);
        continue;
      }
      expect(entry.isFile(), `Unsupported filesystem entry in package tree: ${relativePath}`);

      const info = await lstat(absolutePath);
      files.push({ absolutePath, path: relativePath, size: info.size });
    }
  }

  await visit(root, '');
  files.sort((left, right) => compareOrdinal(left.path, right.path));
  return files;
}

function assertSafeXml(xml, label) {
  expect(!/<!(?:DOCTYPE|ENTITY)\b/iu.test(xml), `${label} must not contain DTD or entity declarations.`);

  const stripped = xml
    .replace(/<\?[\s\S]*?\?>/gu, '')
    .replace(/<!--[\s\S]*?-->/gu, '')
    .replace(/<!\[CDATA\[[\s\S]*?\]\]>/gu, '');
  const stack = [];
  const tagPattern = /<\s*(\/?)\s*([A-Za-z_][\w:.-]*)(?:\s[^<>]*?)?(\/?)\s*>/gu;
  let match;
  let tagCount = 0;

  while ((match = tagPattern.exec(stripped)) !== null) {
    tagCount += 1;
    const [, closing, name, selfClosing] = match;
    if (closing) {
      const opened = stack.pop();
      expect(opened === name, `${label} has mismatched XML tags: expected </${opened ?? '(none)'}>, found </${name}>.`);
    } else if (!selfClosing) {
      stack.push(name);
    }
  }

  expect(tagCount > 0, `${label} does not contain XML elements.`);
  expect(stack.length === 0, `${label} has unclosed XML element <${stack.at(-1)}>.`);
}

function parseAttributes(source, label) {
  const attributes = Object.create(null);
  const pattern = /([A-Za-z_][\w:.-]*)\s*=\s*"([^"]*)"/gu;
  let residue = source.replace(/\/\s*$/u, '');
  let match;

  while ((match = pattern.exec(source)) !== null) {
    const [, name, value] = match;
    expect(attributes[name] === undefined, `${label} repeats XML attribute "${name}".`);
    attributes[name] = value;
    residue = residue.replace(match[0], ' ');
  }

  expect(residue.trim().length === 0, `${label} contains unsupported or malformed XML attributes: "${residue.trim()}".`);
  return attributes;
}

function openingTagAttributes(xml, name, label, expectedCount = 1) {
  const escapedName = escapeRegExp(name);
  const matches = [...xml.matchAll(new RegExp(`<${escapedName}(?=\\s|>)([^>]*)>`, 'gu'))];
  expect(matches.length === expectedCount, `${label} must contain exactly ${expectedCount} <${name}> opening tag(s); found ${matches.length}.`);
  return matches.map((match, index) => parseAttributes(match[1], `${label} <${name}> #${index + 1}`));
}

function elementText(xml, name, label) {
  const escapedName = escapeRegExp(name);
  const matches = [...xml.matchAll(new RegExp(`<${escapedName}(?:\\s[^>]*)?>\\s*([\\s\\S]*?)\\s*</${escapedName}>`, 'gu'))];
  expect(matches.length === 1, `${label} must contain exactly one <${name}> element; found ${matches.length}.`);
  return matches[0][1].trim();
}

function assetGroups(packageXml) {
  const groups = [];
  const pattern = /<AssetGroup\b([^>]*)>([\s\S]*?)<\/AssetGroup>/gu;
  let match;
  while ((match = pattern.exec(packageXml)) !== null) {
    const attributes = parseAttributes(match[1], 'Package definition <AssetGroup>');
    groups.push({ attributes, body: match[2] });
  }
  return groups;
}

function assertExactAttribute(attributes, name, value, label) {
  expect(attributes[name] === value, `${label} attribute ${name} must be "${value}"; found "${attributes[name] ?? '(missing)'}".`);
}

function parseJson(buffer, label) {
  const text = buffer.toString('utf8');
  expect(!text.startsWith('\uFEFF'), `${label} must be UTF-8 without a byte-order mark.`);
  try {
    return JSON.parse(text);
  } catch (error) {
    throw new ValidationError(`${label} is not valid JSON: ${error.message}`);
  }
}

function localHtmlAssetReferences(html, htmlPath, allowedRoot) {
  const references = [];
  const tagPattern = /<(script|link|img)\b([^>]*)>/giu;
  let tagMatch;

  while ((tagMatch = tagPattern.exec(html)) !== null) {
    const [, tagName, attributes] = tagMatch;
    const attributeName = tagName.toLowerCase() === 'link' ? 'href' : 'src';
    const attributePattern = new RegExp(`\\b${attributeName}\\s*=\\s*(["'])(.*?)\\1`, 'iu');
    const attributeMatch = attributePattern.exec(attributes);
    if (attributeMatch === null) {
      continue;
    }

    const reference = attributeMatch[2].trim();
    if (reference.length === 0 || reference.startsWith('#') || reference.startsWith('data:')) {
      continue;
    }
    expect(!/^(?:https?:)?\/\//iu.test(reference), `Panel HTML must not load a remote ${tagName} asset: ${reference}`);
    if (reference.startsWith('/')) {
      // Root-relative assets such as /JS/coherent.js are supplied by the simulator VFS.
      continue;
    }

    const unqualifiedReference = reference.split(/[?#]/u, 1)[0];
    expect(unqualifiedReference.length > 0, `Panel HTML contains an invalid local asset reference: ${reference}`);
    const resolved = path.posix.normalize(path.posix.join(path.posix.dirname(htmlPath), unqualifiedReference));
    assertPortableRelativePath(resolved, `Panel HTML local ${tagName} asset`);
    expect(resolved === allowedRoot || resolved.startsWith(`${allowedRoot}/`), `Panel HTML local asset escapes ${allowedRoot}: ${reference}`);
    references.push(resolved);
  }

  return [...new Set(references)].sort(compareOrdinal);
}

async function contentHash(files) {
  const aggregate = createHash('sha256');
  for (const file of [...files].sort((left, right) => compareOrdinal(left.path, right.path))) {
    const bytes = await readFile(file.absolutePath);
    const fileHash = createHash('sha256').update(bytes).digest('hex');
    aggregate.update(file.path, 'utf8');
    aggregate.update('\0', 'utf8');
    aggregate.update(String(file.size), 'utf8');
    aggregate.update('\0', 'utf8');
    aggregate.update(fileHash, 'ascii');
    aggregate.update('\n', 'utf8');
  }
  return aggregate.digest('hex');
}

export async function validateSourceTree(rootPath) {
  const root = path.resolve(rootPath);
  await assertDirectory(root, 'MSFS integration root');

  const projectFile = await readRequiredFile(root, EXPECTED.sourceProject, 'MSFS project definition');
  const packageFile = await readRequiredFile(root, EXPECTED.sourcePackageDefinition, 'MSFS package definition');
  const panelFile = await readRequiredFile(root, EXPECTED.sourcePanelDefinition, 'in-game panel definition');
  const panelHtmlFile = await readRequiredFile(root, EXPECTED.sourcePanelHtml, 'panel HTML entry point');
  const iconFile = await readRequiredFile(root, EXPECTED.sourceIcon, 'toolbar icon');

  const projectXml = projectFile.buffer.toString('utf8');
  const packageXml = packageFile.buffer.toString('utf8');
  const panelXml = panelFile.buffer.toString('utf8');
  const iconXml = iconFile.buffer.toString('utf8');

  assertSafeXml(projectXml, 'MSFS project definition');
  assertSafeXml(packageXml, 'MSFS package definition');
  assertSafeXml(panelXml, 'in-game panel definition');
  assertSafeXml(iconXml, 'toolbar icon');

  const [projectAttributes] = openingTagAttributes(projectXml, 'Project', 'MSFS project definition');
  assertExactAttribute(projectAttributes, 'Version', '2', 'Project');
  assertExactAttribute(projectAttributes, 'Name', EXPECTED.projectName, 'Project');
  assertExactAttribute(projectAttributes, 'FolderName', 'Packages', 'Project');
  expect(elementText(projectXml, 'OutputDirectory', 'MSFS project definition') === '.', 'Project OutputDirectory must be ".".');
  expect(elementText(projectXml, 'TemporaryOutputDirectory', 'MSFS project definition') === '_PackageInt', 'Project TemporaryOutputDirectory must be "_PackageInt".');
  expect(elementText(projectXml, 'Package', 'MSFS project definition') === 'PackageDefinitions\\metar-viewer-toolbar.xml', 'Project must reference PackageDefinitions\\metar-viewer-toolbar.xml.');

  const [packageAttributes] = openingTagAttributes(packageXml, 'AssetPackage', 'MSFS package definition');
  assertExactAttribute(packageAttributes, 'Name', EXPECTED.packageName, 'AssetPackage');
  expect(PACKAGE_VERSION_PATTERN.test(packageAttributes.Version ?? ''), 'AssetPackage Version must use major.minor.patch digits.');
  expect(elementText(packageXml, 'ContentType', 'MSFS package definition') === 'MISC', 'Package ContentType must be MISC.');
  expect(elementText(packageXml, 'Title', 'MSFS package definition').length > 0, 'Package Title must not be empty.');
  expect(elementText(packageXml, 'Creator', 'MSFS package definition').length > 0, 'Package Creator must not be empty.');

  const groups = assetGroups(packageXml);
  expect(groups.length === 2, `Package definition must contain exactly two asset groups; found ${groups.length}.`);
  const copyGroup = groups.find((group) => group.attributes.Name === 'Copy_MetarViewer');
  const spbGroup = groups.find((group) => group.attributes.Name === 'InGamePanels_MetarViewer');
  expect(copyGroup !== undefined, 'Package definition is missing Copy_MetarViewer asset group.');
  expect(spbGroup !== undefined, 'Package definition is missing InGamePanels_MetarViewer asset group.');
  expect(elementText(copyGroup.body, 'Type', 'Copy_MetarViewer asset group') === 'Copy', 'Copy_MetarViewer Type must be Copy.');
  expect(elementText(copyGroup.body, 'AssetDir', 'Copy_MetarViewer asset group') === 'PackageSources\\html_ui\\', 'Copy_MetarViewer AssetDir must be PackageSources\\html_ui\\.');
  expect(elementText(copyGroup.body, 'OutputDir', 'Copy_MetarViewer asset group') === 'html_ui\\', 'Copy_MetarViewer OutputDir must be html_ui\\.');
  expect(elementText(spbGroup.body, 'Type', 'InGamePanels_MetarViewer asset group') === 'SPB', 'InGamePanels_MetarViewer Type must be SPB.');
  expect(elementText(spbGroup.body, 'AssetDir', 'InGamePanels_MetarViewer asset group') === 'PackageSources\\InGamePanels\\', 'InGamePanels_MetarViewer AssetDir must be PackageSources\\InGamePanels\\.');
  expect(elementText(spbGroup.body, 'OutputDir', 'InGamePanels_MetarViewer asset group') === 'InGamePanels\\', 'InGamePanels_MetarViewer OutputDir must be InGamePanels\\.');

  const [documentAttributes] = openingTagAttributes(panelXml, 'SimBase.Document', 'in-game panel definition');
  assertExactAttribute(documentAttributes, 'Type', 'InGamePanels', 'SimBase.Document');
  assertExactAttribute(documentAttributes, 'version', '1.0', 'SimBase.Document');
  expect(elementText(panelXml, 'Filename', 'in-game panel definition') === 'InGamePanel_MetarViewer.spb', 'Panel Filename must be InGamePanel_MetarViewer.spb.');
  const [panelAttributes] = openingTagAttributes(panelXml, 'InGamePanels.InGamePanelDefinition', 'in-game panel definition');
  assertExactAttribute(panelAttributes, 'id', EXPECTED.panelId, 'InGamePanelDefinition');
  assertExactAttribute(panelAttributes, 'url', EXPECTED.panelUrl, 'InGamePanelDefinition');
  assertExactAttribute(panelAttributes, 'icon', EXPECTED.iconId, 'InGamePanelDefinition');
  assertExactAttribute(panelAttributes, 'buttonVisible', 'true', 'InGamePanelDefinition');
  assertExactAttribute(panelAttributes, 'resizeDirections', 'Both', 'InGamePanelDefinition');
  expect((panelAttributes.Name ?? '').trim().length > 0, 'InGamePanelDefinition Name must not be empty.');
  for (const dimension of ['minWidth', 'minHeight', 'defaultWidth', 'defaultHeight']) {
    expect(/^\d+$/u.test(panelAttributes[dimension] ?? '') && Number(panelAttributes[dimension]) > 0, `InGamePanelDefinition ${dimension} must be a positive integer.`);
  }

  const [svgAttributes] = openingTagAttributes(iconXml, 'svg', 'toolbar icon');
  assertExactAttribute(svgAttributes, 'id', EXPECTED.iconId, 'Toolbar SVG');
  assertExactAttribute(svgAttributes, 'xmlns', 'http://www.w3.org/2000/svg', 'Toolbar SVG');
  expect(/^\s*-?\d+(?:\.\d+)?(?:\s+-?\d+(?:\.\d+)?){3}\s*$/u.test(svgAttributes.viewBox ?? ''), 'Toolbar SVG must define a four-number viewBox.');
  expect(!/<script\b/iu.test(iconXml), 'Toolbar SVG must not contain scripts.');
  expect(!/\son[A-Za-z]+\s*=/u.test(iconXml), 'Toolbar SVG must not contain event-handler attributes.');
  expect(!/(?:href|src)\s*=\s*"(?:https?:|\/\/)/iu.test(iconXml), 'Toolbar SVG must not reference remote resources.');

  const panelHtml = panelHtmlFile.buffer.toString('utf8');
  expect(panelHtml.trim().length > 0, 'Panel HTML entry point must not be blank.');
  const sourceHtmlReferences = localHtmlAssetReferences(panelHtml, EXPECTED.sourcePanelHtml, 'PackageSources/html_ui');
  for (const reference of sourceHtmlReferences) {
    await readRequiredFile(root, reference, `local panel asset referenced by ${EXPECTED.sourcePanelHtml}`);
  }

  const sourceFiles = await walkRegularFiles(path.join(root, 'PackageSources'));
  for (const file of sourceFiles) {
    const extension = path.posix.extname(file.path).toLowerCase();
    expect(!FORBIDDEN_PACKAGE_EXTENSIONS.has(extension), `Forbidden executable/script extension in PackageSources: ${file.path}`);
  }

  const hashedFiles = [
    { ...projectFile, path: EXPECTED.sourceProject, size: projectFile.info.size },
    { ...packageFile, path: EXPECTED.sourcePackageDefinition, size: packageFile.info.size },
    ...sourceFiles.map((file) => ({
      ...file,
      path: `PackageSources/${file.path}`,
    })),
  ];

  return {
    contentHash: await contentHash(hashedFiles),
    fileCount: hashedFiles.length,
    packageName: EXPECTED.packageName,
    root,
  };
}

function validateManifest(manifest) {
  expect(manifest !== null && typeof manifest === 'object' && !Array.isArray(manifest), 'manifest.json root must be an object.');
  expect(Array.isArray(manifest.dependencies), 'manifest.json dependencies must be an array.');
  expect(manifest.content_type === 'MISC', 'manifest.json content_type must be MISC.');
  expect(typeof manifest.title === 'string' && manifest.title.trim().length > 0, 'manifest.json title must be a non-empty string.');
  expect(typeof manifest.manufacturer === 'string', 'manifest.json manufacturer must be a string.');
  expect(typeof manifest.creator === 'string' && manifest.creator.trim().length > 0, 'manifest.json creator must be a non-empty string.');
  expect(PACKAGE_VERSION_PATTERN.test(manifest.package_version ?? ''), 'manifest.json package_version must use major.minor.patch digits.');
  expect(PACKAGE_VERSION_PATTERN.test(manifest.minimum_game_version ?? ''), 'manifest.json minimum_game_version must use major.minor.patch digits.');
  if (manifest.total_package_size !== undefined) {
    expect(typeof manifest.total_package_size === 'string' && /^\d{20}$/u.test(manifest.total_package_size), 'manifest.json total_package_size must be a zero-padded 20-digit string when present.');
  }
}

function validateLayoutEntry(entry, index) {
  expect(entry !== null && typeof entry === 'object' && !Array.isArray(entry), `layout.json content[${index}] must be an object.`);
  assertPortableRelativePath(entry.path, `layout.json content[${index}].path`);
  expect(Number.isSafeInteger(entry.size) && entry.size >= 0, `layout.json content[${index}].size must be a non-negative safe integer.`);
  expect(typeof entry.date === 'number' && Number.isFinite(entry.date) && Number.isInteger(entry.date), `layout.json content[${index}].date must be a Windows FILETIME integer.`);
  expect(entry.date >= WINDOWS_FILETIME_AT_UNIX_EPOCH, `layout.json content[${index}].date predates the Unix epoch and is probably not a Windows FILETIME.`);
}

export async function validateBuiltPackage(packagePath) {
  const root = path.resolve(packagePath);
  await assertDirectory(root, 'Built package directory');
  expect(path.basename(root).toLowerCase() === EXPECTED.packageName, `Built package directory must be named ${EXPECTED.packageName}; found ${path.basename(root)}.`);

  const files = await walkRegularFiles(root);
  const filesByPath = new Map(files.map((file) => [file.path, file]));

  const manifestFile = await readRequiredFile(root, 'manifest.json', 'built manifest');
  const layoutFile = await readRequiredFile(root, 'layout.json', 'built layout');
  const panelHtmlFile = await readRequiredFile(root, EXPECTED.builtPanelHtml, 'built panel HTML entry point');
  await readRequiredFile(root, EXPECTED.builtIcon, 'built toolbar icon');
  const spbFile = await readRequiredFile(root, EXPECTED.spbPath, 'compiled panel SPB');

  expect(spbFile.info.size >= 16, `Compiled panel SPB is implausibly small (${spbFile.info.size} bytes); do not package a placeholder.`);
  expect(!spbFile.buffer.subarray(0, 128).toString('utf8').trimStart().startsWith('<'), 'Compiled panel SPB appears to contain XML; the SDK-compiled binary is required.');

  for (const file of files) {
    const extension = path.posix.extname(file.path).toLowerCase();
    expect(!FORBIDDEN_PACKAGE_EXTENSIONS.has(extension), `Forbidden executable/script extension in built package: ${file.path}`);
  }

  const manifest = parseJson(manifestFile.buffer, 'manifest.json');
  const layout = parseJson(layoutFile.buffer, 'layout.json');
  const warnings = [];
  validateManifest(manifest);
  expect(layout !== null && typeof layout === 'object' && !Array.isArray(layout), 'layout.json root must be an object.');
  expect(Array.isArray(layout.content), 'layout.json content must be an array.');

  const builtHtmlReferences = localHtmlAssetReferences(panelHtmlFile.buffer.toString('utf8'), EXPECTED.builtPanelHtml, 'html_ui');
  for (const reference of builtHtmlReferences) {
    expect(filesByPath.has(reference), `Built panel HTML references a missing local asset: ${reference}`);
  }

  const layoutPaths = new Map();
  for (const [index, entry] of layout.content.entries()) {
    validateLayoutEntry(entry, index);
    const key = portableKey(entry.path);
    const previous = layoutPaths.get(key);
    expect(previous === undefined, `layout.json contains a duplicate/case-colliding path: "${previous}" and "${entry.path}".`);
    layoutPaths.set(key, entry.path);

    expect(entry.path !== 'manifest.json' && entry.path !== 'layout.json', `layout.json must not list root metadata file ${entry.path}.`);
    const file = filesByPath.get(entry.path);
    expect(file !== undefined, `layout.json lists a file that does not exist with exact casing: ${entry.path}`);
    expect(file.size === entry.size, `layout.json size mismatch for ${entry.path}: expected ${file.size}, found ${entry.size}.`);
  }

  const payloadFiles = files.filter((file) => file.path !== 'manifest.json' && file.path !== 'layout.json');
  for (const file of payloadFiles) {
    expect(layoutPaths.has(portableKey(file.path)), `Built package file is not listed in layout.json: ${file.path}`);
  }
  expect(layout.content.length === payloadFiles.length, `layout.json entry count ${layout.content.length} does not match payload file count ${payloadFiles.length}.`);

  const actualTotalSize = files.reduce((total, file) => total + BigInt(file.size), 0n);
  if (manifest.total_package_size === undefined) {
    warnings.push('manifest.json does not contain total_package_size; this is valid for older MSFS 2020 SDK output, but only layout entry sizes can be checked.');
  } else {
    const declaredTotalSize = BigInt(manifest.total_package_size);
    expect(declaredTotalSize === actualTotalSize, `manifest.json total_package_size mismatch: expected ${actualTotalSize.toString().padStart(20, '0')}, found ${manifest.total_package_size}.`);
  }

  const orderedLayoutPaths = layout.content.map((entry) => entry.path);
  const sortedLayoutPaths = [...orderedLayoutPaths].sort(compareOrdinal);
  if (orderedLayoutPaths.some((entry, index) => entry !== sortedLayoutPaths[index])) {
    warnings.push('layout.json entries are not ordinally sorted; the SDK output is valid but not byte-deterministic.');
  }

  return {
    contentHash: await contentHash(files),
    fileCount: files.length,
    packageName: EXPECTED.packageName,
    payloadFileCount: payloadFiles.length,
    root,
    totalSize: actualTotalSize,
    warnings,
  };
}
