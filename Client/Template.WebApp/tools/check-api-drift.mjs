import { readdirSync, readFileSync, rmSync } from 'node:fs';
import { relative, resolve, sep } from 'node:path';
import { spawnSync } from 'node:child_process';
import { normalizeGeneratedOutput } from './normalize-generated-output.mjs';

const root = resolve(import.meta.dirname, '..');
const pnpmCli = process.env['npm_execpath'];
if (!pnpmCli) throw new Error('Run this check through pnpm.');
const pnpmIsScript = /\.(?:c?js)$/i.test(pnpmCli);
const pnpmExecutable = pnpmIsScript ? process.execPath : pnpmCli;
const pnpmArguments = pnpmIsScript ? [pnpmCli] : [];
const expectedRoot = resolve(root, 'src/app/infrastructure/api/generated');
const temporaryRoot = resolve(root, '.api-drift-generated');
if (!temporaryRoot.startsWith(root + sep)) throw new Error('Unsafe generated-client path.');

function files(directory, current = directory) {
  return readdirSync(current, { withFileTypes: true })
    .flatMap((entry) => {
      const path = resolve(current, entry.name);
      return entry.isDirectory()
        ? files(directory, path)
        : [relative(directory, path).replaceAll('\\', '/')];
    })
    .sort();
}

try {
  rmSync(temporaryRoot, { recursive: true, force: true });
  const result = spawnSync(
    pnpmExecutable,
    [...pnpmArguments, 'exec', 'orval', '--config', 'orval.config.ts'],
    {
      cwd: root,
      env: { ...process.env, ORVAL_OUTPUT: temporaryRoot },
      encoding: 'utf8',
    },
  );
  if (result.status !== 0) throw new Error(`${result.stdout}\n${result.stderr}`);
  normalizeGeneratedOutput(temporaryRoot);
  const expectedFiles = files(expectedRoot);
  const actualFiles = files(temporaryRoot);
  if (JSON.stringify(expectedFiles) !== JSON.stringify(actualFiles))
    throw new Error('Generated client file list has drifted.');
  for (const file of expectedFiles) {
    const normalize = (value) => value.replaceAll('\r\n', '\n');
    if (
      normalize(readFileSync(resolve(expectedRoot, file), 'utf8')) !==
      normalize(readFileSync(resolve(temporaryRoot, file), 'utf8'))
    ) {
      throw new Error(`Generated client drift: ${file}`);
    }
  }
  console.log(`${expectedFiles.length} generated OpenAPI files match.`);
} finally {
  rmSync(temporaryRoot, { recursive: true, force: true });
}
