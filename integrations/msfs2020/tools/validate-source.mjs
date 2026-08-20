#!/usr/bin/env node

import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { validateSourceTree } from './msfs-validation.mjs';

function usage() {
  console.error('Usage: node tools/validate-source.mjs [integrations/msfs2020]');
}

const args = process.argv.slice(2);
if (args.includes('--help') || args.includes('-h')) {
  usage();
  process.exit(0);
}
if (args.length > 1) {
  usage();
  process.exit(2);
}

const defaultRoot = fileURLToPath(new URL('../', import.meta.url));
const root = path.resolve(args[0] ?? defaultRoot);

try {
  const result = await validateSourceTree(root);
  console.log(`MSFS source validation passed: ${result.fileCount} files, SHA-256 ${result.contentHash}`);
} catch (error) {
  console.error(`MSFS source validation failed: ${error.message}`);
  process.exitCode = 1;
}
