#!/usr/bin/env node

import path from 'node:path';
import { validateBuiltPackage } from './msfs-validation.mjs';

function usage() {
  console.error('Usage: node tools/validate-package.mjs <Packages/metar-viewer-toolbar>');
}

const args = process.argv.slice(2);
if (args.includes('--help') || args.includes('-h')) {
  usage();
  process.exit(0);
}
if (args.length !== 1) {
  usage();
  process.exit(2);
}

try {
  const result = await validateBuiltPackage(path.resolve(args[0]));
  for (const warning of result.warnings) {
    console.warn(`MSFS package validation warning: ${warning}`);
  }
  console.log(`MSFS package validation passed: ${result.payloadFileCount} payload files, ${result.totalSize} bytes, SHA-256 ${result.contentHash}`);
  console.log('Simulator validation is still required; structural validation cannot prove toolbar registration or Coherent runtime behavior.');
} catch (error) {
  console.error(`MSFS package validation failed: ${error.message}`);
  process.exitCode = 1;
}
