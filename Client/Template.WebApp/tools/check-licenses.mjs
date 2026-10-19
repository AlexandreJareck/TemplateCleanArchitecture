import { spawnSync } from 'node:child_process';

const pnpmCli = process.env['npm_execpath'];
if (!pnpmCli) throw new Error('Run this check through pnpm.');
const pnpmIsScript = /\.(?:c?js)$/i.test(pnpmCli);
const pnpmExecutable = pnpmIsScript ? process.execPath : pnpmCli;
const pnpmArguments = pnpmIsScript ? [pnpmCli] : [];
const result = spawnSync(pnpmExecutable, [...pnpmArguments, 'licenses', 'list', '--json'], {
  encoding: 'utf8',
});
if (result.status !== 0) throw new Error(result.stderr || 'pnpm license inventory failed.');

const inventory = JSON.parse(result.stdout);
const allowed = new Set([
  'MIT',
  'MIT-0',
  'ISC',
  '0BSD',
  'Apache-2.0',
  'BSD-2-Clause',
  'BSD-3-Clause',
  'BlueOak-1.0.0',
  'CC0-1.0',
  'CC-BY-4.0',
  'MPL-2.0',
  'Python-2.0',
  '(Apache-2.0 AND BSD-3-Clause)',
  '(MIT OR CC0-1.0)',
  '(WTFPL OR MIT)',
]);
const exceptions = new Set(['union@0.5.0']); // Its packaged LICENSE is the MIT text; package.json omits the identifier.
const rejected = [];
let packages = 0;

for (const [license, entries] of Object.entries(inventory)) {
  for (const entry of entries) {
    packages += 1;
    const coordinates = entry.versions.map((version) => `${entry.name}@${version}`);
    if (!allowed.has(license) && !coordinates.every((item) => exceptions.has(item))) {
      rejected.push(`${license}: ${coordinates.join(', ')}`);
    }
  }
}

if (rejected.length > 0) throw new Error(`Unapproved licenses:\n${rejected.join('\n')}`);
console.log(
  `${packages} dependency records use approved licenses (1 documented metadata exception).`,
);
