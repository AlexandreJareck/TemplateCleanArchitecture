import { readdirSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const defaultGeneratedRoot = resolve(
  import.meta.dirname,
  '../src/app/infrastructure/api/generated',
);

export function normalizeGeneratedOutput(directory = defaultGeneratedRoot) {
  for (const entry of readdirSync(directory, { withFileTypes: true })) {
    const path = resolve(directory, entry.name);
    if (entry.isDirectory()) {
      normalizeGeneratedOutput(path);
      continue;
    }

    const source = readFileSync(path, 'utf8');
    const normalized = source.replace(/[ \t]+$/gm, '').replace(/(?:\r?\n)+$/, '\n');
    if (source !== normalized) writeFileSync(path, normalized, 'utf8');
  }
}

if (process.argv[1] && fileURLToPath(import.meta.url) === resolve(process.argv[1])) {
  normalizeGeneratedOutput(process.argv[2] ? resolve(process.argv[2]) : defaultGeneratedRoot);
}
